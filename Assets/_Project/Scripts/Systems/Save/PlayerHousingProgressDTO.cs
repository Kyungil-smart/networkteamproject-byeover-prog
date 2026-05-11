using System;

using DeadZone.Core;

namespace DeadZone.Systems.Save
{
    // 플레이어별 하우징 시설 레벨 저장 데이터
    // MonoBehaviour, NetworkVariable, ScriptableObject를 넣지 않는 저장용 DTO
    [Serializable]
    public sealed class PlayerHousingProgressDTO
    {
        public int workbenchLevel = 1;
        public int medicalLevel = 1;
        public int gymLevel = 1;
        public int stashLevel = 1;
        public int kitchenLevel = 1;
        public int bedLevel = 1;
        public int commStationLevel = 1;

        public int GetLevel(FacilityType facilityType)
        {
            return facilityType switch
            {
                FacilityType.Workbench => workbenchLevel,
                FacilityType.Medical => medicalLevel,
                FacilityType.Gym => gymLevel,
                FacilityType.Stash => stashLevel,
                FacilityType.Kitchen => kitchenLevel,
                FacilityType.Bed => bedLevel,
                FacilityType.CommStation => commStationLevel,
                _ => 1
            };
        }

        public void SetLevel(FacilityType facilityType, int level)
        {
            int safeLevel = ClampLevel(level);

            switch (facilityType)
            {
                case FacilityType.Workbench:
                    workbenchLevel = safeLevel;
                    break;

                case FacilityType.Medical:
                    medicalLevel = safeLevel;
                    break;

                case FacilityType.Gym:
                    gymLevel = safeLevel;
                    break;

                case FacilityType.Stash:
                    stashLevel = safeLevel;
                    break;

                case FacilityType.Kitchen:
                    kitchenLevel = safeLevel;
                    break;

                case FacilityType.Bed:
                    bedLevel = safeLevel;
                    break;

                case FacilityType.CommStation:
                    commStationLevel = safeLevel;
                    break;
            }
        }

        public void Normalize()
        {
            workbenchLevel = ClampLevel(workbenchLevel);
            medicalLevel = ClampLevel(medicalLevel);
            gymLevel = ClampLevel(gymLevel);
            stashLevel = ClampLevel(stashLevel);
            kitchenLevel = ClampLevel(kitchenLevel);
            bedLevel = ClampLevel(bedLevel);
            commStationLevel = ClampLevel(commStationLevel);
        }

        private static int ClampLevel(int level)
        {
            if (level < 1)
                return 1;

            if (level > 4)
                return 4;

            return level;
        }
    }
}