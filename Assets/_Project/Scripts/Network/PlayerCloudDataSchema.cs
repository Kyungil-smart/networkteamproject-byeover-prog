using System.Collections.Generic;
using Firebase.Firestore;


namespace DeadZone.Network
{
    /// <summary>
    /// Firestore users/{uid} 문서에 저장되는 개인 데이터 스키마.
    /// Firebase Firestore SDK가 [FirestoreData] 속성을 읽어 자동 직렬화한다.
    ///
    /// 주의:
    ///  - 클래스는 public 이어야 하고, 파라미터 없는 public 생성자 필요
    ///  - [FirestoreProperty] 이름이 Firestore 문서의 필드명이 된다
    ///  - 변경 시 기존 유저 데이터 호환 주의 (schemaVersion 으로 마이그레이션)
    /// </summary>
    [FirestoreData]
    public class PlayerCloudData
    {
        [FirestoreProperty] public ProfileData profile { get; set; } = new ProfileData();
        [FirestoreProperty] public ProgressData progress { get; set; } = new ProgressData();
        [FirestoreProperty] public StashData stash { get; set; } = new StashData();
        [FirestoreProperty] public SafePocketData safePocket { get; set; } = new SafePocketData();
        [FirestoreProperty] public EquipmentData equipment { get; set; } = new EquipmentData();
        [FirestoreProperty] public FacilitiesData facilities { get; set; } = new FacilitiesData();
        [FirestoreProperty] public List<InsuranceEntry> insurance { get; set; } = new List<InsuranceEntry>();

        /// <summary>스키마 버전. 향후 마이그레이션 대비.</summary>
        [FirestoreProperty] public int schemaVersion { get; set; } = 1;
    }

    [FirestoreData]
    public class ProfileData
    {
        [FirestoreProperty] public string email { get; set; } = "";
        [FirestoreProperty] public string displayName { get; set; } = "";
        [FirestoreProperty] public long createdAtUnix { get; set; }      // Unix timestamp (초)
        [FirestoreProperty] public long lastPlayedAtUnix { get; set; }
        [FirestoreProperty] public long totalPlayTimeSec { get; set; }
    }

    [FirestoreData]
    public class ProgressData
    {
        [FirestoreProperty] public int credits { get; set; }
        [FirestoreProperty] public List<string> personalActiveQuestIds { get; set; } = new List<string>();
        [FirestoreProperty] public List<string> personalCompletedQuestIds { get; set; } = new List<string>();
        [FirestoreProperty] public List<string> unlockedZones { get; set; } = new List<string>();
    }

    [FirestoreData]
    public class StashSlot
    {
        [FirestoreProperty] public string itemId { get; set; } = "";
        [FirestoreProperty] public int stackCount { get; set; }
        [FirestoreProperty] public int gridX { get; set; }
        [FirestoreProperty] public int gridY { get; set; }
        [FirestoreProperty] public bool rotated { get; set; }
        [FirestoreProperty] public int currentDurability { get; set; }
        [FirestoreProperty] public int currentAmmo { get; set; }
    }

    [FirestoreData]
    public class StashData
    {
        [FirestoreProperty] public List<StashSlot> slots { get; set; } = new List<StashSlot>();
    }

    [FirestoreData]
    public class SafePocketSlot
    {
        [FirestoreProperty] public string itemId { get; set; } = "";
        [FirestoreProperty] public int stackCount { get; set; }
    }

    [FirestoreData]
    public class SafePocketData
    {
        [FirestoreProperty] public List<SafePocketSlot> slots { get; set; } = new List<SafePocketSlot>();
    }

    [FirestoreData]
    public class EquipmentData
    {
        [FirestoreProperty] public string helmetId { get; set; } = "";
        [FirestoreProperty] public string armorId { get; set; } = "";
        [FirestoreProperty] public string primary1 { get; set; } = "";
        [FirestoreProperty] public string primary2 { get; set; } = "";
        [FirestoreProperty] public string secondary { get; set; } = "";
        [FirestoreProperty] public string melee { get; set; } = "";
        [FirestoreProperty] public float helmetDurability { get; set; }
        [FirestoreProperty] public float armorDurability { get; set; }
    }

    [FirestoreData]
    public class FacilitiesData
    {
        // 개인 은신처 6시설 레벨 (0~4). 팀장 결정: 각자 자기 하우징을 가짐.
        [FirestoreProperty] public int workbench { get; set; } = 1;
        [FirestoreProperty] public int commStation { get; set; } = 1;
        [FirestoreProperty] public int gym { get; set; } = 1;
        [FirestoreProperty] public int stash { get; set; } = 1;
        [FirestoreProperty] public int kitchen { get; set; } = 1;
        [FirestoreProperty] public int bed { get; set; } = 1;
    }

    [FirestoreData]
    public class InsuranceEntry
    {
        [FirestoreProperty] public string itemId { get; set; } = "";
        [FirestoreProperty] public long returnAtUnix { get; set; }
        [FirestoreProperty] public int stackCount { get; set; }
        [FirestoreProperty] public int currentDurability { get; set; }
    }


    // =====================================================================
    // sessions/{hostUid} 문서 - 호스트 방의 공유 데이터
    // 팀장 결정: 퀘스트는 "둘 다: 개인+공유 플래그 별도"
    //   - 개인 퀘스트 진행도 → PlayerCloudData.progress.personalXxx
    //   - 공유 퀘스트 플래그 → SessionCloudData.sharedXxx (아래)
    // =====================================================================

    [FirestoreData]
    public class SessionCloudData
    {
        [FirestoreProperty] public string hostUid { get; set; } = "";
        [FirestoreProperty] public long lastPlayedAtUnix { get; set; }
        [FirestoreProperty] public List<string> memberUids { get; set; } = new List<string>();

        /// <summary>이 방에서 4인이 공유로 수주한 퀘스트 ID.</summary>
        [FirestoreProperty] public List<string> sharedActiveQuestIds { get; set; } = new List<string>();
        [FirestoreProperty] public List<string> sharedCompletedQuestIds { get; set; } = new List<string>();

        [FirestoreProperty] public int schemaVersion { get; set; } = 1;
    }
}
