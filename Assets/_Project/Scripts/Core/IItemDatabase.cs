namespace DeadZone.Core
{
    public interface IItemDatabase
    {
        /// <summary>itemId로 ItemDataSO 조회. 없으면 null.</summary>
        ItemDataSO GetById(string itemId);

        /// <summary>itemId로 특정 서브타입 SO 조회. 타입 불일치/없음이면 null.</summary>
        T GetById<T>(string itemId) where T : ItemDataSO;
    }
}