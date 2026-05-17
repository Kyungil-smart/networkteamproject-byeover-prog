using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

using DeadZone.Network;

namespace DeadZone.Actors.UI.Hideout
{
    // 은신처 시설 UI 흐름을 관리
    // 시설 선택, 카메라 이동, 업그레이드 창, 제작 창, 로비 복귀만 담당
    [DisallowMultipleComponent]
    public sealed class HideoutFacilityUIController : MonoBehaviour
    {
        [Header("카메라 선택")]
        [SerializeField]
        [Tooltip("시설 카메라 이동을 담당하는 선택자입니다.")]
        private HideoutCameraFacilitySelector cameraFacilitySelector;

        [Header("UI 버튼 루트")]
        [SerializeField]
        [Tooltip("시설을 선택했을 때 보이는 시설 열기 버튼 루트입니다.")]
        private GameObject openFacilityButtonRoot;

        [SerializeField]
        [Tooltip("작업대와 의료시설을 선택했을 때만 보이는 아이템 제작 버튼 루트입니다.")]
        private GameObject itemCraftButtonRoot;

        [Header("창 UI")]
        [SerializeField]
        [Tooltip("시설 업그레이드 창입니다.")]
        private FacilityUpgradeWindowUI facilityUpgradeWindowUI;

        [SerializeField]
        [Tooltip("아이템 제작 창입니다.")]
        private FacilityCraftWindowUI facilityCraftWindowUI;

        [Header("로비 복귀")]
        [SerializeField]
        [Tooltip("뒤로가기 버튼을 한 번 더 눌렀을 때 이동할 로비 씬 이름입니다.")]
        private string lobbySceneName = "Lobby";

        [SerializeField]
        [Tooltip("로비 복귀 시 현재 시설 창과 선택 상태를 정리합니다.")]
        private bool clearStateBeforeReturnToLobby = true;

        [Header("동작 옵션")]
        [SerializeField]
        [Tooltip("다른 시설을 선택할 때 열려 있던 업그레이드/제작 창을 닫습니다.")]
        private bool closeContentWhenSelectFacility = true;

        [SerializeField]
        [Tooltip("콘솔 로그를 출력합니다.")]
        private bool showDebugLog = true;

        private HideoutCameraFacilitySelector.FacilityView selectedFacility =
            HideoutCameraFacilitySelector.FacilityView.None;

        public HideoutCameraFacilitySelector.FacilityView SelectedFacility => selectedFacility;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();

            CloseContentOnly();
            SetOpenFacilityButtonVisible(false);
            SetItemCraftButtonVisible(false);
        }

        private void ResolveReferences()
        {
            if (cameraFacilitySelector == null)
                cameraFacilitySelector = FindFirstObjectByType<HideoutCameraFacilitySelector>();

            if (facilityUpgradeWindowUI == null)
                facilityUpgradeWindowUI = FindFirstObjectByType<FacilityUpgradeWindowUI>(FindObjectsInactive.Include);

            if (facilityCraftWindowUI == null)
                facilityCraftWindowUI = FindFirstObjectByType<FacilityCraftWindowUI>(FindObjectsInactive.Include);
        }

        public void SelectFacility(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            if (facilityView == HideoutCameraFacilitySelector.FacilityView.None)
            {
                CloseFacilityView();
                return;
            }

            selectedFacility = facilityView;

            if (cameraFacilitySelector == null)
            {
                Debug.LogWarning("[HideoutFacilityUIController] HideoutCameraFacilitySelector가 연결되지 않았습니다.", this);
            }
            else
            {
                cameraFacilitySelector.SelectFacility(facilityView);
            }

            if (closeContentWhenSelectFacility)
                CloseContentOnly();

            SetOpenFacilityButtonVisible(true);
            SetItemCraftButtonVisible(CanOpenItemCraft(facilityView));

            DebugLog($"{facilityView} 시설을 선택했습니다.");
        }

        public void OpenSelectedFacility()
        {
            if (selectedFacility == HideoutCameraFacilitySelector.FacilityView.None)
            {
                Debug.LogWarning("[HideoutFacilityUIController] 선택된 시설이 없습니다.", this);
                return;
            }

            if (facilityUpgradeWindowUI == null)
            {
                Debug.LogWarning("[HideoutFacilityUIController] FacilityUpgradeWindowUI가 연결되지 않았습니다.", this);
                return;
            }

            HideCraftWindow();

            facilityUpgradeWindowUI.Open(selectedFacility);

            SetOpenFacilityButtonVisible(false);
            SetItemCraftButtonVisible(false);

            DebugLog($"{selectedFacility} 시설 업그레이드 창을 열었습니다.");
        }

