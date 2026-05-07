using System.Reflection;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class ItemTooltipUI : MonoBehaviour
    {
        [BoxGroup("루트")]
        [Tooltip("툴팁이 표시될 때 활성화할 루트 오브젝트입니다.")]
        [SerializeField] private GameObject tooltipRoot;

        [BoxGroup("UI 연결")]
        [SerializeField] private Image tooltipBackground;

        [BoxGroup("UI 연결")]
        [SerializeField] private Image iconSlotBackground;

        [BoxGroup("UI 연결")]
        [SerializeField] private Image itemIconImage;

        [BoxGroup("UI 연결")]
        [SerializeField] private TMP_Text itemNameText;

        [BoxGroup("UI 연결")]
        [SerializeField] private TMP_Text descriptionText;

        [BoxGroup("UI 연결")]
        [SerializeField] private TMP_Text weightText;

        [BoxGroup("표시 옵션")]
        [SerializeField] private string weightUnit = "kg";

        [BoxGroup("표시 옵션")]
        [SerializeField] private string emptyDescriptionText = " ";

        [BoxGroup("위치")]
        [Tooltip("툴팁이 표시되는 동안 마우스 위치를 따라가게 할지 여부입니다.")]
        [SerializeField] private bool followMouse = true;

        [BoxGroup("위치")]
        [Tooltip("마우스 위치에서 툴팁을 얼마나 떨어뜨려 표시할지 정합니다.")]
        [SerializeField] private Vector2 mouseOffset = new(24f, -24f);

        [BoxGroup("위치")]
        [Tooltip("현재 마우스 오버 중인 슬롯과 툴팁 사이에 확보할 최소 간격입니다.")]
        [SerializeField] private float slotAvoidPadding = 12f;

        [BoxGroup("위치")]
        [Tooltip("툴팁이 화면 밖으로 나가지 않도록 위치를 보정합니다.")]
        [SerializeField] private bool keepInsideScreen = true;

        [BoxGroup("디버그")]
        [Tooltip("툴팁 문제를 확인하기 위해 표시/숨김 상태를 로그로 출력합니다.")]
        [SerializeField] private bool debugTooltipEvents = true;

        [BoxGroup("테스트")]
        [SerializeField] private ScriptableObject testItem;

        private ScriptableObject currentItem;
        private RectTransform currentSourceRect;

        private void Awake()
        {
            if (tooltipRoot == null)
                tooltipRoot = gameObject;

            EnsureTooltipDoesNotBlockRaycasts();
            Hide();
        }

        private void Update()
        {
            if (!followMouse || tooltipRoot == null || !tooltipRoot.activeSelf)
                return;

            UpdatePosition(GetMouseScreenPosition());
        }

        public void Show(ScriptableObject itemData)
        {
            Show(itemData, 1);
        }

        public void Show(ScriptableObject itemData, int stackCount)
        {
            Show(itemData, stackCount, null);
        }

        public void Show(ScriptableObject itemData, int stackCount, RectTransform sourceRect)
        {
            if (debugTooltipEvents)
                Debug.Log($"[ItemTooltipUI] Show 호출됨. Item={itemData}, Stack={stackCount}, RootActiveBefore={(tooltipRoot != null && tooltipRoot.activeSelf)}", this);

            if (itemData == null)
            {
                Hide();
                return;
            }

            if (tooltipRoot == null)
                tooltipRoot = gameObject;

            currentItem = itemData;
            currentSourceRect = sourceRect;

            Sprite icon = GetSprite(itemData, "icon", "Icon", "itemIcon", "ItemIcon");
            string itemName = GetString(itemData, "displayName", "DisplayName", "itemName", "ItemName", "itemID", "ItemID", "name");
            string description = GetString(itemData, "description", "Description", "desc", "Desc");
            float weight = GetFloat(itemData, "weight", "Weight", "weightKg", "WeightKg");

            SetIcon(icon);
            SetText(itemNameText, itemName);
            SetText(descriptionText, description);
            SetWeight(weight, stackCount);

            EnsureTooltipDoesNotBlockRaycasts();
            tooltipRoot.SetActive(true);
            tooltipRoot.transform.SetAsLastSibling();
            UpdatePosition(GetMouseScreenPosition());

            if (debugTooltipEvents)
                Debug.Log($"[ItemTooltipUI] tooltipRoot.activeSelf={tooltipRoot.activeSelf}", this);
        }

        public void Hide()
        {
            currentItem = null;
            currentSourceRect = null;

            if (itemIconImage != null)
            {
                itemIconImage.sprite = null;
                itemIconImage.enabled = false;
                itemIconImage.gameObject.SetActive(false);
            }

            SetText(itemNameText, string.Empty);
            SetText(descriptionText, string.Empty);
            SetText(weightText, string.Empty);

            if (tooltipRoot != null)
                tooltipRoot.SetActive(false);
            
        }

        public void Refresh()
        {
            if (currentItem == null)
                return;

            Show(currentItem);
        }

        private void SetIcon(Sprite icon)
        {
            if (itemIconImage == null)
                return;

            itemIconImage.sprite = icon;
            itemIconImage.enabled = icon != null;
            itemIconImage.gameObject.SetActive(icon != null);
        }

        private void SetWeight(float weight, int stackCount)
        {
            if (weightText == null)
                return;

            weightText.text = stackCount > 1
                ? $"무게: {weight:0.##} {weightUnit} x {stackCount} = {weight * stackCount:0.##} {weightUnit}"
                : $"무게: {weight:0.##} {weightUnit}";
        }

        private void SetText(TMP_Text targetText, string value)
        {
            if (targetText == null)
                return;

            if (targetText == descriptionText && string.IsNullOrWhiteSpace(value))
            {
                targetText.text = emptyDescriptionText;
                return;
            }

            targetText.text = value ?? string.Empty;
        }

        private void EnsureTooltipDoesNotBlockRaycasts()
        {
            GameObject root = tooltipRoot != null ? tooltipRoot : gameObject;

            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = root.AddComponent<CanvasGroup>();

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        private void UpdatePosition(Vector2 mouseScreenPosition)
        {
            RectTransform tooltipRect = GetTooltipRectTransform();
            if (tooltipRect == null)
                return;

            Canvas canvas = tooltipRect.GetComponentInParent<Canvas>();
            RectTransform parentRect = tooltipRect.parent as RectTransform;
            if (canvas == null || parentRect == null)
                return;

            Vector2 targetScreenPosition = mouseScreenPosition + mouseOffset;
            targetScreenPosition = MoveAwayFromSourceSlot(targetScreenPosition, tooltipRect);

            if (keepInsideScreen)
                targetScreenPosition = ClampToScreen(targetScreenPosition, tooltipRect);

            Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, targetScreenPosition, eventCamera, out Vector2 localPoint))
                tooltipRect.anchoredPosition = localPoint;
        }

        private RectTransform GetTooltipRectTransform()
        {
            GameObject root = tooltipRoot != null ? tooltipRoot : gameObject;
            return root.transform as RectTransform;
        }

        private Vector2 MoveAwayFromSourceSlot(Vector2 targetScreenPosition, RectTransform tooltipRect)
        {
            if (currentSourceRect == null)
                return targetScreenPosition;

            Rect sourceScreenRect = GetScreenRect(currentSourceRect);
            Vector2 size = tooltipRect.rect.size;
            Vector2 pivot = tooltipRect.pivot;

            Rect tooltipScreenRect = new(
                targetScreenPosition.x - size.x * pivot.x,
                targetScreenPosition.y - size.y * pivot.y,
                size.x,
                size.y);

            if (!tooltipScreenRect.Overlaps(sourceScreenRect))
                return targetScreenPosition;

            float rightX = sourceScreenRect.xMax + slotAvoidPadding + size.x * pivot.x;
            float leftX = sourceScreenRect.xMin - slotAvoidPadding - size.x * (1f - pivot.x);
            bool canPlaceRight = rightX + size.x * (1f - pivot.x) <= Screen.width;

            targetScreenPosition.x = canPlaceRight ? rightX : leftX;
            return targetScreenPosition;
        }

        private static Rect GetScreenRect(RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            float minX = corners[0].x;
            float minY = corners[0].y;
            float maxX = corners[0].x;
            float maxY = corners[0].y;

            for (int i = 1; i < corners.Length; i++)
            {
                minX = Mathf.Min(minX, corners[i].x);
                minY = Mathf.Min(minY, corners[i].y);
                maxX = Mathf.Max(maxX, corners[i].x);
                maxY = Mathf.Max(maxY, corners[i].y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private Vector2 ClampToScreen(Vector2 targetScreenPosition, RectTransform tooltipRect)
        {
            Vector2 size = tooltipRect.rect.size;
            Vector2 pivot = tooltipRect.pivot;

            float minX = size.x * pivot.x;
            float maxX = Screen.width - size.x * (1f - pivot.x);
            float minY = size.y * pivot.y;
            float maxY = Screen.height - size.y * (1f - pivot.y);

            if (minX <= maxX)
                targetScreenPosition.x = Mathf.Clamp(targetScreenPosition.x, minX, maxX);

            if (minY <= maxY)
                targetScreenPosition.y = Mathf.Clamp(targetScreenPosition.y, minY, maxY);

            return targetScreenPosition;
        }

        private static Vector2 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
                return UnityEngine.InputSystem.Mouse.current.position.ReadValue();
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }

        private static string GetString(ScriptableObject itemData, params string[] memberNames)
        {
            object value = GetMemberValue(itemData, memberNames);

            if (value == null)
                return string.Empty;

            return value.ToString();
        }

        private static float GetFloat(ScriptableObject itemData, params string[] memberNames)
        {
            object value = GetMemberValue(itemData, memberNames);

            if (value == null)
                return 0f;

            if (value is float floatValue)
                return floatValue;

            if (value is int intValue)
                return intValue;

            if (double.TryParse(value.ToString(), out double parsedValue))
                return (float)parsedValue;

            return 0f;
        }

        private static Sprite GetSprite(ScriptableObject itemData, params string[] memberNames)
        {
            object value = GetMemberValue(itemData, memberNames);
            return value as Sprite;
        }

        private static object GetMemberValue(ScriptableObject itemData, params string[] memberNames)
        {
            if (itemData == null)
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            System.Type type = itemData.GetType();

            foreach (string memberName in memberNames)
            {
                FieldInfo field = type.GetField(memberName, flags);

                if (field != null)
                    return field.GetValue(itemData);

                PropertyInfo property = type.GetProperty(memberName, flags);

                if (property != null)
                    return property.GetValue(itemData);
            }

            return null;
        }

#if UNITY_EDITOR
        [BoxGroup("테스트")]
        [Button("테스트 아이템 표시")]
        private void TestShow()
        {
            Show(testItem);
        }

        [BoxGroup("테스트")]
        [Button("툴팁 숨기기")]
        private void TestHide()
        {
            Hide();
        }
#endif
    }
}
