using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Actors.Player;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 인게임에서 로비 파티 색상을 UI 아이콘에 적용합니다.
    /// - 로컬 플레이어: 미니맵 Icon_Team
    /// - 팀원: 미니맵 Icon_Team1~3
    /// - 팀원 HP바 아이콘: TeamHpIcons에 같은 순서로 적용
    /// </summary>
    public class RaidMinimapTeamIconView : MonoBehaviour
    {
        [Header("==== 미니맵 - 로컬 플레이어 아이콘 ====")]
        [Tooltip("내 플레이어를 표시하는 미니맵 아이콘입니다. Icon_Team 연결")]
        [SerializeField] private Image localPlayerIcon;

        [Header("==== 미니맵 - 팀원 아이콘 ====")]
        [Tooltip("다른 플레이어를 표시하는 미니맵 아이콘들입니다. Icon_Team1, Icon_Team2, Icon_Team3 연결")]
        [SerializeField] private Image[] teammateIcons;

        [Header("==== 팀원 HP바 아이콘 ====")]
        [Tooltip("왼쪽 팀원 HP바에 있는 아이콘들입니다. 팀원 순서대로 연결")]
        [SerializeField] private Image[] teammateHpIcons;

        [Header("==== 갱신 설정 ====")]
        [Tooltip("레이드 씬 진입 직후 PlayerPrefab 스폰 타이밍을 기다리는 시간입니다.")]
        [SerializeField] private float refreshDelay = 0.25f;

        private Coroutine refreshCoroutine;

        private void OnEnable()
        {
            StartRefresh();
        }

        private void OnDisable()
        {
            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
                refreshCoroutine = null;
            }
        }

        private void StartRefresh()
        {
            if (refreshCoroutine != null)
                StopCoroutine(refreshCoroutine);

            refreshCoroutine = StartCoroutine(RefreshRoutine());
        }

        private IEnumerator RefreshRoutine()
        {
            yield return null;
            yield return new WaitForSeconds(refreshDelay);

            RefreshIcons();

            refreshCoroutine = null;
        }

        public void RefreshIcons()
        {
            if (NetworkManager.Singleton == null)
                return;

            ulong localClientId = NetworkManager.Singleton.LocalClientId;

            PlayerTeamIdentity[] players =
                FindObjectsByType<PlayerTeamIdentity>(FindObjectsSortMode.None);

            List<PlayerTeamIdentity> sortedPlayers = new(players);
            sortedPlayers.Sort(ComparePlayersByOwnerClientId);

            SetIconVisible(localPlayerIcon, false);
            HideAll(teammateIcons);
            HideAll(teammateHpIcons);

            int teammateIndex = 0;

            foreach (PlayerTeamIdentity player in sortedPlayers)
            {
                if (player == null || !player.IsSpawned)
                    continue;

                bool isLocalPlayer = player.OwnerClientId == localClientId;

                if (isLocalPlayer)
                {
                    ApplyIcon(localPlayerIcon, player.CurrentColor);
                    continue;
                }

                ApplyIconAt(teammateIcons, teammateIndex, player.CurrentColor);
                ApplyIconAt(teammateHpIcons, teammateIndex, player.CurrentColor);

                teammateIndex++;
            }
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

        private void HideAll(Image[] icons)
        {
            if (icons == null)
                return;

            foreach (Image icon in icons)
                SetIconVisible(icon, false);
        }

        private void SetIconVisible(Image icon, bool visible)
        {
            if (icon == null)
                return;

            icon.gameObject.SetActive(visible);
        }
    }
}