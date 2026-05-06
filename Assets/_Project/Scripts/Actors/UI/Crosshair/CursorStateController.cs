using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public static class CursorStateController
    {
        private static readonly HashSet<object> GameplayOwners = new();
        private static readonly HashSet<object> UiOwners = new();

        public static bool IsGameplayMode => GameplayOwners.Count > 0 && UiOwners.Count == 0;
        public static bool IsUiMode => UiOwners.Count > 0;

        public static event System.Action<bool> GameplayModeChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            GameplayOwners.Clear();
            UiOwners.Clear();
            GameplayModeChanged = null;
        }

        public static void RegisterGameplayOwner(object owner)
        {
            if (owner == null)
                return;

            bool previous = IsGameplayMode;
            GameplayOwners.Add(owner);
            Apply(previous);
        }

        public static void UnregisterGameplayOwner(object owner)
        {
            if (owner == null)
                return;

            bool previous = IsGameplayMode;
            GameplayOwners.Remove(owner);
            Apply(previous);
        }

        public static void PushUiOwner(object owner)
        {
            if (owner == null)
                return;

            bool previous = IsGameplayMode;
            UiOwners.Add(owner);
            Apply(previous);
        }

        public static void PopUiOwner(object owner)
        {
            if (owner == null)
                return;

            bool previous = IsGameplayMode;
            UiOwners.Remove(owner);
            Apply(previous);
        }

        public static void ToggleUiOwner(object owner)
        {
            if (owner == null)
                return;

            if (UiOwners.Contains(owner))
                PopUiOwner(owner);
            else
                PushUiOwner(owner);
        }

        private static void Apply(bool previousGameplayMode)
        {
            bool gameplayMode = IsGameplayMode;

            Cursor.lockState = gameplayMode
                ? CursorLockMode.Confined
                : CursorLockMode.None;
            Cursor.visible = !gameplayMode;

            if (previousGameplayMode != gameplayMode)
                GameplayModeChanged?.Invoke(gameplayMode);
        }
    }
}
