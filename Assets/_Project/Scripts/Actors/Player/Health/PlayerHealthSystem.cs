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
    /// 하우징 보너스는 기본 maxHP를 직접 덮어쓰지 않고 별도 보너스로 합산한다.
    /// </remarks>
    public class PlayerHealthSystem : NetworkBehaviour, IDamageable, IRevivable, IRecoverable
    {
        [Header("====생존 상태 설정====")]
        [Tooltip("Alive 상태에서 사용하는 기본 최대 체력입니다.\n하우징 보너스는 이 값을 직접 바꾸지 않고 별도 보너스로 더합니다.")]
        [SerializeField, Min(1f)] private float maxHP = 100f;

        [Header("====하우징 보너스====")]
        [Tooltip("의료 시설 등 하우징 효과로 증가한 최대 체력 보너스입니다. 런타임에서 PlayerHousingBonusReceiver가 갱신합니다.")]
        [SerializeField, Min(0f)] private float housingMaxHpBonus;

        [Tooltip("최대 체력이 증가할 때 현재 체력도 증가분만큼 같이 회복할지 여부입니다.")]
        [SerializeField] private bool fillHpWhenMaxHpIncreased = true;

        [Header("====기절 상태 설정(PUBG-style)====")]
        [Tooltip("HP가 0 이하가 되어 Knocked 상태로 전환될 때 KnockedHP에 설정되는 최대 기절 체력\n이 값이 0이 되면 Dead 상태로 전환")]
        [SerializeField, Min(1f)] private float knockedMaxHP = 100f;

        [Tooltip("Knocked 상태에서 Dead 상태로 전환되기까지 허용되는 최대 시간\n이 시간이 0이 되면 Dead 상태로 전환")]
        [SerializeField] private float bleedoutSeconds = 60f;

        [Tooltip("Knocked 상태에서 초당 감소하는 KnockedHP 양\n부활 중이 아닐 때만 적용되며, KnockedHP가 0이 되면 Dead 상태로 전환")]
        [SerializeField] private float bleedoutDamagePerSecond = 1.5f;

        [Header("====부활 설정====")]
        [Tooltip("부활 완료 시 CurrentHP에 설정되는 체력\nKnocked 상태에서 Alive 상태로 복귀할 때의 시작 체력")]
        [SerializeField] private float reviveHpAmount = 20f;

        [Header("====사망 처리====")]
        [Tooltip("Dead 상태로 전환될 때 서버에서 생성할 시체 프리팹\n인벤토리 이전을 위해 NetworkObject, PlayerCorpse, CorpseInventory 구성이 필요")]
        [SerializeField] private GameObject corpsePrefab;

        public NetworkVariable<float> CurrentHP = new(100f);
        public NetworkVariable<float> KnockedHP = new(0f);
        public NetworkVariable<float> BleedoutRemaining = new(0f);
        public NetworkVariable<PlayerState> State = new(PlayerState.Alive);

        /// <summary>
        /// 생존 기준 최대 체력(기본 + 하우징·의료시설 등 보너스). <see cref="housingMaxHpBonus"/>는 NV가 아니므로
        /// 팀 HUD·원격 UI가 동일한 상한을 쓰도록 서버에서만 갱신합니다.
        /// </summary>
        public NetworkVariable<float> ReplicatedMaxHp = new(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private RollSystem rollSystem;
        private PlayerAnimatorDriver animatorDriver;

        public float BaseMaxHP => maxHP;
        public float HousingMaxHpBonus => housingMaxHpBonus;
        public float MaxHP => Mathf.Max(1f, maxHP + housingMaxHpBonus);

        public bool IsAlive => State.Value == PlayerState.Alive;
        public bool IsKnocked => State.Value == PlayerState.Knocked;
        public bool IsDead => State.Value == PlayerState.Dead;
        public bool CanBeRevived => IsKnocked && !IsDead && KnockedHP.Value > 0f && BleedoutRemaining.Value > 0f;
        public bool IsBeingRevived => isBeingRevived;
        public ulong CurrentReviverClientId => reviverClientId;
        public float ReviveHpAmount => reviveHpAmount;

        private bool isBeingRevived;
        private ulong reviverClientId;

        private void Awake()
        {
            rollSystem = GetComponent<RollSystem>();
            animatorDriver = GetComponent<PlayerAnimatorDriver>();
        }

        private void OnValidate()
        {
            if (maxHP < 1f)
                maxHP = 1f;

            if (housingMaxHpBonus < 0f)
                housingMaxHpBonus = 0f;

            if (knockedMaxHP < 1f)
                knockedMaxHP = 1f;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CurrentHP.Value = MaxHP;
                KnockedHP.Value = 0f;
                BleedoutRemaining.Value = 0f;
                State.Value = PlayerState.Alive;
                ReplicatedMaxHp.Value = MaxHP;
            }

            CurrentHP.OnValueChanged += BroadcastHpChanged;
            KnockedHP.OnValueChanged += BroadcastKnockedHpChanged;
            State.OnValueChanged += BroadcastStateChanged;

            BroadcastHpChanged(CurrentHP.Value, CurrentHP.Value);
            BroadcastStateChanged(State.Value, State.Value);
        }

        public override void OnNetworkDespawn()
        {
            CurrentHP.OnValueChanged -= BroadcastHpChanged;
            KnockedHP.OnValueChanged -= BroadcastKnockedHpChanged;
            State.OnValueChanged -= BroadcastStateChanged;
        }

        private void Update()
        {
            if (IsSpawned && !IsServer) return;

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
            if (IsDead)
                return;
            
            if (IsSpawned && !IsServer) return;

            if (ShouldIgnoreDamage())
                return;

            if (ShouldIgnoreFriendlyFire(attackerClientId))
                return;

            if (IsAlive)
            {
                float previousHp = CurrentHP.Value;
                CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);

                if (CurrentHP.Value <= 0f)
                {
                    TransitionToKnocked(attackerClientId);
                }
                else if (CurrentHP.Value < previousHp)
                {
                    PlayHitReaction();
                }
            }
            else if (IsKnocked)
            {
                KnockedHP.Value = Mathf.Max(0f, KnockedHP.Value - damage);

                if (KnockedHP.Value <= 0f)
                    TransitionToDead(attackerClientId);
            }
        }

        public void ApplyDamage(int damage, ulong attackerClientId, Vector3 hit)
        {
            if (!IsServer || IsDead)
                return;

            if (ShouldIgnoreDamage())
                return;

            if (ShouldIgnoreFriendlyFire(attackerClientId))
                return;

            if (IsAlive)
            {
                float previousHp = CurrentHP.Value;
                CurrentHP.Value = Mathf.Max(0f, CurrentHP.Value - damage);

                if (CurrentHP.Value <= 0f)
                {
                    TransitionToKnocked(attackerClientId);
                }
                else if (CurrentHP.Value < previousHp)
                {
                    PlayHitReaction();
                }
            }
            else if (IsKnocked)
            {
                KnockedHP.Value = Mathf.Max(0f, KnockedHP.Value - damage);

                if (KnockedHP.Value <= 0f)
                    TransitionToDead(attackerClientId);
            }
        }

        private bool ShouldIgnoreDamage()
        {
            if (!IsAlive)
                return false;

            return rollSystem != null && rollSystem.IsDamageImmune;
        }

        private bool ShouldIgnoreFriendlyFire(ulong attackerClientId)
        {
            if (attackerClientId == DamageSystem.AI_SHOOTER_ID)
                return false;

            if (attackerClientId == OwnerClientId)
                return false;

            return true;
        }

        private void PlayHitReaction()
        {
            if (IsSpawned)
            {
                PlayHitReactionClientRpc();
                return;
            }

            if (animatorDriver == null)
                animatorDriver = GetComponent<PlayerAnimatorDriver>();

            animatorDriver?.TriggerHitReaction();
        }

        [ClientRpc]
        private void PlayHitReactionClientRpc()
        {
            if (animatorDriver == null)
                animatorDriver = GetComponent<PlayerAnimatorDriver>();

            animatorDriver?.TriggerHitReaction();
        }

        public void Heal(float amount)
        {
            if (!IsAlive) return;
            if (IsSpawned && !IsServer) return;

            CurrentHP.Value = Mathf.Min(MaxHP, CurrentHP.Value + amount);
        }

        /// <summary>
        /// 하우징 시설에서 계산된 최대 체력 보너스를 적용합니다.
        /// Medical 시설 보너스는 PlayerHousingBonusReceiver를 통해 이 메서드로 들어옵니다.
        /// </summary>
        public void ApplyHousingMaxHpBonus(float bonus)
        {
            ApplyHousingMaxHpBonus(bonus, fillHpWhenMaxHpIncreased);
        }

        /// <summary>
        /// 하우징 최대 체력 보너스를 적용합니다.
        /// 서버 스폰 상태에서는 서버에서만 값이 바뀌어야 합니다.
        /// </summary>
        public void ApplyHousingMaxHpBonus(float bonus, bool fillIncreasedAmount)
        {
            if (IsSpawned && !IsServer)
                return;

            float nextBonus = Mathf.Max(0f, bonus);

            if (Mathf.Approximately(housingMaxHpBonus, nextBonus))
                return;

            float previousMaxHp = MaxHP;
            float previousCurrentHp = CurrentHP.Value;

            housingMaxHpBonus = nextBonus;

            if (IsAlive)
            {
                float increasedAmount = Mathf.Max(0f, MaxHP - previousMaxHp);

                if (fillIncreasedAmount && increasedAmount > 0f)
                {
                    CurrentHP.Value = Mathf.Min(MaxHP, CurrentHP.Value + increasedAmount);
                }
                else if (CurrentHP.Value > MaxHP)
                {
                    CurrentHP.Value = MaxHP;
                }
            }
            else if (CurrentHP.Value > MaxHP)
            {
                CurrentHP.Value = MaxHP;
            }

            // 스폰된 이후에는 CurrentHP.OnValueChanged가 모든 클라이언트에서 BroadcastHpChanged를 호출한다.
            // 아직 스폰 전인 경우에만 여기서 이벤트를 한 번 보낸다.
            if (!IsSpawned && !Mathf.Approximately(previousCurrentHp, CurrentHP.Value))
                BroadcastHpChanged(previousCurrentHp, CurrentHP.Value);

            if (IsSpawned && IsServer)
                ReplicatedMaxHp.Value = MaxHP;

            Debug.Log(
                $"[PlayerHealthSystem] 하우징 최대 체력 보너스 적용\n" +
                $"기본 최대 체력: {maxHP:0.##}\n" +
                $"보너스: +{housingMaxHpBonus:0.##}\n" +
                $"최종 최대 체력: {MaxHP:0.##}",
                this
            );
        }

        public void ResetHousingMaxHpBonus()
        {
            ApplyHousingMaxHpBonus(0f, false);
        }

        private void TransitionToKnocked(ulong attackerClientId)
        {
            if (IsSpawned && !IsServer) return;

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
            if (IsSpawned && !IsServer) return;

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
            if (!IsServer || corpsePrefab == null)
                return;

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
            if (!IsServer || !CanBeRevived)
                return;

            isBeingRevived = true;
            this.reviverClientId = reviverClientId;
            SetReviveMoveLockedClientRpc(true, BuildOwnerClientRpcParams());
        }

        public void OnReviveCancel()
        {
            if (!IsServer)
                return;

            isBeingRevived = false;
            reviverClientId = 0;
            SetReviveMoveLockedClientRpc(false, BuildOwnerClientRpcParams());
        }

        public void OnReviveComplete(ulong reviverClientId)
        {
            TryReviveFromKnocked(Mathf.RoundToInt(reviveHpAmount));
        }

        public bool TryReviveFromKnocked(int reviveHealth)
        {
            if (!IsServer || !CanBeRevived)
                return false;

            isBeingRevived = false;
            reviverClientId = 0;
            SetReviveMoveLockedClientRpc(false, BuildOwnerClientRpcParams());

            CurrentHP.Value = Mathf.Clamp(reviveHealth, 1f, MaxHP);
            KnockedHP.Value = 0f;
            BleedoutRemaining.Value = 0f;
            State.Value = PlayerState.Alive;

            return true;
        }

        private ClientRpcParams BuildOwnerClientRpcParams()
        {
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
        }

        [ClientRpc]
        private void SetReviveMoveLockedClientRpc(bool locked, ClientRpcParams rpcParams = default)
        {
            FPSController fps = GetComponent<FPSController>();
            if (fps != null)
                fps.SetMoveLocked(locked);
        }

        public Vector3 GetCorpsePosition()
        {
            return transform.position;
        }

        public Quaternion GetCorpseRotation()
        {
            return transform.rotation;
        }

        public void TransferInventoryToCorpse(GameObject corpse)
        {
            if (!IsServer || corpse == null)
                return;

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

            if (!IsServer && newState == PlayerState.Knocked)
            {
                float replicatedBleedoutSeconds = BleedoutRemaining.Value > 0f
                    ? BleedoutRemaining.Value
                    : bleedoutSeconds;

                EventBus.Publish(new PlayerKnockedEvent
                {
                    victimClientId = OwnerClientId,
                    attackerClientId = 0,
                    position = transform.position,
                    bleedoutSeconds = replicatedBleedoutSeconds,
                });
            }
        }

#if UNITY_EDITOR
        [ContextMenu("테스트 하우징 체력 보너스 +15 적용")]
        private void DebugApplyHousingHpBonus()
        {
            ApplyHousingMaxHpBonus(15f, true);
        }

        [ContextMenu("테스트 하우징 체력 보너스 초기화")]
        private void DebugResetHousingHpBonus()
        {
            ResetHousingMaxHpBonus();
        }
#endif
    }
}
