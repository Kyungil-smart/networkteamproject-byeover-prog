// TEMPORARY: Step 5에서 삭제
using UnityEngine;
using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors._LSH_Temp
{
    /// <summary>
    /// Step 2 테스트용 디버그 커맨드.
    /// H: 30 데미지 (Torso)
    /// K: 즉사
    /// </summary>
    [RequireComponent(typeof(PlayerHealthSystem))]
    public class HealthDebugCommands : MonoBehaviour
    {
        private PlayerHealthSystem health;

        private void Awake()
        {
            health = GetComponent<PlayerHealthSystem>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.H))
            {
                var hit = new HitInfo
                {
                    victim = gameObject,
                    zone = BodyPart.Torso,
                    hitPoint = transform.position,
                    hitNormal = Vector3.up,
                    distance = 0f,
                };
                health.ApplyDamage(30, 0, hit);
                Debug.Log($"[Debug] Torso에 30 데미지 → HP:{health.CurrentHP.Value} State:{health.State.Value}");
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                var hit = new HitInfo
                {
                    victim = gameObject,
                    zone = BodyPart.Torso,
                    hitPoint = transform.position,
                    hitNormal = Vector3.up,
                    distance = 0f,
                };
                health.ApplyDamage(999, 0, hit);
                Debug.Log($"[Debug] 즉사 → HP:{health.CurrentHP.Value} KnockedHP:{health.KnockedHP.Value} State:{health.State.Value}");
            }
        }
    }
}