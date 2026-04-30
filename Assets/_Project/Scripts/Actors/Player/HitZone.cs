using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 피격 부위 콜라이더 태그. HitboxRoot의 각 Hitbox 자식에 부착된다.
    /// DamageSystem이 부위별 배율을 결정할 때 읽는다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HitZone : MonoBehaviour
    {
        [SerializeField] private BodyPart zoneType = BodyPart.Torso;

        public BodyPart ZoneType => zoneType;

        public T GetOwner<T>() where T : class // Component -> class로 변경, 인터페이스에 대응하지만 null 체크 필요
        {
            return GetComponentInParent<T>();
        }
    }
}
