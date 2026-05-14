using Unity.Collections;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Systems;

namespace DeadZone.Core
{
    public enum BodyPart : byte { Head, Torso, Limb }
    public enum EnemyTier : byte { T1 = 1, T2, T3, T4, T5 }
    public enum FacilityType : byte { Workbench, CommStation, Gym, Stash, Kitchen, Bed, Medical }
    public enum ObjectiveType : byte { Kill, Collect, Reach }
    public enum PlayerState : byte { Alive, Knocked, Dead }
    public enum ReviveResult : byte { Completed, Cancelled, Interrupted }

    public struct PlayerHpChangedEvent : IGameEvent
    {
        public ulong clientId;
        public float oldValue;
        public float newValue;
    }

    public struct PlayerStaminaChangedEvent : IGameEvent
    {
        public ulong clientId;
        public float oldValue;
        public float newValue;
    }

    public struct PlayerDiedEvent : IGameEvent
    {
        public ulong victimClientId;
        public ulong killerClientId;
    }

    public struct EnemyKilledEvent : IGameEvent
    {
        public ulong attackerClientId;
        public EnemyTier tier;
        public bool isBoss;
        public Vector3 position;
        /// <summary>[v2.1 추가] 처치된 적의 식별자 (Boss_PowerPlant, Enemy_Zone1_Any 등). QuestManager가 Kill objective 매칭에 사용.</summary>
        public FixedString64Bytes enemyId;
    }

    public struct EnemyAlertedEvent : IGameEvent
    {
        public ulong enemyNetworkObjectId;
        public ulong targetNetworkObjectId;
        public Vector3 position;
    }

    public struct CriticalHitEvent : IGameEvent
    {
        public ulong attackerClientId;
        public BodyPart zone;
        public int damage;
    }

    public struct WeaponFiredEvent : IGameEvent
    {
        public ulong shooterClientId;
        public FixedString64Bytes weaponId;
        public WeaponDataSO weaponData;
        public WeaponCategory weaponCategory;
        public int maxAmmo;
        public float maxDurability;
        public Vector3 origin;
        public float loudness;
    }

