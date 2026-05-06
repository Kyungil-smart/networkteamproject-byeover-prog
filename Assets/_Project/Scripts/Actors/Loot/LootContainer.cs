using System.Collections.Generic;      // List<T> 컬렉션 사용
using Unity.Collections;              // Netcode용 컬렉션 타입 사용 가능
using Unity.Netcode;                  // NGO(Network for GameObjects)
using UnityEngine;                    // Unity 기본 기능

using DeadZone.Core;                  // 프로젝트 내부 Core 네임스페이스
using DeadZone.Systems;               // 프로젝트 내부 Systems 네임스페이스

namespace DeadZone.Actors
{
    public class LootContainer : NetworkBehaviour, IInteractable
    {
        // =========================================================
        // 상자 기본 데이터
        // =========================================================

        [Header("상자 기본 데이터")]

        [Tooltip("이 상자에서 나올 아이템 후보 테이블")]
        [SerializeField]
        private LootTableSO lootTable;

        [Tooltip("Console 로그에 표시할 상자 등급 이름")]
        [SerializeField]
        private string containerGradeName = "Common";

        // =========================================================
        // 슬롯 / 랜덤 생성 설정
        // =========================================================

        [Header("슬롯/랜덤 생성")]

        [Tooltip("상자 내부 슬롯 수")]
        [SerializeField]
        private int slotCount = 6;

        [Tooltip("처음 열 때 랜덤 생성할 아이템 개수")]
        [SerializeField]
        private int rollCount = 6;

        [Tooltip("weight 총합이 100이 아니면 생성 막기")]
        [SerializeField]
        private bool requireTotalWeight100 = true;

        // =========================================================
        // 총기 상자 규칙
        // =========================================================

        [Header("총기 상자 규칙")]

        [Tooltip("총기 상자 모드 여부")]
        [SerializeField]
        private bool weaponBoxMode = false;

        [Tooltip("최소 무기 개수")]
        [SerializeField]
        private int minWeaponCount = 1;

        [Tooltip("최대 무기 개수")]
        [SerializeField]
        private int maxWeaponCount = 2;

        // =========================================================
        // 디버그 설정
        // =========================================================

        [Header("Console 디버그")]

        [Tooltip("상자 열었을 때 Console 출력")]
        [SerializeField]
        private bool printDebugLogOnOpen = true;

        [Tooltip("Console에 표시할 상자 이름")]
        [SerializeField]
        private string debugContainerName = "";

        // =========================================================
        // 상호작용 문구
        // =========================================================

        [Header("상호작용 문구")]

        [Tooltip("닫힌 상자 문구")]
        [SerializeField]
        private string closedPrompt = "[F] 파밍 상자 열기";

        [Tooltip("열린 상자 문구")]
        [SerializeField]
        private string openedPrompt = "[F] 파밍 상자 확인";
        
        public NetworkVariable<bool> IsOpened =
            new NetworkVariable<bool>(false);
        public NetworkList<global::ContainerSlotNetData> Slots;
        private bool localOpened;
        private List<global::ContainerSlotNetData> localSlots;

        // =========================================================
        // 프로퍼티
        // =========================================================

        /// <summary>
        /// 외부에서 LootTable 읽기 가능
        /// </summary>
        public LootTableSO LootTable
        {
            get
            {
                return lootTable;
            }
        }

        // =========================================================
        // Awake
        // =========================================================

        /// <summary>
        /// 객체 생성 시 실행
        ///
        /// NetworkList 초기화
        /// </summary>
        private void Awake()
        {
            Slots = new NetworkList<global::ContainerSlotNetData>(
                values: null,

                // 모든 클라이언트 읽기 가능
                readPerm: NetworkVariableReadPermission.Everyone,

                // 서버만 수정 가능
                writePerm: NetworkVariableWritePermission.Server);
        }

        // =========================================================
        // OnValidate
        // =========================================================

