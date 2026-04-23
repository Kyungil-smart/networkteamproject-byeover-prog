using Unity.Netcode;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 플레이어별 크레딧. PlayerPrefab Root에 부착 (ServiceLocator에는 등록하지 않음 —
    /// 각 플레이어가 자기 인스턴스를 가지기 때문).
    /// </summary>
    public class WalletSystem : NetworkBehaviour
    {
        public NetworkVariable<int> Credits = new(
            value: 0,
            readPerm: NetworkVariableReadPermission.Owner,
            writePerm: NetworkVariableWritePermission.Server);

        public bool TryPay(int amount)
        {
            if (!IsServer) return false;
            if (amount < 0) return false;
            if (Credits.Value < amount) return false;
            int oldVal = Credits.Value;
            Credits.Value -= amount;
            EventBus.Publish(new CreditsChangedEvent
            {
                clientId = OwnerClientId,
                delta = -amount,
                newBalance = Credits.Value,
            });
            return true;
        }

        public void Earn(int amount)
        {
            if (!IsServer) return;
            if (amount <= 0) return;
            Credits.Value += amount;
            EventBus.Publish(new CreditsChangedEvent
            {
                clientId = OwnerClientId,
                delta = amount,
                newBalance = Credits.Value,
            });
        }
    }
}
