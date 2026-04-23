using Unity.Netcode;
using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// 배고픔과 갈증. 둘 다 시간에 따라 감소하고, 소모품으로 보충된다
    /// (소모품 로직은 다른 곳에서 처리 — 본 클래스는 값만 소유).
    /// </summary>
    public class PlayerNutritionSystem : NetworkBehaviour
    {
        [Header("Capacity")]
        [SerializeField] private float maxHunger = 100f;
        [SerializeField] private float maxThirst = 100f;

        [Header("Decay (per minute)")]
        [SerializeField] private float hungerDecayPerMin = 1.5f;
        [SerializeField] private float thirstDecayPerMin = 2.0f;

        public NetworkVariable<float> CurrentHunger = new(100f);
        public NetworkVariable<float> CurrentThirst = new(100f);

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            CurrentHunger.Value = maxHunger;
            CurrentThirst.Value = maxThirst;
        }

        private void Update()
        {
            if (!IsServer) return;
            float dt = Time.deltaTime / 60f;
            CurrentHunger.Value = Mathf.Max(0f, CurrentHunger.Value - hungerDecayPerMin * dt);
            CurrentThirst.Value = Mathf.Max(0f, CurrentThirst.Value - thirstDecayPerMin * dt);
        }

        public void RestoreHunger(float amount)
        {
            if (!IsServer) return;
            CurrentHunger.Value = Mathf.Min(maxHunger, CurrentHunger.Value + amount);
        }

        public void RestoreThirst(float amount)
        {
            if (!IsServer) return;
            CurrentThirst.Value = Mathf.Min(maxThirst, CurrentThirst.Value + amount);
        }
    }
}
