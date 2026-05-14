#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

using DeadZone.Actors;
using DeadZone.Actors.UI;
using DeadZone.Actors.UI.Hideout;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    [DefaultExecutionOrder(-999)]
    public sealed class HousingCraftMaterialF12Tester : MonoBehaviour
    {
        private static HousingCraftMaterialF12Tester instance;

        [SerializeField]
        private bool shiftF12RemovesMaterials = true;

        [SerializeField]
        private bool logDiagnostics = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            GameObject go = new GameObject(nameof(HousingCraftMaterialF12Tester));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HousingCraftMaterialF12Tester>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (!logDiagnostics)
                return;

            Debug.Log("[HousingCraftMaterialF12Tester] Active. Press F12 to add Workbench/Medical craft materials, Shift+F12 to remove test materials.");
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return;

            if (!keyboard.f12Key.wasPressedThisFrame)
                return;

            bool removeMaterials =
                shiftF12RemovesMaterials &&
                (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

            if (logDiagnostics)
            {
                Debug.Log(
                    $"[HousingCraftMaterialF12Tester] F12 detected. Scene={SceneManager.GetActiveScene().name}, " +
                    $"NetworkListening={NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening}, " +
                    $"Remove={removeMaterials}");
            }

            if (TryRunThroughNetworkPlayer(removeMaterials))
                return;

            TryRunOfflineDebugInventory(removeMaterials);
        }

        private bool TryRunThroughNetworkPlayer(bool removeMaterials)
        {
            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null || !networkManager.IsListening)
            {
                LogDiagnostic("Network path skipped. NetworkManager is not listening.");
                return false;
            }

            NetworkClient localClient = networkManager.LocalClient;
            NetworkObject playerObject = localClient?.PlayerObject;

            if (playerObject == null)
            {
                LogDiagnostic("Network path skipped. Local PlayerObject is null.");
                return false;
            }

            PlayerHousingSaveSyncer saveSyncer = playerObject.GetComponent<PlayerHousingSaveSyncer>();

            if (saveSyncer == null)
                saveSyncer = playerObject.GetComponentInChildren<PlayerHousingSaveSyncer>(true);

            if (saveSyncer == null)
            {
                Debug.LogWarning("[HousingCraftMaterialF12Tester] Network path failed. PlayerHousingSaveSyncer is missing on the local PlayerObject.");
                return false;
            }

            bool requested = saveSyncer.TryRunDebugCraftMaterialTest(removeMaterials);

            if (!requested)
            {
                Debug.LogWarning("[HousingCraftMaterialF12Tester] Network path failed. PlayerHousingSaveSyncer is not spawned as the local owner yet.");
                return false;
            }

            Debug.Log(removeMaterials
                ? "[HousingCraftMaterialF12Tester] Shift+F12 requested through local network player."
                : "[HousingCraftMaterialF12Tester] F12 requested through local network player.");

            return true;
        }

        private void LogDiagnostic(string message)
        {
            if (!logDiagnostics)
                return;

            Debug.Log($"[HousingCraftMaterialF12Tester] {message} Scene={SceneManager.GetActiveScene().name}");
        }

        private void TryRunOfflineDebugInventory(bool removeMaterials)
        {
            HousingCraftMaterialDebugInventory inventory = HousingCraftMaterialDebugInventory.Instance;
            // 네트워크가 없는 로비 테스트에서는 보이는 보관함 UI에도 재료를 넣어 사용자가 직접 확인할 수 있게 합니다.
            IInventory visibleInventory = ResolveVisibleOfflineInventory();

            if (removeMaterials)
                inventory.RemoveTestCraftMaterials(visibleInventory);
            else
                inventory.AddAllCraftMaterialsForTest(visibleInventory);

            Debug.Log(removeMaterials
                ? "[HousingCraftMaterialF12Tester] Shift+F12 applied to offline craft test inventory."
                : "[HousingCraftMaterialF12Tester] F12 applied to offline craft test inventory.");

            RefreshOpenCraftWindows();
        }

        private static IInventory ResolveVisibleOfflineInventory()
        {
            // 로비 보관함이 씬에 있으면 F12 테스트 재료를 디버그 인벤토리와 함께 이쪽에도 반영합니다.
            StashGridUI stashGrid = FindFirstObjectByType<StashGridUI>(FindObjectsInactive.Include);
            return stashGrid != null ? stashGrid : null;
        }

        private static void RefreshOpenCraftWindows()
        {
            FacilityCraftWindowUI[] windows = FindObjectsByType<FacilityCraftWindowUI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null && windows[i].IsOpen)
                    windows[i].Refresh();
            }
        }
    }
}
#endif
