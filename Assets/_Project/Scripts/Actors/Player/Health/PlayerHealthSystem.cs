using System;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 플레이어 체력 관리 + Alive/Knocked/Dead 상태 머신.
    /// IDamageable + IRevivable + IRecoverable 구현.
    /// </summary>
    /// <remarks>
    /// 서버 권위. 상태 전환은 서버에서만 일어난다.
    /// 모든 클라이언트는 NetworkVariable과 EventBus를 통해 상태를 수신한다.
    /// IArmored는 EquipmentSlots가 구현 — DamageSystem은 장비를 별도로 질의하여
    /// 관심사를 분리한다.
    /// </remarks>
    public class PlayerHealthSystem : NetworkBehaviour, IDamageable, IRevivable, IRecoverable
    {
        [Header("====생존 상태 설정====")]
        [Tooltip("Alive 상태에서 사용하는 최대체력" +
                 "\n서버가 네트워크 스폰 시 CurrentHP를 이 값으로 초기화")]
        [SerializeField, Min(1f)] private float maxHP = 100f;

        [Header("====기절 상태 설정(PUBG-style)====")]
        [Tooltip("HP가 0 이하가 되어 Knocked 상태로 전환될 때 KnockedHP에 설정되는 최대 기절 체력" +
                 "\n이 값이 0이 되면 Dead 상태로 전환")]
        [SerializeField, Min(1f)] private float knockedMaxHP = 100f;
        
        [Tooltip("Knocked 상태에서 Dead 상태로 전환되기까지 허용되는 최대 시간" +
                 "\n이 시간이 0이 되면 Dead 상태로 전환")]
        [SerializeField] private float bleedoutSeconds = 60f;
        
        [Tooltip("Knocked 상태에서 초당 감소하는 KnockedHP 양" +
                 "\n부활 중이 아닐 때만 적용되며, KnockedHP가 0이 되면 Dead 상태로 전환")]
        [SerializeField] private float bleedoutDamagePerSecond = 1.5f;
        
        [Header("====부활 설정====")]
        [Tooltip("부활 완료 시 CurrentHP에 설정되는 체력" +
                 "\nKnocked 상태에서 Alive 상태로 복귀할 때의 시작 체력")]
        [SerializeField] private float reviveHpAmount = 30f;

        [Header("====사망 처리====")]
        [Tooltip("Dead 상태로 전환될 때 서버에서 생성할 시체 프리팹" +
                 "\n인벤토리 이전을 위해 NetworkObject, PlayerCorpse, CorpseInventory 구성이 필요")]
        [SerializeField] private GameObject corpsePrefab;

        public NetworkVariable<float> CurrentHP = new(100f);
        public NetworkVariable<float> KnockedHP = new(0f);
        public NetworkVariable<float> BleedoutRemaining = new(0f);
        public NetworkVariable<PlayerState> State = new(PlayerState.Alive);

        private RollSystem rollSystem;
        
        public float MaxHP => maxHP;
        public bool IsAlive => State.Value == PlayerState.Alive;
        public bool IsKnocked => State.Value == PlayerState.Knocked;
        public bool IsDead => State.Value == PlayerState.Dead;
        public bool CanBeRevived => IsKnocked && KnockedHP.Value > 0;
        public float ReviveHpAmount => reviveHpAmount;

        private bool isBeingRevived;
        private ulong reviverClientId;

        private void Awake()
        {
            rollSystem = GetComponent<RollSystem>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CurrentHP.Value = maxHP;
                KnockedHP.Value = 0f;
                BleedoutRemaining.Value = 0f;
                State.Value = PlayerState.Alive;
            }

            CurrentHP.OnValueChanged += BroadcastHpChanged;
            KnockedHP.OnValueChanged += BroadcastKnockedHpChanged;
            State.OnValueChanged += BroadcastStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            CurrentHP.OnValueChanged -= BroadcastHpChanged;
            KnockedHP.OnValueChanged -= BroadcastKnockedHpChanged;
            State.OnValueChanged -= BroadcastStateChanged;
        }

        private void Update()
        {
            // TODO(NetworkAuthority): 로컬 단일 플레이 테스트 중에는 서버 권위 가드를 임시 비활성화
            // 복구 조건: 서버 전용 호출 경로가 검증되면 활성화
            // if (IsSpawned && !IsServer) return;

            if (IsKnocked && !isBeingRevived)
            {
                BleedoutRemaining.Value = Mathf.Max(0f, BleedoutRemaining.Value - Time.deltaTime);
                KnockedHP.Value = Mathf.Max(0f, KnockedHP.Value - bleedoutDamagePerSecond * Time.deltaTime);

                if (BleedoutRemaining.Value <= 0f || KnockedHP.Value <= 0f)
                {
                    TransitionToDead(0);
                }
            }
        }

        public bool IsPlayer => true;

        public void ApplyDamage(int damage, ulong attackerClientId, HitInfo hit)
        {
            if (IsDead) return;
            
            // TODO(NetworkAuthority): 로컬 단일 플레이 테스트 중에는 서버 권위 가드를 임시 비활성화
            // 복구 조건: 서버 전용 호출 경로가 검증되면 활성화
            // if (IsSpawned && !IsServer) return;

            if (ShouldIgnoreDamage()) return;
            
            if (IsAlive)
            {
                CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);
                if (CurrentHP.Value <= 0f) TransitionToKnocked(attackerClientId);
            }
            else if (IsKnocked)
            {
                KnockedHP.Value = Mathf.Max(0f, KnockedHP.Value - damage);
                if (KnockedHP.Value <= 0f) TransitionToDead(attackerClientId);
            }
        }

        public void ApplyDamage(int damage, ulong attackerClientId, Vector3 hit)
        {
            if (!IsServer || IsDead) return;

            if (ShouldIgnoreDamage()) return;

            if (IsAlive)
            {
                CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);
                if (CurrentHP.Value <= 0f) TransitionToKnocked(attackerClientId);
            }
            else if (IsKnocked)
            {
                KnockedHP.Value = Mathf.Max(0f, KnockedHP.Value - damage);
                if (KnockedHP.Value <= 0f) TransitionToDead(attackerClientId);
            }
        }

        private bool ShouldIgnoreDamage()
        {
            if (!IsAlive) return false;

            return rollSystem != null && rollSystem.IsDamageImmune;
        }
        public void Heal(float amount)
        {
            if (!IsAlive) return;
            
            // TODO(NetworkAuthority): 로컬 단일 플레이 테스트 중에는 서버 권위 가드를 임시 비활성화
            // 복구 조건: 서버 전용 호출 경로가 검증되면 활성화
            // if (IsSpawned && !IsServer) return;
            
            CurrentHP.Value = Mathf.Min(maxHP, CurrentHP.Value + amount);
        }

        private void TransitionToKnocked(ulong attackerClientId)
        {
            // TODO(NetworkAuthority): 로컬 단일 플레이 테스트 중에는 서버 권위 가드를 임시 비활성화
            // 복구 조건: 서버 전용 호출 경로가 검증되면 활성화
            // if (IsSpawned && !IsServer) return;
            
            KnockedHP.Value = knockedMaxHP;
            BleedoutRemaining.Value = bleedoutSeconds;
            State.Value = PlayerState.Knocked;

            EventBus.Publish(new PlayerKnockedEvent
            {
                victimClientId = OwnerClientId,
                attackerClientId = attackerClientId,
                position = transform.position,
                bleedoutSeconds = bleedoutSeconds,
            });
        }

        private void TransitionToDead(ulong attackerClientId)
        {
            // TODO(NetworkAuthority): 로컬 단일 플레이 테스트 중에는 서버 권위 가드를 임시 비활성화
            // 복구 조건: 서버 전용 호출 경로가 검증되면 활성화
            // if (IsSpawned && !IsServer) return;
            
            KnockedHP.Value = 0f;
            BleedoutRemaining.Value = 0f;
            State.Value = PlayerState.Dead;

            SpawnCorpse();

            EventBus.Publish(new PlayerDiedEvent
            {
                victimClientId = OwnerClientId,
                killerClientId = attackerClientId,
            });
        }

        private void SpawnCorpse()
        {
            if (!IsServer || corpsePrefab == null) return;

            var corpseGO = Instantiate(corpsePrefab, GetCorpsePosition(), GetCorpseRotation());
            var corpseNetObj = corpseGO.GetComponent<NetworkObject>();
            if (corpseNetObj == null)
            {
                Debug.LogError("[PlayerHealthSystem] corpsePrefab missing NetworkObject");
                Destroy(corpseGO);
                return;
            }
            corpseNetObj.Spawn(destroyWithScene: true);

            TransferInventoryToCorpse(corpseGO);
        }

        public void OnReviveBegin(ulong reviverClientId)
        {
            if (!IsServer || !CanBeRevived) return;
            isBeingRevived = true;
            this.reviverClientId = reviverClientId;
        }

        public void OnReviveCancel()
        {
            if (!IsServer) return;
            isBeingRevived = false;
            reviverClientId = 0;
        }

        public void OnReviveComplete(ulong reviverClientId)
        {
            if (!IsServer || !CanBeRevived) return;
            isBeingRevived = false;
            this.reviverClientId = 0;

            CurrentHP.Value = reviveHpAmount;
            KnockedHP.Value = 0f;
            BleedoutRemaining.Value = 0f;
            State.Value = PlayerState.Alive;
        }

        public Vector3 GetCorpsePosition() => transform.position;
        public Quaternion GetCorpseRotation() => transform.rotation;

        public void TransferInventoryToCorpse(GameObject corpse)
        {
            if (!IsServer || corpse == null) return;

            var corpseInv = corpse.GetComponent<DeadZone.Actors.CorpseInventory>();
            var corpseScript = corpse.GetComponent<DeadZone.Actors.PlayerCorpse>();
            var sourceInv = GetComponent<GridInventory>();
            var sourceEquip = GetComponent<EquipmentSlots>();

            if (corpseInv != null)
            {
                corpseInv.PopulateFromPlayer(sourceInv, sourceEquip);
            }
            if (corpseScript != null)
            {
                corpseScript.InitializeServer(OwnerClientId, $"Player {OwnerClientId}");
            }

            if (sourceInv != null)
            {
                while (sourceInv.ServerGrid.Count > 0)
                {
                    sourceInv.ServerGrid.RemoveAt(sourceInv.ServerGrid.Count - 1);
                }
            }
            if (sourceEquip != null)
            {
                sourceEquip.HeadSlotId.Value = "";
                sourceEquip.TorsoSlotId.Value = "";
                sourceEquip.Primary1Id.Value = "";
                sourceEquip.Primary2Id.Value = "";
                sourceEquip.SecondaryId.Value = "";
                sourceEquip.MeleeId.Value = "";
                sourceEquip.CurrentEquipped.Value = "";
                sourceEquip.HelmetDurability.Value = 0f;
                sourceEquip.ArmorDurability.Value = 0f;
            }
        }

        private void BroadcastHpChanged(float oldVal, float newVal)
        {
            EventBus.Publish(new PlayerHpChangedEvent
            {
                clientId = OwnerClientId,
                oldValue = oldVal,
                newValue = newVal,
            });
        }

        private void BroadcastKnockedHpChanged(float oldVal, float newVal)
        {
            EventBus.Publish(new PlayerKnockedHpChangedEvent
            {
                clientId = OwnerClientId,
                oldValue = oldVal,
                newValue = newVal,
            });
        }

        private void BroadcastStateChanged(PlayerState oldState, PlayerState newState)
        {
            EventBus.Publish(new PlayerStateChangedEvent
            {
                clientId = OwnerClientId,
                oldState = oldState,
                newState = newState,
            });
        }
    }
}
