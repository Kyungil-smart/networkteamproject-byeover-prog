using UnityEngine;

namespace DeadZone.Systems.Housing
{
    // 시설 저장/로드에 사용할 고유 ID를 보관
    // 오브젝트 이름이 바뀌어도 저장 데이터가 깨지지 않도록 별도 키를 둡니다.
    [DisallowMultipleComponent]
    public sealed class HousingFacilitySaveKey : MonoBehaviour
    {
        [Header("시설 저장 ID")]
        [SerializeField]
        private string facilityId;

        public string FacilityId => facilityId;

        public bool IsValid => !string.IsNullOrWhiteSpace(facilityId);
    }
}