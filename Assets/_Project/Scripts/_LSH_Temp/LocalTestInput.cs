using System;
using UnityEngine;

namespace DeadZone.Actors._LSH_Temp
{
    /// <summary>
    /// 로컬 테스트용.
    /// Input Manager 사용
    /// wasd, Shift 입력을 FPSController의 SetMove/SetSprint로 전달
    /// </summary>
    [RequireComponent(typeof(FPSController))]
    public class LocalTestInput : MonoBehaviour
    {
        private FPSController fpsController;

        private void Awake()
        {
            fpsController = GetComponent<FPSController>();
        }

        private void Update()
        {
            Vector2 move = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")
            );
            fpsController.SetMove(move);
            
            fpsController.SetSprint(Input.GetKey(KeyCode.LeftShift));
            
        }
    }
}