    // 장전 상태가 시작되거나 종료될 때 발행된다.
    public struct ReloadStateChangedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes weaponId;
        public FixedString64Bytes ammoId;
        public AmmoGrade grade;
        public bool isReloading;
        public float duration;
    }

    // 장전이 정상 완료되어 탄창 상태가 갱신된 뒤 발행된다.
    public struct ReloadCompletedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes weaponId;
        public FixedString64Bytes ammoId;
        public AmmoGrade grade;
        public int currentAmmo;
        public int maxAmmo;
    }

    // 장전이 실패하거나 진행 중 취소되었을 때 발행된다.
    // reason은 ReloadSystem 쪽 ReloadCancelReason 값을 byte로 전달한다.
    public struct ReloadCancelledEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes weaponId;
        public byte reason;
    }

    // 다른 시스템이 현재 장전을 중단시키고 싶을 때 발행한다.
    // reason은 ReloadSystem 쪽 ReloadCancelReason 값을 byte로 전달한다.
    public struct ReloadCancelRequestedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes weaponId;
        public byte reason;
    }

    // 현재 무기의 탄종 Grade 변경을 요청할 때 발행된다.
    public struct AmmoGradeChangeRequestedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes weaponId;
        public FixedString64Bytes targetAmmoId;
        public AmmoGrade targetGrade;
    }

    // 장전 시간이 끝난 뒤 GridInventory에 실제 장전 처리를 요청할 때 발행된다.
    public struct ReloadExecuteRequestedEvent : IGameEvent
    {
        public ulong clientId;
        public bool changeGrade;
        public AmmoGrade targetGrade;
    }

    // 무기 탄창 수량 또는 장착 탄약 ID가 변경된 뒤 발행된다.
    // reason은 EquipmentSlots 쪽 WeaponAmmoChangeReason 값을 byte로 전달한다.
    public struct WeaponAmmoChangedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes weaponId;
        public byte weaponSlot;
        public FixedString64Bytes beforeAmmoId;
        public FixedString64Bytes afterAmmoId;
        public AmmoGrade beforeGrade;
        public AmmoGrade afterGrade;
        public int beforeAmmo;
        public int afterAmmo;
        public int maxAmmo;
        public byte reason;
    }

    public struct ItemLootedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes itemId;
        public int amount;
    }

    public struct ItemAddedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes itemId;
    }

    public struct ItemRemovedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes itemId;
    }

    // ShootingSystem이 플레이어와 총구 사이의 장애물을 감지해 투사체 대신 벽 피격 FX를 출력해야 할 때 발행된다.
    public struct BlockedShotImpactEvent : IGameEvent
    {
        public ulong shooterClientId;
        public Vector3 hitPoint;
        public Vector3 hitNormal;
    }

    public struct DoorStateChangedEvent : IGameEvent
    {
        public Vector3 position;
        public bool isOpen;
    }

    // CameraCutoutTarget이 활성화되어 카메라 컷아웃 관리 대상에 등록될 때 발행된다.
    // 씬 초기화 시 Start에서 한 번 발행되고, 이후 비활성화된 오브젝트가 다시 활성화될 때 OnEnable에서 발행된다.
    public struct CameraCutoutTargetRegisteredEvent : IGameEvent
    {
        public CameraCutoutTarget target;
    }

    // CameraCutoutTarget이 비활성화되어 카메라 컷아웃 관리 대상에서 해제될 때 발행된다.
    // 등록된 대상이 OnDisable을 호출할 때 발행된다.
    public struct CameraCutoutTargetUnregisteredEvent : IGameEvent
    {
        public CameraCutoutTarget target;
    }

    // 플레이어 NetworkObject가 스폰되어 공유 시야 시스템에 플레이어 루트 Transform을 제공할 때 발행된다.
    public struct PlayerRootRegisteredEvent : IGameEvent
    {
        public Transform playerRoot;
    }

    // 플레이어 NetworkObject가 디스폰되어 공유 시야 시스템이 플레이어 루트 Transform 참조를 해제해야 할 때 발행된다.
    public struct PlayerRootUnregisteredEvent : IGameEvent
    {
        public Transform playerRoot;
    }

    // VisionMask 셰이더 제어를 받을 Renderer들이 활성화되어 등록될 때 발행된다.
    public struct VisionMaskRenderersRegisteredEvent : IGameEvent
    {
        public Renderer[] renderers;
    }

    // VisionMask 셰이더 제어를 받던 Renderer들이 비활성화되거나 제거되어 해제될 때 발행된다.
    public struct VisionMaskRenderersUnregisteredEvent : IGameEvent
    {
        public Renderer[] renderers;
    }

    // 로컬 Owner 플레이어가 스폰되어 로컬 카메라/컷아웃 시스템에 플레이어 루트 Transform을 제공할 때 발행된다.
    public struct OwnerPlayerRootRegisteredEvent : IGameEvent
    {
        public Transform playerRoot;
    }

    // 로컬 Owner 플레이어가 디스폰되어 로컬 카메라/컷아웃 시스템이 플레이어 루트 Transform 참조를 해제해야 할 때 발행된다.
    public struct OwnerPlayerRootUnregisteredEvent : IGameEvent
    {
        public Transform playerRoot;
    }

    // 로컬 Owner 플레이어 카메라가 활성화되어 로컬 카메라/컷아웃 시스템에 Camera 참조를 제공할 때 발행된다.
    public struct OwnerPlayerCameraRegisteredEvent : IGameEvent
    {
        public Camera playerCamera;
    }

    // 로컬 Owner 플레이어 카메라가 비활성화되거나 디스폰되어 로컬 카메라/컷아웃 시스템이 Camera 참조를 해제해야 할 때 발행된다.
    public struct OwnerPlayerCameraUnregisteredEvent : IGameEvent
    {
        public Camera playerCamera;
    }

    public struct ZoneEnteredEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes zoneId;
    }

    public struct QuestAcceptedEvent : IGameEvent
    {
        public FixedString64Bytes questId;
        /// <summary>[v2.1 추가] 퀘스트를 수락한 플레이어.</summary>
        public ulong clientId;
    }

    public struct QuestProgressEvent : IGameEvent
    {
        public FixedString64Bytes questId;
        public ObjectiveType objectiveType;
        public int currentCount;
        public int requiredCount;
        /// <summary>[v2.1 추가] 진행 보고한 플레이어.</summary>
        public ulong clientId;
        /// <summary>[v2.1 추가] 진행된 objective의 targetID.</summary>
        public FixedString64Bytes targetId;
    }

    /// <summary>QuestManager가 저장된 퀘스트 상태를 복원한 뒤, HUD가 현재 표시해야 할 퀘스트 진행도를 초기화할 때 발행된다.</summary>
    public struct QuestTrackerSnapshotEvent : IGameEvent
    {
        public FixedString64Bytes questId;
        public ObjectiveType objectiveType;
        public int currentCount;
        public int requiredCount;
        public ulong clientId;
        public FixedString64Bytes targetId;
        public bool isPendingCompletion;
    }

    public struct QuestCompletedEvent : IGameEvent
    {
        public FixedString64Bytes questId;
        /// <summary>[v2.1 추가] 퀘스트를 완료한 플레이어.</summary>
        public ulong clientId;
        /// <summary>[v2.1 추가] 완료 시 해금되는 구역 ID (ZoneUnlockSystem 구독용).</summary>
        public FixedString64Bytes unlockZoneId;
    }

    public struct ExtractionStartedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes extractionId;
        public float countdownSeconds;
    }

    public struct ExtractionCompletedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes extractionId;
    }

    public struct FacilityUpgradedEvent : IGameEvent
    {
        public FacilityType facilityType;
        public int newLevel;
    }

    public struct CreditsChangedEvent : IGameEvent
    {
        public ulong clientId;
        public int delta;
        public int newBalance;
    }

    public struct SceneChangedEvent : IGameEvent
    {
        public FixedString64Bytes sceneName;
    }

    public struct PlayerStateChangedEvent : IGameEvent
    {
        public ulong clientId;
        public PlayerState oldState;
        public PlayerState newState;
    }

    public struct PlayerKnockedEvent : IGameEvent
    {
        public ulong victimClientId;
        public ulong attackerClientId;
        public Vector3 position;
        public float bleedoutSeconds;
    }

    public struct ReviveStartedEvent : IGameEvent
    {
        public ulong reviverClientId;
        public ulong targetClientId;
        public float duration;
    }

    public struct ReviveProgressEvent : IGameEvent
    {
        public ulong reviverClientId;
        public ulong targetClientId;
        public float progress01;
    }

    public struct ReviveEndedEvent : IGameEvent
    {
        public ulong reviverClientId;
        public ulong targetClientId;
        public ReviveResult result;
    }

    public struct PlayerKnockedHpChangedEvent : IGameEvent
    {
        public ulong clientId;
        public float oldValue;
        public float newValue;
    }

    public struct CorpseSpawnedEvent : IGameEvent
    {
        public ulong ownerClientId;
        public Vector3 position;
    }

    public struct CorpseLootedEvent : IGameEvent
    {
        public ulong looterClientId;
        public ulong corpseOwnerClientId;
        public FixedString64Bytes itemId;
    }

    public struct SpectatorTargetChangedEvent : IGameEvent
    {
        public ulong spectatorClientId;
        public ulong newTargetClientId;
    }

    //테스트
    public struct FireInputEvent : IGameEvent {}
    public struct AuthSignedInEvent : IGameEvent
    {
        public FixedString64Bytes firebaseUid;
        public FixedString128Bytes email;
    }

    public struct AuthSignedOutEvent : IGameEvent
    {
        public FixedString64Bytes firebaseUid;  // 로그아웃한 유저의 기존 UID
    }

    public struct CloudSaveLoadedEvent : IGameEvent
    {
        public FixedString64Bytes firebaseUid;
        public bool isNewUser;  // Firestore에 문서가 없어서 기본값으로 시작한 경우 true
    }

    public struct CloudSaveUploadedEvent : IGameEvent
    {
        public FixedString64Bytes firebaseUid;
        public bool success;
    }

    public struct RelayAllocationCreatedEvent : IGameEvent
    {
        // 호스트 전용. 이 이벤트 수신 시 UI에 JoinCode 표시.
        public FixedString32Bytes joinCode;   // Relay JoinCode는 6자리 영숫자
        public int maxConnections;
    }

    public struct RelayJoinedEvent : IGameEvent
    {
        // 클라이언트 전용. 접속한 Relay의 JoinCode를 에코백.
        public FixedString32Bytes joinCode;
    }
}
