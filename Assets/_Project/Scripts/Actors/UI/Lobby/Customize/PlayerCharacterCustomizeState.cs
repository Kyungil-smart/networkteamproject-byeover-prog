using System;

using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerCharacterCustomizeState : NetworkBehaviour
    {
        private const string BodyPrefsKey = "DZ_Custom_Body";
        private const string HeadPrefsKey = "DZ_Custom_Head";
        private const string BeardPrefsKey = "DZ_Custom_Beard";
        private const string HatPrefsKey = "DZ_Custom_Hat";
        private const string ClientScopedPrefsPrefix = "DZ_ClientCustom";

        [Header("외형 적용 대상")]
        [SerializeField] private PlayerCharacterCustomizeView customizeView;

        [Header("로드 옵션")]
        [SerializeField] private bool loadOwnerSavedDataOnSpawn = true;

        [Header("디버그")]
        [SerializeField] private bool showDebugLogs = false;

        public readonly NetworkVariable<CharacterCustomizeNetworkData> CustomizeData = new(
            new CharacterCustomizeNetworkData(0, 0, 0, 0),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (customizeView == null)
                customizeView = GetComponentInChildren<PlayerCharacterCustomizeView>(true);

            CustomizeData.OnValueChanged += HandleCustomizeDataChanged;

            ApplyCustomizeData(CustomizeData.Value);

            if (IsOwner && loadOwnerSavedDataOnSpawn)
                SubmitLocalSavedCustomizeData();
        }

        public override void OnNetworkDespawn()
        {
            CustomizeData.OnValueChanged -= HandleCustomizeDataChanged;

            base.OnNetworkDespawn();
        }

        private void HandleCustomizeDataChanged(
            CharacterCustomizeNetworkData previousValue,
            CharacterCustomizeNetworkData newValue)
        {
            ApplyCustomizeData(newValue);
        }

        private void ApplyCustomizeData(CharacterCustomizeNetworkData data)
        {
            if (customizeView == null)
            {
                customizeView = GetComponentInChildren<PlayerCharacterCustomizeView>(true);

                if (customizeView == null)
                {
                    Debug.LogWarning("[PlayerCharacterCustomizeState] PlayerCharacterCustomizeView를 찾지 못했습니다.", this);
                    return;
                }
            }

            customizeView.Apply(data);
        }

        public void SubmitLocalSavedCustomizeData()
        {
            if (!IsOwner)
                return;

            CharacterCustomizeNetworkData data = LoadLocalSavedCustomizeData();

            if (showDebugLogs)
            {
                Debug.Log(
                    $"[PlayerCharacterCustomizeState] Submit customize. " +
                    $"Body={data.BodyIndex}, Head={data.HeadIndex}, Beard={data.BeardIndex}, Hat={data.HatIndex}",
                    this);
            }

            if (IsServer)
            {
                ApplyCustomizeDataServer(data);
                return;
            }

            SubmitCustomizeDataRpc(data);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitCustomizeDataRpc(
            CharacterCustomizeNetworkData data,
            RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[PlayerCharacterCustomizeState] 소유자가 아닌 클라이언트의 커스터마이징 적용 요청을 거부했습니다. " +
                    $"Sender={rpcParams.Receive.SenderClientId}, Owner={OwnerClientId}",
                    this);
                return;
            }

            ApplyCustomizeDataServer(data);
        }

        public bool ApplyCustomizeDataServer(CharacterCustomizeNetworkData data)
        {
            if (!IsServer)
                return false;

            CustomizeData.Value = data.Normalize();

            if (showDebugLogs)
            {
                Debug.Log(
                    $"[PlayerCharacterCustomizeState] Server applied customize. " +
                    $"Owner={OwnerClientId}, Body={data.BodyIndex}, Head={data.HeadIndex}, Beard={data.BeardIndex}, Hat={data.HatIndex}",
                    this);
            }

            return true;
        }

        public static CharacterCustomizeNetworkData LoadLocalSavedCustomizeData()
        {
            if (TryGetLocalClientScopedKey(BodyPrefsKey, out string scopedBodyKey) &&
                PlayerPrefs.HasKey(scopedBodyKey))
            {
                return new CharacterCustomizeNetworkData(
                    PlayerPrefs.GetInt(scopedBodyKey, 0),
                    PlayerPrefs.GetInt(GetLocalClientScopedKey(HeadPrefsKey), 0),
                    PlayerPrefs.GetInt(GetLocalClientScopedKey(BeardPrefsKey), 0),
                    PlayerPrefs.GetInt(GetLocalClientScopedKey(HatPrefsKey), 0)
                ).Normalize();
            }

            return new CharacterCustomizeNetworkData(
                PlayerPrefs.GetInt(BodyPrefsKey, 0),
                PlayerPrefs.GetInt(HeadPrefsKey, 0),
                PlayerPrefs.GetInt(BeardPrefsKey, 0),
                PlayerPrefs.GetInt(HatPrefsKey, 0)
            ).Normalize();
        }

        public static void SaveLocalCustomizeData(CharacterCustomizeNetworkData data)
        {
            data = data.Normalize();

            if (TryGetLocalClientScopedKey(BodyPrefsKey, out string scopedBodyKey))
            {
                PlayerPrefs.SetInt(scopedBodyKey, data.BodyIndex);
                PlayerPrefs.SetInt(GetLocalClientScopedKey(HeadPrefsKey), data.HeadIndex);
                PlayerPrefs.SetInt(GetLocalClientScopedKey(BeardPrefsKey), data.BeardIndex);
                PlayerPrefs.SetInt(GetLocalClientScopedKey(HatPrefsKey), data.HatIndex);
            }
            else
            {
                PlayerPrefs.SetInt(BodyPrefsKey, data.BodyIndex);
                PlayerPrefs.SetInt(HeadPrefsKey, data.HeadIndex);
                PlayerPrefs.SetInt(BeardPrefsKey, data.BeardIndex);
                PlayerPrefs.SetInt(HatPrefsKey, data.HatIndex);
            }

            PlayerPrefs.Save();
        }

        private static bool TryGetLocalClientScopedKey(string prefsKey, out string scopedKey)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                scopedKey = string.Empty;
                return false;
            }

            scopedKey = GetClientScopedKey(networkManager.LocalClientId, prefsKey);
            return true;
        }

        private static string GetLocalClientScopedKey(string prefsKey)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null
                ? GetClientScopedKey(networkManager.LocalClientId, prefsKey)
                : prefsKey;
        }

        private static string GetClientScopedKey(ulong clientId, string prefsKey)
        {
            return $"{ClientScopedPrefsPrefix}_{clientId}_{prefsKey}";
        }
    }

    [Serializable]
    public struct CharacterCustomizeNetworkData :
        INetworkSerializable,
        IEquatable<CharacterCustomizeNetworkData>
    {
        public int BodyIndex;
        public int HeadIndex;
        public int BeardIndex;
        public int HatIndex;

        public CharacterCustomizeNetworkData(
            int bodyIndex,
            int headIndex,
            int beardIndex,
            int hatIndex)
        {
            BodyIndex = bodyIndex;
            HeadIndex = headIndex;
            BeardIndex = beardIndex;
            HatIndex = hatIndex;
        }

        public CharacterCustomizeNetworkData Normalize()
        {
            return new CharacterCustomizeNetworkData(
                Mathf.Max(0, BodyIndex),
                Mathf.Max(0, HeadIndex),
                Mathf.Max(0, BeardIndex),
                Mathf.Max(0, HatIndex)
            );
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref BodyIndex);
            serializer.SerializeValue(ref HeadIndex);
            serializer.SerializeValue(ref BeardIndex);
            serializer.SerializeValue(ref HatIndex);
        }

        public bool Equals(CharacterCustomizeNetworkData other)
        {
            return BodyIndex == other.BodyIndex
                   && HeadIndex == other.HeadIndex
                   && BeardIndex == other.BeardIndex
                   && HatIndex == other.HatIndex;
        }
    }
}
