using Unity.Collections;
using UnityEngine;

using DeadZone.Systems;

namespace DeadZone.Core
{
    public enum BodyPart : byte { Head, Torso, Limb }
    public enum EnemyTier : byte { T1 = 1, T2, T3, T4, T5 }
    public enum FacilityType : byte { Workbench, CommStation, Gym, Stash, Kitchen, Bed }
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
        public Vector3 origin;
        public float loudness;
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

    public struct DoorStateChangedEvent : IGameEvent
    {
        public Vector3 position;
        public bool isOpen;
    }

    public struct ZoneEnteredEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes zoneId;
    }

    public struct QuestAcceptedEvent : IGameEvent
    {
        public FixedString64Bytes questId;
    }

    public struct QuestProgressEvent : IGameEvent
    {
        public FixedString64Bytes questId;
        public ObjectiveType objectiveType;
        public int currentCount;
        public int requiredCount;
    }

    public struct QuestCompletedEvent : IGameEvent
    {
        public FixedString64Bytes questId;
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


    // =====================================================================
    // Firebase / Relay 이벤트 (v1.3 추가, Part VII Addendum)
    //
    // 주의: Firebase/Relay 이벤트 페이로드는 string(uid, email, joinCode)를
    // 포함해야 하는데 struct에 string 필드를 넣으면 매번 allocation이 발생
    // (GC 부담). 발생 빈도가 매우 낮으므로(로그인 1회, 방 생성 1회 등)
    // 여기서는 단순성을 위해 FixedString 사용. 길이 제한에 주의.
    //   - FixedString64Bytes  : UID 28자(Firebase) + 여유 → OK
    //   - FixedString128Bytes : email 최대 254자 RFC 기준에는 부족하지만
    //                           실제 사용 이메일은 대부분 128자 미만이라 타협
    // =====================================================================

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
