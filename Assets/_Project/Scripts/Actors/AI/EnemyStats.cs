using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    public class EnemyStats : NetworkBehaviour, IDamageable, IArmored
    {
        [SerializeField] private EnemyStatsSO statsSO;

        [Header("Corpse")]
        [Tooltip("EnemyStatsSO.corpsePrefab이 null일 때 사용할 범용 시체 프리팹")]
        [SerializeField] private GameObject fallbackCorpsePrefab;

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
                CurrentArmorDurability.Value = statsSO.defaultArmor != null
                    ? statsSO.defaultArmor.maxDurability
                    : 0f;
            }
        }

        public void ApplyDamage(int damage, ulong attackerClientId, HitInfo hit)
        {
            if (!IsServer || IsDead) return;
            CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);
            
            GetComponent<EnemyAnimHandler>()?.PlayHitFlash();

            if (CurrentHP.Value <= 0f)
                Die(attackerClientId);
        }

        public void ApplyDamage(int damage, ulong attackerClientId, Vector3 hit)
        {
            if (!IsServer || IsDead) return;
            CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);
            if (CurrentHP.Value <= 0f)
                Die(attackerClientId);
        }

        private void Die(ulong attackerClientId)
        {
            // 1. EnemyKilledEvent 발행 (QuestManager, KillFeedUI 등이 구독)
            EventBus.Publish(new EnemyKilledEvent
            {
                attackerClientId = attackerClientId,
                tier = statsSO != null ? statsSO.tier : EnemyTier.T1,
                position = transform.position,
                enemyId = statsSO != null && !string.IsNullOrEmpty(statsSO.enemyId)
                    ? new FixedString64Bytes(statsSO.enemyId)
                    : default,
            });

            // 2. 시체 스폰
            SpawnCorpse();

            // 3. 원본 적 오브젝트 제거
            NetworkObject?.Despawn(destroy: true);
        }

        private void SpawnCorpse()
        {
            if (statsSO == null) return;

            // 시체 프리팹 결정: SO 지정 > 범용 폴백
            GameObject prefab = statsSO.corpsePrefab != null
                ? statsSO.corpsePrefab
                : fallbackCorpsePrefab;

            if (prefab == null)
            {
                Debug.LogWarning($"[EnemyStats] {statsSO.displayName}: 시체 프리팹 없음 — 시체 스킵");
                return;
            }

            // 사망 위치/회전에 시체 스폰
            var corpseObj = Instantiate(prefab, transform.position, transform.rotation);
            var netObj = corpseObj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"[EnemyStats] 시체 프리팹에 NetworkObject가 없음: {prefab.name}");
                Destroy(corpseObj);
                return;
            }

            netObj.Spawn();

            // 시체 초기화 (장비 + 루팅 아이템 채우기)
            var enemyCorpse = corpseObj.GetComponent<EnemyCorpse>();
            if (enemyCorpse != null)
            {
                enemyCorpse.InitializeServer(statsSO);
            }
        }

        // ───────── IArmored ─────────

        public HelmetDataSO GetEquippedHelmet() => null;
        public ArmorDataSO GetEquippedArmor() => statsSO != null ? statsSO.defaultArmor : null;
        public float GetHelmetDurability() => 0f;
        public float GetArmorDurability() => CurrentArmorDurability.Value;

        public void DamageHelmetDurability(float amount) { }

        public void DamageArmorDurability(float amount)
        {
            if (!IsServer) return;
            CurrentArmorDurability.Value = Mathf.Max(0f, CurrentArmorDurability.Value - amount);
        }
    }
}