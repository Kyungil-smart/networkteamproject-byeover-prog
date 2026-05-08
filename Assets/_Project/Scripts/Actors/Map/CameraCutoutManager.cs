using System;
using UnityEngine;
using DeadZone.Actors;

public class CameraCutoutManager : MonoBehaviour
{
    [Header("====추적 대상====")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private FPSController fpsController;

    [Header("====컷아웃 범위====")]
    [Tooltip("남길 높이 보정치")]
    [SerializeField] private float cutoutHeightOffset = 1f;
    [Tooltip("플레이어 주변 확보 범위")]
    [SerializeField] private float playerRadius = 6f;
    [SerializeField] private float lookDistance = 4f;
    [Tooltip("커서 주변 확보 범위")]
    [SerializeField] private float lookRadius = 5f;
    
    // 셰이더 프로퍼티 ID
    private static readonly int cutoutTargetId = Shader.PropertyToID("_CutoutPlayerCenter");
    private static readonly int cutoutRadiusId = Shader.PropertyToID("_CutoutPlayerRadius");
    private static readonly int cutoutLookCenterId = Shader.PropertyToID("_CutoutLookCenter");
    private static readonly int cutoutLookRadiusId = Shader.PropertyToID("_CutoutPlayerLookRadius");
    private static readonly int cutoutMinYId = Shader.PropertyToID("_CutoutMinY");
    private static readonly int isPlayerInId = Shader.PropertyToID("_PlayerInside");

    private void OnEnable()
    {
        
    }
    private void OnDisable()
    {
        
    }
}
