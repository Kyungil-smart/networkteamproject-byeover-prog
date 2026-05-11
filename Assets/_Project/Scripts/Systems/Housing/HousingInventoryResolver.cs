using Unity.Netcode;
using UnityEngine;

using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 하우징 제작/업그레이드에서 요청자의 실제 인벤토리를 찾는 공용 유틸
    // 테스트 인벤토리 대신 PlayerObject의 IInventory만 사용
    public static class HousingInventoryResolver
    {
        public static bool TryGetRequesterInventory(
            ulong requesterClientId,
            out IInventory inventory,
            out string failReason)
        {
            inventory = null;
            failReason = string.Empty;

            if (NetworkManager.Singleton == null)
            {
                failReason = "NetworkManager가 없습니다.";
                return false;
            }

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(requesterClientId, out NetworkClient client))
            {
                failReason = $"요청자 클라이언트를 찾지 못했습니다. ClientId: {requesterClientId}";
                return false;
            }

            if (client.PlayerObject == null)
            {
                failReason = $"요청자 PlayerObject가 없습니다. ClientId: {requesterClientId}";
                return false;
            }

            inventory = client.PlayerObject.GetComponent<IInventory>();

            if (inventory != null)
                return true;

            inventory = client.PlayerObject.GetComponentInChildren<IInventory>(true);

            if (inventory != null)
                return true;

            failReason = $"요청자 PlayerObject에서 IInventory를 찾지 못했습니다. PlayerObject: {client.PlayerObject.name}";
            return false;
        }

        public static bool IsNetworkReady(out string failReason)
        {
            failReason = string.Empty;

            if (NetworkManager.Singleton == null)
            {
                failReason = "NetworkManager가 없습니다.";
                return false;
            }

            if (!NetworkManager.Singleton.IsListening)
            {
                failReason = "네트워크가 시작되지 않았습니다. Host 또는 Client 실행 후 요청해야 합니다.";
                return false;
            }

            return true;
        }
    }
}