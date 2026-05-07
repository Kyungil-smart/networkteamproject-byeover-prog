using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// 투사체에 발광 트레이서 이펙트를 추가한다.
    /// Trail Renderer + Point Light로 탄도를 시각화한다.
    /// Bullet_Trail_FX 프리팹에 부착한다.
    /// </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public class BulletTracerEffect : MonoBehaviour
    {
        [Header("트레이서 색상")]
        [Tooltip("탄도 색상 (노란색 = 일반탄, 빨간색 = 적 탄환)")]
        [SerializeField] private Color tracerColor = new Color(1f, 0.8f, 0.2f, 1f);

        [Header("트레이서 크기")]
        [Tooltip("트레이서 시작 폭")]
        [SerializeField] private float startWidth = 0.08f;

        [Tooltip("트레이서 끝 폭")]
        [SerializeField] private float endWidth = 0.01f;

        [Tooltip("트레이서 잔상 길이 (초)")]
        [SerializeField] private float trailTime = 0.05f;

        [Header("발광 라이트")]
        [Tooltip("총알 주변 발광 라이트 사용 여부")]
        [SerializeField] private bool useLight = true;

        [Tooltip("라이트 범위")]
        [SerializeField] private float lightRange = 3f;

        [Tooltip("라이트 밝기")]
        [SerializeField] private float lightIntensity = 2f;

        private TrailRenderer trail;
        private Light pointLight;

        private void Awake()
        {
            SetupTrailRenderer();

            if (useLight)
                SetupPointLight();
        }

        /// <summary>
        /// Trail Renderer를 설정한다. 에셋 없이 코드만으로 발광 머티리얼 생성.
        /// </summary>
        private void SetupTrailRenderer()
        {
            trail = GetComponent<TrailRenderer>();

            // 기본 발광 머티리얼 생성 (Sprites/Default 셰이더 = Additive 블렌딩 가능)
            Material tracerMat = new Material(Shader.Find("Sprites/Default"));
            tracerMat.SetColor("_Color", tracerColor);
            // Additive 블렌딩으로 발광 느낌
            tracerMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            tracerMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            tracerMat.renderQueue = 3100;

            trail.material = tracerMat;
            trail.startWidth = startWidth;
            trail.endWidth = endWidth;
            trail.time = trailTime;
            trail.minVertexDistance = 0.1f;
            trail.numCapVertices = 2;
            trail.emitting = true;

            // 색상 그라데이션: 밝은 시작 → 투명 끝
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(tracerColor, 0f),
                    new GradientColorKey(tracerColor, 0.5f),
                    new GradientColorKey(tracerColor * 0.5f, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.3f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            trail.colorGradient = gradient;

            // 기존 메시 렌더러가 너무 안 보이면 끄기 (선택)
            // var meshRend = GetComponent<MeshRenderer>();
            // if (meshRend != null) meshRend.enabled = false;
        }

        /// <summary>
        /// Point Light를 추가하여 총알 주변에 발광 효과를 준다.
        /// </summary>
        private void SetupPointLight()
        {
            GameObject lightObj = new GameObject("TracerLight");
            lightObj.transform.SetParent(transform);
            lightObj.transform.localPosition = Vector3.zero;

            pointLight = lightObj.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = tracerColor;
            pointLight.range = lightRange;
            pointLight.intensity = lightIntensity;
            pointLight.shadows = LightShadows.None;
            pointLight.renderMode = LightRenderMode.Auto;
        }
    }
}