using DeadZone.Core;


namespace DeadZone.Systems
{
    /// <summary>
    /// 인벤토리 추상화. 다른 시스템(Loot, Trader, Quest)이 구체 GridInventory 클래스를
    /// 알 필요 없게 한다.
    /// </summary>
    public interface IInventory
    {
        bool TryAddItem(ItemDataSO item, int amount = 1);
        bool HasItem(string itemId, int count);
        bool ConsumeItem(string itemId, int count);
    }
}