        public void OpenSelectedFacilityCraft()
        {
            if (selectedFacility == HideoutCameraFacilitySelector.FacilityView.None)
            {
                Debug.LogWarning("[HideoutFacilityUIController] 선택된 시설이 없습니다.", this);
                return;
            }

            if (!CanOpenItemCraft(selectedFacility))
            {
                Debug.LogWarning($"[HideoutFacilityUIController] {selectedFacility} 시설은 아이템 제작 기능이 없습니다.", this);
                return;
            }

            if (facilityCraftWindowUI == null)
            {
                Debug.LogWarning("[HideoutFacilityUIController] FacilityCraftWindowUI가 연결되지 않았습니다.", this);
                return;
            }

            if (facilityUpgradeWindowUI != null)
                facilityUpgradeWindowUI.Close();

            facilityCraftWindowUI.Open(selectedFacility);

            SetOpenFacilityButtonVisible(false);
            SetItemCraftButtonVisible(false);

            DebugLog($"{selectedFacility} 아이템 제작 창을 열었습니다.");
        }

        public void HandleBackButton()
        {
            if (IsFacilityDetailActive())
            {
                CloseFacilityView();
                return;
            }

            ReturnToLobbyScene();
        }

        public void ReturnToLobbyScene()
        {
            if (clearStateBeforeReturnToLobby)
            {
                selectedFacility = HideoutCameraFacilitySelector.FacilityView.None;
                CloseContentOnly();
                SetOpenFacilityButtonVisible(false);
                SetItemCraftButtonVisible(false);
            }

            if (string.IsNullOrWhiteSpace(lobbySceneName))
            {
                Debug.LogWarning("[HideoutFacilityUIController] 로비 씬 이름이 비어 있어 로비로 돌아갈 수 없습니다.", this);
                return;
            }

            DebugLog($"로비 씬으로 이동합니다. Scene={lobbySceneName}");
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (!NetworkManager.Singleton.IsServer)
                {
                    Debug.LogWarning("[HideoutFacilityUIController] Party session is active. Lobby return is blocked on non-host clients to avoid leaving the party session.", this);
                    return;
                }

                NetworkGameManager.LoadSceneWithLoading(lobbySceneName, LoadSceneMode.Single);
                return;
            }

            SceneManager.LoadScene(lobbySceneName);
        }

        public void CloseFacilityView()
        {
            selectedFacility = HideoutCameraFacilitySelector.FacilityView.None;

            CloseContentOnly();
            SetOpenFacilityButtonVisible(false);
            SetItemCraftButtonVisible(false);

            if (cameraFacilitySelector != null)
                cameraFacilitySelector.ReturnToDefaultView();

            DebugLog("시설 UI를 닫고 기본 시점으로 돌아갑니다.");
        }

        public void CloseContentOnly()
        {
            if (facilityUpgradeWindowUI != null)
                facilityUpgradeWindowUI.Close();

            HideCraftWindow();
        }

        public void SelectWorkbench()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Workbench);
        }

        public void SelectStash()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Stash);
        }

        public void SelectKitchen()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Kitchen);
        }

        public void SelectBed()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Bed);
        }

        public void SelectGym()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Gym);
        }

        public void SelectCommStation()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.CommStation);
        }

        public void SelectMedical()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Medical);
        }

        private bool IsFacilityDetailActive()
        {
            if (selectedFacility != HideoutCameraFacilitySelector.FacilityView.None)
                return true;

            if (facilityUpgradeWindowUI != null && facilityUpgradeWindowUI.IsOpen)
                return true;

            return facilityCraftWindowUI != null && facilityCraftWindowUI.IsOpen;
        }

        private bool CanOpenItemCraft(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView == HideoutCameraFacilitySelector.FacilityView.Workbench ||
                   facilityView == HideoutCameraFacilitySelector.FacilityView.Medical;
        }

        private void SetOpenFacilityButtonVisible(bool visible)
        {
            if (openFacilityButtonRoot != null)
                openFacilityButtonRoot.SetActive(visible);
        }

        private void SetItemCraftButtonVisible(bool visible)
        {
            if (itemCraftButtonRoot != null)
                itemCraftButtonRoot.SetActive(visible);
        }

        private void HideCraftWindow()
        {
            if (facilityCraftWindowUI != null)
                facilityCraftWindowUI.Close();
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
                return;

            Debug.Log($"[HideoutFacilityUIController] {message}", this);
        }
    }
}
