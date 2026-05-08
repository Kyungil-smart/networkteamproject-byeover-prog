using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DeadZone.Actors.UI
{
    public class LobbyTabController : MonoBehaviour
    {
        private static readonly string[][] TabKeywords =
        {
            new[] { "party", "파티" },
            new[] { "inventory", "인벤토리" },
            new[] { "craft", "제작" },
            new[] { "trader", "트레이더" },
            new[] { "quest", "퀘스트" },
            new[] { "facility", "시설" }
        };

        private static readonly string[] PageNames =
        {
            "Page_Party",
            "Page_Inventory",
            "Page_Craft",
            "Page_Trader",
            "Page_Quest",
            "Page_Facility"
        };

        [Title("탭 목록")]
        [Required]
        [SerializeField] private LobbyTabButtonUI[] tabs;

        [Title("페이지 목록")]
        [Required]
        [SerializeField] private GameObject[] pages;

        [Title("기본 탭")]
        [SerializeField] private int defaultTabIndex = 0;

        [Title("자동 연결")]
        [SerializeField] private bool autoCollectTabsInScene = true;

        [SerializeField] private bool autoCollectPagesInScene = true;

        [SerializeField] private bool autoDisableBackgroundRaycastBlockers = true;

        [SerializeField] private bool autoBringTabsToFront = true;

        [SerializeField] private int tabCanvasSortingOrder = 500;

        [Title("디버그")]
        [SerializeField] private bool logTabSelection = true;

        [SerializeField] private bool logLifecycle = true;

        [SerializeField] private bool logPointerRaycasts = true;

        [SerializeField] private bool selectTabFromPointerRaycast = true;

        [SerializeField] private bool selectTabFromRectFallback = true;

        [ReadOnly]
        [SerializeField] private int currentIndex = -1;

        private UnityAction[] tabClickActions;
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();

        private void Reset()
        {
            AutoConnectTabsAndPages();
        }

        private void Awake()
        {
            if (logLifecycle)
                Debug.Log($"[LobbyTabController] Awake 실행됨. Object={name}, Active={gameObject.activeInHierarchy}, Enabled={enabled}", this);

            ResolveMissingReferences();
            DisableBackgroundRaycastBlockers();
            BringTabsToFront();
            BindTabButtons();
        }

        private void Start()
        {
            SelectTabInstant(defaultTabIndex);
        }

        private void Update()
        {
            if (!WasPrimaryPointerPressedThisFrame())
                return;

            if (!logPointerRaycasts && !selectTabFromPointerRaycast)
                return;

            HandlePointerRaycastClick();
        }

        private void OnDestroy()
        {
            UnbindTabButtons();
        }

        public void SelectTab(int index)
        {
            if (!IsValidTabIndex(index))
            {
                Debug.LogWarning($"[LobbyTabController] 잘못된 탭 인덱스입니다. Index={index}", this);
                return;
            }

            if (logTabSelection)
                Debug.Log($"[LobbyTabController] SelectTab({index}) 호출됨. Page={GetPageName(index)}", this);

            currentIndex = index;
            ApplyTabState(index, instant: false);
        }

        [Button("탭/페이지 자동 연결")]
        private void AutoConnectTabsAndPages()
        {
            if (autoCollectTabsInScene)
                tabs = ResolveTabsByKeywordOrder();

            if (autoCollectPagesInScene)
                pages = ResolvePagesByName();
        }

        [Button("설정 검증")]
        private void ValidateSetup()
        {
            ResolveMissingReferences();

            if (tabs == null || tabs.Length != PageNames.Length)
                Debug.LogWarning($"[LobbyTabController] 탭 배열 개수가 맞지 않습니다. 현재={GetLength(tabs)}, 필요={PageNames.Length}", this);

            if (pages == null || pages.Length != PageNames.Length)
                Debug.LogWarning($"[LobbyTabController] 페이지 배열 개수가 맞지 않습니다. 현재={GetLength(pages)}, 필요={PageNames.Length}", this);

            for (int i = 0; i < PageNames.Length; i++)
            {
                LobbyTabButtonUI tab = tabs != null && i < tabs.Length ? tabs[i] : null;
                GameObject page = pages != null && i < pages.Length ? pages[i] : null;
                Debug.Log($"[LobbyTabController] {i}: Tab={GetObjectName(tab)}, Button={GetObjectName(tab != null ? tab.Button : null)}, Page={GetObjectName(page)}", this);
            }
        }

        private void ResolveMissingReferences()
        {
            if (autoCollectTabsInScene && !HasUsableManualTabs())
                tabs = ResolveTabsByKeywordOrder();

            if (autoCollectPagesInScene && !HasUsableManualPages())
                pages = ResolvePagesByName();
        }

        private LobbyTabButtonUI[] ResolveTabsByKeywordOrder()
        {
            LobbyTabButtonUI[] foundTabs = FindAllSceneTabs();
            LobbyTabButtonUI[] orderedTabs = new LobbyTabButtonUI[TabKeywords.Length];

            for (int i = 0; i < TabKeywords.Length; i++)
            {
                orderedTabs[i] = FindTabByKeywords(foundTabs, TabKeywords[i]);

                if (orderedTabs[i] == null)
                    Debug.LogWarning($"[LobbyTabController] '{string.Join("/", TabKeywords[i])}' 탭을 찾지 못했습니다.\n후보:\n{BuildTabCandidateLog(foundTabs)}", this);
            }

            return orderedTabs;
        }

        private GameObject[] ResolvePagesByName()
        {
            GameObject[] orderedPages = new GameObject[PageNames.Length];

            for (int i = 0; i < PageNames.Length; i++)
            {
                orderedPages[i] = FindSceneObjectByName(PageNames[i]);

                if (orderedPages[i] == null)
                    Debug.LogWarning($"[LobbyTabController] '{PageNames[i]}' 페이지를 찾지 못했습니다.", this);
            }

            return orderedPages;
        }

        private void BindTabButtons()
        {
            UnbindTabButtons();

            if (tabs == null)
                return;

            tabClickActions = new UnityAction[tabs.Length];

            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                LobbyTabButtonUI tab = tabs[index];

                if (tab == null || !tab.EnsureReady() || tab.Button == null)
                {
                    Debug.LogWarning($"[LobbyTabController] {index}번 탭에 Button 참조가 없습니다.", this);
                    continue;
                }

                Button button = tab.Button;
                if (!button.interactable)
                    Debug.LogWarning($"[LobbyTabController] {tab.name} Button의 Interactable이 꺼져 있습니다.", button);

                UnityAction action = () => SelectTab(index);
                tabClickActions[index] = action;
                tab.Clicked += action;

                if (logLifecycle)
                    Debug.Log($"[LobbyTabController] 탭 바인딩 완료. Index={index}, Tab={tab.name}, Button={button.name}", tab);
            }
        }

        private void DisableBackgroundRaycastBlockers()
        {
            if (!autoDisableBackgroundRaycastBlockers)
                return;

            string[] blockerNames =
            {
                "BG_Lobby",
                "Bg_Lobby",
                "Background_Lobby"
            };

            GameObject[] roots = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Graphic[] graphics = roots[i].GetComponentsInChildren<Graphic>(true);
                for (int j = 0; j < graphics.Length; j++)
                {
                    Graphic graphic = graphics[j];
                    if (graphic == null || !graphic.raycastTarget)
                        continue;

                    if (!IsNamedRaycastBlocker(graphic.name, blockerNames))
                        continue;

                    if (graphic.GetComponentInParent<Button>() != null || graphic.GetComponentInParent<LobbyTabButtonUI>() != null)
                        continue;

                    graphic.raycastTarget = false;

                    if (logLifecycle)
                        Debug.Log($"[LobbyTabController] 탭 클릭을 막는 배경 RaycastTarget을 해제했습니다. Target={GetHierarchyPath(graphic.transform)}", graphic);
                }
            }
        }

        private void BringTabsToFront()
        {
            if (!autoBringTabsToFront || tabs == null || tabs.Length == 0)
                return;

            Transform tabRoot = FindTabRootTransform();
            if (tabRoot != null)
            {
                tabRoot.SetAsLastSibling();

                if (logLifecycle)
                    Debug.Log($"[LobbyTabController] 탭 루트를 Hierarchy 마지막 형제로 이동했습니다. Target={GetHierarchyPath(tabRoot)}", tabRoot);
            }

            Canvas tabCanvas = FindTabCanvas();
            if (tabCanvas == null)
                return;

            tabCanvas.overrideSorting = true;
            tabCanvas.sortingOrder = Mathf.Max(tabCanvas.sortingOrder, tabCanvasSortingOrder);

            if (logLifecycle)
                Debug.Log($"[LobbyTabController] 탭 Canvas를 최상단 정렬로 설정했습니다. Canvas={GetHierarchyPath(tabCanvas.transform)}, SortingOrder={tabCanvas.sortingOrder}", tabCanvas);
        }

        private Transform FindTabRootTransform()
        {
            if (tabs == null)
                return null;

            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == null)
                    continue;

                Transform current = tabs[i].transform;
                while (current != null)
                {
                    if (current.name == "TopTab" || current.name == "TopTabRoot" || current.name == "TopTabBar")
                        return current;

                    current = current.parent;
                }
            }

            return tabs.Length > 0 && tabs[0] != null ? tabs[0].transform.parent : null;
        }

        private Canvas FindTabCanvas()
        {
            if (tabs != null)
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    if (tabs[i] == null)
                        continue;

                    Canvas canvas = tabs[i].GetComponentInParent<Canvas>(true);
                    if (canvas != null)
                        return canvas;
                }
            }

            Transform tabRoot = FindTabRootTransform();
            return tabRoot != null ? tabRoot.GetComponentInParent<Canvas>(true) : null;
        }

        private void UnbindTabButtons()
        {
            if (tabs == null || tabClickActions == null)
                return;

            for (int i = 0; i < tabs.Length && i < tabClickActions.Length; i++)
            {
                if (tabs[i] == null || tabClickActions[i] == null)
                    continue;

                tabs[i].Clicked -= tabClickActions[i];
            }

            tabClickActions = null;
        }

        private void SelectTabInstant(int index)
        {
            if (!IsValidTabIndex(index))
                return;

            currentIndex = index;
            ApplyTabState(index, instant: true);
        }

        private void ApplyTabState(int selectedIndex, bool instant)
        {
            int tabCount = tabs != null ? tabs.Length : 0;
            int pageCount = pages != null ? pages.Length : 0;
            int count = Mathf.Max(tabCount, pageCount);

            for (int i = 0; i < count; i++)
            {
                bool selected = i == selectedIndex;

                if (tabs != null && i < tabs.Length && tabs[i] != null)
                {
                    if (instant)
                        tabs[i].SetSelectedInstant(selected);
                    else
                        tabs[i].SetSelected(selected);
                }

                if (pages != null && i < pages.Length && pages[i] != null)
                    pages[i].SetActive(selected);
            }

            NotifySelectedPageOpened(selectedIndex);
        }

        private void NotifySelectedPageOpened(int selectedIndex)
        {
            if (pages == null || selectedIndex < 0 || selectedIndex >= pages.Length)
                return;

            GameObject selectedPage = pages[selectedIndex];
            if (selectedPage == null || selectedPage.name != "Page_Trader")
                return;

            LobbyTraderUI traderUI = selectedPage.GetComponent<LobbyTraderUI>();
            if (traderUI == null)
                traderUI = selectedPage.GetComponentInChildren<LobbyTraderUI>(true);

            if (traderUI != null)
                traderUI.OpenTraderDefault();
        }

        private bool IsValidTabIndex(int index)
        {
            return tabs != null && pages != null && index >= 0 && index < tabs.Length && index < pages.Length && tabs[index] != null && pages[index] != null;
        }

        private void HandlePointerRaycastClick()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                Debug.LogWarning("[LobbyTabController] EventSystem.current가 없습니다. UI 클릭 이벤트가 동작하지 않습니다.", this);
                return;
            }

            PointerEventData pointerEventData = new PointerEventData(eventSystem)
            {
                position = GetPointerScreenPosition()
            };

            raycastResults.Clear();
            eventSystem.RaycastAll(pointerEventData, raycastResults);

            if (logPointerRaycasts)
                Debug.Log($"[LobbyTabController] Pointer Raycast Count={raycastResults.Count}\n{BuildRaycastLog(raycastResults)}", this);

            if (selectTabFromRectFallback && TrySelectTabByScreenPosition(pointerEventData.position))
                return;

            if (!selectTabFromPointerRaycast)
                return;

            for (int i = 0; i < raycastResults.Count; i++)
            {
                GameObject hitObject = raycastResults[i].gameObject;
                if (hitObject == null)
                    continue;

                LobbyTabButtonUI tab = hitObject.GetComponentInParent<LobbyTabButtonUI>();
                int tabIndex = GetTabIndex(tab);
                if (tabIndex < 0)
                    continue;

                if (logTabSelection)
                    Debug.Log($"[LobbyTabController] Pointer Raycast로 탭 선택. Hit={hitObject.name}, Tab={tab.name}, Index={tabIndex}", tab);

                SelectTab(tabIndex);
                return;
            }
        }

        private bool TrySelectTabByScreenPosition(Vector2 screenPosition)
        {
            if (tabs == null)
                return false;

            for (int i = 0; i < tabs.Length; i++)
            {
                LobbyTabButtonUI tab = tabs[i];
                if (tab == null)
                    continue;

                RectTransform tabRect = GetTabRectTransform(tab);
                if (tabRect == null)
                    continue;

                Camera eventCamera = GetCanvasEventCamera(tabRect);
                if (!RectTransformUtility.RectangleContainsScreenPoint(tabRect, screenPosition, eventCamera))
                    continue;

                if (logTabSelection)
                    Debug.Log($"[LobbyTabController] Rect fallback으로 탭 선택. Tab={tab.name}, Index={i}, ScreenPosition={screenPosition}", tab);

                SelectTab(i);
                return true;
            }

            return false;
        }

        private RectTransform GetTabRectTransform(LobbyTabButtonUI tab)
        {
            if (tab == null)
                return null;

            if (tab.Button != null && tab.Button.transform is RectTransform buttonRect)
                return buttonRect;

            return tab.transform as RectTransform;
        }

        private Camera GetCanvasEventCamera(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return null;

            Canvas canvas = rectTransform.GetComponentInParent<Canvas>(true);
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;

            return canvas.worldCamera;
        }

        private static bool WasPrimaryPointerPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static Vector2 GetPointerScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private int GetTabIndex(LobbyTabButtonUI tab)
        {
            if (tab == null || tabs == null)
                return -1;

            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == tab)
                    return i;
            }

            return -1;
        }

        private LobbyTabButtonUI[] FindAllSceneTabs()
        {
            GameObject[] roots = gameObject.scene.GetRootGameObjects();
            List<LobbyTabButtonUI> result = new List<LobbyTabButtonUI>();

            for (int i = 0; i < roots.Length; i++)
            {
                LobbyTabButtonUI[] found = roots[i].GetComponentsInChildren<LobbyTabButtonUI>(true);
                for (int j = 0; j < found.Length; j++)
                {
                    if (found[j] != null && !result.Contains(found[j]))
                        result.Add(found[j]);
                }
            }

            return result.ToArray();
        }

        private LobbyTabButtonUI FindTabByKeywords(LobbyTabButtonUI[] foundTabs, string[] keywords)
        {
            if (foundTabs == null || keywords == null)
                return null;

            for (int i = 0; i < foundTabs.Length; i++)
            {
                LobbyTabButtonUI tab = foundTabs[i];
                if (tab == null)
                    continue;

                string searchText = tab.GetSearchText().ToLowerInvariant();
                for (int j = 0; j < keywords.Length; j++)
                {
                    string keyword = keywords[j];
                    if (!string.IsNullOrWhiteSpace(keyword) && searchText.Contains(keyword.ToLowerInvariant()))
                        return tab;
                }
            }

            return null;
        }

        private GameObject FindSceneObjectByName(string objectName)
        {
            GameObject[] roots = gameObject.scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < transforms.Length; j++)
                {
                    if (transforms[j] != null && transforms[j].name == objectName)
                        return transforms[j].gameObject;
                }
            }

            return null;
        }

        private bool HasUsableManualTabs()
        {
            if (NeedsAutoResolve(tabs))
                return false;

            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == null || !tabs[i].EnsureReady() || tabs[i].Button == null)
                    return false;
            }

            return true;
        }

        private bool HasUsableManualPages()
        {
            return !NeedsAutoResolve(pages);
        }

        private string BuildTabCandidateLog(LobbyTabButtonUI[] foundTabs)
        {
            if (foundTabs == null || foundTabs.Length == 0)
                return "LobbyTabButtonUI 후보 없음";

            string log = string.Empty;
            for (int i = 0; i < foundTabs.Length; i++)
            {
                LobbyTabButtonUI tab = foundTabs[i];
                if (tab == null)
                    continue;

                string labels = string.Empty;
                TMP_Text[] texts = tab.GetComponentsInChildren<TMP_Text>(true);
                for (int j = 0; j < texts.Length; j++)
                {
                    if (texts[j] != null)
                        labels += $"[{texts[j].name}:{texts[j].text}]";
                }

                log += $"- {GetHierarchyPath(tab.transform)} {labels}\n";
            }

            return log;
        }

        private string BuildRaycastLog(List<RaycastResult> results)
        {
            if (results == null || results.Count == 0)
                return "Raycast 후보 없음";

            string log = string.Empty;
            int count = Mathf.Min(results.Count, 10);

            for (int i = 0; i < count; i++)
            {
                RaycastResult result = results[i];
                GameObject hitObject = result.gameObject;
                string path = hitObject != null ? GetHierarchyPath(hitObject.transform) : "None";
                string raycastTarget = hitObject != null ? GetGraphicRaycastInfo(hitObject) : string.Empty;
                log += $"{i}: {path} {raycastTarget}\n";
            }

            return log;
        }

        private static string GetGraphicRaycastInfo(GameObject target)
        {
            Graphic graphic = target != null ? target.GetComponent<Graphic>() : null;
            if (graphic == null)
                return string.Empty;

            return $"Graphic={graphic.GetType().Name}, RaycastTarget={graphic.raycastTarget}";
        }

        private string GetPageName(int index)
        {
            if (pages == null || index < 0 || index >= pages.Length || pages[index] == null)
                return "None";

            return pages[index].name;
        }

        private static bool NeedsAutoResolve<T>(T[] array) where T : Object
        {
            if (array == null || array.Length != PageNames.Length)
                return true;

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null)
                    return true;
            }

            return false;
        }

        private static bool IsNamedRaycastBlocker(string objectName, string[] blockerNames)
        {
            if (string.IsNullOrEmpty(objectName) || blockerNames == null)
                return false;

            for (int i = 0; i < blockerNames.Length; i++)
            {
                if (objectName == blockerNames[i])
                    return true;
            }

            return false;
        }

        private static int GetLength<T>(T[] array)
        {
            return array != null ? array.Length : 0;
        }

        private static string GetObjectName(Object value)
        {
            return value != null ? value.name : "None";
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return string.Empty;

            string path = target.name;
            Transform parent = target.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
