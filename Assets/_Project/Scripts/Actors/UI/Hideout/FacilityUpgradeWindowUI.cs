using System;
using System.Collections.Generic;
using Unity.Netcode;

using TMPro;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Housing;

namespace DeadZone.Actors.UI.Hideout
{
    // 시설 업그레이드 창 UI
    // UI는 시설 정보와 재료 상태를 표시하고, 실제 업그레이드는 FacilityUpgradeController에 요청
    [DisallowMultipleComponent]
    public sealed class FacilityUpgradeWindowUI : MonoBehaviour
    {
        [Serializable]
        private sealed class FacilityViewBinding
        {
            public HideoutCameraFacilitySelector.FacilityView facilityView;
            public FacilityBase facility;
        }

        [Header("창 루트")]
        [SerializeField] private GameObject windowRoot;

        [Header("시설 연결")]
        [SerializeField] private List<FacilityViewBinding> facilityBindings = new();

        [Header("인벤토리 표시용")]
        [SerializeField] private MonoBehaviour inventoryBehaviour;

        [Header("상단 표시")]
        [SerializeField] private TMP_Text facilityNameText;
        [SerializeField] private TMP_Text currentLevelText;
        [SerializeField] private TMP_Text currentEffectText;

        [Header("업그레이드 Row")]
        [SerializeField] private FacilityUpgradeRowUI level2Row;
        [SerializeField] private FacilityUpgradeRowUI level3Row;
        [SerializeField] private FacilityUpgradeRowUI level4Row;

        [Header("로그")]
        [SerializeField] private bool showDebugLog = true;

        private HideoutCameraFacilitySelector.FacilityView currentFacilityView =
            HideoutCameraFacilitySelector.FacilityView.None;

        private FacilityBase currentFacility;
        private IInventory inventory;
        private bool isInitialized;

        public bool IsOpen => windowRoot != null && windowRoot.activeSelf;
        public FacilityBase CurrentFacility => currentFacility;
        public GameObject WindowRoot => windowRoot != null ? windowRoot : gameObject;

        private void Reset()
        {
            windowRoot = gameObject;
        }

        private void Awake()
        {
            Initialize();
        }

        public void Open(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            Initialize();

            if (!CanUseUpgradeWindow(facilityView))
            {
                Debug.LogWarning($"[FacilityUpgradeWindowUI] {facilityView} 시설은 현재 업그레이드 UI 대상이 아닙니다.", this);
                return;
            }

            ResolveInventory();

            if (!TryFindFacility(facilityView, out FacilityBase facility))
            {
                Debug.LogWarning($"[FacilityUpgradeWindowUI] {facilityView}에 연결된 FacilityBase가 없습니다.", this);
                return;
            }

            currentFacilityView = facilityView;
            currentFacility = facility;

            if (windowRoot != null)
                windowRoot.SetActive(true);

            Refresh();

            DebugLog($"{facilityView} 업그레이드 창을 열었습니다.");
        }

        public void Close()
        {
            currentFacilityView = HideoutCameraFacilitySelector.FacilityView.None;
            currentFacility = null;

            if (windowRoot != null)
                windowRoot.SetActive(false);

            ClearTexts();
            ClearRows();

            DebugLog("업그레이드 창을 닫았습니다.");
        }

        public void Refresh()
        {
            ResolveInventory();

            if (currentFacility == null)
            {
                ClearTexts();
                ClearRows();
                return;
            }

            FacilityLevel currentLevelData = currentFacility.GetCurrentLevelData();
            int currentLevel = currentFacility.GetCurrentLevel();
            int maxLevel = currentFacility.GetMaxLevel();

            if (facilityNameText != null)
                facilityNameText.text = GetFacilityDisplayName(currentFacilityView);

            if (currentLevelText != null)
                currentLevelText.text = $"LV {currentLevel} / {maxLevel}";

            if (currentEffectText != null)
            {
                currentEffectText.text =
                    currentLevelData != null && !string.IsNullOrWhiteSpace(currentLevelData.effectDescription)
                        ? currentLevelData.effectDescription
                        : "현재 시설 효과가 설정되어 있지 않습니다.";
            }

            RefreshUpgradeRows();

            DebugLog($"시설 데이터 갱신: {currentFacilityView}, 현재 레벨 {currentLevel}, 최대 레벨 {maxLevel}");
        }

        private void Initialize()
        {
            if (isInitialized)
                return;

            if (windowRoot == null)
                windowRoot = gameObject;

            ResolveInventory();

            isInitialized = true;
            DebugLog("초기화 완료");
        }

        private void RefreshUpgradeRows()
        {
            if (currentFacility == null)
            {
                ClearRows();
                return;
            }

            SetRow(level2Row, 2);
            SetRow(level3Row, 3);
            SetRow(level4Row, 4);
        }

        private void SetRow(FacilityUpgradeRowUI row, int targetLevel)
        {
            if (row == null)
                return;

            FacilityLevel levelData = currentFacility.GetLevelData(targetLevel);
            row.Set(currentFacility, targetLevel, levelData, inventory, RequestUpgrade);
        }

