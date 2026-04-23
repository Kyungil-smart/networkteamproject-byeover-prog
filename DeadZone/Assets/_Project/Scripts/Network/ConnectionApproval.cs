using Unity.Netcode;
using UnityEngine;


namespace DeadZone.Network
{
    /// <summary>
    /// 접속 승인을 처리한다. 현재 최대 4명까지 허용.
    /// 레이드 중에는 신규 접속을 차단한다.
    /// </summary>
    public class ConnectionApproval : MonoBehaviour
    {
        [SerializeField] private int maxPlayers = 4;

        private void Start()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.ConnectionApprovalCallback = null;
            }
        }

        private void ApprovalCheck(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            int currentPlayers = NetworkManager.Singleton.ConnectedClientsList.Count;
            if (currentPlayers >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = "Server is full";
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Pending = false;
        }
    }
}
