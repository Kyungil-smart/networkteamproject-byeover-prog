using System.Collections.Generic;
using MoreMountains.Feedbacks;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace DeadZone.EditorTools
{
    public static class UIFeedbackSetupTool
    {
        private const string SetupMenuPath = "Tools/UI/Setup Default MMF Feedbacks";
        private const string AuditMenuPath = "Tools/UI/Audit MMF Players";
        private const string ClearMenuPath = "Tools/UI/Clear Auto Generated MMF Visuals";

        private const string AutoDebugLabel = "[AutoUI] Debug Log";
        private const string AutoVisualLabelPrefix = "[AutoUI] Visual";

        private enum VisualStyle
        {
            None,
            Punch,
            Pop,
            Emphasis,
        }

        private readonly struct TargetResolution
        {
            public TargetResolution(Transform target, string source, bool fallback, bool containerOnly, string stateHint)
            {
                Target = target;
                Source = source;
                Fallback = fallback;
                ContainerOnly = containerOnly;
                StateHint = stateHint;
            }

            public Transform Target { get; }
            public string Source { get; }
            public bool Fallback { get; }
            public bool ContainerOnly { get; }
            public string StateHint { get; }
        }

        [MenuItem(AuditMenuPath)]
        public static void AuditMmfPlayers()
        {
            List<MMF_Player> players = FindScenePlayers();
            Debug.Log($"[UIFeedbackSetupTool] Audit found {players.Count} MMF_Player(s) in open scenes.");

            foreach (MMF_Player player in players)
            {
                int feedbackCount = player.FeedbacksList != null ? player.FeedbacksList.Count : 0;
                bool hasDebug = HasDebugLog(player);
                bool hasVisual = HasVisualFeedback(player);
                TargetResolution resolved = ResolveTarget(player);

                Debug.Log(
                    $"[UIFeedbackSetupTool] {GetHierarchyPath(player.transform)} | " +
                    $"activeInHierarchy={player.gameObject.activeInHierarchy}, enabled={player.enabled}, " +
                    $"feedbacks={feedbackCount}, debugLog={hasDebug}, visual={hasVisual}, " +
                    $"resolvedTarget={(resolved.Target != null ? GetHierarchyPath(resolved.Target) : "none")}, " +
                    $"source={resolved.Source}, fallback={resolved.Fallback}",
                    player);

                AuditVisualFeedbackTargets(player);
            }
        }

        [MenuItem(SetupMenuPath)]
        public static void SetupDefaultFeedbacks()
        {
            List<MMF_Player> players = FindScenePlayers();

            Debug.Log($"[UIFeedbackSetupTool] Found {players.Count} MMF_Player(s) in open scenes.");
            foreach (MMF_Player player in players)
                Debug.Log($"[UIFeedbackSetupTool] Found: {GetHierarchyPath(player.transform)}", player);

            int debugAddedCount = 0;
            int visualAddedCount = 0;
            int visualUpdatedCount = 0;
            int skippedManualVisualCount = 0;

            foreach (MMF_Player player in players)
            {
                bool changed = false;
                Undo.RecordObject(player, "Setup Default MMF Feedbacks");

                if (!HasDebugLog(player))
                {
                    AddDebugLog(player);
                    debugAddedCount++;
                    changed = true;
                }

                VisualStyle style = ResolveVisualStyle(player.gameObject.name);
                TargetResolution target = ResolveTarget(player);
                MMF_Scale autoScale = FindAutoOrSelfTargetScaleVisual(player);

                if (style == VisualStyle.None)
                {
                    Debug.LogWarning($"[UIFeedbackSetupTool] No visual style matched for {GetHierarchyPath(player.transform)}. Debug Log only.", player);
                }
                else if (target.Target == null)
                {
                    Debug.LogWarning($"[UIFeedbackSetupTool] No target found for {GetHierarchyPath(player.transform)}. Debug Log only.", player);
                }
                else if (autoScale != null)
                {
                    ConfigureScaleVisual(autoScale, target.Target, style);
                    visualUpdatedCount++;
                    changed = true;
                    Debug.Log($"[UIFeedbackSetupTool] Updated auto {style} visual on {GetHierarchyPath(player.transform)} targeting {GetHierarchyPath(target.Target)} via {target.Source}", player);
                }
                else if (HasManualVisualFeedback(player))
                {
                    skippedManualVisualCount++;
                    Debug.Log($"[UIFeedbackSetupTool] Manual visual exists, auto visual not added: {GetHierarchyPath(player.transform)}", player);
                }
                else
                {
                    AddScaleVisual(player, target.Target, style);
                    visualAddedCount++;
                    changed = true;
                    Debug.Log($"[UIFeedbackSetupTool] Added {style} visual to {GetHierarchyPath(player.transform)} targeting {GetHierarchyPath(target.Target)} via {target.Source}", player);
                }

                if (target.Target != null)
                    LogTargetWarnings(player, target.Target, target.ContainerOnly, target.StateHint);

                if (!changed) continue;

                player.RefreshCache();
                EditorUtility.SetDirty(player);
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            }

            Debug.Log(
                $"[UIFeedbackSetupTool] Complete. Debug logs added={debugAddedCount}, " +
                $"visuals added={visualAddedCount}, visuals updated={visualUpdatedCount}, " +
                $"manual visuals skipped={skippedManualVisualCount}.");
        }

        [MenuItem(ClearMenuPath)]
        public static void ClearAutoGeneratedVisuals()
        {
            List<MMF_Player> players = FindScenePlayers();
            int removedCount = 0;

            foreach (MMF_Player player in players)
            {
                if (player.FeedbacksList == null || player.FeedbacksList.Count == 0)
                    continue;

                bool changed = false;
                Undo.RecordObject(player, "Clear Auto Generated MMF Visuals");

                for (int i = player.FeedbacksList.Count - 1; i >= 0; i--)
                {
                    MMF_Feedback feedback = player.FeedbacksList[i];
                    if (!IsAutoVisualFeedback(feedback))
                        continue;

                    player.FeedbacksList.RemoveAt(i);
                    removedCount++;
                    changed = true;
                }

                if (!changed) continue;

                player.RefreshCache();
                EditorUtility.SetDirty(player);
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
                Debug.Log($"[UIFeedbackSetupTool] Cleared auto visual(s): {GetHierarchyPath(player.transform)}", player);
            }

            Debug.Log($"[UIFeedbackSetupTool] Clear complete. Removed {removedCount} auto-generated visual feedback(s).");
        }

        private static List<MMF_Player> FindScenePlayers()
        {
            List<MMF_Player> players = new();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                    players.AddRange(root.GetComponentsInChildren<MMF_Player>(true));
            }

            return players;
        }

        private static bool HasDebugLog(MMF_Player player)
        {
            if (player.FeedbacksList == null) return false;

            foreach (MMF_Feedback feedback in player.FeedbacksList)
            {
                if (feedback is MMF_DebugLog)
                    return true;
            }

            return false;
        }

        private static bool HasVisualFeedback(MMF_Player player)
        {
            if (player.FeedbacksList == null) return false;

            foreach (MMF_Feedback feedback in player.FeedbacksList)
            {
                if (IsVisualFeedback(feedback))
                    return true;
            }

            return false;
        }

        private static bool HasManualVisualFeedback(MMF_Player player)
        {
            if (player.FeedbacksList == null) return false;

            foreach (MMF_Feedback feedback in player.FeedbacksList)
            {
                if (!IsVisualFeedback(feedback))
                    continue;

                if (!IsAutoVisualFeedback(feedback))
                    return true;
            }

            return false;
        }

        private static MMF_Scale FindAutoScaleVisual(MMF_Player player)
        {
            if (player.FeedbacksList == null) return null;

            foreach (MMF_Feedback feedback in player.FeedbacksList)
            {
                if (feedback is MMF_Scale scale && IsAutoVisualFeedback(feedback))
                    return scale;
            }

            return null;
        }

        private static MMF_Scale FindAutoOrSelfTargetScaleVisual(MMF_Player player)
        {
            MMF_Scale autoScale = FindAutoScaleVisual(player);
            if (autoScale != null)
                return autoScale;

            if (player.FeedbacksList == null) return null;

            foreach (MMF_Feedback feedback in player.FeedbacksList)
            {
                if (feedback is MMF_Scale scale && scale.AnimateScaleTarget == player.transform)
                    return scale;
            }

            return null;
        }

        private static bool IsVisualFeedback(MMF_Feedback feedback)
        {
            if (feedback == null) return false;

            return feedback is MMF_Scale || IsAutoVisualFeedback(feedback);
        }

        private static bool IsAutoVisualFeedback(MMF_Feedback feedback)
        {
            return feedback != null
                   && !string.IsNullOrEmpty(feedback.Label)
                   && feedback.Label.StartsWith(AutoVisualLabelPrefix);
        }

        private static void AuditVisualFeedbackTargets(MMF_Player player)
        {
            if (player.FeedbacksList == null) return;

            foreach (MMF_Feedback feedback in player.FeedbacksList)
            {
                if (!IsVisualFeedback(feedback))
                    continue;

                Transform target = GetFeedbackTarget(feedback);
                bool targetNull = target == null;
                bool activeInHierarchy = target != null && target.gameObject.activeInHierarchy;
                bool isRectTransform = target is RectTransform;
                bool containerOnly = IsKillFeedContainerOnly(player, target);

                Debug.Log(
                    $"[UIFeedbackSetupTool] Visual Audit | player={player.gameObject.name}, " +
                    $"feedbackType={feedback.GetType().Name}, label={feedback.Label}, " +
                    $"targetObject={(target != null ? target.name : "null")}, targetNull={targetNull}, " +
                    $"targetActiveInHierarchy={activeInHierarchy}, targetIsRectTransform={isRectTransform}, " +
                    $"targetPath={(target != null ? GetHierarchyPath(target) : "none")}",
                    player);

                if (target == null)
                {
                    Debug.LogWarning($"[UIFeedbackSetupTool] Target is null: {GetHierarchyPath(player.transform)} / {feedback.GetType().Name}", player);
                    continue;
                }

                LogTargetWarnings(player, target, containerOnly, GetStateHint(player, target));
            }
        }

        private static Transform GetFeedbackTarget(MMF_Feedback feedback)
        {
            if (feedback is MMF_Scale scale)
                return scale.AnimateScaleTarget;

            return null;
        }

        private static void AddDebugLog(MMF_Player player)
        {
            MMF_DebugLog debugLog = player.AddFeedback(typeof(MMF_DebugLog)) as MMF_DebugLog;
            if (debugLog == null) return;

            debugLog.Label = AutoDebugLabel;
            debugLog.DebugLogMode = MMF_DebugLog.DebugLogModes.Log;
            debugLog.DebugMessage = $"[MMF Test] Played feedback object: {GetHierarchyPath(player.transform)}";
            debugLog.DisplayFrameCount = true;
        }

        private static void AddScaleVisual(MMF_Player player, Transform target, VisualStyle style)
        {
            MMF_Scale scale = player.AddFeedback(typeof(MMF_Scale)) as MMF_Scale;
            if (scale == null) return;

            ConfigureScaleVisual(scale, target, style);
        }

        private static void ConfigureScaleVisual(MMF_Scale scale, Transform target, VisualStyle style)
        {
            scale.Label = $"{AutoVisualLabelPrefix} {style}";
            scale.AnimateScaleTarget = target;
            scale.Mode = MMF_Scale.Modes.Absolute;
            scale.UniformScaling = true;
            scale.AllowAdditivePlays = true;
            scale.DetermineScaleOnPlay = true;

            switch (style)
            {
                case VisualStyle.Punch:
                    scale.AnimateScaleDuration = 0.12f;
                    scale.RemapCurveZero = 1f;
                    scale.RemapCurveOne = 1.18f;
                    break;
                case VisualStyle.Emphasis:
                    scale.AnimateScaleDuration = 0.16f;
                    scale.RemapCurveZero = 1f;
                    scale.RemapCurveOne = 1.24f;
                    break;
                case VisualStyle.Pop:
                    scale.AnimateScaleDuration = 0.18f;
                    scale.RemapCurveZero = 1f;
                    scale.RemapCurveOne = 1.12f;
                    break;
            }
        }

        private static VisualStyle ResolveVisualStyle(string objectName)
        {
            string name = objectName.ToLowerInvariant();

            if (ContainsAny(name, "lowhp", "heartbeat", "finaltick"))
                return VisualStyle.Emphasis;

            if (ContainsAny(name, "damaged", "damage", "knocked", "critical"))
                return VisualStyle.Punch;

            if (ContainsAny(name, "healed", "heal", "revive", "completed"))
                return VisualStyle.Pop;

            if (ContainsAny(name, "quest", "killfeed", "spectator", "extraction"))
                return VisualStyle.Pop;

            return VisualStyle.None;
        }

        private static TargetResolution ResolveTarget(MMF_Player player)
        {
            string name = player.gameObject.name;

            if (StartsWithAny(name, "HUD_HpHealedFeedback", "PlayerStats_HpHealedFeedback"))
                return FindFirst(player, string.Empty, false, "Player_Hp_Fill", "PlayerStatsUI", "PlayerHUD");

            if (StartsWithAny(name, "HUD_HpDamagedFeedback", "HUD_LowHpFeedback"))
                return FindFirst(player, string.Empty, false, "Player_Hp_Fill", "PlayerStatsUI", "PlayerHUD");

            if (StartsWithAny(name, "PlayerStats_HpDamagedFeedback", "PlayerStats_LowHpFeedback"))
                return FindFirst(player, string.Empty, false, "Player_Hp_Fill", "PlayerStatsUI");

            if (StartsWithAny(name, "Quest_AcceptedFeedback", "Quest_ProgressFeedback", "Quest_NearCompleteFeedback", "Quest_CompletedFeedback"))
                return FindFirst(player, string.Empty, false, "QuestTrackerUI", "BG_Quest");

            if (StartsWithAny(name, "KillFeed_LocalKillFeedback", "KillFeed_LocalCritFeedback"))
                return ResolveKillFeedTarget(player);

            if (StartsWithAny(name, "KillFeed_TeammateDeathFeedback"))
                return FindFirst(player, string.Empty, false, "EntriesContainer", "KillFeedUI");

            if (StartsWithAny(name, "Knocked_OnKnockedFeedback"))
                return FindFirst(player, "기절 상태에서 테스트해야 보임.", false, "KnockedHUD", "Text_KnockedTitle");

            if (StartsWithAny(name, "Knocked_HeartbeatLoopFeedback", "Knocked_CriticalBleedoutFeedback"))
                return FindFirst(player, "기절 상태에서 테스트해야 보임.", false, "BloodVignette", "BleedoutProgress", "KnockedHUD");

            if (StartsWithAny(name, "Knocked_ReviveStartedFeedback", "Knocked_ReviveEndedFeedback"))
                return FindFirst(player, "기절/부활 진행 상태에서 테스트해야 보임.", false, "BleedoutProgress", "KnockedHUD");

            if (StartsWithAny(name, "Extraction_StartFeedback", "Extraction_TickFeedback", "Extraction_FinalTickFeedback", "Extraction_CompletedFeedback"))
                return FindFirst(
                    player,
                    "탈출 UI가 표시된 상태에서 테스트해야 보임.",
                    false,
                    "ExtractionUI",
                    "Text_Countdown",
                    "CountdownText",
                    "ExtractionCountdownText",
                    "Text_ExtractionCountdown",
                    "ExtractionPanel",
                    "Panel_Extraction",
                    "Panel",
                    "Root");

            if (StartsWithAny(name, "Spectator_StartFeedback", "Spectator_TeammateTargetFeedback", "Spectator_FreeCameraFeedback"))
                return ResolveSpectatorTarget(player);

            if (StartsWithAny(name, "Spectator_StartFeedback", "Spectator_TeammateTargetFeedback", "Spectator_FreeCameraFeedback"))
                return FindFirst(
                    player,
                    "관전 상태에서 테스트해야 보임.",
                    false,
                    "SpectatorHUD",
                    "Text_TargetName",
                    "TargetNameText",
                    "SpectatorTargetNameText",
                    "SpectatorPanel",
                    "Panel_Spectator",
                    "Panel",
                    "Root");

            Transform fallback = ResolveFallbackTarget(player);
            return new TargetResolution(fallback, "Fallback RectTransform", true, false, GetStateHint(player, fallback));
        }

        private static TargetResolution ResolveKillFeedTarget(MMF_Player player)
        {
            Transform entry = FindFirstExisting(player.gameObject.scene, "KillFeedEntry", "KillFeedEntry(Clone)");
            if (entry != null)
                return new TargetResolution(entry, entry.name, false, false, "킬피드 엔트리가 생성된 상태에서 테스트해야 보임.");

            Transform entries = FindFirstExisting(player.gameObject.scene, "EntriesContainer");
            if (entries != null)
                return new TargetResolution(entries, "EntriesContainer", false, true, "킬피드 엔트리가 생성된 상태에서 테스트해야 보임.");

            Transform killFeedUi = FindFirstExisting(player.gameObject.scene, "KillFeedUI");
            if (killFeedUi != null)
                return new TargetResolution(killFeedUi, "KillFeedUI", false, false, "킬피드 엔트리가 생성된 상태에서 테스트해야 보임.");

            Transform fallback = ResolveFallbackTarget(player);
            return new TargetResolution(fallback, "Fallback RectTransform", true, false, "킬피드 엔트리가 생성된 상태에서 테스트해야 보임.");
        }

        private static TargetResolution ResolveSpectatorTarget(MMF_Player player)
        {
            const string stateHint = "관전 상태에서 테스트해야 보임.";

            Transform spectatorRoot = FindFirstExisting(player.gameObject.scene, "SpectatorHUD");
            Transform targetNameText = FindSpectatorTargetNameText(player.gameObject.scene, spectatorRoot);
            Transform panel = FindSpectatorPanel(player.gameObject.scene, spectatorRoot);
            bool teammateTarget = player.gameObject.name.StartsWith("Spectator_TeammateTargetFeedback");

            if (teammateTarget)
                return FirstResolved(
                    player,
                    stateHint,
                    ("Spectator target name text", targetNameText),
                    ("Spectator panel", panel),
                    ("SpectatorHUD", spectatorRoot));

            return FirstResolved(
                player,
                stateHint,
                ("Spectator panel", panel),
                ("SpectatorHUD", spectatorRoot),
                ("Spectator target name text", targetNameText));
        }

        private static TargetResolution FirstResolved(MMF_Player player, string stateHint, params (string source, Transform target)[] candidates)
        {
            foreach ((string source, Transform target) in candidates)
            {
                if (target != null && target != player.transform)
                    return new TargetResolution(target, source, false, false, stateHint);
            }

            Transform fallback = ResolveFallbackTarget(player);
            return new TargetResolution(fallback, "Fallback RectTransform", true, false, stateHint);
        }

        private static Transform FindSpectatorTargetNameText(Scene scene, Transform spectatorRoot)
        {
            Transform exact = FindFirstUnderRootOrScene(
                scene,
                spectatorRoot,
                "Text_TargetName",
                "TargetNameText",
                "SpectatorTargetNameText",
                "Text_SpectatorTargetName",
                "CurrentTargetNameText");
            if (exact != null)
                return exact;

            Transform tmpText = FindBestSpectatorText<TMP_Text>(spectatorRoot);
            if (tmpText != null)
                return tmpText;

            return FindBestSpectatorText<Text>(spectatorRoot);
        }

        private static Transform FindBestSpectatorText<T>(Transform spectatorRoot) where T : Component
        {
            if (spectatorRoot == null) return null;

            T[] texts = spectatorRoot.GetComponentsInChildren<T>(true);
            Transform fallback = null;

            foreach (T text in texts)
            {
                string lowerName = text.name.ToLowerInvariant();
                if (ContainsAny(lowerName, "key", "hint", "guide", "help"))
                    continue;

                if (ContainsAny(lowerName, "target", "name", "spectator"))
                    return text.transform;

                fallback ??= text.transform;
            }

            return fallback;
        }

        private static Transform FindSpectatorPanel(Scene scene, Transform spectatorRoot)
        {
            Transform exact = FindFirstUnderRootOrScene(
                scene,
                spectatorRoot,
                "SpectatorPanel",
                "Panel_Spectator",
                "Panel",
                "Root",
                "BG_Spectator");
            if (exact != null && exact != spectatorRoot)
                return exact;

            if (spectatorRoot == null) return null;

            RectTransform[] rects = spectatorRoot.GetComponentsInChildren<RectTransform>(true);
            foreach (RectTransform rect in rects)
            {
                if (rect.transform == spectatorRoot)
                    continue;

                if (rect.GetComponent<TMP_Text>() != null || rect.GetComponent<Text>() != null)
                    continue;

                string lowerName = rect.name.ToLowerInvariant();
                if (ContainsAny(lowerName, "panel", "root", "container", "content", "bg", "background"))
                    return rect;
            }

            return null;
        }

        private static Transform FindFirstUnderRootOrScene(Scene scene, Transform root, params string[] targetNames)
        {
            if (root != null)
            {
                foreach (string targetName in targetNames)
                {
                    Transform target = FindInChildren(root, targetName);
                    if (target != null)
                        return target;
                }
            }

            return FindFirstExisting(scene, targetNames);
        }

        private static TargetResolution FindFirst(MMF_Player player, string stateHint, bool containerOnly, params string[] targetNames)
        {
            foreach (string targetName in targetNames)
            {
                Transform target = FindFirstExisting(player.gameObject.scene, targetName);
                if (target != null)
                    return new TargetResolution(target, targetName, false, containerOnly || IsContainerOnlyName(targetName), stateHint);
            }

            Transform fallback = ResolveFallbackTarget(player);
            return new TargetResolution(fallback, "Fallback RectTransform", true, false, stateHint);
        }

        private static Transform FindFirstExisting(Scene scene, params string[] targetNames)
        {
            foreach (string targetName in targetNames)
            {
                Transform target = FindInScene(scene, targetName);
                if (target != null)
                    return target;
            }

            return null;
        }

        private static Transform FindInScene(Scene scene, string targetName)
        {
            if (!scene.isLoaded) return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform result = FindInChildren(root.transform, targetName);
                if (result != null)
                    return result;
            }

            return null;
        }

        private static Transform FindInChildren(Transform root, string targetName)
        {
            if (root.name == targetName)
                return root;

            foreach (Transform child in root)
            {
                Transform result = FindInChildren(child, targetName);
                if (result != null)
                    return result;
            }

            return null;
        }

        private static Transform ResolveFallbackTarget(MMF_Player player)
        {
            if (player.TryGetComponent<RectTransform>(out RectTransform rectTransform))
                return rectTransform;

            Transform parent = player.transform.parent;
            while (parent != null)
            {
                if (parent.TryGetComponent<RectTransform>(out RectTransform parentRect))
                    return parentRect;

                parent = parent.parent;
            }

            return player.transform;
        }

        private static void LogTargetWarnings(MMF_Player player, Transform target, bool containerOnly, string stateHint)
        {
            if (target == null)
            {
                Debug.LogWarning($"[UIFeedbackSetupTool] Target is null: {GetHierarchyPath(player.transform)}", player);
                return;
            }

            if (!target.gameObject.activeInHierarchy)
            {
                string hint = string.IsNullOrEmpty(stateHint) ? "해당 UI가 표시되는 상태에서 테스트해야 보일 수 있음." : stateHint;
                Debug.LogWarning($"[UIFeedbackSetupTool] Target is inactive: {GetHierarchyPath(player.transform)} -> {GetHierarchyPath(target)}. {hint}", player);
            }

            if (IsLikelyTooHighLevelTarget(target))
                Debug.LogWarning($"[UIFeedbackSetupTool] Target may be too high-level or Canvas root: {GetHierarchyPath(player.transform)} -> {GetHierarchyPath(target)}. 더 안쪽 Text/Image/Fill 오브젝트를 타겟으로 잡아야 시각 효과가 잘 보일 수 있음.", player);

            if (containerOnly || IsKillFeedContainerOnly(player, target))
                Debug.LogWarning($"[UIFeedbackSetupTool] KillFeed target is EntriesContainer only: {GetHierarchyPath(player.transform)} -> {GetHierarchyPath(target)}. 런타임에 생성되는 KillFeedEntry 루트가 실제로 보여야 효과가 더 잘 보임.", player);
        }

        private static bool IsKillFeedContainerOnly(MMF_Player player, Transform target)
        {
            return target != null
                   && player.gameObject.name.StartsWith("KillFeed_Local")
                   && target.name == "EntriesContainer";
        }

        private static bool IsLikelyTooHighLevelTarget(Transform target)
        {
            if (target == null) return false;
            if (target.parent == null) return true;
            if (target.GetComponent<Canvas>() != null) return true;

            Canvas parentCanvas = target.GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas.transform == target)
                return true;

            return target.name == "PlayerHUD"
                   || target.name == "PlayerStatsUI"
                   || target.name == "KillFeedUI"
                   || target.name == "KnockedHUD"
                   || target.name == "ExtractionUI"
                   || target.name == "SpectatorHUD";
        }

        private static bool IsContainerOnlyName(string targetName)
        {
            return targetName == "EntriesContainer";
        }

        private static string GetStateHint(MMF_Player player, Transform target)
        {
            string playerName = player != null ? player.gameObject.name.ToLowerInvariant() : string.Empty;
            string targetName = target != null ? target.name.ToLowerInvariant() : string.Empty;
            string combined = $"{playerName} {targetName}";

            if (combined.Contains("knocked") || combined.Contains("bleedout") || combined.Contains("bloodvignette"))
                return "기절 상태에서 테스트해야 보임.";

            if (combined.Contains("spectator"))
                return "관전 상태에서 테스트해야 보임.";

            if (combined.Contains("extraction"))
                return "탈출 UI가 표시된 상태에서 테스트해야 보임.";

            if (combined.Contains("killfeed"))
                return "킬피드 엔트리가 생성된 상태에서 테스트해야 보임.";

            return string.Empty;
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                if (value.StartsWith(prefix))
                    return true;
            }

            return false;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (value.Contains(needle))
                    return true;
            }

            return false;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return $"{transform.gameObject.scene.name}:{path}";
        }
    }
}
