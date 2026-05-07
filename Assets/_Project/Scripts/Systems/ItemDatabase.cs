// Assets/_Project/Scripts/Systems/ItemDatabase.cs
using UnityEngine;
using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 씬에 하나. ServiceLocator에 등록되어
    /// 다른 시스템이 ServiceLocator.Get&lt;ItemDatabase&gt;()로 접근.
    /// </summary>
    public class ItemDatabase : MonoBehaviour
    {
        [SerializeField] private ItemDatabaseSO databaseSO;

        private void Awake()
        {
            if (databaseSO == null)
            {
                Debug.LogError("[ItemDatabase] databaseSO가 할당되지 않음!");
                return;
            }
            databaseSO.BuildCache();
            ServiceLocator.Register(this);
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister<ItemDatabase>();
        }

        public ItemDataSO GetByID(string itemID) => databaseSO.GetByID(itemID);
        public int Count => databaseSO.Count;
    }
}