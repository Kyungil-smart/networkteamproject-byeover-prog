using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적 생체값 + IDamageable + IArmored.
    /// 적은 상체 아머만 있고 헬멧은 없다. 헬멧 getter는 null을 반환하고
    /// 내구도는 0을 반환한다 — DamageSystem이 null 체크로 분기한다.
    /// </summary>
    public class EnemyStats : NetworkBehaviour, IDamageable, IArmored
    {
        [SerializeField] private EnemyStatsSO statsSO;

        public NetworkVariable<float> CurrentHP = new(80f);
        public NetworkVariable<float> CurrentArmorDurability = new(0f);

        public EnemyStatsSO StatsSO => statsSO;
        public bool IsDead => CurrentHP.Value <= 0;
        public bool IsPlayer => false;

        public override void OnNetworkSpawn()
        {
            if (IsServer && statsSO != null)
            {
                CurrentHP.Value = statsSO.maxHP;
                CurrentArmorDurability.Value = statsSO.defaultArmor != null ? statsSO.defaultArmor.maxDurability : 0f;
            }
        }

        public void ApplyDamage(int damage, ulong attackerClientId, HitInfo hit)
        {
            if (!IsServer || IsDead) return;
            CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);
            if (CurrentHP.Value <= 0f)
            {
                EventBus.Publish(new EnemyKilledEvent
                {
                    attackerClientId = attackerClientId,
                    tier = statsSO != null ? statsSO.tier : EnemyTier.T1,
                    position = transform.position,
                });
                NetworkObject?.Despawn(destroy: true);
            }
        }
        
        public void ApplyDamage(int damage, ulong attackerClientId, Vector3 hit)
        {
            if (!IsServer || IsDead) return;
            CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);
            if (CurrentHP.Value <= 0f)
            {
                EventBus.Publish(new EnemyKilledEvent
                {
                    attackerClientId = attackerClientId,
                    tier = statsSO != null ? statsSO.tier : EnemyTier.T1,
                    position = transform.position,
                });
                NetworkObject?.Despawn(destroy: true);
            }
        }

        public HelmetDataSO GetEquippedHelmet() => null;
        public ArmorDataSO GetEquippedArmor() => statsSO != null ? statsSO.defaultArmor : null;
        public float GetHelmetDurability() => 0f;
        public float GetArmorDurability() => CurrentArmorDurability.Value;

        public void DamageHelmetDurability(float amount)
        {
        }

        public void DamageArmorDurability(float amount)
        {
            if (!IsServer) return;
            CurrentArmorDurability.Value = Mathf.Max(0f, CurrentArmorDurability.Value - amount);
        }
    }
}
