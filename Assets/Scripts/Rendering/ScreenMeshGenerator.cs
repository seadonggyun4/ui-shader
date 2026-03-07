// Assets/Scripts/Rendering/ScreenMeshGenerator.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — Phase 3 고분할 평면 메시 생성기 (LOD 시스템)
// ══════════════════════════════════════════════════════════════════════
//
// 3단계 LOD 메시를 사전 생성하여 카메라 거리에 따라 즉시 전환한다.
//   High:   511x511 세그먼트 (262,144 정점) — 근거리, 최대 디테일
//   Medium: 255x255 세그먼트 ( 65,536 정점) — 중거리
//   Low:     63x63 세그먼트  (  4,096 정점) — 원거리, 최소 비용
//
// 메시 토폴로지: XY 평면 (Z=0), 법선 +Z, UV (0,0)~(1,1)
// 32-bit 인덱스를 사용하여 65,535 정점 한계를 초과하는 High LOD를 지원한다.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ScreenMeshGenerator : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // LOD 정의
    // ═══════════════════════════════════════════════════

    public enum LODLevel
    {
        High   = 511,   // 512x512 정점, 524,288 삼각형
        Medium = 255,   // 256x256 정점, 130,050 삼각형
        Low    = 63     //  64x64 정점,   7,938 삼각형
    }

    // ═══════════════════════════════════════════════════
    // 인스펙터
    // ═══════════════════════════════════════════════════

    [SerializeField] private UIShaderConfig config;

    [Header("LOD")]
    [SerializeField] private LODLevel initialLOD = LODLevel.High;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private Dictionary<LODLevel, Mesh> lodMeshPool;
    private LODLevel activeLOD;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;

    // ═══════════════════════════════════════════════════
    // 공개 프로퍼티
    // ═══════════════════════════════════════════════════

    /// <summary>현재 활성 LOD 레벨</summary>
    public LODLevel CurrentLOD => activeLOD;

    /// <summary>현재 활성 메시 (읽기 전용)</summary>
    public Mesh CurrentMesh => meshFilter != null ? meshFilter.sharedMesh : null;

    /// <summary>현재 세그먼트 수</summary>
    public int CurrentSegments => (int)activeLOD;

    // ═══════════════════════════════════════════════════
    // 이벤트
    // ═══════════════════════════════════════════════════

    /// <summary>LOD가 전환될 때 발생 (이전 LOD, 새 LOD)</summary>
    public event Action<LODLevel, LODLevel> OnLODChanged;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
    }

    void Start()
    {
        GenerateAllLODs();
        SetLOD(initialLOD);
    }

    void OnDestroy()
    {
        if (lodMeshPool != null)
        {
            foreach (var mesh in lodMeshPool.Values)
            {
                if (mesh != null)
                    DestroyImmediate(mesh);
            }
            lodMeshPool.Clear();
        }
    }

    // ═══════════════════════════════════════════════════
    // LOD 관리
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// LOD를 전환한다. 사전 생성된 메시를 즉시 할당하므로 프레임 히치가 없다.
    /// </summary>
    public void SetLOD(LODLevel level)
    {
        if (lodMeshPool == null || !lodMeshPool.ContainsKey(level))
        {
            Debug.LogWarning($"[UIShader] LOD {level} 메시가 준비되지 않음. GenerateAllLODs()를 먼저 호출하세요.");
            return;
        }

        LODLevel previousLOD = activeLOD;
        activeLOD = level;

        Mesh targetMesh = lodMeshPool[level];
        meshFilter.sharedMesh = targetMesh;

        // MeshCollider 동기화 (입력 레이캐스트용)
        if (meshCollider != null)
            meshCollider.sharedMesh = targetMesh;

        if (previousLOD != level)
        {
            OnLODChanged?.Invoke(previousLOD, level);
            Debug.Log($"[UIShader] LOD 전환: {previousLOD} → {level} " +
                      $"({(int)level}x{(int)level} seg, {targetMesh.vertexCount:N0} verts)");
        }
    }

    /// <summary>
    /// 지정된 LOD의 메시를 반환한다.
    /// </summary>
    public Mesh GetMeshForLOD(LODLevel level)
    {
        if (lodMeshPool != null && lodMeshPool.TryGetValue(level, out Mesh mesh))
            return mesh;
        return null;
    }

    // ═══════════════════════════════════════════════════
    // 메시 생성
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 모든 LOD 레벨의 메시를 사전 생성한다.
    /// </summary>
    private void GenerateAllLODs()
    {
        float size = config != null ? config.screenWorldSize : 6f;

        lodMeshPool = new Dictionary<LODLevel, Mesh>();

        foreach (LODLevel level in Enum.GetValues(typeof(LODLevel)))
        {
            lodMeshPool[level] = CreateSubdividedPlane((int)level, size, level.ToString());
        }

        Debug.Log($"[UIShader] LOD 메시 풀 생성 완료: {lodMeshPool.Count}개 레벨");
    }

    /// <summary>
    /// 지정 세그먼트 수로 분할된 평면 메시를 절차적 생성한다.
    /// </summary>
    private Mesh CreateSubdividedPlane(int segments, float size, string label)
    {
        int vertsPerSide = segments + 1;
        int totalVerts = vertsPerSide * vertsPerSide;
        int totalTris = segments * segments * 2;

        Mesh mesh = new Mesh();
        mesh.name = $"ScreenMesh_{label}_{segments}x{segments}";

        // 262,144 정점은 16-bit 인덱스 한계(65,535)를 초과
        if (totalVerts > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        Vector3[] normals = new Vector3[totalVerts];
        Vector4[] tangents = new Vector4[totalVerts];

        float halfSize = size * 0.5f;
        float step = size / segments;

        // ─── 정점 생성 ───
        // XY 평면에 배치, 법선은 +Z 방향 (카메라를 향함)
        // UV: 좌하단 (0,0) ~ 우상단 (1,1)
        for (int y = 0; y < vertsPerSide; y++)
        {
            for (int x = 0; x < vertsPerSide; x++)
            {
                int idx = y * vertsPerSide + x;

                vertices[idx] = new Vector3(
                    -halfSize + x * step,
                     halfSize - y * step,
                     0f
                );

                uvs[idx] = new Vector2(
                    (float)x / segments,
                    1f - (float)y / segments
                );

                normals[idx] = Vector3.forward;

                // 접선 = +X, bitangent sign = -1 (오른손 좌표계)
                tangents[idx] = new Vector4(1f, 0f, 0f, -1f);
            }
        }

        // ─── 삼각형 인덱스 ───
        int[] triangles = new int[totalTris * 3];
        int triIdx = 0;
        for (int y = 0; y < segments; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                int tl = y * vertsPerSide + x;
                int tr = tl + 1;
                int bl = (y + 1) * vertsPerSide + x;
                int br = bl + 1;

                // 삼각형 1: 좌상 → 좌하 → 우상
                triangles[triIdx++] = tl;
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tr;

                // 삼각형 2: 우상 → 좌하 → 우하
                triangles[triIdx++] = tr;
                triangles[triIdx++] = bl;
                triangles[triIdx++] = br;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        Debug.Log($"[UIShader] 메시 생성: {label} — {segments}x{segments} seg, " +
                  $"{totalVerts:N0} verts, {totalTris:N0} tris");

        return mesh;
    }

    // ═══════════════════════════════════════════════════
    // 하위 호환성 (Phase 0 API)
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 설정에 따라 스크린 메시를 (재)생성한다.
    /// Phase 0 호환성용. 새 코드는 SetLOD()를 사용할 것.
    /// </summary>
    public void GenerateScreenMesh()
    {
        if (lodMeshPool == null)
            GenerateAllLODs();
        SetLOD(initialLOD);
    }

    /// <summary>
    /// MeshCollider를 추가하고 현재 메시를 할당한다.
    /// Phase 1의 CEFInputForwarder가 레이캐스트에 사용한다.
    /// </summary>
    public MeshCollider EnsureMeshCollider()
    {
        if (meshCollider == null)
            meshCollider = gameObject.AddComponent<MeshCollider>();

        if (meshFilter.sharedMesh != null)
            meshCollider.sharedMesh = meshFilter.sharedMesh;

        return meshCollider;
    }
}
