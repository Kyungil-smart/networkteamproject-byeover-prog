using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Actors.Extraction;

namespace DeadZone.Systems.Raid
{
    public sealed class Stage3CarEndingTriggerBootstrap : MonoBehaviour
    {
        private const string StageSceneName = "Game_Stage_1";
        private const string Stage3CarName = "Stage3_Car";
        private const string EndingSceneName = "Ending";

        private static bool registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            if (!registered)
            {
                SceneManager.sceneLoaded += HandleSceneLoaded;
                registered = true;
            }

            TryAttach(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryAttach(scene);
        }

        private static void TryAttach(Scene scene)
        {
            if (!scene.IsValid() || scene.name != StageSceneName)
                return;

            GameObject car = GameObject.Find(Stage3CarName);
            if (car == null)
                return;

            Collider trigger = EnsureTriggerCollider(car);
            if (trigger == null)
                return;

            ExtractionResultTrigger extractionTrigger = car.GetComponent<ExtractionResultTrigger>();
            if (extractionTrigger == null)
                extractionTrigger = car.AddComponent<ExtractionResultTrigger>();

            extractionTrigger.ConfigureRuntime(
                EndingSceneName,
                5f,
                "Ending",
                "Boss_Stage2_All",
                2,
                "Ending");
        }

        private static Collider EnsureTriggerCollider(GameObject target)
        {
            BoxCollider boxCollider = target.GetComponent<BoxCollider>();
            if (boxCollider == null)
                boxCollider = target.AddComponent<BoxCollider>();

            Bounds bounds = ResolveBounds(target);
            Vector3 localCenter = target.transform.InverseTransformPoint(bounds.center);
            Vector3 localSize = target.transform.InverseTransformVector(bounds.size);

            boxCollider.isTrigger = true;
            boxCollider.center = localCenter;
            boxCollider.size = new Vector3(
                Mathf.Max(2f, Mathf.Abs(localSize.x)),
                Mathf.Max(2f, Mathf.Abs(localSize.y)),
                Mathf.Max(2f, Mathf.Abs(localSize.z)));

            return boxCollider;
        }

        private static Bounds ResolveBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return new Bounds(target.transform.position, Vector3.one * 4f);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }
    }
}
