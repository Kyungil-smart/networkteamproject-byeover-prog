using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// Legacy prompt-only component.
    /// Runtime HP, stamina, HP feedback, and knocked HP color are owned by HUDManager.
    /// </summary>
    public class PlayerStatsUI : MonoBehaviour
    {
        [FoldoutGroup("Interact Prompt")]
        [SerializeField] private GameObject interactPromptRoot;

        [FoldoutGroup("Interact Prompt")]
        [SerializeField] private TMP_Text interactPromptText;

        private void OnEnable()
        {
            ShowInteractPrompt(string.Empty);
        }

        public void ShowInteractPrompt(string text)
        {
            if (interactPromptRoot != null)
                interactPromptRoot.SetActive(!string.IsNullOrEmpty(text));

            if (interactPromptText != null)
                interactPromptText.text = text;
        }

#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button("상호작용 문구 보이기")]
        private void TestShowPrompt() => ShowInteractPrompt("[E] 상호작용");

        [TitleGroup("Debug")]
        [Button("상호작용 문구 숨기기")]
        private void TestHidePrompt() => ShowInteractPrompt(string.Empty);
#endif
    }
}
