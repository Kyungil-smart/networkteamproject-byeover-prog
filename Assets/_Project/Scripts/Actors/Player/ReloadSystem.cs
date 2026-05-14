using System.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Audio;

namespace DeadZone.Actors
{
    public enum ReloadCancelReason : byte
    {
        // 취소 사유가 지정되지 않았을 때 사용한다.
        None,

        // 현재 장착된 무기가 없어 장전을 시작할 수 없을 때 사용한다.
        NoWeapon,

        // 현재 탄창이 이미 가득 차 있어 장전할 필요가 없을 때 사용한다.
        FullMagazine,

        // 인벤토리에 장전에 사용할 탄약이 없을 때 사용한다.
        NoAmmo,

        // 무기 구경, 탄약 구경, 또는 탄약 등급이 맞지 않을 때 사용한다.
        AmmoMismatch,

        // 이미 장전 중인 상태에서 장전을 다시 요청했을 때 사용한다.
        AlreadyReloading,

        // 외부 상태 변화로 장전이 중단되었지만 세부 사유를 구분하지 않을 때 사용한다.
        Interrupted,

        // 무기 교체로 인해 현재 장전을 취소해야 할 때 사용한다.
        WeaponSwitched,

        // 플레이어 기절, 사망 등 상태 전환으로 장전을 취소해야 할 때 사용한다.
        PlayerStateChanged,

        // UI 버튼이나 입력으로 플레이어가 직접 장전 취소를 요청했을 때 사용한다.
        ButtonRequested
    }

    public class ReloadSystem : NetworkBehaviour
    {
        [SerializeField] private float defaultReloadTime = 2.5f; // 기본 장전 시간이다.

        private Coroutine reloadRoutine; // 현재 진행 중인 장전 코루틴이다.
        private bool isReloading; // 현재 장전 진행 중인지 나타낸다.
        private bool pendingChangeGrade; // 이번 장전이 탄종 등급 변경 장전인지 나타낸다.
        private AmmoGrade pendingTargetGrade; // 탄종 등급 변경 장전일 때 목표 탄약 등급이다.
        private EquipmentSlots equipment;
        private PlayerAnimatorDriver animatorDriver;

        public bool IsReloading => isReloading;

        private void Awake()
        {
            equipment = GetComponent<EquipmentSlots>();
            animatorDriver = GetComponent<PlayerAnimatorDriver>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            EventBus.Subscribe<ReloadCancelRequestedEvent>(OnReloadCancelRequested);
            EventBus.Subscribe<AmmoGradeChangeRequestedEvent>(OnAmmoGradeChangeRequested);
        }

