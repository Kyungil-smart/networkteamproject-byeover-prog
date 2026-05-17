using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using DeadZone.Core;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// HJO_IngameHUD hierarchy setup:
    /// Canvas_HUD
    ///   HUD_Panel
    ///     Crosshair  <- attach CrosshairController here
    ///       Dot_Center      Image
    ///       Line_Top        Image
    ///       Line_Bottom     Image
    ///       Line_Left       Image
    ///       Line_Right      Image
    ///       HitMarker_Group optional CanvasGroup or GameObject
    ///
    /// This is local-only UI. Do not add NetworkObject or network sync components.
    /// </summary>
    public sealed class CrosshairController : MonoBehaviour
    {
        private static CrosshairController activeInstance;

#if ODIN_INSPECTOR
        [Title("크로스헤어 참조")]
#else
        [Header("크로스헤어 참조")]
#endif
        [SerializeField] private RectTransform crosshairRoot;
        [SerializeField] private Image dotCenter;
        [SerializeField] private Image lineTop;
        [SerializeField] private Image lineBottom;
        [SerializeField] private Image lineLeft;
        [SerializeField] private Image lineRight;

#if ODIN_INSPECTOR
        [Title("마우스 추적")]
#else
        [Header("마우스 추적")]
#endif
        [SerializeField] private bool followMouse = true;
        [SerializeField] private Canvas targetCanvas;
        [SerializeField] private RectTransform canvasRoot;
        [SerializeField] private RectTransform crosshairRect;
        [SerializeField] private bool clampToCanvas = true;
        [SerializeField] private bool manageCursorState = true;
        [SerializeField] private bool hideWhenUiCursorVisible = true;

#if ODIN_INSPECTOR
        [Title("히트마커")]
#else
        [Header("히트마커")]
#endif
        [SerializeField] private CanvasGroup hitMarkerGroup;
        [SerializeField] private float hitMarkerDuration = 0.12f;

#if ODIN_INSPECTOR
        [Title("확산 설정")]
        [MinValue(0f)]
#else
        [Header("확산 설정")]
        [Min(0f)]
#endif
        [SerializeField] private float baseSpread = 10f;

#if ODIN_INSPECTOR
        [MinValue(0f)]
#else
        [Min(0f)]
#endif
        [SerializeField] private float maxSpread = 80f;

#if ODIN_INSPECTOR
        [MinValue(0f)]
#else
        [Min(0f)]
#endif
        [SerializeField] private float spreadPerShot = 15f;

#if ODIN_INSPECTOR
        [MinValue(0f)]
#else
        [Min(0f)]
#endif
        [SerializeField] private float recoverSpeed = 20f;

#if ODIN_INSPECTOR
        [Title("ADS 표시 설정")]
#else
        [Header("ADS 표시 설정")]
#endif
        [SerializeField] private bool hideWhenAds = false;

#if ODIN_INSPECTOR
        [PropertyRange(0f, 1f)]
#else
        [Range(0f, 1f)]
#endif
        [SerializeField] private float adsAlpha = 1f;

        [SerializeField] private bool subscribeCriticalHitAsHitMarker = true;

#if ODIN_INSPECTOR
        [Title("디버그")]
        [ShowInInspector, ReadOnly]
#endif
        private float currentSpread;

        private CanvasGroup crosshairCanvasGroup;
        private Coroutine hitMarkerRoutine;
        private bool isAds;
        private bool subscribed;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
            currentSpread = baseSpread;
            ApplySpread();
            ApplyVisibility();
            HideHitMarkerImmediate();
        }

        private void OnEnable()
        {
            if (activeInstance != null && activeInstance != this)
            {
                gameObject.SetActive(false);
                return;
            }

            activeInstance = this;
            EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
            EventBus.Subscribe<ADSStateChangedEvent>(OnADSStateChanged);

            if (subscribeCriticalHitAsHitMarker)
                EventBus.Subscribe<CriticalHitEvent>(OnCriticalHit);

            subscribed = true;
            CursorStateController.GameplayModeChanged += OnGameplayModeChanged;

            if (manageCursorState)
                CursorStateController.RegisterGameplayOwner(this);

            ApplyVisibility();
        }

        private void OnDisable()
        {
            if (manageCursorState)
                CursorStateController.UnregisterGameplayOwner(this);

            CursorStateController.GameplayModeChanged -= OnGameplayModeChanged;

            if (subscribed)
            {
                EventBus.Unsubscribe<WeaponFiredEvent>(OnWeaponFired);
                EventBus.Unsubscribe<ADSStateChangedEvent>(OnADSStateChanged);
                EventBus.Unsubscribe<CriticalHitEvent>(OnCriticalHit);
                subscribed = false;
            }

            if (activeInstance == this)
                activeInstance = null;
        }

        private void Update()
        {
            FollowMousePosition();
            RecoverSpread();
        }

        /// <summary>
        /// Temporary extension point for firing systems that cannot publish WeaponFiredEvent yet.
        /// </summary>
        public void OnShotFired()
        {
            AddShotSpread();
        }

        /// <summary>
        /// Call this from ADS UI/input glue when a local ADS state event is added.
        /// </summary>
        public void SetADS(bool aiming)
        {
            if (isAds == aiming)
                return;

            isAds = aiming;
            ApplyVisibility();
        }

        /// <summary>
        /// Temporary extension point for hit confirmation until a dedicated hit-confirmed event exists.
        /// </summary>
        public void OnHitConfirmed()
        {
            ShowHitMarker();
        }

        private void OnGameplayModeChanged(bool gameplayMode)
        {
            ApplyVisibility();
        }

        private void OnWeaponFired(WeaponFiredEvent e)
        {
            if (!IsLocalClient(e.shooterClientId))
                return;

            AddShotSpread();
        }

        private void OnADSStateChanged(ADSStateChangedEvent e)
        {
            if (!IsLocalClient(e.clientId))
                return;

            SetADS(e.isAiming);
        }

        private void OnCriticalHit(CriticalHitEvent e)
        {
            if (!IsLocalClient(e.attackerClientId))
                return;

            ShowHitMarker();
        }

        private void FollowMousePosition()
        {
            if (!followMouse || (hideWhenUiCursorVisible && !CursorStateController.IsGameplayMode))
                return;

            if (Mouse.current == null)
                return;

            ResolveCanvasReferences();

            if (targetCanvas == null || canvasRoot == null || crosshairRect == null)
                return;

            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Camera uiCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : targetCanvas.worldCamera;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRoot,
                    mouseScreenPos,
                    uiCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            if (clampToCanvas)
            {
                Rect rect = canvasRoot.rect;
                localPoint.x = Mathf.Clamp(localPoint.x, rect.xMin, rect.xMax);
                localPoint.y = Mathf.Clamp(localPoint.y, rect.yMin, rect.yMax);
            }

            crosshairRect.position = canvasRoot.TransformPoint(localPoint);
        }

        private void RecoverSpread()
        {
            float nextSpread = Mathf.MoveTowards(
                currentSpread,
                baseSpread,
                recoverSpeed * Time.deltaTime);

            if (Mathf.Approximately(nextSpread, currentSpread))
                return;

            currentSpread = nextSpread;
            ApplySpread();
        }

        private void AddShotSpread()
        {
            currentSpread = Mathf.Clamp(currentSpread + spreadPerShot, baseSpread, maxSpread);
            ApplySpread();
        }

        private void ApplySpread()
        {
            SetAnchoredPosition(lineTop, Vector2.up * currentSpread);
            SetAnchoredPosition(lineBottom, Vector2.down * currentSpread);
            SetAnchoredPosition(lineLeft, Vector2.left * currentSpread);
            SetAnchoredPosition(lineRight, Vector2.right * currentSpread);

            if (dotCenter != null)
                dotCenter.rectTransform.anchoredPosition = Vector2.zero;
        }

        private static void SetAnchoredPosition(Image image, Vector2 position)
        {
            if (image == null)
                return;

            image.rectTransform.anchoredPosition = position;
        }

        private void ApplyVisibility()
        {
            if (crosshairRoot == null)
                return;

            EnsureCrosshairCanvasGroup();

            bool uiCursorVisible = hideWhenUiCursorVisible && !CursorStateController.IsGameplayMode;
            if (uiCursorVisible)
                crosshairCanvasGroup.alpha = 0f;
            else if (isAds)
                crosshairCanvasGroup.alpha = hideWhenAds ? 0f : adsAlpha;
            else
                crosshairCanvasGroup.alpha = 1f;

            crosshairCanvasGroup.interactable = false;
            crosshairCanvasGroup.blocksRaycasts = false;
        }

        private void EnsureCrosshairCanvasGroup()
        {
            if (crosshairCanvasGroup == null)
                crosshairCanvasGroup = crosshairRoot.GetComponent<CanvasGroup>();

            if (crosshairCanvasGroup == null)
                crosshairCanvasGroup = crosshairRoot.gameObject.AddComponent<CanvasGroup>();
        }

        private void ShowHitMarker()
        {
            if (hitMarkerGroup == null)
                return;

            if (hitMarkerRoutine != null)
                StopCoroutine(hitMarkerRoutine);

            hitMarkerRoutine = StartCoroutine(HitMarkerRoutine());
        }

        private IEnumerator HitMarkerRoutine()
        {
            hitMarkerGroup.gameObject.SetActive(true);
            hitMarkerGroup.alpha = 1f;

            yield return new WaitForSeconds(hitMarkerDuration);

            HideHitMarkerImmediate();
            hitMarkerRoutine = null;
        }

        private void HideHitMarkerImmediate()
        {
            if (hitMarkerGroup == null)
                return;

            hitMarkerGroup.alpha = 0f;
            hitMarkerGroup.gameObject.SetActive(false);
        }

        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton == null
                || clientId == NetworkManager.Singleton.LocalClientId;
        }

        private void ResolveReferences()
        {
            if (crosshairRoot == null)
                crosshairRoot = transform as RectTransform;

            if (crosshairRect == null)
                crosshairRect = transform as RectTransform;

            ResolveCanvasReferences();

            if (crosshairRoot == null)
                return;

            if (dotCenter == null)
                dotCenter = FindChildImage("Dot_Center");

            if (lineTop == null)
                lineTop = FindChildImage("Line_Top");

            if (lineBottom == null)
                lineBottom = FindChildImage("Line_Bottom");

            if (lineLeft == null)
                lineLeft = FindChildImage("Line_Left");

            if (lineRight == null)
                lineRight = FindChildImage("Line_Right");

            if (hitMarkerGroup == null)
            {
                Transform hitMarker = FindChild("HitMarker_Group");
                if (hitMarker != null)
                {
                    hitMarkerGroup = hitMarker.GetComponent<CanvasGroup>();
                    if (hitMarkerGroup == null)
                        hitMarkerGroup = hitMarker.gameObject.AddComponent<CanvasGroup>();
                }
            }

            crosshairCanvasGroup = crosshairRoot.GetComponent<CanvasGroup>();
        }

        private void ResolveCanvasReferences()
        {
            if (crosshairRect == null)
                crosshairRect = transform as RectTransform;

            if (targetCanvas == null)
                targetCanvas = GetComponentInParent<Canvas>();

            if (targetCanvas != null)
                targetCanvas = targetCanvas.rootCanvas;

            if (targetCanvas != null)
                canvasRoot = targetCanvas.GetComponent<RectTransform>();
        }

        private Image FindChildImage(string childName)
        {
            Transform child = FindChild(childName);
            return child != null ? child.GetComponent<Image>() : null;
        }

        private Transform FindChild(string childName)
        {
            Transform[] children = crosshairRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                    return children[i];
            }

            return null;
        }
    }
}
