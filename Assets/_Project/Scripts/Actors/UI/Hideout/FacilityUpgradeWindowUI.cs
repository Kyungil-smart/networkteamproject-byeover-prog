using System;
using System.Collections.Generic;

using TMPro;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Housing;

namespace DeadZone.Actors.UI.Hideout
{
    // 시설 업그레이드 창 UI
    // 시설 오브젝트의 공용 레벨이 아니라, 로컬 플레이어의 PlayerHousingProgress를 기준으로 표시
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
        [SerializeField]
        private GameObject windowRoot;

        [Header("시설 연결")]
        [SerializeField]
        private List<FacilityViewBinding> facilityBindings = new();

        [Header("인벤토리 표시용")]
        [SerializeField]
        private MonoBehaviour inventoryBehaviour;

        [Header("상단 표시")]
        [SerializeField]
        private TMP_Text facilityNameText;

        [SerializeField]
        private TMP_Text currentLevelText;

        [SerializeField]
        private TMP_Text currentEffectText;

        [SerializeField]
        private TMP_Text messageText;

        [Header("업그레이드 Row")]
        [SerializeField]
        private FacilityUpgradeRowUI level2Row;

        [SerializeField]
        private FacilityUpgradeRowUI level3Row;

        [SerializeField]
        private FacilityUpgradeRowUI level4Row;

        [Header("로그")]
        [SerializeField]
        private bool showDebugLog = true;

        private HideoutCameraFacilitySelector.FacilityView currentFacilityView =
            HideoutCameraFacilitySelector.FacilityView.None;

        private FacilityBase currentFacility;
        private IInventory inventory;
        private PlayerHousingProgress localHousingProgress;
        private bool isInitialized;
        private PlayerHousingProgress subscribedHousingProgress;

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

        private void OnEnable()
        {
            EventBus.Subscribe<HousingUpgradeResultEvent>(HandleUpgradeResult);
            EventBus.Subscribe<HousingSaveResultEvent>(HandleSaveResult);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<HousingUpgradeResultEvent>(HandleUpgradeResult);
            EventBus.Unsubscribe<HousingSaveResultEvent>(HandleSaveResult);
        }

        private void OnDestroy()
        {
            UnsubscribeHousingProgress();
        }

        public void Open(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            Initialize();

            if (!CanUseUpgradeWindow(facilityView))
            {
                Debug.LogWarning($"[FacilityUpgradeWindowUI] {facilityView} 시설은 현재 업그레이드 UI 대상이 아닙니다.", this);
                return;
            }

            ResolveLocalPlayerReferences();
            UpdateHousingProgressSubscription();

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
            UnsubscribeHousingProgress();
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
            ResolveLocalPlayerReferences();
            UpdateHousingProgressSubscription();

            if (currentFacility == null)
            {
                ClearTexts();
                ClearRows();
                return;
            }

            int playerCurrentLevel = GetLocalPlayerFacilityLevel();
            int maxLevel = currentFacility.GetMaxLevel();
            FacilityLevel currentLevelData = currentFacility.GetLevelData(playerCurrentLevel);

            if (facilityNameText != null)
                facilityNameText.text = GetFacilityDisplayName(currentFacilityView);

            if (currentLevelText != null)
                currentLevelText.text = $"LV {playerCurrentLevel} / {maxLevel}";

            if (currentEffectText != null)
            {
                currentEffectText.text =
                    currentLevelData != null && !string.IsNullOrWhiteSpace(currentLevelData.effectDescription)
                        ? currentLevelData.effectDescription
                        : "현재 시설 효과가 설정되어 있지 않습니다.";
            }

            RefreshUpgradeRows(playerCurrentLevel);

            DebugLog($"시설 데이터 갱신: {currentFacilityView}, 내 현재 레벨 {playerCurrentLevel}, 최대 레벨 {maxLevel}");
        }

        private void Initialize()
        {
            if (isInitialized)
                return;

            if (windowRoot == null)
                windowRoot = gameObject;

            ResolveLocalPlayerReferences();

            isInitialized = true;
            DebugLog("초기화 완료");
        }

        private void RefreshUpgradeRows(int playerCurrentLevel)
        {
            if (currentFacility == null)
            {
                ClearRows();
                return;
            }

            SetRow(level2Row, playerCurrentLevel, 2);
            SetRow(level3Row, playerCurrentLevel, 3);
            SetRow(level4Row, playerCurrentLevel, 4);
        }

        private void SetRow(FacilityUpgradeRowUI row, int playerCurrentLevel, int targetLevel)
        {
            if (row == null)
                return;

            FacilityLevel levelData = currentFacility.GetLevelData(targetLevel);

            row.Set(
                currentFacility,
                playerCurrentLevel,
                targetLevel,
                levelData,
                inventory,
                RequestUpgrade);
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

            int playerCurrentLevel = GetLocalPlayerFacilityLevel();

            if (targetLevel != playerCurrentLevel + 1)
            {
                Debug.LogWarning(
                    $"[FacilityUpgradeWindowUI] LV{targetLevel}은 현재 업그레이드 대상 레벨이 아닙니다. 내 현재 레벨: LV{playerCurrentLevel}",
                    this
                );

                Refresh();
                return;
            }

            if (!TryGetUpgradeController(out FacilityUpgradeController upgradeController))
            {
                Debug.LogWarning("[FacilityUpgradeWindowUI] FacilityUpgradeController가 연결되어 있지 않습니다.", this);
                return;
            }

            upgradeController.RequestUpgrade();

            SetMessage($"LV{targetLevel} 업그레이드를 서버에 요청했습니다.");
            DebugLog($"LV{targetLevel} 업그레이드를 서버에 요청했습니다.");
        }

