using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 우상단 킬피드. 크리티컬 히트는 골드 색상으로 강조.
    /// </summary>
    public class KillFeedUI : MonoBehaviour
    {
        [SerializeField] private RectTransform entriesRoot;
        [SerializeField] private TMP_Text entryPrefab;
        [SerializeField] private int maxEntries = 5;
        [SerializeField] private float entryLifetime = 5f;
        [SerializeField] private Color critColor = new(1f, 0.84f, 0f);
        [SerializeField] private Color normalColor = Color.white;

        private readonly Queue<TMP_Text> activeEntries = new();

        private void OnEnable()
        {
            EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Subscribe<CriticalHitEvent>(OnCritical);
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Unsubscribe<CriticalHitEvent>(OnCritical);
            EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        private void OnEnemyKilled(EnemyKilledEvent e)
        {
            string attackerName = ResolveName(e.attackerClientId);
            AddEntry($"{attackerName} killed Enemy ({e.tier})", normalColor);
        }

        private void OnCritical(CriticalHitEvent e)
        {
            string attackerName = ResolveName(e.attackerClientId);
            AddEntry($"{attackerName} CRIT {e.zone} {e.damage}dmg", critColor);
        }

        private void OnPlayerDied(PlayerDiedEvent e)
        {
            string victim = ResolveName(e.victimClientId);
            string killer = ResolveName(e.killerClientId);
            AddEntry($"{killer} killed {victim}", normalColor);
        }

        private string ResolveName(ulong clientId)
        {
            if (clientId == ulong.MaxValue) return "Enemy";
            return $"Player {clientId}";
        }

        private void AddEntry(string text, Color color)
        {
            if (entryPrefab == null || entriesRoot == null) return;

            var entry = Instantiate(entryPrefab, entriesRoot);
            entry.text = text;
            entry.color = color;
            activeEntries.Enqueue(entry);

            while (activeEntries.Count > maxEntries)
            {
                var old = activeEntries.Dequeue();
                if (old != null) Destroy(old.gameObject);
            }

            Destroy(entry.gameObject, entryLifetime);
        }
    }
}
