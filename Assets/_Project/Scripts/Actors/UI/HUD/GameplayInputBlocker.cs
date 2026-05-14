using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Core
{
    public enum GameplayInputBlockReason
    {
        Inventory,
        Looting,
        Pause,
        Setting,
        Map,
        Quest,
        Dialogue,
        Cutscene
    }

    /// <summary>
    /// UI가 열렸을 때 이동/발사/조준 같은 게임플레이 입력을 막는 전역 로컬 상태.
    /// 네트워크 동기화 대상이 아니다. 로컬 플레이어 입력 차단용이다.
    /// </summary>
    public static class GameplayInputBlocker
    {
        private static readonly HashSet<GameplayInputBlockReason> BlockReasons = new();

        public static bool IsBlocked => BlockReasons.Count > 0;

        public static event Action<bool> OnBlockStateChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            BlockReasons.Clear();
            OnBlockStateChanged = null;
        }

        public static void SetBlocked(GameplayInputBlockReason reason, bool blocked)
        {
            bool wasBlocked = IsBlocked;

            bool changed = blocked
                ? BlockReasons.Add(reason)
                : BlockReasons.Remove(reason);

            if (!changed)
                return;

            bool isBlocked = IsBlocked;

            if (wasBlocked != isBlocked)
                OnBlockStateChanged?.Invoke(isBlocked);
        }

        public static bool IsBlockedFor(GameplayInputBlockReason reason)
        {
            return BlockReasons.Contains(reason);
        }

        public static void ClearAll()
        {
            bool wasBlocked = IsBlocked;

            BlockReasons.Clear();

            if (wasBlocked)
                OnBlockStateChanged?.Invoke(false);
        }
    }
}