        private void HandleUpgradeResult(HousingUpgradeResultEvent evt)
        {
            if (!IsOpen)
                return;

            SetMessage(evt.success ? $"업그레이드 완료: LV{evt.currentLevel}" : evt.reason);
            Refresh();
        }

        private void HandleSaveResult(HousingSaveResultEvent evt)
        {
            if (!IsOpen || evt.success)
                return;

            SetMessage(evt.reason);
        }

        private void UpdateHousingProgressSubscription()
        {
            if (subscribedHousingProgress == localHousingProgress)
                return;

            UnsubscribeHousingProgress();

            subscribedHousingProgress = localHousingProgress;
            if (subscribedHousingProgress != null)
                subscribedHousingProgress.FacilityLevelChanged += HandleLocalHousingLevelChanged;
        }

        private void UnsubscribeHousingProgress()
        {
            if (subscribedHousingProgress != null)
                subscribedHousingProgress.FacilityLevelChanged -= HandleLocalHousingLevelChanged;

            subscribedHousingProgress = null;
        }

        private void HandleLocalHousingLevelChanged(FacilityType facilityType, int oldLevel, int newLevel)
        {
            if (!IsOpen || currentFacility == null)
                return;

            if (currentFacility.Type != facilityType)
                return;

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

        private int GetLocalPlayerFacilityLevel()
        {
            if (currentFacility == null)
                return 1;

            if (localHousingProgress == null)
                ResolveLocalPlayerReferences();

            if (localHousingProgress == null)
                return currentFacility.GetCurrentLevel();

            return localHousingProgress.GetLevel(currentFacility.Type);
        }

        private void ResolveLocalPlayerReferences()
        {
            inventory = null;
            localHousingProgress = null;

            if (HousingInventoryResolver.TryCreateLobbySavedInventory(out IInventory savedInventory))
            {
                inventory = savedInventory;
                inventoryBehaviour = null;
                DebugLog("로비 저장 인벤토리/보관함 재료 연결 완료");
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                ulong localClientId = NetworkManager.Singleton.LocalClientId;

                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out NetworkClient localClient))
                {
                    if (localClient.PlayerObject != null)
                    {
                        localHousingProgress = localClient.PlayerObject.GetComponent<PlayerHousingProgress>();

                        if (localHousingProgress == null)
                            localHousingProgress = localClient.PlayerObject.GetComponentInChildren<PlayerHousingProgress>(true);

                        IInventory playerInventory = localClient.PlayerObject.GetComponent<IInventory>();

                        if (playerInventory == null)
                            playerInventory = localClient.PlayerObject.GetComponentInChildren<IInventory>(true);

                        if (playerInventory != null && inventory == null)
                        {
                            inventory = playerInventory;
                            inventoryBehaviour = playerInventory as MonoBehaviour;

                            DebugLog($"로컬 플레이어 인벤토리 연결 완료: {inventoryBehaviour.gameObject.name}");
                        }

                        if (localHousingProgress != null)
                            DebugLog($"로컬 플레이어 하우징 진행도 연결 완료: {localHousingProgress.gameObject.name}");

                        if (inventory != null || localHousingProgress != null)
                            return;
                    }
                }
            }

            if (inventory == null && inventoryBehaviour != null)
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

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (inventory == null && behaviours[i] is IInventory foundInventory)
                {
                    inventory = foundInventory;
                    inventoryBehaviour = behaviours[i];

                    DebugLog($"IInventory 자동 연결 완료: {behaviours[i].GetType().Name} / 오브젝트: {behaviours[i].gameObject.name}");
                }

                if (localHousingProgress == null && behaviours[i] is PlayerHousingProgress foundProgress)
                {
                    localHousingProgress = foundProgress;
                    DebugLog($"PlayerHousingProgress 자동 연결 완료: {foundProgress.gameObject.name}");
                }

                if (inventory != null && localHousingProgress != null)
                    return;
            }

            if (inventory == null)
                Debug.LogWarning("[FacilityUpgradeWindowUI] 씬에서 IInventory 구현체를 찾지 못했습니다.", this);

            if (localHousingProgress == null)
                Debug.LogWarning("[FacilityUpgradeWindowUI] 씬에서 PlayerHousingProgress를 찾지 못했습니다.", this);
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

        private void ClearTexts()
        {
            if (facilityNameText != null)
                facilityNameText.text = string.Empty;

            if (currentLevelText != null)
                currentLevelText.text = string.Empty;

            if (currentEffectText != null)
                currentEffectText.text = string.Empty;

            if (messageText != null)
                messageText.text = string.Empty;
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

        private void SetMessage(string message)
        {
            if (messageText != null)
                messageText.text = message;

            DebugLog(message);
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
                return;

            Debug.Log($"[FacilityUpgradeWindowUI] {message}", this);
        }
    }
}
