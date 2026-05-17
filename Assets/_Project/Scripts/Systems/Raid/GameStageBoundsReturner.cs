using System.Collections.Generic;

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadZone.Systems.Raid
{
    public sealed class GameStageBoundsReturner : MonoBehaviour
    {
        private const string StageSceneName = "Game_Stage_1";
        private const string RootName = "__GameStageBoundsReturner";

        private static readonly Vector3 MinBounds = new(-410f, -50f, -155f);
        private static readonly Vector3 MaxBounds = new(10f, 80f, 60f);
        private static bool registered;

        private readonly Dictionary<Transform, Vector3> lastValidPositions = new();
        private float nextScanTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            if (!registered)
            {
                SceneManager.sceneLoaded += HandleSceneLoaded;
                registered = true;
            }

            TryCreate(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryCreate(scene);
        }

        private static void TryCreate(Scene scene)
        {
            if (!scene.IsValid() || scene.name != StageSceneName)
                return;

            if (GameObject.Find(RootName) != null)
                return;

            new GameObject(RootName, typeof(GameStageBoundsReturner));
        }

        private void Update()
        {
            if (Time.time < nextScanTime)
                return;

            nextScanTime = Time.time + 0.25f;
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            for (int i = 0; i < players.Length; i++)
                ClampPlayer(players[i]);
        }

        private void ClampPlayer(GameObject player)
        {
            if (player == null)
                return;

            NetworkObject networkObject = player.GetComponent<NetworkObject>();
            NetworkManager networkManager = NetworkManager.Singleton;
            bool isServer = networkManager == null || !networkManager.IsListening || networkManager.IsServer;
            if (networkObject != null && networkObject.IsSpawned && !isServer && !networkObject.IsOwner)
                return;

            Transform target = player.transform;
            Vector3 position = target.position;
            bool inside = IsInside(position);
            if (inside)
            {
                lastValidPositions[target] = position;
                return;
            }

            Vector3 fallback = lastValidPositions.TryGetValue(target, out Vector3 validPosition)
                ? validPosition
                : new Vector3(
                    Mathf.Clamp(position.x, MinBounds.x, MaxBounds.x),
                    position.y,
                    Mathf.Clamp(position.z, MinBounds.z, MaxBounds.z));

            fallback.y = Mathf.Clamp(fallback.y, MinBounds.y, MaxBounds.y);
            target.position = fallback;
            lastValidPositions[target] = fallback;
        }

        private static bool IsInside(Vector3 position)
        {
            return position.x >= MinBounds.x && position.x <= MaxBounds.x &&
                   position.y >= MinBounds.y && position.y <= MaxBounds.y &&
                   position.z >= MinBounds.z && position.z <= MaxBounds.z;
        }
    }
}
