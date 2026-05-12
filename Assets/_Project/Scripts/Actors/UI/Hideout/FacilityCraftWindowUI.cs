using System;
using System.Collections.Generic;

using TMPro;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Housing;
using DeadZone.Systems.Save;

namespace DeadZone.Actors.UI.Hideout
{
    // 작업대/의료시설 제작 창 UI
    // UI는 레시피 표시와 제작 요청만 담당하고, 재료 소모와 결과 지급은 서버 제작 컨트롤러가 처리
    [DisallowMultipleComponent]
    public sealed class FacilityCraftWindowUI : MonoBehaviour
    {
        [Serializable]
        private sealed class FacilityViewBinding
        {
            public HideoutCameraFacilitySelector.FacilityView facilityView;
            public FacilityBase facility;
        }

        [Header("창 루트")]
        [SerializeField] private GameObject windowRoot;

        [Header("텍스트 표시")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text messageText;

        [Header("시설 연결")]
        [SerializeField] private List<FacilityViewBinding> facilityBindings = new();

        [Header("인벤토리 표시용")]
        [SerializeField] private MonoBehaviour inventoryBehaviour;

        [Header("레시피 목록")]
        [SerializeField] private List<RecipeSO> workbenchRecipes = new();
        [SerializeField] private List<RecipeSO> medicalRecipes = new();

        [Header("레시피 UI")]
        [SerializeField] private Transform recipeListRoot;
        [SerializeField] private FacilityCraftRecipeRowUI recipeRowPrefab;

        [Header("로그")]
        [SerializeField] private bool showDebugLog = true;

        private readonly List<FacilityCraftRecipeRowUI> spawnedRows = new();

        private HideoutCameraFacilitySelector.FacilityView currentFacilityView =
            HideoutCameraFacilitySelector.FacilityView.None;

        private FacilityBase currentFacility;
        private IInventory inventory;
        private PlayerHousingProgress localHousingProgress;
        private bool isInitialized;
        private PlayerHousingProgress subscribedHousingProgress;

        public bool IsOpen => windowRoot != null && windowRoot.activeSelf;
        public GameObject WindowRoot => windowRoot != null ? windowRoot : gameObject;

        private void Reset()
        {
            windowRoot = gameObject;
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            UnsubscribeHousingProgress();
        }

        public void Open(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            Initialize();

            if (!CanUseCraftWindow(facilityView))
            {
                Debug.LogWarning($"[FacilityCraftWindowUI] {facilityView} 시설은 제작 창을 사용할 수 없습니다.", this);
                return;
            }

            if (!TryFindFacility(facilityView, out FacilityBase facility))
            {
                Debug.LogWarning($"[FacilityCraftWindowUI] {facilityView}에 연결된 FacilityBase가 없습니다.", this);
                return;
            }

            currentFacilityView = facilityView;
            currentFacility = facility;

            ResolveInventory();
            UpdateHousingProgressSubscription();

            if (windowRoot != null)
                windowRoot.SetActive(true);

            Refresh();

            DebugLog($"{facilityView} 제작 창을 열었습니다.");
        }

        public void Close()
        {
            Initialize();
            UnsubscribeHousingProgress();

            currentFacilityView = HideoutCameraFacilitySelector.FacilityView.None;
            currentFacility = null;

            if (windowRoot != null)
                windowRoot.SetActive(false);

            ClearTexts();
            ClearRows();

            DebugLog("제작 창을 닫았습니다.");
        }

        public void Refresh()
        {
            ResolveInventory();
            UpdateHousingProgressSubscription();

            if (currentFacility == null)
            {
                ClearTexts();
                ClearRows();
                return;
            }

            RefreshTexts();
            RefreshRecipeRows();
        }

        private void RequestCraftRecipe(RecipeSO recipe)
        {
            if (recipe == null)
            {
                SetMessage("레시피 데이터가 없습니다.");
                return;
            }

            if (!IsRecipeValid(recipe))
                return;

            if (currentFacility == null)
            {
                SetMessage("현재 선택된 시설이 없습니다.");
                return;
            }

            int currentLevel = GetLocalPlayerFacilityLevel();
            int requiredLevel = Mathf.Max(1, recipe.requiredFacilityLevel);

            if (currentLevel < requiredLevel)
            {
                SetMessage($"시설 레벨이 부족합니다. 필요 LV{requiredLevel}");
                Refresh();
                return;
            }

            if (!RequestCraftToCurrentFacility(recipe.recipeID))
            {
                SetMessage("제작 컨트롤러가 연결되어 있지 않습니다.");
                return;
            }

            string resultName = recipe.result != null && !string.IsNullOrWhiteSpace(recipe.result.displayName)
                ? recipe.result.displayName
                : recipe.recipeID;

            SetMessage($"{resultName} 제작을 서버에 요청했습니다.");
            DebugLog($"제작 요청: {recipe.recipeID}");
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


        private bool RequestCraftToCurrentFacility(string recipeID)
        {
            if (currentFacility == null)
                return false;

            WorkbenchCraftingController workbenchController =
                currentFacility.GetComponent<WorkbenchCraftingController>();

            if (workbenchController == null)
                workbenchController = currentFacility.GetComponentInChildren<WorkbenchCraftingController>(true);

            if (workbenchController != null)
            {
                workbenchController.RequestCraft(recipeID);
                return true;
            }

            MedicalCraftingController medicalController =
                currentFacility.GetComponent<MedicalCraftingController>();

            if (medicalController == null)
                medicalController = currentFacility.GetComponentInChildren<MedicalCraftingController>(true);

            if (medicalController != null)
            {
                medicalController.RequestCraft(recipeID);
                return true;
            }

            return false;
        }

        private bool IsRecipeValid(RecipeSO recipe)
        {
            if (recipe == null)
            {
                SetMessage("레시피 데이터가 없습니다.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(recipe.recipeID))
            {
                SetMessage("Recipe ID가 비어 있습니다.");
                return false;
            }

            if (recipe.result == null)
            {
                SetMessage("결과 아이템이 연결되어 있지 않습니다.");
                return false;
            }

            return true;
        }

        private void Initialize()
        {
            if (isInitialized)
                return;

            if (windowRoot == null)
                windowRoot = gameObject;

            ResolveInventory();
            isInitialized = true;
        }

        private void ResolveInventory()
        {
            inventory = null;
            localHousingProgress = null;

            // 네트워크 실사용 기준: 현재 로컬 플레이어의 PlayerObject 인벤토리를 가장 먼저 찾는다.
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

                        if (playerInventory != null)
                        {
                            inventory = playerInventory;
                            inventoryBehaviour = playerInventory as MonoBehaviour;

                            if (inventoryBehaviour != null)
                                DebugLog($"로컬 플레이어 인벤토리 연결 완료: {inventoryBehaviour.gameObject.name}");

                            if (localHousingProgress != null)
                                return;
                        }
                    }
                }
            }

            // 인스펙터에 직접 연결된 인벤토리
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

            // 최후의 자동 검색
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

            Debug.LogWarning("[FacilityCraftWindowUI] 씬에서 IInventory 구현체를 찾지 못했습니다.", this);
        }

        private void RefreshTexts()
        {
            int currentLevel = GetLocalPlayerFacilityLevel();
            int maxLevel = currentFacility != null ? currentFacility.GetMaxLevel() : 0;

            if (titleText != null)
                titleText.text = GetTitleText(currentFacilityView);

            if (descriptionText != null)
                descriptionText.text = GetDescriptionText(currentFacilityView);

            if (levelText != null)
                levelText.text = $"현재 시설 레벨: LV {currentLevel} / {maxLevel}";
        }

        private void RefreshRecipeRows()
        {
            ClearRows();

            IReadOnlyList<RecipeSO> recipes = GetCurrentRecipes();

            if (recipes == null || recipes.Count == 0)
            {
                DebugLog($"{currentFacilityView} 제작 레시피가 없습니다.");
                return;
            }

            if (recipeListRoot == null)
            {
                Debug.LogWarning("[FacilityCraftWindowUI] Recipe List Root가 연결되어 있지 않습니다.", this);
                return;
            }

            if (recipeRowPrefab == null)
            {
                Debug.LogWarning("[FacilityCraftWindowUI] Recipe Row Prefab이 연결되어 있지 않습니다.", this);
                return;
            }

            int currentLevel = GetLocalPlayerFacilityLevel();

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO recipe = recipes[i];

                if (recipe == null)
                    continue;

                FacilityCraftRecipeRowUI row = Instantiate(recipeRowPrefab, recipeListRoot);
                row.Set(recipe, currentLevel, inventory, RequestCraftRecipe);
                spawnedRows.Add(row);
            }

            DebugLog($"{currentFacilityView} 제작 Row {spawnedRows.Count}개를 생성했습니다.");
        }

