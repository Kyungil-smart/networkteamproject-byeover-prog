using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 파티 슬롯 하나의 Empty/Filled/Ready 표시를 담당합니다.
    /// 네트워크 상태 변경은 직접 처리하지 않고 Ready 클릭 이벤트만 상위 View로 전달합니다.
    /// </summary>
    public class LobbyPartySlotUI : MonoBehaviour
    {
        [Header("==== 슬롯 루트 ====")]
        [Tooltip("빈 슬롯 표시 루트입니다. PartySlot_2~4의 Slot_Empty를 연결" +
                 "\nPartySlot_1(host)는 비워둘 수 있습니다.")]
        [SerializeField] private GameObject emptyRoot;

        [Tooltip("채워진 슬롯 표시 루트입니다. PartySlot_2~4의 Slot_Party를 연결" +
                 "\nPartySlot_1(host)는 별도 루트가 없으면 비워둘 수 있습니다.")]
        [SerializeField] private GameObject filledRoot;

        [SerializeField] private Image playerIconImage;

        [Header("==== 텍스트 ====")]
        [Tooltip("플레이어 이름을 표시할 TMP 텍스트입니다.")]
        [SerializeField] private TMP_Text playerNameText;

        [Tooltip("Host 표시 텍스트" +
                 "\nHost 표시가 없는 슬롯은 비워둘 수 있습니다.")]
        [SerializeField] private TMP_Text hostText;

        [Tooltip("Ready 상태를 표시할 TMP 텍스트")]
        [SerializeField] private TMP_Text readyText;

        [Header("==== 버튼 ====")]
        [Tooltip("기존 PartySlot 안의 Btn_Ready를 연결" +
                 "\n로컬 플레이어 슬롯일 때만 interactable 처리됩니다.")]
        [SerializeField] private Button readyButton;

        [Header("==== 표시 문구 ====")]
        [Tooltip("Ready가 아닐 때 표시할 문구")]
        [SerializeField] private string notReadyLabel = "준비";

        [Tooltip("Ready 상태일 때 표시할 문구")]
        [SerializeField] private string readyLabel = "준비 완료";

        [Tooltip("Host 텍스트에 표시할 문구")]
        [SerializeField] private string hostLabel = "방장";

        private bool currentReady;

        public event Action<bool> ReadyClicked;

        private void Awake()
        {
            if (emptyRoot != null || filledRoot != null)
                RenderEmpty();
        }

        private void OnEnable()
        {
            if (readyButton != null)
                readyButton.onClick.AddListener(HandleReadyButtonClicked);
        }
        
        private void OnDisable()
        {
            if (readyButton != null)
                readyButton.onClick.RemoveListener(HandleReadyButtonClicked);
        }
        
        /// <summary>
        /// 표시용 슬롯 데이터에 따라 Empty 또는 Filled 상태를 갱신
        /// </summary>
        public void Render(LobbyPartySlotViewData data)
        {
            if (!data.HasPlayer)
            {
                RenderEmpty();
                return;
            }

            currentReady = data.IsReady;

            SetActive(emptyRoot, false);
            SetActive(filledRoot, true);

            if (playerNameText != null)
            {
                string displayName = data.DisplayName;

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = data.IsLocalPlayer ? "나" : "플레이어";
                }

                playerNameText.text = displayName;
            }

            if (hostText != null)
            {
                hostText.text = hostLabel;
                hostText.gameObject.SetActive(data.IsHost);
            }

            if (readyText != null)
                readyText.text = data.IsReady ? readyLabel : notReadyLabel;

            if (readyButton != null)
                readyButton.interactable = data.IsLocalPlayer;

            ApplyPlayerIconColor(data.IconColor);
        }

        /// <summary>
        /// 빈 슬롯 상태를 표시
        /// </summary>
        public void RenderEmpty()
        {
            currentReady = false;

            SetActive(emptyRoot, true);
            SetActive(filledRoot, false);

            if (playerNameText != null) playerNameText.text = string.Empty;
            if (hostText != null) hostText.gameObject.SetActive(false);
            if (readyText != null) readyText.text = notReadyLabel;
            if (readyButton != null) readyButton.interactable = false;
            ApplyPlayerIconColor(Color.white);
        }

        private void HandleReadyButtonClicked() => ReadyClicked?.Invoke(!currentReady);

        private void ApplyPlayerIconColor(Color color)
        {
            if (playerIconImage == null)
                return;

            playerIconImage.color = color;
        }
        
        private void SetActive(GameObject target, bool active)
        {
            if (target == null) return;
            
            target.SetActive(active);
        }
    }
}
