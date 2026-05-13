using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// 상시 시야 원기둥과 지향 시야 부채꼴 기둥 Mesh를 생성한다.
    /// 생성된 Mesh는 Stencil Write용 Renderer에서 사용하며, 머티리얼은 면 방향에 영향받지 않도록 Cull Off로 설정해야 한다.
    /// </summary>
    public class FOVMesh : MonoBehaviour
    {
        [Header("시야 설정")]
        [Tooltip("지향 시야 거리")]
        [SerializeField, Min(0f)] private float viewDistance = 10f;

        [Tooltip("지향 시야 각도")]
        [SerializeField, Range(1f, 360f)] private float viewAngle = 120f;

        [Tooltip("상시 시야 반경")]
        [SerializeField, Min(0f)] private float minimumRange = 3f;

        [Tooltip("플레이어 기준 상단 높이")]
        [SerializeField, Min(0f)] private float topHeight = 7f;

        [Tooltip("플레이어 기준 하단 깊이")]
        [SerializeField, Min(0f)] private float bottomDepth = 3f;

        [Tooltip("곡선 표현의 정교함")]
        [SerializeField, Min(3)] private int segments = 30;

        [Header("메쉬")]
        [Tooltip("상시 시야 원기둥 Mesh를 받을 하위 MeshFilter")]
        [SerializeField] private MeshFilter cylinderMesh;

        [Tooltip("지향 시야 부채꼴 기둥 Mesh를 받을 하위 MeshFilter")]
        [SerializeField] private MeshFilter viewMesh;

        private Mesh cylinderMeshInstance;
        private Mesh viewMeshInstance;

        private readonly List<Vector3> vertices = new();
        private readonly List<int> triangles = new();

        private void Awake()
        {
            InitializeMeshes();
        }

        /// <summary>
        /// 하위 MeshFilter에 런타임 Mesh 인스턴스를 연결하고 최초 Mesh를 생성한다.
        /// </summary>
        private void InitializeMeshes()
        {
            if (cylinderMesh == null || viewMesh == null)
            {
                Debug.LogError("[FOVMesh] MeshFilter 참조가 비어 있습니다.", this);
                enabled = false;
                return;
            }

            cylinderMeshInstance = new Mesh { name = "FOV Cylinder Mesh" };
            viewMeshInstance = new Mesh { name = "FOV View Mesh" };

            cylinderMesh.sharedMesh = cylinderMeshInstance;
            viewMesh.sharedMesh = viewMeshInstance;

            RebuildMeshes();
        }

        /// <summary>
        /// 현재 시야 설정값을 기준으로 상시 시야와 지향 시야 Mesh를 다시 생성한다.
        /// </summary>
        public void RebuildMeshes()
        {
            GenerateCylinderMesh();
            GenerateViewMesh();
        }

        /// <summary>
        /// 외부 시스템에서 시야 스탯이 변경되었을 때 값을 갱신하고 Mesh를 다시 생성한다.
        /// </summary>
        public void SetVisionShape(
            float viewDistance,
            float viewAngle,
            float minimumRange,
            float topHeight,
            float bottomDepth)
        {
            this.viewDistance = Mathf.Max(0f, viewDistance);
            this.viewAngle = Mathf.Clamp(viewAngle, 1f, 360f);
            this.minimumRange = Mathf.Max(0f, minimumRange);
            this.topHeight = Mathf.Max(0f, topHeight);
            this.bottomDepth = Mathf.Max(0f, bottomDepth);

            RebuildMeshes();
        }

        /// <summary>
        /// 플레이어 주변의 상시 시야 영역을 원기둥 형태로 생성한다.
        /// Stencil용 Mesh는 Cull Off 머티리얼을 사용하므로 와인딩이 일부 뒤집혀도 기록 자체는 유지된다.
        /// </summary>
        private void GenerateCylinderMesh()
        {
            vertices.Clear();
            triangles.Clear();

            for (int i = 0; i <= segments; i++)
            {
                float angle = 360f / segments * i * Mathf.Deg2Rad;
                Vector3 dir = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                vertices.Add(dir * minimumRange - Vector3.up * bottomDepth);
                vertices.Add(dir * minimumRange + Vector3.up * topHeight);
            }

            int bottomCenter = vertices.Count;
            vertices.Add(Vector3.down * bottomDepth);

            int topCenter = vertices.Count;
            vertices.Add(Vector3.up * topHeight);

            for (int i = 0; i < segments; i++)
            {
                int currentBottom = i * 2;
                int currentTop = currentBottom + 1;
                int nextBottom = (i + 1) * 2;
                int nextTop = nextBottom + 1;

                AddQuad(currentBottom, nextBottom, nextTop, currentTop);
                AddTriangle(bottomCenter, nextBottom, currentBottom);
                AddTriangle(topCenter, currentTop, nextTop);
            }

            ApplyMesh(cylinderMeshInstance);
        }

        /// <summary>
        /// 플레이어 전방의 지향 시야 영역을 부채꼴 기둥 형태로 생성한다.
        /// RT 마스크에 빈틈이 생기지 않도록 플레이어 중심부터 viewDistance까지 채워지는 형태로 만든다.
        /// </summary>
        private void GenerateViewMesh()
        {
            vertices.Clear();
            triangles.Clear();

            float angleStep = viewAngle / segments;
            float startAngle = -viewAngle * 0.5f;
            float outerRadius = Mathf.Max(0f, viewDistance);

            int bottomCenter = vertices.Count;
            vertices.Add(Vector3.down * bottomDepth);

            int topCenter = vertices.Count;
            vertices.Add(Vector3.up * topHeight);

            for (int i = 0; i <= segments; i++)
            {
                float angle = (startAngle + angleStep * i) * Mathf.Deg2Rad;
                Vector3 dir = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                vertices.Add(dir * outerRadius - Vector3.up * bottomDepth);
                vertices.Add(dir * outerRadius + Vector3.up * topHeight);
            }

            for (int i = 0; i < segments; i++)
            {
                int currentBottom = 2 + i * 2;
                int currentTop = currentBottom + 1;
                int nextBottom = 2 + (i + 1) * 2;
                int nextTop = nextBottom + 1;

                AddQuad(currentBottom, nextBottom, nextTop, currentTop);
                AddTriangle(bottomCenter, currentBottom, nextBottom);
                AddTriangle(topCenter, nextTop, currentTop);
            }

            int firstBottom = 2;
            int firstTop = firstBottom + 1;
            int lastBottom = 2 + segments * 2;
            int lastTop = lastBottom + 1;

            AddQuad(bottomCenter, firstBottom, firstTop, topCenter);
            AddQuad(lastBottom, bottomCenter, topCenter, lastTop);

            ApplyMesh(viewMeshInstance);
        }

        /// <summary>
        /// 전달받은 네 꼭짓점을 순서대로 연결해 사각형 면을 만든다.
        /// Stencil 머티리얼은 Cull Off를 사용하지만, 디버깅 시 면을 확인하기 쉽도록 일관된 삼각형 순서를 유지한다.
        /// </summary>
        private void AddQuad(int a, int b, int c, int d)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);

            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        private void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        /// <summary>
        /// 생성된 정점과 삼각형 목록을 Mesh에 반영한다.
        /// Stencil 기록에는 노멀이 필요 없지만, Scene 뷰에서 일반 머티리얼로 확인할 때를 위해 노멀도 재계산한다.
        /// </summary>
        private void ApplyMesh(Mesh targetMesh)
        {
            targetMesh.Clear();
            targetMesh.SetVertices(vertices);
            targetMesh.SetTriangles(triangles, 0);
            targetMesh.RecalculateBounds();
            targetMesh.RecalculateNormals();
        }
    }
}
