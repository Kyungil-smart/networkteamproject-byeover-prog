using System.Collections;
using DeadZone.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적 유닛의 이동 애니메이션 전달 + 피격 시 히트 플래시를 처리한다.
    /// 사망 애니메이션은 없음 — 즉시 Despawn 후 시체 프리팹으로 대체.
    /// 루트 오브젝트에 Animator, NavMeshAgent와 함께 부착한다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyAnimHandler : NetworkBehaviour
    {
        // ───────── Animator 파라미터 해시 ─────────

        private static readonly int HashSpeed      = Animator.StringToHash("Speed");
        private static readonly int HashMoveX      = Animator.StringToHash("MoveX");
        private static readonly int HashMoveY      = Animator.StringToHash("MoveY");
        private static readonly int HashIsMoving   = Animator.StringToHash("IsMoving");
        private static readonly int HashWeaponType = Animator.StringToHash("WeaponType");
        private static readonly int HashFire       = Animator.StringToHash("Fire");
        private static readonly int HashReload     = Animator.StringToHash("Reload");
        private static readonly int HashHit        = Animator.StringToHash("Hit");

        // ───────── 컴포넌트 캐시 ─────────

        private Animator _animator;
        private NavMeshAgent _agent;
        private EnemyStats _stats;

        // ───────── Inspector ─────────

        [Header("히트 플래시")]
        [Tooltip("CharacterVisual 하위의 Renderer들을 자동 수집한다. 비워두면 Awake에서 자동 탐색")]
        [SerializeField] private Renderer[] flashRenderers;

        [Tooltip("피격 시 반짝이는 색상 (흰색 추천)")]
        [SerializeField] private Color flashColor = Color.white;

        [Tooltip("반짝임 지속 시간 (초)")]
        [SerializeField] private float flashDuration = 0.1f;

        [Header("속도 정규화")]
        [Tooltip("Speed 파라미터를 0~1로 정규화할 때 기준 최대 속도. 0이면 SO moveSpeed 사용")]
        [SerializeField] private float maxSpeedForNormalize = 0f;

        [Header("무기 타입")]
        [Tooltip("체크하면 EnemyStatsSO.defaultWeapon.weaponCategory를 기준으로 WeaponType 파라미터를 자동 설정합니다. 데이터가 없으면 아래 수동 값을 사용합니다.")]
        [SerializeField] private bool useDefaultWeaponCategory = true;

        [Tooltip("WeaponType 파라미터 수동 fallback 값입니다. 자동 설정에 필요한 EnemyStatsSO 또는 defaultWeapon이 비어 있을 때 사용합니다.")]
        [SerializeField] private int weaponTypeIndex = 0;

        // ───────── 내부 상태 ─────────

        private MaterialPropertyBlock _propBlock;
        private static readonly int ShaderColorID = Shader.PropertyToID("_Color");
        private static readonly int ShaderEmissionID = Shader.PropertyToID("_EmissionColor");
        private Coroutine _flashCoroutine;
        private int _aimLayerIndex = -1;

        // ───────── 라이프사이클 ─────────

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _agent = GetComponent<NavMeshAgent>();
            _stats = GetComponent<EnemyStats>();
            _propBlock = new MaterialPropertyBlock();
            _aimLayerIndex = _animator.GetLayerIndex("Aim Layer");

            // Renderer가 Inspector에서 미할당이면 CharacterVisual 하위에서 자동 수집
            if (flashRenderers == null || flashRenderers.Length == 0)
            {
                Transform visual = transform.Find("CharacterVisual");
                if (visual != null)
                {
                    flashRenderers = visual.GetComponentsInChildren<Renderer>(true);
                }
                else
                {
                    flashRenderers = GetComponentsInChildren<Renderer>(true);
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            // 정규화 기준 속도
            if (maxSpeedForNormalize <= 0f && _stats != null && _stats.StatsSO != null)
                maxSpeedForNormalize = _stats.StatsSO.moveSpeed;
            if (maxSpeedForNormalize <= 0f)
                maxSpeedForNormalize = 6f;

            // 무기 타입 초기 설정
            if (_animator != null)
            {
                // WeaponType은 Animator Controller에서 Int 파라미터이므로 SetInteger를 사용한다.
                int resolvedWeaponTypeIndex = ResolveWeaponTypeIndex();
                _animator.SetInteger(HashWeaponType, resolvedWeaponTypeIndex);
                _aimLayerIndex = _animator.GetLayerIndex("Aim Layer");
            }
        }

        // ───────── 이동 애니메이션 (매 프레임) ─────────

        private void LateUpdate()
        {
            // 서버 권위 NetworkAnimator 사용 전제. 클라이언트는 동기화된 값만 표시한다.
            // 클라가 자기 NavMeshAgent.velocity(=0)로 Speed/MoveX/MoveY를 덮어쓰는 것을 방지한다.
            if (!IsServer) return;
            if (_animator == null || _agent == null) return;

            Vector3 velocity = _agent.velocity;
            float speed = velocity.magnitude;

            // Speed (0~1 정규화), IsMoving
            _animator.SetFloat(HashSpeed, Mathf.Clamp01(speed / maxSpeedForNormalize));
            _animator.SetBool(HashIsMoving, speed > 0.1f);

            // MoveX / MoveY (로컬 기준)
            if (speed > 0.1f)
            {
                Vector3 localVel = transform.InverseTransformDirection(velocity).normalized;
                _animator.SetFloat(HashMoveX, localVel.x);
                _animator.SetFloat(HashMoveY, localVel.z);
            }
            else
            {
                _animator.SetFloat(HashMoveX, 0f);
                _animator.SetFloat(HashMoveY, 0f);
            }
        }

        // ───────── Aim Layer 제어 ─────────

        /// <summary>
        /// Aim Layer 활성화/비활성화. Combat 진입 시 true, 이탈 시 false.
        /// </summary>
        public void SetAimMode(bool active)
        {
            if (_animator == null || _aimLayerIndex < 0) return;
            _animator.SetLayerWeight(_aimLayerIndex, active ? 1f : 0f);
        }

        public void TriggerFire()
        {
            if (_animator == null) return;
            _animator.SetTrigger(HashFire);
        }

        public void TriggerReload()
        {
            if (_animator == null) return;
            _animator.SetTrigger(HashReload);
        }

        public void TriggerHit()
        {
            if (_animator == null) return;
            _animator.SetTrigger(HashHit);
        }

        private int ResolveWeaponTypeIndex()
        {
            if (!useDefaultWeaponCategory)
            {
                return weaponTypeIndex;
            }

            WeaponDataSO defaultWeapon = _stats != null && _stats.StatsSO != null
                ? _stats.StatsSO.defaultWeapon
                : null;

            if (defaultWeapon == null)
            {
                Debug.LogWarning("[EnemyAnimHandler] EnemyStatsSO.defaultWeapon이 비어 있어 수동 WeaponType 값을 사용합니다.", this);
                return weaponTypeIndex;
            }

            return ResolveWeaponTypeIndex(defaultWeapon);
        }

        private int ResolveWeaponTypeIndex(WeaponDataSO weaponData)
        {
            // Animator의 WeaponType 숫자 계약은 PlayerWeaponAnimationType과 맞춰 둔다.
            // Enemy Animator Controller는 후속 작업에서 이 값을 기준으로 Handgun/RifleLike 분기를 추가할 수 있다.
            switch (weaponData.weaponCategory)
            {
                case WeaponCategory.AR:
                case WeaponCategory.SMG:
                case WeaponCategory.Sniper:
                case WeaponCategory.Shotgun:
                    return (int)PlayerWeaponAnimationType.RifleLike;

                case WeaponCategory.Handgun:
                    return (int)PlayerWeaponAnimationType.Handgun;

                case WeaponCategory.Melee:
                    Debug.LogWarning($"[EnemyAnimHandler] {weaponData.name}은 Melee 계열입니다. Enemy 총기 애니메이션 분류 대상이 아니므로 수동 WeaponType 값을 사용합니다.", this);
                    return weaponTypeIndex;

                default:
                    Debug.LogWarning($"[EnemyAnimHandler] {weaponData.name}의 무기 카테고리({weaponData.weaponCategory})를 분류할 수 없어 수동 WeaponType 값을 사용합니다.", this);
                    return weaponTypeIndex;
            }
        }

        // ───────── 히트 플래시 API ─────────

        /// <summary>
        /// 피격 시 모델을 잠깐 반짝이게 한다.
        /// EnemyStats.ApplyDamage에서 호출한다.
        /// </summary>
        public void PlayHitFlash()
        {
            // 서버에서 클라이언트로 플래시 동기화
            if (IsServer)
                PlayHitFlashClientRpc();

            // 서버 자신도 로컬 호스트면 플래시
            DoFlash();
        }

        /// <summary>
        /// 실제 플래시 코루틴을 시작한다. 서버/클라이언트 양쪽에서 호출 가능.
        /// </summary>
        private void DoFlash()
        {
            if (flashRenderers == null || flashRenderers.Length == 0) return;

            if (_flashCoroutine != null)
                StopCoroutine(_flashCoroutine);

            _flashCoroutine = StartCoroutine(FlashRoutine());
        }

        /// <summary>
        /// MaterialPropertyBlock으로 색상을 잠깐 바꿨다가 복원한다.
        /// 원본 Material을 건드리지 않으므로 인스턴스 누수가 없다.
        /// </summary>
        private IEnumerator FlashRoutine()
        {
            // 플래시 ON — 모든 렌더러에 흰색 오버레이
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                if (flashRenderers[i] == null) continue;

                flashRenderers[i].GetPropertyBlock(_propBlock);
                _propBlock.SetColor(ShaderColorID, flashColor);
                _propBlock.SetColor(ShaderEmissionID, flashColor * 2f);
                flashRenderers[i].SetPropertyBlock(_propBlock);
            }

            yield return new WaitForSeconds(flashDuration);

            // 플래시 OFF — PropertyBlock 초기화
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                if (flashRenderers[i] == null) continue;

                flashRenderers[i].GetPropertyBlock(_propBlock);
                _propBlock.Clear();
                flashRenderers[i].SetPropertyBlock(_propBlock);
            }

            _flashCoroutine = null;
        }

        // ───────── 클라이언트 동기화 ─────────

        /// <summary>모든 클라이언트에 히트 플래시를 동기화한다.</summary>
        [ClientRpc]
        private void PlayHitFlashClientRpc()
        {
            if (IsServer) return;
            DoFlash();
        }
    }
}
