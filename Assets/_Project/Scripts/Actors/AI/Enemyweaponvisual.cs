using UnityEngine;

namespace DeadZone.Actors
{
    public class EnemyWeaponVisual : MonoBehaviour
    {
        [Header("자동 장착")]
        [Tooltip("체크하면 EnemyStats의 SO에서 defaultWeapon.worldPrefab을 자동으로 장착")]
        [SerializeField] private bool autoEquipFromSO = true;

        [Header("수동 장착 (자동 장착 끌 때 사용)")]
        [Tooltip("직접 지정할 무기 프리팹")]
        [SerializeField] private GameObject weaponPrefabOverride;

        [Header("위치 보정")]
        [Tooltip("무기 모델의 로컬 위치 오프셋")]
        [SerializeField] private Vector3 positionOffset = Vector3.zero;

        [Tooltip("무기 모델의 로컬 회전 오프셋")]
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [Tooltip("무기 모델의 스케일")]
        [SerializeField] private Vector3 scale = Vector3.one;

        private GameObject spawnedWeapon;

        /// <summary>장착된 무기의 MuzzlePoint Transform</summary>
        public Transform MuzzlePoint { get; private set; }

        private void Start()
        {
            if (autoEquipFromSO)
                AutoEquipFromStats();
            else if (weaponPrefabOverride != null)
                EquipWeapon(weaponPrefabOverride);
        }

        /// <summary>
        /// EnemyStats의 SO에서 무기 프리팹을 가져와 자동 장착한다.
        /// </summary>
        private void AutoEquipFromStats()
        {
            var stats = GetComponentInParent<EnemyStats>();
            if (stats == null || stats.StatsSO == null) return;

            // SO의 defaultWeapon에서 worldPrefab 가져오기
            var weaponSO = stats.StatsSO.defaultWeapon;
            if (weaponSO == null) return;

            // 추측으로 작성됨: WeaponDataSO의 worldPrefab 필드 접근
            // weaponSO가 ItemDataSO 상속이면 worldPrefab 필드가 있어야 함
            GameObject prefab = GetWorldPrefab(weaponSO);
            if (prefab != null)
                EquipWeapon(prefab);
        }

        /// <summary>
        /// 무기 프리팹을 장착한다. MuzzlePoint를 자동으로 찾아 EnemyShooter에 연결한다.
        /// </summary>
        /// <param name="weaponPrefab">무기 프리팹</param>
        public void EquipWeapon(GameObject weaponPrefab)
        {
            // 기존 무기 제거
            if (spawnedWeapon != null)
                Destroy(spawnedWeapon);

            if (weaponPrefab == null) return;

            // 무기 모델 생성
            spawnedWeapon = Instantiate(weaponPrefab, transform);
            spawnedWeapon.transform.localPosition = positionOffset;
            spawnedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);
            spawnedWeapon.transform.localScale = scale;

            // MuzzlePoint 탐색 (무기 프리팹 안에 있는 자식 오브젝트)
            Transform muzzle = spawnedWeapon.transform.Find("MuzzlePoint");
            if (muzzle != null)
            {
                MuzzlePoint = muzzle;
                ConnectMuzzleToShooter();
            }
            else
            {
                Debug.LogWarning($"[EnemyWeaponVisual] {weaponPrefab.name}에 MuzzlePoint가 없습니다.", this);
            }
        }

        /// <summary>
        /// 찾은 MuzzlePoint를 EnemyShooter의 Muzzle에 자동 연결한다.
        /// </summary>
        private void ConnectMuzzleToShooter()
        {
            var shooter = GetComponentInParent<EnemyShooter>();
            if (shooter == null || MuzzlePoint == null) return;

            // EnemyShooter의 muzzle 필드에 리플렉션으로 할당
            var field = typeof(EnemyShooter).GetField("muzzle",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                field.SetValue(shooter, MuzzlePoint);
            }
            else
            {
                Debug.LogWarning("[EnemyWeaponVisual] EnemyShooter.muzzle 필드를 찾을 수 없습니다.", this);
            }
        }

        /// <summary>
        /// SO에서 worldPrefab을 가져온다.
        /// 추측으로 작성됨: 실제 필드 접근 방식은 프로젝트에 따라 다를 수 있음.
        /// </summary>
        private GameObject GetWorldPrefab(ScriptableObject weaponSO)
        {
            // worldPrefab 필드를 리플렉션으로 접근
            var field = weaponSO.GetType().GetField("worldPrefab",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            if (field != null)
                return field.GetValue(weaponSO) as GameObject;

            // 프로퍼티로 접근 시도
            var prop = weaponSO.GetType().GetProperty("worldPrefab");
            if (prop != null)
                return prop.GetValue(weaponSO) as GameObject;

            return null;
        }
    }
}