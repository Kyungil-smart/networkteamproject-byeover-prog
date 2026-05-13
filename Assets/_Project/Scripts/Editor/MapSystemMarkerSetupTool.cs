using System;
using DeadZone.Actors.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadZone.Editor
{
    public static class MapSystemMarkerSetupTool
    {
        private const string ScenePath = "Assets/Scenes/HJO/HJO_IngameHUD.unity";

        [MenuItem("DeadZone/Map/Apply HJO Ingame HUD Marker Setup")]
        public static void ApplyHjoIngameHudMarkerSetup()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject mapRoot = GameObject.Find("Map");
            if (mapRoot == null)
                throw new InvalidOperationException("Map root object was not found.");

            Bounds mapBounds = CalculateBounds(mapRoot);
            Vector2 worldMin = new(mapBounds.min.x, mapBounds.min.z);
            Vector2 worldMax = new(mapBounds.max.x, mapBounds.max.z);

            ConfigureMinimapCameraFollower();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                $"[MapSystemMarkerSetupTool] Applied marker setup. " +
                $"bounds min=({mapBounds.min.x:F3}, {mapBounds.min.y:F3}, {mapBounds.min.z:F3}), " +
                $"max=({mapBounds.max.x:F3}, {mapBounds.max.y:F3}, {mapBounds.max.z:F3}), " +
                $"worldMin=({worldMin.x:F3}, {worldMin.y:F3}), worldMax=({worldMax.x:F3}, {worldMax.y:F3})");
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Bounds? result = null;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                Encapsulate(ref result, renderer.bounds);

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                Encapsulate(ref result, collider.bounds);

            if (!result.HasValue)
                throw new InvalidOperationException("Map root has no Renderer or Collider bounds.");

            return result.Value;
        }

        private static void Encapsulate(ref Bounds? result, Bounds bounds)
        {
            if (bounds.size == Vector3.zero)
                return;

            if (result.HasValue)
            {
                Bounds merged = result.Value;
                merged.Encapsulate(bounds);
                result = merged;
            }
            else
            {
                result = bounds;
            }
        }

        private static void ConfigureMinimapCameraFollower()
        {
            GameObject minimapCamera = GameObject.Find("MinimapCamera");
            if (minimapCamera == null)
            {
                Debug.LogWarning("[MapSystemMarkerSetupTool] MinimapCamera was not found.");
                return;
            }

            MinimapCameraFollower follower = minimapCamera.GetComponent<MinimapCameraFollower>();
            if (follower == null)
                follower = Undo.AddComponent<MinimapCameraFollower>(minimapCamera);

            EditorUtility.SetDirty(follower);
        }
    }
}
