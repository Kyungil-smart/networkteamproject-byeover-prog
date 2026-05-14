using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Systems.Save
{
    public class LobbyInventoryState : MonoBehaviour
    {
        [Header("씬 유지")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("저장 상태")]
        [SerializeField] private bool hasCredits;
        [SerializeField] private int credits;
        [SerializeField] private List<ItemSaveDTO> inventoryItems = new();
        [SerializeField] private List<ItemSaveDTO> stashItems = new();
        [SerializeField] private List<ItemSaveDTO> quickSlotItems = new();
        [SerializeField] private List<EquipmentSaveDTO> equipmentItems = new();
        [SerializeField] private List<ItemSaveDTO> quickSlotItems = new();

        public bool HasCredits => hasCredits;
        public int Credits => credits;
        public IReadOnlyList<ItemSaveDTO> InventoryItems => inventoryItems;
        public IReadOnlyList<ItemSaveDTO> StashItems => stashItems;
        public IReadOnlyList<ItemSaveDTO> QuickSlotItems => quickSlotItems;
        public IReadOnlyList<EquipmentSaveDTO> EquipmentItems => equipmentItems;
        public IReadOnlyList<ItemSaveDTO> QuickSlotItems => quickSlotItems;

        private void Awake()
        {
            if (dontDestroyOnLoad)
            {
                Debug.Log(
                    "[LobbyInventoryState] dontDestroyOnLoad is ignored. Lobby inventory state is a scene-local cache; CloudSaveSystem is the persistent authority.",
                    this);
            }
        }

        public void SetInventoryItems(IEnumerable<ItemSaveDTO> items)
        {
            ReplaceList(inventoryItems, items);
        }

        public void SetCredits(int value)
        {
            hasCredits = true;
            credits = Mathf.Max(0, value);
        }

        public void SetStashItems(IEnumerable<ItemSaveDTO> items)
        {
            ReplaceList(stashItems, items);
        }

        public void SetQuickSlotItems(IEnumerable<ItemSaveDTO> items)
        {
            ReplaceList(quickSlotItems, items);
        }

        public void SetEquipmentItems(IEnumerable<EquipmentSaveDTO> items)
        {
            ReplaceList(equipmentItems, items);
        }

        public void SetQuickSlotItems(IEnumerable<ItemSaveDTO> items)
        {
            ReplaceList(quickSlotItems, items);
        }

        [Button("인벤토리 상태 비우기")]
        public void Clear()
        {
            hasCredits = false;
            credits = 0;
            inventoryItems.Clear();
            stashItems.Clear();
            quickSlotItems.Clear();
            equipmentItems.Clear();
            quickSlotItems.Clear();
        }

        private static void ReplaceList<T>(List<T> target, IEnumerable<T> source)
        {
            target.Clear();

            if (source == null)
                return;

            target.AddRange(source);
        }
    }
}