        private int GetLocalPlayerFacilityLevel()
        {
            if (currentFacility == null)
                return 1;

            if (localHousingProgress == null)
                ResolveInventory();

            if (localHousingProgress != null)
                return localHousingProgress.GetLevel(currentFacility.Type);

            if (TryGetLobbyFacilityLevel(currentFacility.Type, out int lobbyLevel))
                return lobbyLevel;

            return currentFacility.GetCurrentLevel();
        }

        private static bool TryGetLobbyFacilityLevel(FacilityType facilityType, out int level)
        {
            level = 1;

            LobbyFacilityState facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);

            if (facilityState == null || facilityState.Facilities == null)
                return false;

            string expectedId = GetFacilitySaveId(facilityType);

            for (int i = 0; i < facilityState.Facilities.Count; i++)
            {
                FacilitySaveDTO facility = facilityState.Facilities[i];

                if (facility == null)
                    continue;

                if (!string.Equals(facility.facilityId, expectedId, StringComparison.OrdinalIgnoreCase))
                    continue;

                level = Mathf.Clamp(facility.level, 1, 4);
                return true;
            }

            return false;
        }

        private static string GetFacilitySaveId(FacilityType facilityType)
        {
            return facilityType switch
            {
                FacilityType.Workbench => "Workbench",
                FacilityType.Medical => "Medical",
                FacilityType.Gym => "Gym",
                FacilityType.Stash => "Stash",
                FacilityType.Kitchen => "Kitchen",
                FacilityType.Bed => "Bed",
                FacilityType.CommStation => "CommStation",
                _ => facilityType.ToString()
            };
        }

