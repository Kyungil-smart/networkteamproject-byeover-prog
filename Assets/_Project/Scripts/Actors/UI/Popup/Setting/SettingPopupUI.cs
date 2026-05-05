using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public sealed class SettingPopupUI : MonoBehaviour
    {
        private const string CloseButtonName = "Btn_CloseSetting";

        [Header("설정 팝업")]
        [SerializeField] private Button closeButton;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindButtons();
        }

        private void OnDisable()
        {
            UnbindButtons();
        }

        public void Open()
        {
            gameObject.SetActive(true);
            EnsurePopupScale();
            ResolveReferences();
            BindButtons();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        private void BindButtons()
        {
            if (closeButton == null)
                return;

            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        private void UnbindButtons()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
        }

        private void ResolveReferences()
        {
            if (closeButton != null)
                return;

            Transform closeButtonTransform = FindChildByName(transform, CloseButtonName);
            if (closeButtonTransform != null)
                closeButton = closeButtonTransform.GetComponent<Button>();

            if (closeButton == null)
            {
                Debug.LogWarning(
                    "[SettingPopupUI] Btn_CloseSetting Button was not found. Assign closeButton in the inspector.",
                    this);
            }
        }

        private void EnsurePopupScale()
        {
            if (transform.localScale == Vector3.zero)
                transform.localScale = Vector3.one;
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
    }
}
