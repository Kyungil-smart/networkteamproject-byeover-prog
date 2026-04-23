using TMPro;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 현재 관전 대상 이름 + Q/E/Tab 키 힌트 표시.
    /// 로컬 플레이어가 Dead 상태에 진입하면 HUDManager가 활성화한다.
    /// </summary>
    public class SpectatorHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text targetNameText;
        [SerializeField] private TMP_Text keyHintsText;

        private void OnEnable()
        {
            EventBus.Subscribe<SpectatorTargetChangedEvent>(OnTargetChanged);
            if (keyHintsText != null) keyHintsText.text = "[Q/E] Switch teammate   [Tab] Free cam";
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SpectatorTargetChangedEvent>(OnTargetChanged);
        }

        private void OnTargetChanged(SpectatorTargetChangedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.spectatorClientId != NetworkManager.Singleton.LocalClientId) return;

            if (targetNameText != null)
            {
                targetNameText.text = e.newTargetClientId == ulong.MaxValue
                    ? "Free camera"
                    : $"Spectating: Player {e.newTargetClientId}";
            }
        }
    }
}