        /// <summary>
        /// Inspector 값 수정 시 자동 실행
        ///
        /// 잘못된 값 제한
        /// </summary>
        private void OnValidate()
        {
            // 슬롯 최소 1칸
            slotCount = Mathf.Max(1, slotCount);

            // rollCount 제한
            rollCount = Mathf.Clamp(rollCount, 1, slotCount);

            // 최소 무기 개수 제한
            minWeaponCount = Mathf.Clamp(minWeaponCount, 0, rollCount);

            // 최대 무기 개수 제한
            maxWeaponCount = Mathf.Clamp(
                maxWeaponCount,
                minWeaponCount,
                rollCount);
        }

        // =========================================================
        // OnNetworkSpawn
        // =========================================================

        /// <summary>
        /// NetworkObject가 Spawn될 때 실행
        /// </summary>
        public override void OnNetworkSpawn()
        {
            // 서버만 실행
            if (IsServer)
            {
                // 빈 슬롯 생성
                EnsureEmptySlots();
            }
        }

        // =========================================================
        // 상호작용 문구 반환
        // =========================================================

        /// <summary>
        /// UI에 표시할 상호작용 문구
        /// </summary>
        public string GetPromptText()
        {
            // 네트워크 활성 상태
            if (IsNetworkActive())
            {
                // 이미 열린 상자
                if (IsOpened.Value)
                {
                    return openedPrompt;
                }

                // 아직 안 열린 상자
                return closedPrompt;
            }

            // 로컬 테스트 상태
            if (localOpened)
            {
                return openedPrompt;
            }

            return closedPrompt;
        }

        // =========================================================
        // 상호작용 처리
        // =========================================================

        /// <summary>
        /// 플레이어가 F 눌렀을 때 호출
        /// </summary>
        public void OnInteract(ulong clientId)
        {
            // 네트워크 활성 상태
            if (IsNetworkActive())
            {
                // 서버에 열기 요청
                TryOpenServerRpc();

                return;
            }

            // 로컬 테스트
            OpenLocalForEditorDebug(clientId);
        }

        // =========================================================
        // ServerRpc
        // =========================================================

        /// <summary>
        /// 서버에서 상자 열기 처리
        ///
        /// ServerRpc 패턴:
        /// 클라이언트 요청 -> 서버 실행
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void TryOpenServerRpc(
            ServerRpcParams rpcParams = default)
        {
            // 요청 보낸 클라이언트 ID
            ulong senderClientId =
                rpcParams.Receive.SenderClientId;

            // 아직 안 열린 상자만 생성
            if (!IsOpened.Value)
            {
                // LootTable 검사 실패 시 종료
                if (!CanRollByProbabilityRule())
                {
                    return;
                }

                // 랜덤 슬롯 생성
                List<global::ContainerSlotNetData> generated =
                    GenerateSlotList();

                // NetworkList 적용
                ApplyGeneratedSlotsToNetworkList(generated);

                // 열린 상태 저장
                IsOpened.Value = true;
            }

            // 디버그 로그 출력
            if (printDebugLogOnOpen)
            {
                PrintNetworkSlotsDebug(senderClientId);
            }
        }

        // =========================================================
        // 로컬 테스트용 상자 열기
        // =========================================================

        private void OpenLocalForEditorDebug(ulong clientId)
        {
            // 아직 안 열렸을 때만 생성
            if (!localOpened)
            {
                // LootTable 검사
                if (!CanRollByProbabilityRule())
                {
                    return;
                }

                // 로컬 랜덤 생성
                localSlots = GenerateSlotList();

                // 열린 상태 저장
                localOpened = true;
            }

            // Console 출력
            if (printDebugLogOnOpen)
            {
                PrintLocalSlotsDebug(clientId);
            }
        }

        // =========================================================
        // LootTable 검사
        // =========================================================

        /// <summary>
        /// weight 규칙 검사
        /// </summary>
        private bool CanRollByProbabilityRule()
        {
            // LootTable 비어있음
            if (lootTable == null)
            {
                Debug.LogWarning(
                    "[LootContainer] LootTableSO가 비어 있습니다.",
                    this);

                return false;
            }

            // weight 총합 계산
            int totalWeight =
                global::LootRollUtility.GetTotalWeight(lootTable);

            // 100 검사
            if (requireTotalWeight100 &&
                totalWeight != 100)
            {
                Debug.LogError(
                    "[LootContainer] LootTable weight 총합은 반드시 100이어야 합니다. 현재 총합: "
                    + totalWeight,
                    this);

                return false;
            }

            return true;
        }

