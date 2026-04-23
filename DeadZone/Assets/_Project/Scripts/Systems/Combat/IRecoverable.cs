using UnityEngine;


namespace DeadZone.Systems
{
    /// <summary>
    /// 사망 시 루팅 가능한 시체를 드롭하는 엔티티가 구현한다.
    /// </summary>
    public interface IRecoverable
    {
        Vector3 GetCorpsePosition();
        Quaternion GetCorpseRotation();
        void TransferInventoryToCorpse(GameObject corpse);
    }
}
