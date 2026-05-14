using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적 체력, 방어구 내구도, 사망 처리와 시체 스폰을 담당하는 서버 권위 스탯 컴포넌트입니다.
    /// </summary>
    public class EnemyStats : NetworkBehaviour, IDamageable, IArmored
    {
        [Header("스탯 데이터")]
        [Tooltip("적 체력, 방어구, 무기, 시체 드랍 설정을 담은 스탯 SO입니다.")]
        [SerializeField] private EnemyStatsSO statsSO;

        [Header("시체")]
        [Tooltip("EnemyStatsSO.corpsePrefab이 비어 있을 때 사용할 범용 시체 프리팹입니다.")]
        [SerializeField] private GameObject fallbackCorpsePrefab;

        [Header("네트워크 상태")]
        [Tooltip("현재 적 체력입니다.")]
        public NetworkVariable<float> CurrentHP = new(80f);

        [Tooltip("현재 적 방어구 내구도입니다.")]
        public NetworkVariable<float> CurrentArmorDurability = new(0f);

        /// <summary>
        /// 이 적에게 적용된 스탯 SO입니다.
        /// </summary>
        public EnemyStatsSO StatsSO => statsSO;

        /// <summary>
        /// 현재 체력이 0 이하인지 여부입니다.
        /// </summary>
        public bool IsDead => CurrentHP.Value <= 0;

        /// <summary>
        /// IDamageable 호환용 플레이어 여부입니다. 적은 항상 false입니다.
        /// </summary>
        public bool IsPlayer => false;

        /// <summary>
        /// 네트워크 스폰 시 서버에서 체력과 방어구 내구도를 SO 기준으로 초기화합니다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (IsServer && statsSO != null)
            {
                CurrentHP.Value = statsSO.maxHP;
                CurrentArmorDurability.Value = statsSO.defaultArmor != null
                    ? statsSO.defaultArmor.maxDurability
                    : 0f;
            }
        }

        /// <summary>
        /// HitInfo가 포함된 피해를 적에게 적용합니다.
        /// </summary>
        /// <param name="damage">적용할 피해량입니다.</param>
        /// <param name="attackerClientId">공격자 클라이언트 ID입니다.</param>
        /// <param name="hit">피격 부위 정보입니다.</param>
        public void ApplyDamage(int damage, ulong attackerClientId, HitInfo hit)
        {
            if (!IsServer || IsDead) return;
            CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);
            
            GetComponent<EnemyAnimHandler>()?.PlayHitFlash();
            GetComponent<EnemyAnimHandler>()?.TriggerHit();

            if (CurrentHP.Value <= 0f)
                Die(attackerClientId);
        }

        /// <summary>
        /// 월드 좌표 기반 피해를 적에게 적용합니다.
        /// </summary>
        /// <param name="damage">적용할 피해량입니다.</param>
        /// <param name="attackerClientId">공격자 클라이언트 ID입니다.</param>
        /// <param name="hit">피격 위치입니다.</param>
        public void ApplyDamage(int damage, ulong attackerClientId, Vector3 hit)
        {
            if (!IsServer || IsDead) return;
            CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);
            
            GetComponent<EnemyAnimHandler>()?.PlayHitFlash();
            GetComponent<EnemyAnimHandler>()?.TriggerHit();

            if (CurrentHP.Value <= 0f)
                Die(attackerClientId);
        }

        private void Die(ulong attackerClientId)
        {
            // 1. EnemyKilledEvent 발행 (QuestManager, KillFeedUI 등이 구독)
            EventBus.Publish(new EnemyKilledEvent
            {
                attackerClientId = attackerClientId,
                tier = statsSO != null ? statsSO.tier : EnemyTier.T1,
                isBoss = statsSO != null && statsSO.isBoss,
                position = transform.position,
                enemyId = statsSO != null && !string.IsNullOrEmpty(statsSO.enemyId)
                    ? new FixedString64Bytes(statsSO.enemyId)
                    : default,
            });

            // 2. 시체 스폰
            SpawnCorpse();

            // 3. 원본 적 오브젝트 제거
            DespawnEnemyObject();
        }

        private void DespawnEnemyObject()
        {
            if (NetworkObject == null)
            {
                Destroy(gameObject);
                return;
            }

            if (!NetworkObject.IsSpawned)
            {
                Destroy(gameObject);
                return;
            }

            bool isSceneObject = NetworkObject.IsSceneObject.HasValue && NetworkObject.IsSceneObject.Value;
            NetworkObject.Despawn(destroy: !isSceneObject);

            if (isSceneObject)
            {
                gameObject.SetActive(false);
            }
        }

        private void SpawnCorpse()
        {
            if (statsSO == null) return;

            // 시체 프리팹 결정: SO 지정 > 범용 폴백
            GameObject prefab = statsSO.corpsePrefab != null
                ? statsSO.corpsePrefab
                : fallbackCorpsePrefab;

            if (prefab != null && prefab.GetComponent<NetworkObject>() == null)
            {
                if (fallbackCorpsePrefab != null && fallbackCorpsePrefab.GetComponent<NetworkObject>() != null)
                {
                    Debug.LogWarning($"[EnemyStats] Corpse prefab '{prefab.name}' is missing NetworkObject. Using fallback corpse prefab '{fallbackCorpsePrefab.name}'.", this);
                    prefab = fallbackCorpsePrefab;
                }
                else
                {
                    Debug.LogWarning($"[EnemyStats] Corpse prefab '{prefab.name}' is missing NetworkObject. Corpse spawn skipped.", this);
                    return;
                }
            }

            if (prefab == null)
            {
                Debug.LogWarning($"[EnemyStats] {statsSO.displayName}: 시체 프리팹 없음 — 시체 스킵");
                return;
            }

            // 사망 위치/회전에 시체 스폰
            var corpseObj = Instantiate(prefab, transform.position, transform.rotation);
            var netObj = corpseObj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogWarning($"[EnemyStats] Corpse prefab is missing NetworkObject after instantiate: {prefab.name}", this);
                Destroy(corpseObj);
                return;
            }

            netObj.Spawn();

            // 시체 초기화 (장비 + 루팅 아이템 채우기)
            var enemyCorpse = corpseObj.GetComponent<EnemyCorpse>();
            if (enemyCorpse != null)
            {
                enemyCorpse.InitializeServer(statsSO, this);
            }
        }

        // ───────── IArmored ─────────

        /// <summary>
        /// 적이 장착한 헬멧 데이터를 반환합니다. 현재 적은 헬멧을 사용하지 않습니다.
        /// </summary>
        /// <returns>항상 null입니다.</returns>
        public HelmetDataSO GetEquippedHelmet() => null;

        /// <summary>
        /// 적이 장착한 방어구 데이터를 반환합니다.
        /// </summary>
        /// <returns>장착 방어구 SO입니다.</returns>
        public ArmorDataSO GetEquippedArmor() => statsSO != null ? statsSO.defaultArmor : null;

        /// <summary>
        /// 현재 헬멧 내구도를 반환합니다. 현재 적은 헬멧을 사용하지 않습니다.
        /// </summary>
        /// <returns>항상 0입니다.</returns>
        public float GetHelmetDurability() => 0f;

        /// <summary>
        /// 현재 방어구 내구도를 반환합니다.
        /// </summary>
        /// <returns>현재 방어구 내구도입니다.</returns>
        public float GetArmorDurability() => CurrentArmorDurability.Value;

        /// <summary>
        /// 적 헬멧 내구도를 감소시킵니다. 현재 적은 헬멧을 사용하지 않아 동작하지 않습니다.
        /// </summary>
        /// <param name="amount">감소시킬 내구도입니다.</param>
        public void DamageHelmetDurability(float amount) { }

        /// <summary>
        /// 적 방어구 내구도를 감소시킵니다.
        /// </summary>
        /// <param name="amount">감소시킬 내구도입니다.</param>
        public void DamageArmorDurability(float amount)
        {
            if (!IsServer) return;
            CurrentArmorDurability.Value = Mathf.Max(0f, CurrentArmorDurability.Value - amount);
        }
    }
}
