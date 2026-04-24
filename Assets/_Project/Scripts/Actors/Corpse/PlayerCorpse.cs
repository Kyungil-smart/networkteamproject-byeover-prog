using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 플레이어가 완전히 사망하면 스폰되는 영속 시체. 인벤토리 스냅샷을 보유하고
    /// IInteractable로 노출되어 다른 플레이어가 루팅할 수 있다.
    /// 레이드가 끝나거나 모든 루팅이 회수될 때까지 존재한다.
    /// </summary>
    public class PlayerCorpse : NetworkBehaviour, IInteractable
    {
        public NetworkVariable<ulong> OwnerClientIdSnapshot = new(ulong.MaxValue);
        public NetworkVariable<FixedString64Bytes> OwnerName = new("Player");
        public NetworkVariable<bool> IsEmpty = new(false);

        [SerializeField] private CorpseInventory corpseInventory;
        [SerializeField] private float despawnAfterEmptySeconds = 30f;

        private float emptySince = -1f;

        public override void OnNetworkSpawn()
        {
            if (corpseInventory == null) corpseInventory = GetComponent<CorpseInventory>();

            if (IsServer)
            {
                EventBus.Publish(new CorpseSpawnedEvent
                {
                    ownerClientId = OwnerClientIdSnapshot.Value,
                    position = transform.position,
                });
            }
        }

        private void Update()
        {
            if (!IsServer) return;
            if (!IsEmpty.Value && corpseInventory != null && corpseInventory.SlotCount == 0)
            {
                IsEmpty.Value = true;
                emptySince = Time.time;
            }

            if (IsEmpty.Value && emptySince > 0 && Time.time - emptySince > despawnAfterEmptySeconds)
            {
                if (NetworkObject != null) NetworkObject.Despawn(destroy: true);
            }
        }

        public string GetPromptText()
        {
            if (IsEmpty.Value) return "[F] Empty corpse";
            return $"[F] Loot {OwnerName.Value}'s corpse";
        }

        public void OnInteract(ulong clientId)
        {
            if (IsEmpty.Value) return;
            corpseInventory?.RequestOpenServerRpc(clientId);
        }

        public void InitializeServer(ulong ownerId, string name)
        {
            if (!IsServer) return;
            OwnerClientIdSnapshot.Value = ownerId;
            OwnerName.Value = name;
        }
    }
}