        private IReadOnlyList<RecipeSO> GetCurrentRecipes()
        {
            return currentFacilityView switch
            {
                HideoutCameraFacilitySelector.FacilityView.Workbench => workbenchRecipes,
                HideoutCameraFacilitySelector.FacilityView.Medical => medicalRecipes,
                _ => null
            };
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

        private void ClearRows()
        {
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                if (spawnedRows[i] != null)
                    Destroy(spawnedRows[i].gameObject);
            }

            spawnedRows.Clear();
        }

        private void ClearTexts()
        {
            if (titleText != null)
                titleText.text = string.Empty;

            if (descriptionText != null)
                descriptionText.text = string.Empty;

            if (levelText != null)
                levelText.text = string.Empty;

            if (messageText != null)
                messageText.text = string.Empty;
        }

        private void SetMessage(string message)
        {
            if (messageText != null)
                messageText.text = message;

            DebugLog(message);
        }

        private bool CanUseCraftWindow(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView == HideoutCameraFacilitySelector.FacilityView.Workbench ||
                   facilityView == HideoutCameraFacilitySelector.FacilityView.Medical;
        }

        private string GetTitleText(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView switch
            {
                HideoutCameraFacilitySelector.FacilityView.Workbench => "총기 작업대 제작",
                HideoutCameraFacilitySelector.FacilityView.Medical => "의료시설 제작",
                _ => "제작"
            };
        }

        private string GetDescriptionText(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView switch
            {
                HideoutCameraFacilitySelector.FacilityView.Workbench =>
                    "시설 레벨에 따라 총기 제작 레시피가 표시됩니다.",

                HideoutCameraFacilitySelector.FacilityView.Medical =>
                    "시설 레벨에 따라 의료품 제작 레시피가 표시됩니다.",

                _ => string.Empty
            };
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
                return;

            Debug.Log($"[FacilityCraftWindowUI] {message}", this);
        }
    }
}
