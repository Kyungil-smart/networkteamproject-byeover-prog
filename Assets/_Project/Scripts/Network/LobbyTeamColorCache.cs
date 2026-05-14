using System;
using System.Collections.Generic;
using DeadZone.Actors.Player;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Network
{
    /// <summary>
    /// 로비에서 생성된 파티원 색상을 ClientId 기준으로 임시 저장합니다.
    /// 레이드 씬 진입 후 PlayerTeamIdentity가 이 값을 읽어 팀 색상을 적용합니다.
    /// </summary>
    public static class LobbyTeamColorCache
    {
        private static readonly Dictionary<ulong, Color32> ColorsByClientId = new();

        public static void SetColor(ulong clientId, Color32 color)
        {
            ColorsByClientId[clientId] = color;
        }

        public static bool TryGetColor(ulong clientId, out Color32 color)
        {
            return ColorsByClientId.TryGetValue(clientId, out color);
        }

        public static void Clear()
        {
            ColorsByClientId.Clear();
        }
    }

    /// <summary>
    /// 레이드 씬 진입 전 각 클라이언트가 제출한 커스터마이징 값을 ClientId 기준으로 임시 보관합니다.
    /// PlayerCharacterCustomizeState의 NetworkVariable에 서버가 OwnerClientId 기준으로 주입합니다.
    /// </summary>
    public static class LobbyPlayerCustomizeCache
    {
        private static readonly Dictionary<ulong, CharacterCustomizeNetworkData> CustomizesByClientId = new();

        public static void Clear()
        {
            CustomizesByClientId.Clear();
        }

        public static bool StoreLocalCustomizeForLocalClient()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return false;

            CustomizesByClientId[networkManager.LocalClientId] =
                PlayerCharacterCustomizeState.LoadLocalSavedCustomizeData();
            return true;
        }

        public static string CreateLocalCustomizeJson()
        {
            CharacterCustomizeNetworkData data = PlayerCharacterCustomizeState.LoadLocalSavedCustomizeData();
            return JsonUtility.ToJson(ToPayload(data));
        }

        public static bool StoreSubmittedCustomize(ulong clientId, string customizeJson)
        {
            if (string.IsNullOrWhiteSpace(customizeJson))
                return false;

            LobbyCustomizePayload payload;

            try
            {
                payload = JsonUtility.FromJson<LobbyCustomizePayload>(customizeJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyCustomize] Submitted customize JSON could not be parsed. clientId={clientId}, error={ex.Message}");
                return false;
            }

            CustomizesByClientId[clientId] = FromPayload(payload);
            return true;
        }

        public static void SaveCustomizesForClients(IReadOnlyList<ulong> clientIds)
        {
            if (clientIds == null || clientIds.Count == 0)
                return;

            RemoveUnexpectedCustomizes(clientIds);

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return;

            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong clientId = clientIds[i];
                if (CustomizesByClientId.ContainsKey(clientId))
                    continue;

                if (clientId == networkManager.LocalClientId)
                    StoreLocalCustomizeForLocalClient();
            }
        }

        public static bool TryApplyCustomize(ulong clientId, GameObject playerObject)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
                return false;

            if (playerObject == null)
            {
                Debug.LogWarning($"[LobbyCustomize] Cannot apply customize. PlayerObject is null clientId={clientId}");
                return false;
            }

            if (!CustomizesByClientId.TryGetValue(clientId, out CharacterCustomizeNetworkData data))
            {
                Debug.Log(
                    $"[LobbyCustomize] No lobby customize data for clientId={clientId}. " +
                    "Waiting for owner submission or keeping prefab default.",
                    playerObject);
                return false;
            }

            PlayerCharacterCustomizeState customizeState =
                playerObject.GetComponent<PlayerCharacterCustomizeState>();

            if (customizeState == null)
                customizeState = playerObject.GetComponentInChildren<PlayerCharacterCustomizeState>(true);

            if (customizeState == null)
            {
                Debug.LogWarning($"[LobbyCustomize] Missing PlayerCharacterCustomizeState on PlayerPrefab clientId={clientId}", playerObject);
                return false;
            }

            return customizeState.ApplyCustomizeDataServer(data);
        }

        private static void RemoveUnexpectedCustomizes(IReadOnlyList<ulong> expectedClientIds)
        {
            List<ulong> removeBuffer = null;

            foreach (ulong clientId in CustomizesByClientId.Keys)
            {
                if (ContainsClientId(expectedClientIds, clientId))
                    continue;

                removeBuffer ??= new List<ulong>();
                removeBuffer.Add(clientId);
            }

            if (removeBuffer == null)
                return;

            for (int i = 0; i < removeBuffer.Count; i++)
                CustomizesByClientId.Remove(removeBuffer[i]);
        }

        private static bool ContainsClientId(IReadOnlyList<ulong> clientIds, ulong clientId)
        {
            for (int i = 0; i < clientIds.Count; i++)
            {
                if (clientIds[i] == clientId)
                    return true;
            }

            return false;
        }

        private static LobbyCustomizePayload ToPayload(CharacterCustomizeNetworkData data)
        {
            data = data.Normalize();

            return new LobbyCustomizePayload
            {
                bodyIndex = data.BodyIndex,
                headIndex = data.HeadIndex,
                beardIndex = data.BeardIndex,
                hatIndex = data.HatIndex
            };
        }

        private static CharacterCustomizeNetworkData FromPayload(LobbyCustomizePayload payload)
        {
            if (payload == null)
                return new CharacterCustomizeNetworkData(0, 0, 0, 0);

            return new CharacterCustomizeNetworkData(
                payload.bodyIndex,
                payload.headIndex,
                payload.beardIndex,
                payload.hatIndex
            ).Normalize();
        }

        [Serializable]
        private sealed class LobbyCustomizePayload
        {
            public int bodyIndex;
            public int headIndex;
            public int beardIndex;
            public int hatIndex;
        }
    }
}
