using UnityEngine;
using Unity.Netcode;
using DeadZone.Core;


namespace DeadZone.Systems
{
    public class TraderManager : NetworkBehaviour
    {
        [Header("트레이더 데이터")]
        [Tooltip("TraderDataSO 배열: Igor(0), Vera(1), Doc(2), Shade(3)")]
        [SerializeField] private TraderDataSO[] traders;

        // ─── 조회 API ───

        /// <summary>인덱스로 TraderDataSO 반환</summary>
        public TraderDataSO GetTrader(int index)
        {
            if (index < 0 || index >= traders.Length) return null;
            return traders[index];
        }

        /// <summary>트레이더 수</summary>
        public int TraderCount => traders.Length;

        /// <summary>
        /// 유저가 트레이더에게 팔 때의 가격 계산.
        /// 해당 트레이더의 sellMultiplier 적용.
        /// </summary>
        public int CalculateSellPrice(int basePrice, TraderDataSO trader)
        {
            return Mathf.RoundToInt(basePrice * trader.sellMultiplier);
        }

        /// <summary>
        /// 기본 sellMultiplier(0.5) 적용 간편 오버로드.
        /// </summary>
        public int CalculateSellPrice(int basePrice)
        {
            return Mathf.RoundToInt(basePrice * 0.5f);
        }

        // ─── ServerRpc ───

        /// <summary>
        /// 유저가 트레이더에게서 아이템 구매.
        /// 서버: 통신장비 레벨 체크 → 크레딧 차감 → 인벤토리 추가.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void BuyItemServerRpc(string itemID, RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;

            TraderEntry? entry = FindEntryByItemID(itemID);
            if (!entry.HasValue)
            {
                Debug.LogWarning($"[TraderManager] 아이템 '{itemID}' 판매하는 트레이더 없음");
                return;
            }

            // TODO: 통신장비 레벨 체크
            // int commLevel = CommStation.Instance.CurrentLevel.Value;
            // if (entry.Value.requiredCommLevel > commLevel) return;

            // TODO: 크레딧 차감
            // var wallet = GetPlayerWallet(clientId);
            // if (!wallet.TryPay(entry.Value.basePrice)) return;

            // TODO: 인벤토리에 아이템 추가
            // var inventory = GetPlayerInventory(clientId);
            // if (!inventory.TryAddItem(entry.Value.item)) { wallet.Earn(entry.Value.basePrice); return; }

            Debug.Log($"[TraderManager] Client {clientId} 구매: {itemID} ({entry.Value.basePrice} Cr)");
        }
        
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SellItemServerRpc(string itemID, RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;

            TraderEntry? entry = FindEntryByItemID(itemID);
            if (!entry.HasValue)
            {
                Debug.LogWarning($"[TraderManager] 아이템 '{itemID}' 정보 없음");
                return;
            }

            int sellPrice = CalculateSellPrice(entry.Value.basePrice);

            // TODO: 인벤토리에서 아이템 제거
            // var inventory = GetPlayerInventory(clientId);
            // if (!inventory.ConsumeItem(itemID, 1)) return;

            // TODO: 크레딧 지급
            // var wallet = GetPlayerWallet(clientId);
            // wallet.Earn(sellPrice);

            Debug.Log($"[TraderManager] Client {clientId} 판매: {itemID} ({sellPrice} Cr)");
        }

        // ─── 내부 ───

        private TraderEntry? FindEntryByItemID(string itemID)
        {
            foreach (TraderDataSO trader in traders)
            {
                foreach (TraderEntry entry in trader.stock)
                {
                    if (entry.item != null && entry.item.itemID == itemID)
                        return entry;
                }
            }
            return null;
        }
    }
}