        public override void OnNetworkDespawn()
        {
            EventBus.Unsubscribe<ReloadCancelRequestedEvent>(OnReloadCancelRequested);
            EventBus.Unsubscribe<AmmoGradeChangeRequestedEvent>(OnAmmoGradeChangeRequested);

            StopReloadRoutineIfNeeded();
            animatorDriver?.SetReloadingAnimation(false);

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// 일반 장전 입력을 서버 요청으로 변환한다.
        /// 현재 탄약 등급으로 장전하며, 현재 장착 탄약이 없을 때의 기본 LP 장전 판단은 GridInventory가 처리한다.
        /// </summary>
        public void TryReload()
        {
            if (!IsOwner) return;
            RequestReloadServerRpc(false, default);
        }

        /// <summary>
        /// 탄종 등급 변경 버튼이 발행한 요청을 서버 장전 요청으로 변환한다.
        /// ReloadSystem은 목표 등급만 보관하고, 실제 탄약 탐색과 탄창 반영은 장전 시간이 끝난 뒤 GridInventory가 처리한다.
        /// </summary>
        private void OnAmmoGradeChangeRequested(AmmoGradeChangeRequestedEvent e)
        {
            if (!IsOwner || e.clientId != OwnerClientId) return;
            RequestReloadServerRpc(true, e.targetGrade);
        }

        /// <summary>
        /// 외부 시스템이 발행한 장전 취소 요청을 서버 취소 요청으로 변환한다.
        /// 무기 교체, 플레이어 상태 전환, 취소 버튼처럼 ReloadSystem 밖에서 시작된 중단 요청이 이 경로로 들어온다.
        /// </summary>
        private void OnReloadCancelRequested(ReloadCancelRequestedEvent e)
        {
            if (!IsOwner || e.clientId != OwnerClientId) return;
            CancelReloadServerRpc(e.reason);
        }

        /// <summary>
        /// 서버에서 장전 시작을 확정하고 장전 시간을 진행한다.
        /// 중복 장전, 무기 없음, 탄창 가득 참은 시작 전에 차단하고,
        /// 인벤토리 탄약 탐색과 탄창 반영은 기존처럼 GridInventory가 처리한다.
        /// </summary>
        [ServerRpc]
        private void RequestReloadServerRpc(
            bool changeGrade,
            AmmoGrade targetGrade,
            ServerRpcParams rpc = default)
        {
            if (isReloading)
            {
                PublishReloadCancelled(ReloadCancelReason.AlreadyReloading);
                return;
            }

            if (!CanStartReloadRequest(changeGrade, out ReloadCancelReason failureReason))
            {
                PublishReloadCancelled(failureReason);
                return;
            }

            pendingChangeGrade = changeGrade;
            pendingTargetGrade = targetGrade;
            StartReload();
        }

        /// <summary>
        /// 서버에서 진행 중인 장전을 취소한다.
        /// 장전 코루틴을 멈추고 장전 상태 종료 이벤트와 취소 이벤트를 발행한다.
        /// </summary>
        [ServerRpc]
        private void CancelReloadServerRpc(byte reason, ServerRpcParams rpc = default)
        {
            if (!isReloading) return;
            CancelReload((ReloadCancelReason)reason);
        }

        /// <summary>
        /// 장전 상태를 시작하고 장전 시간 코루틴을 실행한다.
        /// 이 함수 이후 실제 탄약 계산은 하지 않고, 시간이 끝날 때 GridInventory에 실행 이벤트만 보낸다.
        /// </summary>
        private void StartReload()
        {
            AudioCueId reloadCueId = GetCurrentReloadCueId();
            if (reloadCueId != AudioCueId.None)
                PlayReloadAudioClientRpc(reloadCueId, transform.position);

            SetReloading(true);
            reloadRoutine = StartCoroutine(ReloadRoutine());
        }

        /// <summary>
        /// 장전 시간을 기다린 뒤 실제 장전 실행 이벤트를 발행한다.
        /// 일반 장전과 탄종 등급 변경 장전 여부만 전달하고, 현재 무기/탄약/인벤토리 판단은 GridInventory가 수행한다.
        /// </summary>
        private IEnumerator ReloadRoutine()
        {
            yield return new WaitForSeconds(defaultReloadTime);

            EventBus.Publish(new ReloadExecuteRequestedEvent
            {
                clientId = OwnerClientId,
                changeGrade = pendingChangeGrade,
                targetGrade = pendingTargetGrade
            });

            SetReloading(false);
            ClearPendingReload();
        }

        /// <summary>
        /// 진행 중인 장전을 중단하고 취소 이벤트를 발행한다.
        /// 코루틴 대기 중인 장전만 취소 대상으로 보며, 실제 탄약 처리가 시작된 뒤의 결과는 GridInventory가 판단한다.
        /// </summary>
        private void CancelReload(ReloadCancelReason reason)
        {
            StopReloadRoutineIfNeeded();
            SetReloading(false);
            ClearPendingReload();
            PublishReloadCancelled(reason);
        }

        /// <summary>
        /// 서버에서 장전을 시작해도 되는 최소 조건을 확인한다.
        /// 탄약 보유 여부는 현재 GridInventory의 장전 실행 단계가 담당하므로 여기서 중복 탐색하지 않는다.
        /// </summary>
        private bool CanStartReloadRequest(bool changeGrade, out ReloadCancelReason failureReason)
        {
            failureReason = ReloadCancelReason.None;

            if (equipment == null)
                equipment = GetComponent<EquipmentSlots>();

            if (equipment == null)
            {
                failureReason = ReloadCancelReason.Interrupted;
                return false;
            }

            WeaponDataSO weapon = equipment.GetCurrentWeapon();
            if (weapon == null)
            {
                failureReason = ReloadCancelReason.NoWeapon;
                return false;
            }

            if (!changeGrade)
            {
                WeaponState weaponState = equipment.CurrentWeaponState;
                if (weaponState.currentAmmo >= weapon.magSize)
                {
                    failureReason = ReloadCancelReason.FullMagazine;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// isReloading 값을 변경하고 ReloadStateChangedEvent와 Animator 동기화 ClientRpc를 발행한다.
        /// 장전 상태 변경 이벤트의 단일 출구로 사용해 UI, 입력 차단, 발사 차단, 표시 애니메이션이 같은 신호를 받게 한다.
        /// </summary>
        private void SetReloading(bool value)
        {
            if (isReloading == value) return;

            isReloading = value;
            EventBus.Publish(new ReloadStateChangedEvent
            {
                clientId = OwnerClientId,
                weaponId = default,
                ammoId = default,
                grade = pendingTargetGrade,
                isReloading = value,
                duration = value ? defaultReloadTime : 0f
            });

            if (IsServer && IsSpawned)
                SetReloadingAnimationClientRpc(value);
        }

        /// <summary>
        /// 서버에서 확정한 재장전 표시 상태를 모든 클라이언트의 해당 Player Animator에 반영한다.
        /// Host도 자신의 화면에서 애니메이션을 봐야 하므로 서버 클라이언트를 제외하지 않는다.
        /// </summary>
        [ClientRpc]
        private void SetReloadingAnimationClientRpc(bool value)
        {
            isReloading = value;

            if (animatorDriver == null)
                animatorDriver = GetComponent<PlayerAnimatorDriver>();

            animatorDriver?.SetReloadingAnimation(value);
        }

        /// <summary>
        /// 진행 중인 장전 코루틴이 있으면 중단하고 참조를 비운다.
        /// 장전 취소와 네트워크 해제 시 코루틴이 남아 다음 요청에 영향을 주지 않게 한다.
        /// </summary>
        private void StopReloadRoutineIfNeeded()
        {
            if (reloadRoutine == null) return;

            StopCoroutine(reloadRoutine);
            reloadRoutine = null;
        }

        /// <summary>
        /// 이번 장전 요청에서 저장한 임시 정보를 초기화한다.
        /// 다음 장전이 이전 탄종 변경 요청 값을 이어받지 않도록 완료와 취소 시 호출한다.
        /// </summary>
        private void ClearPendingReload()
        {
            reloadRoutine = null;
            pendingChangeGrade = false;
            pendingTargetGrade = default;
        }

        /// <summary>
        /// 장전 시작 실패나 중간 취소 결과를 알린다.
        /// 실제 탄약 처리 단계의 실패는 GridInventory가 같은 이벤트로 발행한다.
        /// </summary>
        private void PublishReloadCancelled(ReloadCancelReason reason)
        {
            EventBus.Publish(new ReloadCancelledEvent
            {
                clientId = OwnerClientId,
                weaponId = default,
                reason = (byte)reason
            });
        }

        [ClientRpc]
        private void PlayReloadAudioClientRpc(AudioCueId cueId, Vector3 position)
        {
            EventBus.Publish(new AudioPlayRequestedEvent
            {
                cueId = cueId,
                position = position,
                use3D = true,
                volumeMultiplier = 1f
            });
        }

        private AudioCueId GetCurrentReloadCueId()
        {
            if (equipment == null)
                equipment = GetComponent<EquipmentSlots>();

            WeaponDataSO weapon = equipment != null ? equipment.GetCurrentWeapon() : null;
            if (weapon == null)
                return AudioCueId.None;

            return weapon.weaponCategory switch
            {
                WeaponCategory.AR => AudioCueId.ARReload,
                WeaponCategory.SMG => AudioCueId.SMGReload,
                WeaponCategory.Handgun => AudioCueId.HGReload,
                WeaponCategory.Sniper => AudioCueId.SRDragReload,
                WeaponCategory.Shotgun => AudioCueId.ShotgunReload,
                _ => AudioCueId.None,
            };
        }
    }
}
