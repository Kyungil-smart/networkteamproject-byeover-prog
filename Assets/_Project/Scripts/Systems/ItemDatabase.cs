// Assets/_Project/Scripts/Systems/ItemDatabase.cs

using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 씬에 하나만 배치되는 아이템 데이터베이스입니다.
    /// ItemDatabaseSO를 읽어서 아이템 ID로 ItemDataSO를 찾을 수 있게 해줍니다.
    /// </summary>
    public class ItemDatabase : MonoBehaviour, IItemDatabase
    {
        [SerializeField] private ItemDatabaseSO databaseSO;

        private void Awake()
        {
            if (databaseSO == null)
            {
                Debug.LogError("[ItemDatabase] databaseSO가 할당되지 않았습니다.", this);
                return;
            }

            databaseSO.BuildCache();

            // 기존 코드 호환용 등록입니다.
            // ServiceLocator.Get<ItemDatabase>()를 쓰는 코드가 있어도 동작하게 유지합니다.
            ServiceLocator.Register(this);

            // 최신 코드 호환용 등록입니다.
            // GridInventory, LootContainer, EquipmentSlots 등은 IItemDatabase로 조회합니다.
            ServiceLocator.Register<IItemDatabase>(this);

            Debug.Log($"[ItemDatabase] 아이템 데이터베이스 등록 완료. 등록 아이템 수: {databaseSO.Count}", this);
        }

        private void OnDestroy()
        {
            // 기존 코드 호환용 해제입니다.
            ServiceLocator.Unregister<ItemDatabase>();

            // 최신 코드 호환용 해제입니다.
            ServiceLocator.Unregister<IItemDatabase>();
        }

        /// <summary>
        /// itemId로 아이템 데이터를 찾습니다.
        /// 없으면 null을 반환합니다.
        /// </summary>
        public ItemDataSO GetById(string itemId)
        {
            if (databaseSO == null)
                return null;

            return databaseSO.GetByID(itemId);
        }

        /// <summary>
        /// itemId로 특정 타입의 아이템 데이터를 찾습니다.
        /// 타입이 맞지 않으면 null을 반환합니다.
        /// </summary>
        public T GetById<T>(string itemId) where T : ItemDataSO
        {
            return GetById(itemId) as T;
        }

        /// <summary>
        /// 기존 GetByID 호출 코드와 호환하기 위한 메서드입니다.
        /// </summary>
        public ItemDataSO GetByID(string itemID)
        {
            return GetById(itemID);
        }

        public int Count
        {
            get
            {
                if (databaseSO == null)
                    return 0;

                return databaseSO.Count;
            }
        }
    }
}