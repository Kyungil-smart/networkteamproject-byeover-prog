using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class CustomizeOptionRowUI : MonoBehaviour
    {
        [Header("Text")]
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text valueText;

        [Header("Buttons")]
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        [Header("Options")]
        [SerializeField] private string[] optionNames;

        private int currentIndex;
        private int fallbackOptionCount = 1;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void OnValidate()
        {
            currentIndex = NormalizeIndex(currentIndex);
            AutoBindReferences();
            RefreshText();
        }

        public void Init(Action onPrevious, Action onNext)
        {
            AutoBindReferences();

            if (previousButton != null)
            {
                previousButton.onClick.RemoveAllListeners();

                if (onPrevious != null)
                    previousButton.onClick.AddListener(() => onPrevious());
            }
            else
            {
                Debug.LogWarning($"[CustomizeOptionRowUI] Previous button is not assigned. Object={name}", this);
            }

            if (nextButton != null)
            {
                nextButton.onClick.RemoveAllListeners();

                if (onNext != null)
                    nextButton.onClick.AddListener(() => onNext());
            }
            else
            {
                Debug.LogWarning($"[CustomizeOptionRowUI] Next button is not assigned. Object={name}", this);
            }

            RefreshText();
        }

        public void SetIndex(int index)
        {
            currentIndex = NormalizeIndex(index);
            RefreshText();
        }

        public int GetOptionCount()
        {
            return optionNames != null && optionNames.Length > 0 ? optionNames.Length : Mathf.Max(1, fallbackOptionCount);
        }

        public string GetCurrentOptionName()
        {
            if (optionNames == null || optionNames.Length == 0)
                return $"Option {currentIndex + 1}";

            int safeIndex = Mathf.Clamp(currentIndex, 0, optionNames.Length - 1);
            string optionName = optionNames[safeIndex];
            return string.IsNullOrWhiteSpace(optionName) ? $"Option {safeIndex + 1}" : optionName;
        }

        public void RefreshText()
        {
            if (valueText != null)
                valueText.text = GetCurrentOptionName();
        }

        public void SetFallbackOptionCount(int count)
        {
            fallbackOptionCount = Mathf.Max(1, count);
            currentIndex = NormalizeIndex(currentIndex);
            RefreshText();
        }

        private void AutoBindReferences()
        {
            if (valueText == null)
                valueText = FindValueText();

            if (previousButton == null)
                previousButton = FindButtonByName("pre", "prev", "previous");

            if (nextButton == null)
                nextButton = FindButtonByName("next");
        }

        private TMP_Text FindValueText()
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);

            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null || texts[i] == labelText)
                    continue;

                string textName = texts[i].name.ToLowerInvariant();
                Transform parent = texts[i].transform.parent;
                string parentName = parent != null ? parent.name.ToLowerInvariant() : string.Empty;

                if (textName.Contains("value") || textName.Contains("select") || parentName.Contains("bg"))
                    return texts[i];
            }

            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i] != labelText)
                    return texts[i];
            }

            return null;
        }

        private Button FindButtonByName(params string[] keywords)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                    continue;

                string buttonName = buttons[i].name.ToLowerInvariant();

                for (int j = 0; j < keywords.Length; j++)
                {
                    if (buttonName.Contains(keywords[j]))
                        return buttons[i];
                }
            }

            return null;
        }

        private int NormalizeIndex(int index)
        {
            int count = GetOptionCount();

            if (count <= 0)
                return 0;

            index %= count;

            if (index < 0)
                index += count;

            return index;
        }
    }
}
