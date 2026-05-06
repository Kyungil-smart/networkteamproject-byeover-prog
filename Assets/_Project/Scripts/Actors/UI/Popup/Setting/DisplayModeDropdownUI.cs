using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace DeadZone.Actors.UI
{
    public sealed class DisplayModeDropdownUI : MonoBehaviour
    {
        private const string PlayerPrefsKey = "DisplayMode";
        private const int WindowedIndex = 0;
        private const int FullscreenIndex = 1;
        private const int BorderlessWindowedIndex = 2;

        private static readonly List<string> Options = new List<string>
        {
            "창 모드",
            "전체 화면",
            "테두리 없는 창 모드"
        };

#if ODIN_INSPECTOR
        [Title("Display Mode Dropdown")]
        [Required]
#endif
        [SerializeField] private TMP_Dropdown tmpDropdown;

#if ODIN_INSPECTOR
        [Title("Legacy Fallback")]
#endif
        [SerializeField] private Dropdown legacyDropdown;

        [SerializeField] private Transform popupSettingRoot;

        private bool initialized;

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();
            AddListener();
        }

        private void OnDisable()
        {
            RemoveListener();
        }

        private void Initialize()
        {
            if (!ResolveDropdown())
                return;

            SetupOptions();

            int savedIndex = GetSavedDisplayModeIndex();
            SetDropdownValueWithoutNotify(savedIndex);
            ApplyDisplayMode(savedIndex, save: false);

            initialized = true;
        }

        private bool ResolveDropdown()
        {
            if (tmpDropdown != null || legacyDropdown != null)
                return true;

            Transform dropdownTransform = FindDropdownTransform();
            if (dropdownTransform == null)
            {
                Debug.LogWarning(
                    "[DisplayModeDropdownUI] Popup_Setting child named 'Dropdown' was not found. " +
                    "Assign a TMP_Dropdown or Dropdown in the inspector.",
                    this);
                return false;
            }

            tmpDropdown = dropdownTransform.GetComponent<TMP_Dropdown>();
            legacyDropdown = dropdownTransform.GetComponent<Dropdown>();

            if (tmpDropdown != null || legacyDropdown != null)
                return true;

            Debug.LogWarning(
                "[DisplayModeDropdownUI] The 'Dropdown' object was found, but it has neither TMP_Dropdown nor UnityEngine.UI.Dropdown.",
                dropdownTransform);
            return false;
        }

        private Transform FindDropdownTransform()
        {
            Transform root = popupSettingRoot != null ? popupSettingRoot : transform;
            Transform found = FindChildByName(root, "Dropdown");
            if (found != null)
                return found;

            GameObject popup = FindSceneObjectByName("Popup_Setting");
            return popup != null ? FindChildByName(popup.transform, "Dropdown") : null;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null)
                return null;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                    return children[i];
            }

            return null;
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

        private void SetupOptions()
        {
            if (tmpDropdown != null)
            {
                tmpDropdown.ClearOptions();
                tmpDropdown.AddOptions(Options);
                tmpDropdown.RefreshShownValue();
                return;
            }

            if (legacyDropdown == null)
                return;

            legacyDropdown.ClearOptions();
            legacyDropdown.AddOptions(Options);
            legacyDropdown.RefreshShownValue();
        }

        private void AddListener()
        {
            if (!initialized)
                return;

            RemoveListener();

            if (tmpDropdown != null)
                tmpDropdown.onValueChanged.AddListener(OnDisplayModeChanged);
            else if (legacyDropdown != null)
                legacyDropdown.onValueChanged.AddListener(OnDisplayModeChanged);
        }

        private void RemoveListener()
        {
            if (tmpDropdown != null)
                tmpDropdown.onValueChanged.RemoveListener(OnDisplayModeChanged);

            if (legacyDropdown != null)
                legacyDropdown.onValueChanged.RemoveListener(OnDisplayModeChanged);
        }

        private void SetDropdownValueWithoutNotify(int index)
        {
            if (tmpDropdown != null)
            {
                tmpDropdown.SetValueWithoutNotify(index);
                tmpDropdown.RefreshShownValue();
                return;
            }

            if (legacyDropdown == null)
                return;

            legacyDropdown.SetValueWithoutNotify(index);
            legacyDropdown.RefreshShownValue();
        }

        private static int GetSavedDisplayModeIndex()
        {
            int savedIndex = PlayerPrefs.GetInt(PlayerPrefsKey, BorderlessWindowedIndex);
            return Mathf.Clamp(savedIndex, WindowedIndex, BorderlessWindowedIndex);
        }

        private void OnDisplayModeChanged(int index)
        {
            ApplyDisplayMode(index, save: true);
        }

        private static void ApplyDisplayMode(int index, bool save)
        {
            int modeIndex = Mathf.Clamp(index, WindowedIndex, BorderlessWindowedIndex);

            switch (modeIndex)
            {
                case WindowedIndex:
                    Screen.fullScreenMode = FullScreenMode.Windowed;
                    Screen.fullScreen = false;
                    break;
                case FullscreenIndex:
                    Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                    Screen.fullScreen = true;
                    break;
                case BorderlessWindowedIndex:
                default:
                    Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                    Screen.fullScreen = true;
                    break;
            }

            if (!save)
                return;

            PlayerPrefs.SetInt(PlayerPrefsKey, modeIndex);
            PlayerPrefs.Save();
        }
    }
}
