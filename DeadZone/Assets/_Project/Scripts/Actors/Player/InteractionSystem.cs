using UnityEngine;
using Unity.Netcode;


namespace DeadZone.Actors
{
    /// <summary>
    /// 쿼터뷰 참고: 카메라에서 커서를 통과하는 Raycast를 가정한다.
    /// 카메라 팀이 ray 원점과 방향을 제공한다. 아래 기본 구현은 FPS에서 동작하는
    /// MainCamera + 화면 중앙을 사용하며, 쿼터뷰에서도 합리적인 임시 구현이 된다
    /// 쿼터뷰용 임시 구현 (나중에 커서-지면 레이캐스트로 교체 가능).
    /// </summary>
    public class InteractionSystem : MonoBehaviour
    {
        [SerializeField] private float interactDistance = 2.5f;
        [SerializeField] private LayerMask interactMask = ~0;
        [SerializeField] private Camera cameraOverride;

        private NetworkObject netObj;

        private void Awake()
        {
            netObj = GetComponentInParent<NetworkObject>();
        }

        public void TryInteract()
        {
            if (netObj == null || !netObj.IsOwner) return;

            Camera cam = cameraOverride != null ? cameraOverride : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));

            if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactMask))
            {
                var interactable = hit.collider.GetComponentInParent<IInteractable>();
                interactable?.OnInteract(netObj.OwnerClientId);
            }
        }
    }

    public interface IInteractable
    {
        void OnInteract(ulong clientId);
        string GetPromptText();
    }
}
