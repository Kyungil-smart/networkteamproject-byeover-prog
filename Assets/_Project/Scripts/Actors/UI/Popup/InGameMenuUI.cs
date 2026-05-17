using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.Threading.Tasks;

using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems.Raid;
using DeadZone.Systems.Save;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DeadZone.Actors.UI
{
    public class InGameMenuUI : MonoBehaviour
    {
        private const string SettingPopupName = "Popup_Setting";
        private const string DefaultLobbySceneName = "Lobby";

        [Header("팝업")]
        [SerializeField] private GameObject popupPause;
        [SerializeField] private GameObject popupSetting;
        [SerializeField] private SettingPopupUI settingPopupUI;

        [Header("씬")]
        [SerializeField] private string lobbySceneName = DefaultLobbySceneName;

        [Header("마우스 설정")]
        [SerializeField] private bool lockCursorOnResume = true;

        [Header("Raid Abandon Warning")]
        [SerializeField] private bool warnBeforeRaidAbandon = true;
        [SerializeField, TextArea] private string raidAbandonWarningMessage =
            "로비로 돌아가면 이번 레이드에서 보유 중인 장비, 인벤토리, 퀵슬롯을 모두 잃습니다.\n정말 로비로 돌아가시겠습니까?";
        [SerializeField] private GameObject raidAbandonWarningPopup;
        [SerializeField] private Text raidAbandonWarningText;
        [SerializeField] private Button confirmRaidAbandonButton;
        [SerializeField] private Button cancelRaidAbandonButton;

        private bool isExitingToLobby;

        public bool IsMenuOpen => IsPopupVisible(popupPause);

        public bool BlocksOtherInput => IsMenuOpen || IsSettingOpen() || IsPopupVisible(raidAbandonWarningPopup);

        private void Awake()
        {
            ResolvePopupReferences();
            BindRaidAbandonWarningButtons();
            CloseSetting();
        }

        private void OnDisable()
        {
            SetPopupVisible(raidAbandonWarningPopup, false);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Pause, false);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Setting, false);
            CursorStateController.PopUiOwner(this);
        }

        public void HandleEscapeInput()
        {
            if (IsSettingOpen())
            {
                CloseSetting();
                return;
            }

            if (IsMenuOpen)
                ResumeGame();
            else
                OpenPauseMenu();
        }

        public void OpenPauseMenu()
        {
            SetPopupVisible(popupPause, true);
            EnsurePopupScale(popupPause);
            BringPopupToFront(popupPause);

            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Pause, true);
            CursorStateController.PushUiOwner(this);
        }

        public void ResumeGame()
        {
            SetPopupVisible(popupSetting, false);
            SetPopupVisible(raidAbandonWarningPopup, false);
            SetPopupVisible(popupPause, false);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Setting, false);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Pause, false);

            if (lockCursorOnResume)
                CursorStateController.PopUiOwner(this);
        }

        public void OpenSetting()
        {
            ResolvePopupReferences();

            if (settingPopupUI != null)
                settingPopupUI.Open();
            else
                SetPopupVisible(popupSetting, true);

            BringPopupToFront(popupSetting);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Setting, true);
            CursorStateController.PushUiOwner(this);
        }

        public void CloseSetting()
        {
            ResolvePopupReferences();

            if (settingPopupUI != null)
                settingPopupUI.Close();
            else
                SetPopupVisible(popupSetting, false);

            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Setting, false);
        }

        public void ExitToLobby()
        {
            if (isExitingToLobby)
                return;

            if (warnBeforeRaidAbandon)
            {
                ShowRaidAbandonWarning();
                return;
            }

            ConfirmExitToLobby();
        }

        public async void ConfirmExitToLobby()
        {
            if (isExitingToLobby)
                return;

            isExitingToLobby = true;
            SetPopupVisible(raidAbandonWarningPopup, false);

            await SaveAbandonedRaidStateAsync();

            string targetScene = NormalizeLobbySceneName(lobbySceneName);
            Debug.Log($"[InGameMenu] ExitToLobby confirmed. targetScene={targetScene}", this);

            Time.timeScale = 1f;
            NetworkGameManager.RequestReturnToLobbyAfterRaid(targetScene);
        }

        public void CancelExitToLobby()
        {
            if (isExitingToLobby)
                return;

            SetPopupVisible(raidAbandonWarningPopup, false);
        }

        public void ExitGame()
        {
            Time.timeScale = 1f;

#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ShowRaidAbandonWarning()
        {
            EnsureRaidAbandonWarningPopup();
            BindRaidAbandonWarningButtons();

            if (raidAbandonWarningText != null)
                raidAbandonWarningText.text = raidAbandonWarningMessage;

            SetPopupVisible(raidAbandonWarningPopup, true);
            BringPopupToFront(raidAbandonWarningPopup);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Pause, true);
            CursorStateController.PushUiOwner(this);

            Debug.LogWarning("[InGameMenu] 로비 복귀 확인 대기 중입니다. 확인하면 이번 레이드 장비/인벤토리/퀵슬롯을 잃습니다.", this);
        }

        private async Task SaveAbandonedRaidStateAsync()
        {
            if (!RaidLoadoutTransferService.TryCreateAbandonedRaidLobbySaveDTO(out LobbySaveDTO abandonedSaveDto))
            {
                Debug.LogWarning("[InGameMenu] 레이드 포기 저장 DTO를 생성하지 못했습니다.", this);
                return;
            }

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            if (cloudSaveSystem == null)
            {
                Debug.LogWarning("[InGameMenu] CloudSaveSystem을 찾지 못해 레이드 포기 상태를 클라우드에 저장하지 못했습니다.", this);
                RaidLoadoutTransferService.ClearLocalClientLoadout();
                return;
            }

            bool saved = await cloudSaveSystem.SaveLobbyDataAsync(abandonedSaveDto);
            if (!saved)
            {
                Debug.LogWarning("[InGameMenu] 레이드 포기 상태 저장에 실패했습니다. 클라우드 상태가 이전 장비를 유지할 수 있습니다.", this);
            }
            else
            {
                Debug.Log("[InGameMenu] 레이드 포기 상태 저장 완료. 장비/인벤토리/퀵슬롯을 비웠습니다.", this);
            }

            RaidLoadoutTransferService.ClearLocalClientLoadout();
        }

        private void EnsureRaidAbandonWarningPopup()
        {
            if (raidAbandonWarningPopup != null)
                return;

            Transform parent = popupPause != null && popupPause.transform.parent != null
                ? popupPause.transform.parent
                : transform;

            raidAbandonWarningPopup = new GameObject("Popup_RaidAbandonWarning", typeof(RectTransform));
            raidAbandonWarningPopup.transform.SetParent(parent, false);

            RectTransform rootRect = raidAbandonWarningPopup.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image dim = raidAbandonWarningPopup.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);

            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(raidAbandonWarningPopup.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 280f);
            panel.GetComponent<Image>().color = new Color(0.03f, 0.07f, 0.13f, 0.96f);

            raidAbandonWarningText = CreatePopupText(panel.transform, "Text_Warning", raidAbandonWarningMessage, 24, TextAnchor.MiddleCenter);
            RectTransform textRect = raidAbandonWarningText.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.08f, 0.36f);
            textRect.anchorMax = new Vector2(0.92f, 0.86f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            confirmRaidAbandonButton = CreatePopupButton(panel.transform, "Btn_Confirm", "확인", new Vector2(-95f, -92f));
            cancelRaidAbandonButton = CreatePopupButton(panel.transform, "Btn_Cancel", "취소", new Vector2(95f, -92f));

            raidAbandonWarningPopup.SetActive(false);
        }

        private Text CreatePopupText(
            Transform parent,
            string objectName,
            string text,
            int fontSize,
            TextAnchor alignment)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            Text textComponent = textObject.GetComponent<Text>();
            textComponent.text = text;
            textComponent.font = ResolveDefaultFont();
            textComponent.fontSize = fontSize;
            textComponent.alignment = alignment;
            textComponent.color = Color.white;
            textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            return textComponent;
        }

        private Button CreatePopupButton(Transform parent, string objectName, string label, Vector2 anchoredPosition)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = anchoredPosition;
            buttonRect.sizeDelta = new Vector2(150f, 54f);

            buttonObject.GetComponent<Image>().color = new Color(0.05f, 0.42f, 0.74f, 0.95f);

            Text labelText = CreatePopupText(buttonObject.transform, "Text", label, 22, TextAnchor.MiddleCenter);
            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return buttonObject.GetComponent<Button>();
        }

        private Font ResolveDefaultFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            return font;
        }

        private void BindRaidAbandonWarningButtons()
        {
            if (confirmRaidAbandonButton != null)
            {
                confirmRaidAbandonButton.onClick.RemoveListener(ConfirmExitToLobby);
                confirmRaidAbandonButton.onClick.AddListener(ConfirmExitToLobby);
            }

            if (cancelRaidAbandonButton != null)
            {
                cancelRaidAbandonButton.onClick.RemoveListener(CancelExitToLobby);
                cancelRaidAbandonButton.onClick.AddListener(CancelExitToLobby);
            }
        }

        private CloudSaveSystem ResolveCloudSaveSystem()
        {
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem == null)
                cloudSaveSystem = FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);

            return cloudSaveSystem;
        }

        public static bool IsAnyMenuBlockingInput()
        {
            InGameMenuUI[] menus = Resources.FindObjectsOfTypeAll<InGameMenuUI>();
            for (int i = 0; i < menus.Length; i++)
            {
                InGameMenuUI menu = menus[i];
                if (menu != null && menu.gameObject.scene.IsValid() && menu.BlocksOtherInput)
                    return true;
            }

            return false;
        }

        private bool IsPopupVisible(GameObject popup)
        {
            if (popup == null)
                return false;

            if (!popup.activeSelf)
                return false;

            if (!IsSelfPopup(popup))
                return popup.activeSelf;

            Canvas canvas = popup.GetComponent<Canvas>();
            return canvas != null ? canvas.enabled : popup.activeSelf;
        }

        private void SetPopupVisible(GameObject popup, bool visible)
        {
            if (popup == null)
                return;

            if (!IsSelfPopup(popup))
            {
                popup.SetActive(visible);
                if (visible)
                    EnsurePopupScale(popup);
                return;
            }

            if (!popup.activeSelf)
            {
                popup.SetActive(visible);
                if (visible)
                    EnsurePopupScale(popup);
                return;
            }

            if (!visible)
            {
                popup.SetActive(false);
                return;
            }

            Canvas[] canvases = popup.GetComponentsInChildren<Canvas>(true);
            GraphicRaycaster[] raycasters = popup.GetComponentsInChildren<GraphicRaycaster>(true);

            if (canvases.Length == 0)
            {
                Debug.LogWarning("[InGameMenuUI] popupPause is assigned to this GameObject, but no Canvas was found. ESC input will stop if this object is disabled.", this);
                return;
            }

            for (int i = 0; i < canvases.Length; i++)
                canvases[i].enabled = visible;

            for (int i = 0; i < raycasters.Length; i++)
                raycasters[i].enabled = visible;
        }

        private bool IsSelfPopup(GameObject popup)
        {
            return popup == gameObject;
        }

        private void EnsurePopupScale(GameObject popup)
        {
            if (popup != null && popup.transform.localScale == Vector3.zero)
                popup.transform.localScale = Vector3.one;
        }

        private void BringPopupToFront(GameObject popup)
        {
            if (popup == null)
                return;

            popup.transform.SetAsLastSibling();
            EnsureTopSortingCanvas(popup);
        }

        private void EnsureTopSortingCanvas(GameObject popup)
        {
            Canvas canvas = popup.GetComponent<Canvas>();
            if (canvas == null)
                canvas = popup.AddComponent<Canvas>();

            canvas.overrideSorting = true;

            if (popup.GetComponent<GraphicRaycaster>() == null)
                popup.AddComponent<GraphicRaycaster>();
        }

        private bool IsSettingOpen()
        {
            ResolvePopupReferences();
            return settingPopupUI != null ? settingPopupUI.IsOpen : IsPopupVisible(popupSetting);
        }

        private void ResolvePopupReferences()
        {
            if (popupSetting == null)
                popupSetting = FindSceneObjectByName(SettingPopupName);

            if (settingPopupUI == null && popupSetting != null)
                settingPopupUI = popupSetting.GetComponent<SettingPopupUI>();
        }

        private static GameObject FindSceneObjectByName(string objectName)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate.name == objectName && candidate.scene.IsValid())
                    return candidate;
            }

            return null;
        }

        private static string NormalizeLobbySceneName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return DefaultLobbySceneName;

            return string.Equals(sceneName, "HJO_Lobby", System.StringComparison.Ordinal)
                ? DefaultLobbySceneName
                : sceneName;
        }
    }
}
