using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 타이틀 메뉴 버튼에 마우스를 올렸을 때
    /// 빨간 붓터치 이미지를 표시하는 호버 연출.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class MainMenuButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Hover Visual")]
        [SerializeField] private GameObject hoverObject;

        [Header("Optional")]
        [SerializeField] private bool hideOnAwake = true;

        private void Awake()
        {
            if (hideOnAwake)
                SetHover(false);
        }

        private void OnDisable()
        {
            SetHover(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetHover(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHover(false);
        }

        private void SetHover(bool isOn)
        {
            if (hoverObject != null)
                hoverObject.SetActive(isOn);
        }
    }
}