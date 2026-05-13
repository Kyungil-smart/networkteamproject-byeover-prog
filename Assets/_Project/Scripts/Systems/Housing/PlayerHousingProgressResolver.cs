using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;

namespace DeadZone.Systems.Housing
{
    // 서버에서 ClientId 기준으로 해당 플레이어의 하우징 진행도를 찾습니다.
    // 업그레이드/제작 요청자를 구분하기 위한 공용 유틸
    public static class PlayerHousingProgressResolver
    {
        public static bool TryGetProgress(ulong clientId, out PlayerHousingProgress progress)
        {
            progress = null;

            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null)
            {
                Debug.LogWarning("[PlayerHousingProgressResolver] NetworkManager.Singleton이 없습니다.");
                return false;
            }

            if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            {
                Debug.LogWarning($"[PlayerHousingProgressResolver] 연결된 클라이언트를 찾을 수 없습니다. ClientId: {clientId}");
                return false;
            }

            if (client.PlayerObject == null)
            {
                Debug.LogWarning($"[PlayerHousingProgressResolver] PlayerObject가 없습니다. ClientId: {clientId}");
                return false;
            }

            progress = client.PlayerObject.GetComponent<PlayerHousingProgress>();

            if (progress == null)
            {
                Debug.LogWarning(
                    $"[PlayerHousingProgressResolver] PlayerObject에 PlayerHousingProgress가 없습니다. ClientId: {clientId}",
                    client.PlayerObject
                );
                return false;
            }

            return true;
        }
    }
}