using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Quests;

namespace DeadZone.Actors
{
    public class ZoneUnlockSystem : NetworkBehaviour
    {
        [Header("Zone Lock 매핑")]
        [Tooltip("unlockZoneID → 파괴할 오브젝트 매핑. Inspector에서 설정")]
        [SerializeField] private ZoneLockEntry[] zoneLocks;

        [Header("연출")]
        [Tooltip("장벽 파괴 시 재생할 파티클 (null이면 연출 없음)")]
        [SerializeField] private GameObject destroyEffect;

        private Dictionary<string, ZoneLockEntry> _lockLookup;

        [Serializable]
        public struct ZoneLockEntry
        {
            [Tooltip("QuestDataSO.unlockZoneID와 일치하는 ID (예: MapA_Zone2)")]
            public string zoneId;

            [Tooltip("이 구역 해금 시 파괴/비활성화할 장벽 오브젝트들")]
            public GameObject[] barriers;
        }

        // ───────── 생명주기 ─────────

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // 룩업 테이블 구축
            _lockLookup = new Dictionary<string, ZoneLockEntry>();
            if (zoneLocks != null)
            {
                foreach (var entry in zoneLocks)
                {
                    if (!string.IsNullOrEmpty(entry.zoneId))
                        _lockLookup[entry.zoneId] = entry;
                }
            }

            // 서버에서만 이벤트 구독 + 기존 완료 퀘스트 복원
            if (IsServer)
            {
                EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
                RestoreUnlockedZones();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            }
            base.OnNetworkDespawn();
        }

        // ───────── 이벤트 핸들러: 플레이 중 퀘스트 완료 ─────────

        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (!IsServer) return;

            string zoneId = e.unlockZoneId.ToString();
            if (string.IsNullOrEmpty(zoneId)) return;

            UnlockZone(zoneId);
        }

        // ───────── 씬 로드 시 복원: 이미 완료된 퀘스트의 장벽 제거 ─────────

        /// <summary>
        /// 접속한 모든 플레이어의 UnlockedZones를 확인하여,
        /// 이미 해금된 구역의 장벽을 미리 파괴한다.
        /// 4인 코옵: 누구든 한 명이라도 해금했으면 해금 처리.
        /// </summary>
        private void RestoreUnlockedZones()
        {
            var questMgr = ServiceLocator.Get<QuestManager>();
            if (questMgr == null)
            {
                Debug.LogWarning("[ZoneUnlockSystem] QuestManager not found, skipping restore");
                return;
            }

            HashSet<string> allUnlockedZones = new();

            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                var state = questMgr.GetPlayerState(kvp.Key);
                foreach (string zoneId in state.UnlockedZones)
                    allUnlockedZones.Add(zoneId);
            }

            foreach (string zoneId in allUnlockedZones)
                UnlockZone(zoneId, playEffect: false);

            if (allUnlockedZones.Count > 0)
                Debug.Log($"[ZoneUnlockSystem] Restored {allUnlockedZones.Count} unlocked zones");
        }

        // ───────── 장벽 파괴 실행 ─────────

        private void UnlockZone(string zoneId, bool playEffect = true)
        {
            if (!_lockLookup.TryGetValue(zoneId, out var entry) && !TryResolveZoneLockBySceneObjectName(zoneId, out entry))
            {
                Debug.Log($"[ZoneUnlockSystem] No barrier mapped for zone: {zoneId}");
                return;
            }

            if (entry.barriers == null) return;

            foreach (var barrier in entry.barriers)
            {
                if (barrier == null || !barrier.activeSelf) continue;

                // 파괴 연출
                if (playEffect && destroyEffect != null)
                {
                    Instantiate(destroyEffect, barrier.transform.position, Quaternion.identity);
                }

                // 장벽 비활성화 (Destroy 대신 비활성화 — 재활성화 가능성 대비)
                barrier.SetActive(false);

                Debug.Log($"[ZoneUnlockSystem] Barrier destroyed: {barrier.name} (zone={zoneId})");
            }

            // 모든 클라이언트에 동기화
            UnlockZoneClientRpc(zoneId, playEffect);
        }

        private static bool TryResolveZoneLockBySceneObjectName(string zoneId, out ZoneLockEntry entry)
        {
            entry = default;

            if (string.IsNullOrWhiteSpace(zoneId))
                return false;

            GameObject barrier = GameObject.Find(zoneId);
            if (barrier == null)
                barrier = GameObject.Find(zoneId.Replace(' ', '_'));
            if (barrier == null)
                barrier = GameObject.Find(zoneId.Replace('_', ' '));
            if (barrier == null)
                return false;

            entry = new ZoneLockEntry
            {
                zoneId = zoneId,
                barriers = new[] { barrier }
            };
            return true;
        }

        [ClientRpc]
        private void UnlockZoneClientRpc(string zoneId, bool playEffect)
        {
            // 서버가 이미 처리했으면 스킵
            if (IsServer) return;

            if (!_lockLookup.TryGetValue(zoneId, out var entry) && !TryResolveZoneLockBySceneObjectName(zoneId, out entry)) return;
            if (entry.barriers == null) return;

            foreach (var barrier in entry.barriers)
            {
                if (barrier == null || !barrier.activeSelf) continue;

                if (playEffect && destroyEffect != null)
                    Instantiate(destroyEffect, barrier.transform.position, Quaternion.identity);

                barrier.SetActive(false);
            }
        }
    }
}
