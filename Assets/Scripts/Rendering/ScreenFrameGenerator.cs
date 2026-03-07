// Assets/Scripts/Rendering/ScreenFrameGenerator.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — 스크린 테두리 프레임 생성기
// ══════════════════════════════════════════════════════════════════════
//
// CineShader의 stage_screen_edges.buf에 대응하는 스크린 테두리 프레임.
// 4개의 직육면체(상/하/좌/우)로 구성된 단일 메시를 절차적으로 생성한다.
// 어두운 금속 머티리얼을 사용하여 스크린 경계를 시각적으로 마감한다.

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ScreenFrameGenerator : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 인스펙터
    // ═══════════════════════════════════════════════════

    [SerializeField] private UIShaderConfig config;

    [Header("Frame")]
    [Tooltip("테두리 두께 (월드 유닛)")]
    [SerializeField] private float frameWidth = 0.1f;

    [Tooltip("테두리 돌출 깊이 (Z축)")]
    [SerializeField] private float frameDepth = 0.05f;

    [Tooltip("테두리 머티리얼 (미지정 시 기본 HDRP Lit 사용)")]
    [SerializeField] private Material frameMaterial;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private Mesh frameMesh;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        GenerateFrame();
    }

    void OnDestroy()
    {
        if (frameMesh != null)
            DestroyImmediate(frameMesh);
    }

    // ═══════════════════════════════════════════════════
    // 프레임 생성
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 스크린 크기에 맞는 4변 테두리 프레임 메시를 생성한다.
    /// </summary>
    public void GenerateFrame()
    {
        float size = config != null ? config.screenWorldSize : 6f;
        float half = size * 0.5f;
        float fw = frameWidth;
        float fd = frameDepth;

        if (frameMesh != null)
            DestroyImmediate(frameMesh);

        frameMesh = new Mesh();
        frameMesh.name = "ScreenFrame";

        var verts = new List<Vector3>();
        var tris = new List<int>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        // 상단 막대
        AddBoxBar(verts, tris, normals, uvs,
            new Vector3(-half - fw, half, -fd),
            new Vector3(half + fw, half + fw, fd));

        // 하단 막대
        AddBoxBar(verts, tris, normals, uvs,
            new Vector3(-half - fw, -half - fw, -fd),
            new Vector3(half + fw, -half, fd));

        // 좌측 막대
        AddBoxBar(verts, tris, normals, uvs,
            new Vector3(-half - fw, -half, -fd),
            new Vector3(-half, half, fd));

        // 우측 막대
        AddBoxBar(verts, tris, normals, uvs,
            new Vector3(half, -half, -fd),
            new Vector3(half + fw, half, fd));

        frameMesh.SetVertices(verts);
        frameMesh.SetTriangles(tris, 0);
        frameMesh.SetNormals(normals);
        frameMesh.SetUVs(0, uvs);
        frameMesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = frameMesh;

        if (frameMaterial != null)
            GetComponent<MeshRenderer>().material = frameMaterial;

        Debug.Log($"[UIShader] 스크린 프레임 생성: {verts.Count} verts, " +
                  $"두께={fw}, 깊이={fd}");
    }

    // ═══════════════════════════════════════════════════
    // 직육면체 헬퍼
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// AABB min~max 범위의 직육면체를 메시 데이터에 추가한다.
    /// 각 면에 올바른 법선을 생성한다 (면당 4정점, 스무스 셰이딩 대신 플랫 셰이딩).
    /// </summary>
    private void AddBoxBar(
        List<Vector3> verts,
        List<int> tris,
        List<Vector3> normals,
        List<Vector2> uvs,
        Vector3 min, Vector3 max)
    {
        // 6면 x 4정점 = 24정점 (플랫 셰이딩용 분리 정점)
        // 6면 x 2삼각형 = 12삼각형 x 3인덱스 = 36

        // 8개 코너 정점 (임시)
        Vector3 v0 = new Vector3(min.x, min.y, min.z);
        Vector3 v1 = new Vector3(max.x, min.y, min.z);
        Vector3 v2 = new Vector3(max.x, max.y, min.z);
        Vector3 v3 = new Vector3(min.x, max.y, min.z);
        Vector3 v4 = new Vector3(min.x, min.y, max.z);
        Vector3 v5 = new Vector3(max.x, min.y, max.z);
        Vector3 v6 = new Vector3(max.x, max.y, max.z);
        Vector3 v7 = new Vector3(min.x, max.y, max.z);

        // 면 정의: (4정점, 법선)
        AddFace(verts, tris, normals, uvs, v3, v2, v1, v0, Vector3.back);    // 뒤
        AddFace(verts, tris, normals, uvs, v4, v5, v6, v7, Vector3.forward); // 앞
        AddFace(verts, tris, normals, uvs, v0, v1, v5, v4, Vector3.down);    // 아래
        AddFace(verts, tris, normals, uvs, v7, v6, v2, v3, Vector3.up);      // 위
        AddFace(verts, tris, normals, uvs, v0, v4, v7, v3, Vector3.left);    // 좌
        AddFace(verts, tris, normals, uvs, v5, v1, v2, v6, Vector3.right);   // 우
    }

    private void AddFace(
        List<Vector3> verts,
        List<int> tris,
        List<Vector3> normals,
        List<Vector2> uvs,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector3 normal)
    {
        int baseIdx = verts.Count;

        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
        uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));

        tris.Add(baseIdx);     tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
        tris.Add(baseIdx);     tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
    }
}