        // =========================================================
        // 랜덤 슬롯 생성
        // =========================================================

        /// <summary>
        /// LootRollUtility 이용 랜덤 생성
        /// </summary>
        private List<global::ContainerSlotNetData> GenerateSlotList()
        {
            List<global::ContainerSlotNetData> generated =
                global::LootRollUtility.RollSlots(
                    lootTable,
                    slotCount,
                    rollCount,
                    requireTotalWeight100,
                    weaponBoxMode,
                    minWeaponCount,
                    maxWeaponCount);

            return generated;
        }

        // =========================================================
        // NetworkList 적용
        // =========================================================

        private void ApplyGeneratedSlotsToNetworkList(
            List<global::ContainerSlotNetData> generated)
        {
            // 서버만 가능
            if (!IsServer)
            {
                return;
            }

            // 기존 슬롯 제거
            Slots.Clear();

            // 생성 실패 시 빈 슬롯 채움
            if (generated == null)
            {
                EnsureEmptySlots();

                return;
            }

            // 생성 결과 추가
            for (int i = 0;
                 i < generated.Count && i < slotCount;
                 i++)
            {
                Slots.Add(generated[i]);
            }

            // 부족한 슬롯은 빈 슬롯 추가
            EnsureEmptySlots();
        }

        // =========================================================
        // 빈 슬롯 채우기
        // =========================================================

        private void EnsureEmptySlots()
        {
            if (Slots == null)
            {
                return;
            }

            // 슬롯 개수 맞출 때까지 빈 슬롯 추가
            while (Slots.Count < slotCount)
            {
                Slots.Add(new global::ContainerSlotNetData());
            }
        }

        // =========================================================
        // 네트워크 디버그 출력
        // =========================================================

        private void PrintNetworkSlotsDebug(ulong clientId)
        {
            global::ContainerSlotNetData[] slotArray =
                new global::ContainerSlotNetData[Slots.Count];

            // NetworkList -> 배열 복사
            for (int i = 0; i < Slots.Count; i++)
            {
                slotArray[i] = Slots[i];
            }

            PrintSlotArrayDebug(clientId, slotArray);
        }

        // =========================================================
        // 로컬 디버그 출력
        // =========================================================

        private void PrintLocalSlotsDebug(ulong clientId)
        {
            // 로컬 슬롯 없음
            if (localSlots == null)
            {
                Debug.LogWarning(
                    "[LootContainer] 로컬 슬롯 데이터가 없습니다.",
                    this);

                return;
            }

            PrintSlotArrayDebug(
                clientId,
                localSlots.ToArray());
        }

        // =========================================================
        // Console 로그 출력
        // =========================================================

        private void PrintSlotArrayDebug(
            ulong clientId,
            global::ContainerSlotNetData[] slotArray)
        {
            string containerName = debugContainerName;

            // 이름 비었으면 GameObject 이름 사용
            if (string.IsNullOrEmpty(containerName))
            {
                containerName = gameObject.name;
            }

            // 로그 문자열 생성
            string log =
                global::LootDebugFormatter.BuildContainerLog(
                    containerName,
                    containerGradeName,
                    clientId,
                    slotCount,
                    rollCount,
                    global::LootRollUtility.GetTotalWeight(lootTable),
                    slotArray,
                    lootTable);

            // Console 출력
            Debug.Log(log, this);
        }

        // =========================================================
        // NetworkManager 실행 여부 확인
        // =========================================================

        private bool IsNetworkActive()
        {
            // NetworkManager 없음
            if (NetworkManager.Singleton == null)
            {
                return false;
            }

            // 네트워크 실행 안 됨
            if (!NetworkManager.Singleton.IsListening)
            {
                return false;
            }

            return true;
        }
    }
}