using System;
using DeadZone.Actors;
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

            RectTransform miniMapRect = FindRect("MiniMapBG");
            RectTransform worldMapRect = FindRect("Png_WorldMap_01");
            RectTransform miniMarkerRect = FindRect("PlayerMarker_Minimap");
            RectTransform worldMarkerRect = FindRect("PlayerMarker_WorldMap");

            ConfigureFollower(miniMarkerRect, miniMapRect, miniMarkerRect, worldMin, worldMax);
            ConfigureFollower(worldMarkerRect, worldMapRect, worldMarkerRect, worldMin, worldMax);

            GameObject mapSystem = GameObject.Find("MapSystem");
            if (mapSystem != null && mapSystem.GetComponent<MapSystemTargetBinder>() == null)
                Undo.AddComponent<MapSystemTargetBinder>(mapSystem);

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

        private static RectTransform FindRect(string objectName)
        {
            GameObject obj = GameObject.Find(objectName);
            if (obj == null)
                throw new InvalidOperationException($"{objectName} was not found.");

            RectTransform rectTransform = obj.GetComponent<RectTransform>();
            if (rectTransform == null)
                throw new InvalidOperationException($"{objectName} has no RectTransform.");

            return rectTransform;
        }

        private static void ConfigureFollower(
            RectTransform markerObject,
            RectTransform mapRect,
            RectTransform markerRect,
            Vector2 worldMin,
            Vector2 worldMax)
        {
            MapMarkerFollower follower = markerObject.GetComponent<MapMarkerFollower>();
            if (follower == null)
                follower = Undo.AddComponent<MapMarkerFollower>(markerObject.gameObject);

            SerializedObject serializedFollower = new(follower);
            serializedFollower.FindProperty("mapRect").objectReferenceValue = mapRect;
            serializedFollower.FindProperty("markerRect").objectReferenceValue = markerRect;
            serializedFollower.FindProperty("target").objectReferenceValue = null;
            serializedFollower.FindProperty("worldMin").vector2Value = worldMin;
            serializedFollower.FindProperty("worldMax").vector2Value = worldMax;
            serializedFollower.FindProperty("updateEveryFrame").boolValue = true;
            serializedFollower.FindProperty("clampToMap").boolValue = true;
            serializedFollower.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(follower);
        }
    }
}
