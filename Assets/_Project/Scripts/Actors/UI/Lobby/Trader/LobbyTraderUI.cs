using DeadZone.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public sealed class LobbyTraderUI : MonoBehaviour
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("상점 루트")]
#else
        [Header("상점 루트")]
#endif
        [Tooltip("트레이더 탭을 눌렀을 때 먼저 보이는 상인 4명 선택 화면 루트입니다.")]
        [SerializeField] private GameObject traderSelectionRoot;

        [Tooltip("상인을 선택한 뒤 구매/판매 리스트가 보이는 상세 거래 화면 루트입니다.")]
        [SerializeField] private GameObject traderDetailRoot;

        [Tooltip("TraderDetailRoot에 붙은 상세 거래 화면 View입니다.")]
        [SerializeField] private TraderPageView traderPageView;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("상인 선택 이미지")]
#else
        [Header("상인 선택 이미지")]
#endif
        [Tooltip("상인 선택 화면에서 크게 보이는 Igor 이미지 오브젝트입니다. 상세 거래 화면에서는 이 오브젝트만 숨깁니다.")]
        [SerializeField] private GameObject igorSelectionImage;

        [Tooltip("상인 선택 화면에서 크게 보이는 Vera 이미지 오브젝트입니다. 상세 거래 화면에서는 이 오브젝트만 숨깁니다.")]
        [SerializeField] private GameObject veraSelectionImage;

        [Tooltip("상인 선택 화면에서 크게 보이는 Doc 이미지 오브젝트입니다. 상세 거래 화면에서는 이 오브젝트만 숨깁니다.")]
        [SerializeField] private GameObject docSelectionImage;

        [Tooltip("상인 선택 화면에서 크게 보이는 Shade 이미지 오브젝트입니다. 상세 거래 화면에서는 이 오브젝트만 숨깁니다.")]
        [SerializeField] private GameObject shadeSelectionImage;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("상인 선택 버튼")]
#else
        [Header("상인 선택 버튼")]
