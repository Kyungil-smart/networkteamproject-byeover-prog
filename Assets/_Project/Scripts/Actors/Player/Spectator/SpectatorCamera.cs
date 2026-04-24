using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// 타겟을 따라다니거나(3인칭) 자유 이동하는 카메라.
    /// PlayerPrefab > SpectatorCameraRoot의 자식으로 부착된다.
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private Vector3 followOffset = new(0, 2.5f, -3.5f);
        [SerializeField] private float followLerpSpeed = 8f;
        [SerializeField] private float lookHeight = 1.6f;

        [Header("Free Cam")]
        [SerializeField] private float freeCamSpeed = 12f;
        [SerializeField] private float freeCamLookSensitivity = 2f;

        private Camera cam;
        private Transform followTarget;
        private bool freeCamMode;
        private Vector3 freeCamMoveInput;
        private Vector2 freeCamLookInput;
        private float yaw;
        private float pitch;

        private void Awake()
        {
            cam = GetComponentInChildren<Camera>();
        }

        public void FollowTarget(Transform target)
        {
            freeCamMode = false;
            followTarget = target;
            if (target != null)
            {
                yaw = target.eulerAngles.y;
                pitch = 15f;
            }
        }

        public void SetFreeCam()
        {
            freeCamMode = true;
            followTarget = null;
        }

        public void SetFreeCamInput(Vector2 move, Vector2 look)
        {
            freeCamMoveInput = new Vector3(move.x, 0, move.y);
            freeCamLookInput = look;
        }

        private void LateUpdate()
        {
            if (freeCamMode)
            {
                yaw += freeCamLookInput.x * freeCamLookSensitivity * Time.deltaTime;
                pitch -= freeCamLookInput.y * freeCamLookSensitivity * Time.deltaTime;
                pitch = Mathf.Clamp(pitch, -85f, 85f);

                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                Vector3 worldMove = transform.TransformDirection(freeCamMoveInput);
                transform.position += worldMove * freeCamSpeed * Time.deltaTime;
            }
            else if (followTarget != null)
            {
                Vector3 desired = followTarget.position +
                                  Quaternion.Euler(0, followTarget.eulerAngles.y, 0) * followOffset;
                transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * followLerpSpeed);
                Vector3 lookAt = followTarget.position + Vector3.up * lookHeight;
                transform.LookAt(lookAt);
            }
        }
    }
}
