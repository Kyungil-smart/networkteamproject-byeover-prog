using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DeadZone.Actors.UI
{
    public class InGameMenuUI : MonoBehaviour
    {
        private const string SettingPopupName = "Popup_Setting";

        [Header("팝업")]
        [SerializeField] private GameObject popupPause;
        [SerializeField] private GameObject popupSetting;
        [SerializeField] private SettingPopupUI settingPopupUI;

        [Header("씬")]
        [SerializeField] private string lobbySceneName = "HJO_Lobby";

        [Header("마우스 설정")]
        [SerializeField] private bool lockCursorOnResume = true;

        public bool IsMenuOpen => IsPopupVisible(popupPause);

        public bool BlocksOtherInput => IsMenuOpen || IsSettingOpen();

        private void Awake()
        {
            ResolvePopupReferences();
            CloseSetting();
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

            CursorStateController.PushUiOwner(this);
        }

        public void ResumeGame()
        {
            SetPopupVisible(popupSetting, false);
            SetPopupVisible(popupPause, false);

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
            CursorStateController.PushUiOwner(this);
        }

        public void CloseSetting()
        {
            ResolvePopupReferences();

            if (settingPopupUI != null)
                settingPopupUI.Close();
            else
                SetPopupVisible(popupSetting, false);
        }

        public void ExitToLobby()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            Time.timeScale = 1f;
            LoadingScreenService.LoadSceneOrFallback(lobbySceneName);
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
    }
}
