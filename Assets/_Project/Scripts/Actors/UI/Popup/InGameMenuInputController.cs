using UnityEngine;
using UnityEngine.InputSystem;

namespace DeadZone.Actors.UI
{
    public sealed class InGameMenuInputController : MonoBehaviour
    {
        private const string RuntimeObjectName = "[InGameMenuInputController]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeController()
        {
            if (FindFirstObjectByType<InGameMenuInputController>() != null)
                return;

            GameObject controllerObject = new GameObject(RuntimeObjectName);
            DontDestroyOnLoad(controllerObject);
            controllerObject.AddComponent<InGameMenuInputController>();
        }

        private void Update()
        {
            if (Keyboard.current == null)
                return;

            if (!Keyboard.current.escapeKey.wasPressedThisFrame)
                return;

            InGameMenuUI menu = FindSceneInGameMenu();
            if (menu == null)
                return;

            menu.HandleEscapeInput();
        }

        private static InGameMenuUI FindSceneInGameMenu()
        {
            InGameMenuUI[] menus = Resources.FindObjectsOfTypeAll<InGameMenuUI>();
            for (int i = 0; i < menus.Length; i++)
            {
                InGameMenuUI menu = menus[i];
                if (menu != null && menu.gameObject.scene.IsValid())
                    return menu;
            }

            return null;
        }
    }
}
