using DeadZone.Core;
using DeadZone.Systems;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DeadZone.Actors.UI.Hideout
{
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

        [Header("인벤토리")]
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

        public bool IsOpen => windowRoot != null && windowRoot.activeSelf;
        public FacilityBase CurrentFacility => currentFacility;

        private void Reset()
        {
            windowRoot = gameObject;
        }

        private void Awake()
        {
            DebugLog("Awake 실행");

            if (windowRoot == null)
                windowRoot = gameObject;

            ResolveInventory();
            Close();
        }

        public void Open(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            if (facilityView == HideoutCameraFacilitySelector.FacilityView.None)
            {
                Debug.LogWarning("[FacilityUpgradeWindowUI] 열 시설이 선택되지 않았습니다.", this);
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

            DebugLog("업그레이드 창을 닫았습니다.");
        }

        public void Refresh()
        {
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
                facilityNameText.text = currentFacilityView.ToString();

            if (currentLevelText != null)
                currentLevelText.text = $"LV {currentLevel} / {maxLevel}";

            if (currentEffectText != null)
            {
                currentEffectText.text =
                    currentLevelData != null && !string.IsNullOrWhiteSpace(currentLevelData.effectDescription)
                        ? currentLevelData.effectDescription
                        : "현재 시설 효과가 설정되지 않았습니다.";
            }

            RefreshUpgradeRows();
            DebugLog($"시설 데이터 조회: {currentFacilityView}, 현재 레벨 {currentLevel}, 최대 레벨 {maxLevel}");
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

            ResolveInventory();

            if (inventory == null)
            {
                Debug.LogWarning("[FacilityUpgradeWindowUI] 인벤토리가 연결되지 않아 업그레이드할 수 없습니다.", this);
                return;
            }

            bool success = currentFacility.TryUpgradeToLevelFromServer(targetLevel, inventory);

            if (!success)
            {
                Debug.LogWarning($"[FacilityUpgradeWindowUI] LV{targetLevel} 업그레이드에 실패했습니다.", this);
                Refresh();
                return;
            }

            DebugLog($"LV{targetLevel} 업그레이드 완료");
            Refresh();
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

                    DebugLog(
                        $"IInventory 같은 오브젝트에서 연결 완료: {sameObjectInventory.GetType().Name} / 오브젝트: {inventoryBehaviour.gameObject.name}");

                    return;
                }

                IInventory childInventory = inventoryBehaviour.GetComponentInChildren<IInventory>(true);

                if (childInventory != null)
                {
                    inventory = childInventory;

                    DebugLog(
                        $"IInventory 자식 오브젝트에서 연결 완료: {childInventory.GetType().Name} / 오브젝트: {((MonoBehaviour)childInventory).gameObject.name}");

                    return;
                }

                Debug.LogWarning(
                    $"[FacilityUpgradeWindowUI] 연결된 Inventory Behaviour({inventoryBehaviour.GetType().Name})와 같은 오브젝트/자식에서 IInventory를 찾지 못했습니다.",
                    this);
            }

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is not IInventory foundInventory)
                    continue;

                inventory = foundInventory;
                inventoryBehaviour = behaviours[i];

                DebugLog(
                    $"IInventory 자동 연결 완료: {behaviours[i].GetType().Name} / 오브젝트: {behaviours[i].gameObject.name}");

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

        private void DebugLog(string message)
        {
            if (!showDebugLog)
                return;

            Debug.Log($"[FacilityUpgradeWindowUI] {message}", this);
        }
    }
}