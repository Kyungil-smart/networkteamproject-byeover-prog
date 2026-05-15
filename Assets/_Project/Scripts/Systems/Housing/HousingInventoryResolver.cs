using Unity.Netcode;
using UnityEngine;

using DeadZone.Systems;
using DeadZone.Systems.Save;

namespace DeadZone.Systems.Housing
{
    // 하우징 제작/업그레이드에서 요청자의 실제 인벤토리를 찾는 공용 유틸
    // 로비 저장 인벤토리와 보관함 재료를 우선 사용하고, 없을 때 PlayerObject의 IInventory를 사용합니다.
    public static class HousingInventoryResolver
    {
        public static bool TryGetRequesterInventory(
            ulong requesterClientId,
            out IInventory inventory,
            out string failReason)
        {
            inventory = null;
            failReason = string.Empty;

            if (TryCreateLobbySavedInventory(out inventory))
                return true;

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

        public static bool TryCreateLobbySavedInventory(out IInventory inventory)
        {
            inventory = null;

            LobbyInventoryState inventoryState = Object.FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);
            if (inventoryState == null)
                return false;

            LobbySavedInventoryAdapter adapter = new LobbySavedInventoryAdapter(inventoryState);
            if (!adapter.IsValid || !adapter.HasAnyItems)
                return false;

            inventory = adapter;
            return true;
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