#endif
        [SerializeField] private Button tabIgorButton;
        [SerializeField] private Button tabVeraButton;
        [SerializeField] private Button tabDocButton;
        [SerializeField] private Button tabShadeButton;
        [SerializeField] private Button igorImageButton;
        [SerializeField] private Button veraImageButton;
        [SerializeField] private Button docImageButton;
        [SerializeField] private Button shadeImageButton;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void OnValidate()
        {
            AutoBindReferences();
        }

        private void Awake()
        {
            AutoBindReferences();
            BindButtons();
            OpenTraderDefault();
        }

        private void OnEnable()
        {
            OpenTraderDefault();
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }

        public void OpenTraderDefault()
        {
            SetActiveSafe(traderSelectionRoot, true);
            SetActiveSafe(traderDetailRoot, false);
            SetSelectionImagesVisible(true);
            ClearTraderTabState();

            if (traderPageView != null)
                traderPageView.Close();
        }

        public void SelectIgor()
        {
            SelectTrader(TraderId.Igor);
        }

        public void SelectVera()
        {
            SelectTrader(TraderId.Vera);
        }

        public void SelectDoc()
        {
            SelectTrader(TraderId.Doc);
        }

        public void SelectShade()
        {
            SelectTrader(TraderId.Shade);
        }

        private void SelectTrader(TraderId traderId)
        {
            Debug.Log($"[LobbyTraderUI] 상인 선택 요청: {traderId}", this);

            SetActiveSafe(traderSelectionRoot, true);
            SetActiveSafe(traderDetailRoot, true);
            SetSelectionImagesVisible(false);
            ApplyTraderTabState(traderId);

            if (traderPageView == null)
            {
                Debug.LogWarning("[LobbyTraderUI] TraderPageView가 연결되지 않았습니다. TraderDetailRoot의 TraderPageView를 연결해야 합니다.", this);
                return;
            }

            traderPageView.SelectTrader(traderId);
        }

        private void BindButtons()
        {
            UnbindButtons();

            AddClickListener(tabIgorButton, SelectIgor);
            AddClickListener(tabVeraButton, SelectVera);
            AddClickListener(tabDocButton, SelectDoc);
            AddClickListener(tabShadeButton, SelectShade);

            AddClickListener(igorImageButton, SelectIgor);
            AddClickListener(veraImageButton, SelectVera);
            AddClickListener(docImageButton, SelectDoc);
            AddClickListener(shadeImageButton, SelectShade);
        }

        private void UnbindButtons()
        {
            RemoveClickListener(tabIgorButton, SelectIgor);
            RemoveClickListener(tabVeraButton, SelectVera);
            RemoveClickListener(tabDocButton, SelectDoc);
            RemoveClickListener(tabShadeButton, SelectShade);

            RemoveClickListener(igorImageButton, SelectIgor);
            RemoveClickListener(veraImageButton, SelectVera);
            RemoveClickListener(docImageButton, SelectDoc);
            RemoveClickListener(shadeImageButton, SelectShade);
        }

        private void AutoBindReferences()
        {
            if (traderSelectionRoot == null)
                traderSelectionRoot = FindChildObject("TraderSelectionRoot", "Selection");

            if (traderDetailRoot == null)
                traderDetailRoot = FindChildObject("TraderDetailRoot", "TraderPageView", "Detail");

            if (traderPageView == null)
            {
                if (traderDetailRoot != null)
                    traderPageView = traderDetailRoot.GetComponentInChildren<TraderPageView>(true);

                if (traderPageView == null)
                    traderPageView = GetComponentInChildren<TraderPageView>(true);
            }
            else if (traderDetailRoot != null && !traderPageView.transform.IsChildOf(traderDetailRoot.transform))
            {
                TraderPageView detailView = traderDetailRoot.GetComponentInChildren<TraderPageView>(true);
                if (detailView != null)
                    traderPageView = detailView;
            }

            if (igorSelectionImage == null)
                igorSelectionImage = FindSelectionImage("Img_Igor", "Img_lgor", "Igor");

            if (veraSelectionImage == null)
                veraSelectionImage = FindSelectionImage("Img_Vera", "Vera");

            if (docSelectionImage == null)
                docSelectionImage = FindSelectionImage("Img_Doc", "Doc");

            if (shadeSelectionImage == null)
                shadeSelectionImage = FindSelectionImage("Img_Shade", "Shade");

            if (tabIgorButton == null)
                tabIgorButton = FindButtonByObjectName("Tab_Igor", "Tab_lgor");

            if (tabVeraButton == null)
                tabVeraButton = FindButtonByObjectName("Tab_Vera");

            if (tabDocButton == null)
                tabDocButton = FindButtonByObjectName("Tab_Doc");

            if (tabShadeButton == null)
                tabShadeButton = FindButtonByObjectName("Tab_Shade");

            if (igorImageButton == null)
                igorImageButton = FindButtonByObjectName("Img_Igor", "Img_lgor");

            if (veraImageButton == null)
                veraImageButton = FindButtonByObjectName("Img_Vera");

            if (docImageButton == null)
                docImageButton = FindButtonByObjectName("Img_Doc");

            if (shadeImageButton == null)
                shadeImageButton = FindButtonByObjectName("Img_Shade");
        }

        private void SetSelectionImagesVisible(bool visible)
        {
            SetSelectionImageVisible(tabIgorButton, igorSelectionImage, visible, "Img_Igor", "Img_lgor");
            SetSelectionImageVisible(tabVeraButton, veraSelectionImage, visible, "Img_Vera");
            SetSelectionImageVisible(tabDocButton, docSelectionImage, visible, "Img_Doc");
            SetSelectionImageVisible(tabShadeButton, shadeSelectionImage, visible, "Img_Shade");
        }

        private void SetSelectionImageVisible(Button tabButton, GameObject configuredTarget, bool visible, params string[] imageNames)
        {
            GameObject target = ResolveSelectionImageObject(tabButton, configuredTarget, imageNames);
            if (target == null)
                return;

            target.SetActive(visible);
        }

        private GameObject ResolveSelectionImageObject(Button tabButton, GameObject configuredTarget, string[] imageNames)
        {
            if (IsSelectionImageObject(configuredTarget, imageNames))
                return configuredTarget;

            if (tabButton != null)
            {
                GameObject foundInTab = FindNamedChildObject(tabButton.transform, imageNames);
                if (foundInTab != null)
                    return foundInTab;
            }

            return FindNamedChildObject(transform, imageNames);
        }

        private static bool IsSelectionImageObject(GameObject target, string[] imageNames)
        {
            if (target == null || imageNames == null)
                return false;

            for (int i = 0; i < imageNames.Length; i++)
            {
                if (target.name == imageNames[i])
                    return true;
            }

            return false;
        }

        private static GameObject FindNamedChildObject(Transform root, string[] objectNames)
        {
            if (root == null || objectNames == null)
                return null;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == root)
                    continue;

                for (int nameIndex = 0; nameIndex < objectNames.Length; nameIndex++)
                {
                    if (child.name == objectNames[nameIndex])
                        return child.gameObject;
                }
            }

            return null;
        }

        private void ApplyTraderTabState(TraderId selectedTraderId)
        {
            SetTraderTabState(tabIgorButton, selectedTraderId == TraderId.Igor);
            SetTraderTabState(tabVeraButton, selectedTraderId == TraderId.Vera);
            SetTraderTabState(tabDocButton, selectedTraderId == TraderId.Doc);
            SetTraderTabState(tabShadeButton, selectedTraderId == TraderId.Shade);
        }

        private void ClearTraderTabState()
        {
            SetTraderTabState(tabIgorButton, false);
            SetTraderTabState(tabVeraButton, false);
            SetTraderTabState(tabDocButton, false);
            SetTraderTabState(tabShadeButton, false);
        }

        private static void SetTraderTabState(Button tabButton, bool selected)
        {
            if (tabButton == null)
                return;

            tabButton.gameObject.SetActive(true);
            tabButton.interactable = true;

            Transform tabTransform = tabButton.transform;
            SetNamedChildActive(tabTransform, "Text_Tab", true);
            SetNamedChildActive(tabTransform, "Underline_Image", selected);
        }

        private static void SetNamedChildActive(Transform root, string nameToken, bool active)
        {
            if (root == null)
                return;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == root)
                    continue;

                if (child.name.Contains(nameToken))
                    child.gameObject.SetActive(active);
            }
        }

        private GameObject FindSelectionImage(params string[] nameTokens)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null)
                    continue;

                string lowerName = image.name.ToLowerInvariant();
                if (!lowerName.Contains("img"))
                    continue;

                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return image.gameObject;
                }
            }

            return null;
        }

        private Button FindButtonByObjectName(params string[] objectNames)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null)
                    continue;

                for (int nameIndex = 0; nameIndex < objectNames.Length; nameIndex++)
                {
                    if (button.name == objectNames[nameIndex])
                        return button;
                }
            }

            return null;
        }

        private static void AddClickListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
                button.onClick.AddListener(action);
        }

        private static void RemoveClickListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
                button.onClick.RemoveListener(action);
        }

        private GameObject FindChildObject(params string[] nameTokens)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == transform)
                    continue;

                string lowerName = child.name.ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return child.gameObject;
                }
            }

            return null;
        }

        private Button FindButton(params string[] nameTokens)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null)
                    continue;

                string lowerName = GetHierarchyText(button.transform).ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return button;
                }
            }

            return null;
        }

        private static string GetHierarchyText(Transform target)
        {
            if (target == null)
                return string.Empty;

            string text = target.name;
            TMPro.TMP_Text[] labels = target.GetComponentsInChildren<TMPro.TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null)
                    text += " " + labels[i].text;
            }

            return text;
        }

        private static void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null)
                target.SetActive(active);
        }
    }
}
