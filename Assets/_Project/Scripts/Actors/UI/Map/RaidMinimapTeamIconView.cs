using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Actors.Player;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 로비에서 정해진 팀 색상을 인게임 UI 마커에 적용합니다.
    ///
    /// 적용 대상:
    /// 1. 미니맵 로컬 플레이어 마커
    /// 2. 미니맵 팀원 마커
    /// 3. 월드맵 로컬 플레이어 마커
    /// 4. 월드맵 팀원 마커
    /// 5. 왼쪽 팀원 HP바 아이콘
    ///
    /// PlayerPrefab 스폰 타이밍보다 HUD가 먼저 켜질 수 있으므로,
    /// 인게임 진입 후 일정 시간 동안 반복 갱신합니다.
    /// </summary>
    public class RaidMinimapTeamIconView : MonoBehaviour
    {
        [Header("==== 미니맵 - 로컬 플레이어 아이콘 ====")]
        [Tooltip("미니맵에서 내 플레이어를 표시하는 아이콘입니다. 예: LocalPlayerMarker / Icon_Team")]
        [SerializeField] private Image minimapLocalPlayerIcon;

        [Header("==== 미니맵 - 팀원 아이콘 ====")]
        [Tooltip("미니맵에서 다른 플레이어를 표시하는 아이콘들입니다. 예: LocalPlayerMarker1~3 / Icon_Team1~3")]
        [SerializeField] private Image[] minimapTeammateIcons;

        [Header("==== 월드맵 - 로컬 플레이어 아이콘 ====")]
        [Tooltip("월드맵에서 내 플레이어를 표시하는 아이콘입니다.")]
        [SerializeField] private Image worldMapLocalPlayerIcon;

        [Header("==== 월드맵 - 팀원 아이콘 ====")]
        [Tooltip("월드맵에서 다른 플레이어를 표시하는 아이콘들입니다.")]
        [SerializeField] private Image[] worldMapTeammateIcons;

        [Header("==== 팀원 HP바 아이콘 ====")]
        [Tooltip("왼쪽 팀원 HP바에 있는 아이콘들입니다. 팀원 순서대로 연결합니다.")]
        [SerializeField] private Image[] teammateHpIcons;

        [Header("==== 갱신 설정 ====")]
        [Tooltip("반복 갱신 간격입니다.")]
        [SerializeField] private float refreshInterval = 0.25f;

        [Tooltip("인게임 진입 후 몇 초 동안 반복 갱신할지 설정합니다.")]
        [SerializeField] private float refreshDuration = 5f;

        [Tooltip("디버그 로그 출력 여부입니다.")]
        [SerializeField] private bool logDebug;

        private Coroutine refreshCoroutine;

        private void OnEnable()
        {
            StartRefreshLoop();
        }

        private void OnDisable()
        {
            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
                refreshCoroutine = null;
            }
        }

        private void StartRefreshLoop()
        {
            if (refreshCoroutine != null)
                StopCoroutine(refreshCoroutine);

            refreshCoroutine = StartCoroutine(RefreshLoopRoutine());
        }

        private IEnumerator RefreshLoopRoutine()
        {
            float elapsed = 0f;

            while (elapsed < refreshDuration)
            {
                RefreshIcons();

                yield return new WaitForSeconds(refreshInterval);
                elapsed += refreshInterval;
            }

            RefreshIcons();
            refreshCoroutine = null;
        }

        public void RefreshIcons()
        {
            if (NetworkManager.Singleton == null)
            {
                LogDebug("NetworkManager.Singleton이 없어 마커 색상 갱신을 건너뜁니다.");
                return;
            }

            ulong localClientId = NetworkManager.Singleton.LocalClientId;

            PlayerTeamIdentity[] players =
                FindObjectsByType<PlayerTeamIdentity>(FindObjectsSortMode.None);

            List<PlayerTeamIdentity> sortedPlayers = new(players);
            sortedPlayers.Sort(ComparePlayersByOwnerClientId);

            bool foundLocalPlayer = false;
            int teammateIndex = 0;

            // 플레이어를 찾기 전까지는 기존 마커를 강제로 꺼버리지 않습니다.
            // 단, 플레이어를 하나라도 찾은 뒤에는 남는 팀원 슬롯만 숨깁니다.
            foreach (PlayerTeamIdentity player in sortedPlayers)
            {
                if (player == null || !player.IsSpawned)
                    continue;

                Color32 color = player.CurrentColor;
                bool isLocalPlayer = player.OwnerClientId == localClientId;

                if (isLocalPlayer)
                {
                    foundLocalPlayer = true;

                    ApplyIcon(minimapLocalPlayerIcon, color);
                    ApplyIcon(worldMapLocalPlayerIcon, color);
                    continue;
                }

                ApplyIconAt(minimapTeammateIcons, teammateIndex, color);
                ApplyIconAt(worldMapTeammateIcons, teammateIndex, color);
                ApplyIconAt(teammateHpIcons, teammateIndex, color);

                teammateIndex++;
            }

            if (sortedPlayers.Count <= 0)
            {
                LogDebug("PlayerTeamIdentity를 아직 찾지 못했습니다. 다음 반복에서 다시 시도합니다.");
                return;
            }

            // 남는 팀원 슬롯만 숨김
            HideUnusedIcons(minimapTeammateIcons, teammateIndex);
            HideUnusedIcons(worldMapTeammateIcons, teammateIndex);
            HideUnusedIcons(teammateHpIcons, teammateIndex);

            if (!foundLocalPlayer)
                LogDebug($"로컬 플레이어 마커를 찾지 못했습니다. LocalClientId={localClientId}");
        }

        private int ComparePlayersByOwnerClientId(PlayerTeamIdentity a, PlayerTeamIdentity b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            return a.OwnerClientId.CompareTo(b.OwnerClientId);
        }

        private void ApplyIconAt(Image[] icons, int index, Color color)
        {
            if (icons == null) return;
            if (index < 0 || index >= icons.Length) return;

            ApplyIcon(icons[index], color);
        }

        private void ApplyIcon(Image icon, Color color)
        {
            if (icon == null)
                return;

            icon.color = color;
            icon.gameObject.SetActive(true);
        }

        private void HideUnusedIcons(Image[] icons, int usedCount)
        {
            if (icons == null)
                return;

            for (int i = usedCount; i < icons.Length; i++)
            {
                if (icons[i] != null)
                    icons[i].gameObject.SetActive(false);
            }
        }

        private void LogDebug(string message)
        {
            if (!logDebug) return;

            Debug.Log($"[RaidMinimapTeamIconView] {message}", this);
        }
    }
}