        private void RequestUpgrade(int targetLevel)
        {
            if (currentFacility == null)
            {
                Debug.LogWarning("[FacilityUpgradeWindowUI] 업그레이드할 시설이 없습니다.", this);
                return;
            }

            if (!CanUseUpgradeWindow(currentFacilityView))
            {
                Debug.LogWarning($"[FacilityUpgradeWindowUI] {currentFacilityView} 시설은 업그레이드 요청 대상이 아닙니다.", this);
                return;
            }

            if (!currentFacility.IsUpgradeTargetLevel(targetLevel))
            {
                Debug.LogWarning($"[FacilityUpgradeWindowUI] LV{targetLevel}은 현재 업그레이드 대상 레벨이 아닙니다.", this);
                Refresh();
                return;
            }

            if (!TryGetUpgradeController(out FacilityUpgradeController upgradeController))
            {
                Debug.LogWarning("[FacilityUpgradeWindowUI] FacilityUpgradeController가 연결되어 있지 않습니다.", this);
                return;
            }

            upgradeController.RequestUpgrade();

            DebugLog($"LV{targetLevel} 업그레이드를 서버에 요청했습니다.");

            Refresh();
        }

        private bool TryGetUpgradeController(out FacilityUpgradeController upgradeController)
        {
            upgradeController = null;

            if (currentFacility == null)
                return false;

            upgradeController = currentFacility.GetComponent<FacilityUpgradeController>();

            if (upgradeController != null)
                return true;

            upgradeController = currentFacility.GetComponentInChildren<FacilityUpgradeController>(true);

            return upgradeController != null;
        }

        private void ClearRows()
        {
            if (level2Row != null)
                level2Row.gameObject.SetActive(false);

            if (level3Row != null)
                level3Row.gameObject.SetActive(false);

            if (level4Row != null)
                level4Row.gameObject.SetActive(false);
        }

        private bool TryFindFacility(
            HideoutCameraFacilitySelector.FacilityView facilityView,
            out FacilityBase facility)
        {
            facility = null;

            for (int i = 0; i < facilityBindings.Count; i++)
            {
                FacilityViewBinding binding = facilityBindings[i];

                if (binding == null)
                    continue;

                if (binding.facilityView != facilityView)
                    continue;

                facility = binding.facility;
                return facility != null;
            }

            return false;
        }

        private void ResolveInventory()
        {
            inventory = null;

            // 1순위: 네트워크에서 실제 로컬 플레이어의 PlayerObject 인벤토리를 찾는다.
            // 테스트 아이템을 넣은 Player(Clone)의 GridInventory를 정확히 잡기 위한 기준
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                ulong localClientId = NetworkManager.Singleton.LocalClientId;

                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out NetworkClient localClient))
                {
                    if (localClient.PlayerObject != null)
                    {
                        IInventory playerInventory = localClient.PlayerObject.GetComponent<IInventory>();

                        if (playerInventory == null)
                            playerInventory = localClient.PlayerObject.GetComponentInChildren<IInventory>(true);

                        if (playerInventory != null)
                        {
                            inventory = playerInventory;
                            inventoryBehaviour = playerInventory as MonoBehaviour;

                            DebugLog($"로컬 플레이어 인벤토리 연결 완료: {inventoryBehaviour.gameObject.name}");
                            return;
                        }
                    }
                }
            }

            // 2순위: Inspector에 직접 연결한 인벤토리 사용
            if (inventoryBehaviour != null)
            {
                if (inventoryBehaviour is IInventory directInventory)
                {
                    inventory = directInventory;
                    DebugLog($"IInventory 직접 연결 완료: {inventoryBehaviour.GetType().Name}");
                    return;
                }

                IInventory sameObjectInventory = inventoryBehaviour.GetComponent<IInventory>();

                if (sameObjectInventory != null)
                {
                    inventory = sameObjectInventory;
                    DebugLog($"IInventory 같은 오브젝트에서 연결 완료: {sameObjectInventory.GetType().Name}");
                    return;
                }

                IInventory childInventory = inventoryBehaviour.GetComponentInChildren<IInventory>(true);

                if (childInventory != null)
                {
                    inventory = childInventory;
                    DebugLog($"IInventory 자식 오브젝트에서 연결 완료: {childInventory.GetType().Name}");
                    return;
                }
            }

            // 3순위: 최후의 fallback. 자동 검색은 가장 마지막에만 사용
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is not IInventory foundInventory)
                    continue;

                inventory = foundInventory;
                inventoryBehaviour = behaviours[i];

                DebugLog($"IInventory 자동 연결 완료: {behaviours[i].GetType().Name} / 오브젝트: {behaviours[i].gameObject.name}");
                return;
            }

            Debug.LogWarning("[FacilityUpgradeWindowUI] 씬에서 IInventory 구현체를 찾지 못했습니다.", this);
        }

        private void ClearTexts()
        {
            if (facilityNameText != null)
                facilityNameText.text = string.Empty;

            if (currentLevelText != null)
                currentLevelText.text = string.Empty;

            if (currentEffectText != null)
                currentEffectText.text = string.Empty;
        }

        private bool CanUseUpgradeWindow(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView == HideoutCameraFacilitySelector.FacilityView.Workbench ||
                   facilityView == HideoutCameraFacilitySelector.FacilityView.Medical ||
                   facilityView == HideoutCameraFacilitySelector.FacilityView.Gym ||
                   facilityView == HideoutCameraFacilitySelector.FacilityView.Kitchen ||
                   facilityView == HideoutCameraFacilitySelector.FacilityView.Bed;
        }

        private string GetFacilityDisplayName(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView switch
            {
                HideoutCameraFacilitySelector.FacilityView.Workbench => "총기 작업대",
                HideoutCameraFacilitySelector.FacilityView.Medical => "의료시설",
                HideoutCameraFacilitySelector.FacilityView.Gym => "헬스장",
                HideoutCameraFacilitySelector.FacilityView.Kitchen => "조리시설",
                HideoutCameraFacilitySelector.FacilityView.Bed => "침실",
                _ => facilityView.ToString()
            };
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
                return;

            Debug.Log($"[FacilityUpgradeWindowUI] {message}", this);
        }
    }
}