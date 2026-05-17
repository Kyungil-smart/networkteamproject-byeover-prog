using DeadZone.Actors.Player;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class CharacterCustomizePopupUI : MonoBehaviour
    {
        private const string BodyPrefsKey = "DZ_Custom_Body";
        private const string HeadPrefsKey = "DZ_Custom_Head";
        private const string BeardPrefsKey = "DZ_Custom_Beard";
        private const string HatPrefsKey = "DZ_Custom_Hat";

        [Header("Root")]
        [SerializeField] private GameObject popupRoot;

        [Header("Text")]
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private string defaultPlayerName = "Ock";

        [Header("Option Rows")]
        [SerializeField] private CustomizeOptionRowUI bodyRow;
        [SerializeField] private CustomizeOptionRowUI headRow;
        [SerializeField] private CustomizeOptionRowUI beardRow;
        [SerializeField] private CustomizeOptionRowUI hatRow;

        [Header("Buttons")]
        [SerializeField] private Button randomButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button saveButton;

        [Header("Preview")]
        [SerializeField] private CharacterCustomizePreview preview;

        public event UnityAction<CharacterCustomizeData> Saved;

        private CharacterCustomizeData savedData;
        private CharacterCustomizeData tempData;
        private string currentPlayerName;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void Awake()
        {
            AutoBindReferences();
            BindButtonListeners();

            if (string.IsNullOrWhiteSpace(currentPlayerName))
                currentPlayerName = defaultPlayerName;
        }

        private void OnEnable()
        {
            BindButtonListeners();
        }

        private void OnDisable()
        {
            UnbindButtonListeners();
        }

        private void OnDestroy()
        {
            UnbindButtonListeners();
        }

        public void Open()
        {
            Open(defaultPlayerName);
        }

        public void Open(string playerName)
        {
            currentPlayerName = string.IsNullOrWhiteSpace(playerName) ? defaultPlayerName : playerName;

            if (popupRoot != null)
                popupRoot.SetActive(true);
            else
                Debug.LogWarning($"[CharacterCustomizePopupUI] popupRoot is not assigned. Object={name}", this);

            ConfigureRowFallbackCounts();
            BindButtonListeners();
            LoadSavedData();
            tempData = savedData;
            RefreshAll();
        }

        public void Close()
        {
            if (popupRoot != null)
                popupRoot.SetActive(false);
            else
                Debug.LogWarning($"[CharacterCustomizePopupUI] popupRoot is not assigned. Object={name}", this);
        }

        private void LoadSavedData()
        {
            CharacterCustomizeNetworkData savedNetworkData =
                PlayerCharacterCustomizeState.LoadLocalSavedCustomizeData();

            savedData = new CharacterCustomizeData(
                Mathf.Clamp(savedNetworkData.BodyIndex, 0, GetOptionCount(bodyRow) - 1),
                Mathf.Clamp(savedNetworkData.HeadIndex, 0, GetOptionCount(headRow) - 1),
                Mathf.Clamp(savedNetworkData.BeardIndex, 0, GetOptionCount(beardRow) - 1),
                Mathf.Clamp(savedNetworkData.HatIndex, 0, GetOptionCount(hatRow) - 1));
        }

        private void SaveCurrentData()
        {
            tempData = ClampData(tempData);
            savedData = tempData;

            PlayerCharacterCustomizeState.SaveLocalCustomizeData(
                new CharacterCustomizeNetworkData(
                    savedData.bodyIndex,
                    savedData.headIndex,
                    savedData.beardIndex,
                    savedData.hatIndex));

            SubmitSavedDataToLocalPlayer();
            ApplyPreview();
            Saved?.Invoke(savedData);
            Close();
        }

        private void RefreshAll()
        {
            tempData = ClampData(tempData);

            if (headerText != null)
                headerText.text = $"{currentPlayerName}\uB2D8\uC758 \uCE90\uB9AD\uD130";
            else
                Debug.LogWarning($"[CharacterCustomizePopupUI] headerText is not assigned. Object={name}", this);

            if (bodyRow != null)
                bodyRow.SetIndex(tempData.bodyIndex);
            else
                Debug.LogWarning($"[CharacterCustomizePopupUI] bodyRow is not assigned. Object={name}", this);

            if (headRow != null)
                headRow.SetIndex(tempData.headIndex);
            else
                Debug.LogWarning($"[CharacterCustomizePopupUI] headRow is not assigned. Object={name}", this);

            if (beardRow != null)
                beardRow.SetIndex(tempData.beardIndex);
            else
                Debug.LogWarning($"[CharacterCustomizePopupUI] beardRow is not assigned. Object={name}", this);

            if (hatRow != null)
                hatRow.SetIndex(tempData.hatIndex);

            ApplyPreview();
        }

        private void RandomizeAll()
        {
            tempData = new CharacterCustomizeData(
                Random.Range(0, GetOptionCount(bodyRow)),
                Random.Range(0, GetOptionCount(headRow)),
                Random.Range(0, GetOptionCount(beardRow)),
                Random.Range(0, GetOptionCount(hatRow)));

            RefreshAll();
        }

        private void ChangeBody(int delta)
        {
            tempData.bodyIndex = WrapIndex(tempData.bodyIndex + delta, GetOptionCount(bodyRow));
            RefreshAll();
        }

        private void ChangeHead(int delta)
        {
            tempData.headIndex = WrapIndex(tempData.headIndex + delta, GetOptionCount(headRow));
            RefreshAll();
        }

        private void ChangeBeard(int delta)
        {
            tempData.beardIndex = WrapIndex(tempData.beardIndex + delta, GetOptionCount(beardRow));
            RefreshAll();
        }

        private void ChangeHat(int delta)
        {
            tempData.hatIndex = WrapIndex(tempData.hatIndex + delta, GetOptionCount(hatRow));
            RefreshAll();
        }

        private void Cancel()
        {
            tempData = savedData;
            Close();
        }

        private void BindButtonListeners()
        {
            ConfigureRowFallbackCounts();

            if (bodyRow != null)
                bodyRow.Init(() => ChangeBody(-1), () => ChangeBody(1));

            if (headRow != null)
                headRow.Init(() => ChangeHead(-1), () => ChangeHead(1));

            if (beardRow != null)
                beardRow.Init(() => ChangeBeard(-1), () => ChangeBeard(1));

            if (hatRow != null)
                hatRow.Init(() => ChangeHat(-1), () => ChangeHat(1));

            if (randomButton != null)
            {
                randomButton.onClick.RemoveListener(RandomizeAll);
                randomButton.onClick.AddListener(RandomizeAll);
            }
            else
            {
                Debug.LogWarning($"[CharacterCustomizePopupUI] randomButton is not assigned. Object={name}", this);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(Cancel);
                cancelButton.onClick.AddListener(Cancel);
            }
            else
            {
                Debug.LogWarning($"[CharacterCustomizePopupUI] cancelButton is not assigned. Object={name}", this);
            }

            if (saveButton != null)
            {
                saveButton.onClick.RemoveListener(SaveCurrentData);
                saveButton.onClick.AddListener(SaveCurrentData);
            }
            else
            {
                Debug.LogWarning($"[CharacterCustomizePopupUI] saveButton is not assigned. Object={name}", this);
            }
        }

        private void UnbindButtonListeners()
        {
            if (randomButton != null)
                randomButton.onClick.RemoveListener(RandomizeAll);

            if (cancelButton != null)
                cancelButton.onClick.RemoveListener(Cancel);

            if (saveButton != null)
                saveButton.onClick.RemoveListener(SaveCurrentData);
        }

        private void ApplyPreview()
        {
            if (preview != null)
                preview.Apply(tempData);
            else
                Debug.LogWarning($"[CharacterCustomizePopupUI] preview is not assigned. Object={name}", this);
        }

        private void SubmitSavedDataToLocalPlayer()
        {
            PlayerCharacterCustomizeState[] customizeStates = FindObjectsByType<PlayerCharacterCustomizeState>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < customizeStates.Length; i++)
            {
                PlayerCharacterCustomizeState customizeState = customizeStates[i];
                if (customizeState == null || !customizeState.IsOwner)
                    continue;

                customizeState.SubmitLocalSavedCustomizeData();
                return;
            }
        }

        private void AutoBindReferences()
        {
            if (popupRoot == null)
                popupRoot = gameObject;

            if (preview == null)
                preview = GetComponentInChildren<CharacterCustomizePreview>(true);
        }

        private void ConfigureRowFallbackCounts()
        {
            if (preview == null)
                return;

            if (bodyRow != null)
                bodyRow.SetFallbackOptionCount(preview.BodyOptionCount);

            if (headRow != null)
                headRow.SetFallbackOptionCount(preview.HeadOptionCount);

            if (beardRow != null)
                beardRow.SetFallbackOptionCount(preview.BeardOptionCount);

            if (hatRow != null)
                hatRow.SetFallbackOptionCount(preview.HatOptionCount);
        }

        private CharacterCustomizeData ClampData(CharacterCustomizeData data)
        {
            return new CharacterCustomizeData(
                Mathf.Clamp(data.bodyIndex, 0, GetOptionCount(bodyRow) - 1),
                Mathf.Clamp(data.headIndex, 0, GetOptionCount(headRow) - 1),
                Mathf.Clamp(data.beardIndex, 0, GetOptionCount(beardRow) - 1),
                Mathf.Clamp(data.hatIndex, 0, GetOptionCount(hatRow) - 1));
        }

        private static int GetOptionCount(CustomizeOptionRowUI row)
        {
            return Mathf.Max(1, row != null ? row.GetOptionCount() : 1);
        }

        private static int WrapIndex(int index, int count)
        {
            count = Mathf.Max(1, count);
            index %= count;

            if (index < 0)
                index += count;

            return index;
        }
    }
}
