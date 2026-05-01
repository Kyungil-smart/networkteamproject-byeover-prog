using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 작업대 제작/해금 이벤트가 정상 발행되는지 확인하는 테스트 수신기입니다.
    /// 추후 UI가 완성되면 제거해도 됩니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorkbenchCraftingTestReceiver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("작업대 이벤트 수신 로그를 Console에 출력합니다.")]
        private bool logReceivedEvent = true;

        private void OnEnable()
        {
            EventBus.Subscribe<WorkbenchCraftingUnlockChangedEvent>(HandleUnlockChanged);
            EventBus.Subscribe<WorkbenchCraftSucceededEvent>(HandleCraftSucceeded);
            EventBus.Subscribe<WorkbenchCraftFailedEvent>(HandleCraftFailed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<WorkbenchCraftingUnlockChangedEvent>(HandleUnlockChanged);
            EventBus.Unsubscribe<WorkbenchCraftSucceededEvent>(HandleCraftSucceeded);
            EventBus.Unsubscribe<WorkbenchCraftFailedEvent>(HandleCraftFailed);
        }

        private void HandleUnlockChanged(WorkbenchCraftingUnlockChangedEvent evt)
        {
            if (!logReceivedEvent)
                return;

            Debug.Log($"[WorkbenchCraftingTestReceiver] 작업대 해금 이벤트 수신: Lv.{evt.level}, 제작 가능 등급: {evt.unlockedGradeLabel}", this);
        }

        private void HandleCraftSucceeded(WorkbenchCraftSucceededEvent evt)
        {
            if (!logReceivedEvent)
                return;

            Debug.Log($"[WorkbenchCraftingTestReceiver] 제작 성공 이벤트 수신: {evt.recipeId} → {evt.resultItemId} x{evt.resultCount}", this);
        }

        private void HandleCraftFailed(WorkbenchCraftFailedEvent evt)
        {
            if (!logReceivedEvent)
                return;

            Debug.LogWarning($"[WorkbenchCraftingTestReceiver] 제작 실패 이벤트 수신: {evt.reason} RecipeID: {evt.recipeId}", this);
        }
    }
}
