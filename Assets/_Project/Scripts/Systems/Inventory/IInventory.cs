using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 인벤토리 추상화입니다.
    /// UI, 제작, 업그레이드 시스템이 구체 인벤토리 클래스를 직접 알지 않도록 합니다.
    /// </summary>
    public interface IInventory
    {
        bool TryAddItem(ItemDataSO item, int amount = 1);

        bool HasItem(string itemId, int count);

        bool ConsumeItem(string itemId, int count);

        int GetItemCount(string itemId);
    }
}