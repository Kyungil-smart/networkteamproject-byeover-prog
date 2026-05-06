using UnityEngine;
using UnityEngine.SceneManagement;
using DeadZone.Systems.Raid;

namespace DeadZone.Systems.Raid
{
    public class RaidDeathResultHandler : MonoBehaviour
    {
        [Header("씬 설정")]
        [SerializeField] private string resultSceneName = "HJO_RaidResult";

        [Header("사망 시 아이템 제거 대상")]
        [SerializeField] private GameObject inventoryObject;

        public void ShowDeathResult()
        {
            RaidSessionTracker tracker = RaidSessionTracker.Instance;

            if (tracker == null)
            {
                Debug.LogError("[RaidDeathResultHandler] RaidSessionTracker가 씬에 없습니다.");
                return;
            }

            tracker.StopTracking();

            RaidResultData.SetDead(
                tracker.MapName,
                tracker.KillCount,
                tracker.SurvivalTime
            );

            LoseAllItems();

            SceneManager.LoadScene(resultSceneName);
        }

        private void LoseAllItems()
        {
            if (inventoryObject == null)
                return;
            
            inventoryObject.SendMessage("ClearAllItems", SendMessageOptions.DontRequireReceiver);
            inventoryObject.SendMessage("ClearInventory", SendMessageOptions.DontRequireReceiver);
            inventoryObject.SendMessage("RemoveAllItems", SendMessageOptions.DontRequireReceiver);
        }
    }
}