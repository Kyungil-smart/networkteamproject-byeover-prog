using Unity.Netcode;

using TMPro;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 플레이어별 크레딧. PlayerPrefab Root에 부착 (ServiceLocator에는 등록하지 않음 —
    /// 각 플레이어가 자기 인스턴스를 가지기 때문).
    /// </summary>
    public class WalletSystem : NetworkBehaviour
    {
        [Header("재화 UI")]
        [SerializeField] private TMP_Text goodsText;
        [SerializeField] private string goodsTextFormat = "{0}";

        public NetworkVariable<int> Credits = new(
            value: 50000,
            readPerm: NetworkVariableReadPermission.Owner,
            writePerm: NetworkVariableWritePermission.Server);

        private int localCredits = 50000;

        public int CurrentCredits => IsSpawned ? Credits.Value : localCredits;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void Awake()
        {
            AutoBindReferences();
            RefreshGoodsText();
        }

        private void OnValidate()
        {
            AutoBindReferences();
            RefreshGoodsText();
        }

        private void OnEnable()
        {
            Credits.OnValueChanged += HandleCreditsChanged;
            RefreshGoodsText();
        }

        private void OnDisable()
        {
            Credits.OnValueChanged -= HandleCreditsChanged;
        }

        public bool TryPay(int amount)
        {
            if (!IsServer) return false;
            if (amount < 0) return false;
            if (Credits.Value < amount) return false;
            int oldVal = Credits.Value;
            Credits.Value -= amount;
            RefreshGoodsText();
            EventBus.Publish(new CreditsChangedEvent
            {
                clientId = OwnerClientId,
                delta = -amount,
                newBalance = Credits.Value,
            });
            return true;
        }

        public bool TryPayLocalTest(int amount)
        {
            if (amount < 0) return false;
            if (CurrentCredits < amount) return false;

            if (IsSpawned)
                Credits.Value -= amount;
            else
                localCredits -= amount;

            RefreshGoodsText();
            return true;
        }

        public void EarnLocalTest(int amount)
        {
            if (amount <= 0) return;

            if (IsSpawned)
                Credits.Value += amount;
            else
                localCredits += amount;

            RefreshGoodsText();
        }

        public void SetCreditsLocalTest(int credits)
        {
            int safeCredits = Mathf.Max(0, credits);

            if (IsSpawned)
                Credits.Value = safeCredits;
            else
                localCredits = safeCredits;

            RefreshGoodsText();
        }

        public void Earn(int amount)
        {
            if (!IsServer) return;
            if (amount <= 0) return;
            Credits.Value += amount;
            RefreshGoodsText();
            EventBus.Publish(new CreditsChangedEvent
            {
                clientId = OwnerClientId,
                delta = amount,
                newBalance = Credits.Value,
            });
        }

        private void HandleCreditsChanged(int previousValue, int currentValue)
        {
            RefreshGoodsText();
        }

        private void RefreshGoodsText()
        {
            if (goodsText == null)
                return;

            goodsText.text = string.IsNullOrEmpty(goodsTextFormat)
                ? CurrentCredits.ToString()
                : string.Format(goodsTextFormat, CurrentCredits);
        }

        private void AutoBindReferences()
        {
            if (goodsText != null)
                return;

            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text != null && text.name == "Text_goods")
                {
                    goodsText = text;
                    return;
                }
            }
        }
    }
}
