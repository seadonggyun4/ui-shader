// Assets/Scripts/Studio/StudioEnvironment.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 5 — 프로덕션 품질 스튜디오 환경
// ══════════════════════════════════════════════════════════════════════
//
// CineShader 참조: 어두운 스튜디오에서 스크린이 주요 광원으로 작동.
// 반사성 바닥, 광 흡수 배경막, 쇼케이스 오브젝트로 구성된다.
//
// 주요 구성:
//   - Floor: 고광택 반사성 PBR 표면 (자동차 전시장 효과)
//   - Backdrop: 사이클로라마 (원호형 배경막, 광 흡수)
//   - Showcase Objects: 스크린 조명을 받아 반사/산란하는 오브젝트
//   - Fill Light: 최소한의 보조 환경광
//
// 확장:
//   - ShowcaseObjectConfig 리스트를 인스펙터에서 편집하여 오브젝트 추가/제거
//   - MaterialPreset을 확장하여 새 머티리얼 프리셋 추가 가능
//   - 바닥/배경막 파라미터는 인스펙터에서 실시간 조정 가능

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

public class StudioEnvironment : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 설정
    // ═══════════════════════════════════════════════════

    [Header("Configuration")]
    [Tooltip("UIShaderConfig (스크린 위치 참조용, 선택적)")]
    [SerializeField] private UIShaderConfig config;

    // ═══════════════════════════════════════════════════
    // 바닥
    // ═══════════════════════════════════════════════════

    [Header("Floor")]
    [Tooltip("바닥 크기 (한 변의 길이, 유닛)")]
    [SerializeField] private float floorSize = 30f;

    [Tooltip("바닥 색상 (거의 검정 → 반사 극대화)")]
    [SerializeField] private Color floorColor = new Color(0.04f, 0.04f, 0.04f, 1f);

    [Tooltip("바닥 스무스니스 (높을수록 선명한 반사)")]
    [SerializeField, Range(0f, 1f)] private float floorSmoothness = 0.88f;

    [Tooltip("클리어 코트 강도 (자동차 전시장 효과)")]
    [SerializeField, Range(0f, 1f)] private float floorClearCoatMask = 0.95f;

    // ═══════════════════════════════════════════════════
    // 배경막 (사이클로라마)
    // ═══════════════════════════════════════════════════

    [Header("Backdrop (Cyclorama)")]
    [Tooltip("배경막 높이")]
    [SerializeField] private float backdropHeight = 12f;

    [Tooltip("배경막 반경 (스크린 뒤쪽 원호)")]
    [SerializeField] private float backdropRadius = 15f;

    [Tooltip("원호 각도 (180° = 반원)")]
    [SerializeField, Range(90f, 300f)] private float backdropArcAngle = 200f;

    [Tooltip("원호 세그먼트 수")]
    [SerializeField, Range(12, 64)] private int backdropSegments = 32;

    [Tooltip("바닥→벽 곡면 전환(코브) 반경 (0이면 직각)")]
    [SerializeField, Range(0f, 5f)] private float coveRadius = 2f;

    [Tooltip("코브 세그먼트 수")]
    [SerializeField, Range(2, 16)] private int coveSegments = 8;

    [Tooltip("배경막 색상 (빛 흡수용 어두운 무광)")]
    [SerializeField] private Color backdropColor = new Color(0.1f, 0.1f, 0.1f, 1f);

    [Tooltip("배경막 스무스니스 (낮을수록 무광)")]
    [SerializeField, Range(0f, 1f)] private float backdropSmoothness = 0.05f;

    // ═══════════════════════════════════════════════════
    // 쇼케이스 오브젝트
    // ═══════════════════════════════════════════════════

    [Header("Showcase Objects")]
    [SerializeField] private List<ShowcaseObjectConfig> showcaseObjects = new List<ShowcaseObjectConfig>
    {
        new ShowcaseObjectConfig
        {
            name = "Chrome Sphere",
            type = ShowcaseType.Sphere,
            position = new Vector3(3f, 0.5f, 1f),
            scale = Vector3.one,
            materialPreset = MaterialPreset.ChromeMirror
        },
        new ShowcaseObjectConfig
        {
            name = "Vehicle",
            type = ShowcaseType.Cube,
            position = new Vector3(0f, 0.6f, 2f),
            scale = new Vector3(2f, 1.2f, 4f),
            materialPreset = MaterialPreset.DarkVehicle
        },
        new ShowcaseObjectConfig
        {
            name = "Character",
            type = ShowcaseType.Capsule,
            position = new Vector3(-3f, 0.9f, 1f),
            scale = new Vector3(0.5f, 0.9f, 0.5f),
            materialPreset = MaterialPreset.HumanSkin
        }
    };

    // ═══════════════════════════════════════════════════
    // 보조 환경광
    // ═══════════════════════════════════════════════════

    [Header("Fill Light")]
    [Tooltip("환경 앰비언트 강도 (최소화하여 스크린 조명 부각)")]
    [SerializeField, Range(0f, 0.2f)] private float ambientIntensity = 0.05f;

    [Tooltip("보조 필 라이트 색상 (차가운 청백색)")]
    [SerializeField] private Color fillLightColor = new Color(0.82f, 0.85f, 0.88f, 1f);

    [Tooltip("보조 필 라이트 강도 (럭스, 매우 약하게)")]
    [SerializeField] private float fillLightIntensity = 50f;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private GameObject studioRoot;
    private readonly List<GameObject> generatedObjects = new List<GameObject>();
    private readonly List<Material> generatedMaterials = new List<Material>();

    // ═══════════════════════════════════════════════════
    // 타입 정의
    // ═══════════════════════════════════════════════════

    [System.Serializable]
    public class ShowcaseObjectConfig
    {
        public string name = "Object";
        public ShowcaseType type = ShowcaseType.Sphere;
        public Vector3 position = Vector3.zero;
        public Vector3 rotation = Vector3.zero;
        public Vector3 scale = Vector3.one;
        public MaterialPreset materialPreset = MaterialPreset.Custom;

        [Header("Custom Material (materialPreset = Custom 일 때)")]
        public Color baseColor = Color.white;
        [Range(0f, 1f)] public float metallic = 0f;
        [Range(0f, 1f)] public float smoothness = 0.5f;
    }

    public enum ShowcaseType { Sphere, Cube, Capsule, Cylinder }

    public enum MaterialPreset
    {
        Custom,
        ChromeMirror,
        DarkVehicle,
        HumanSkin,
        GlossyPlastic,
        MatteWhite
    }

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        BuildStudio();
    }

    void OnDestroy()
    {
        CleanupStudio();
    }

    // ═══════════════════════════════════════════════════
    // 공개 API
    // ═══════════════════════════════════════════════════

    /// <summary>스튜디오 환경을 빌드한다. 기존 환경이 있으면 먼저 제거한다.</summary>
    public void BuildStudio()
    {
        CleanupStudio();

        studioRoot = new GameObject("StudioEnvironment");
        studioRoot.transform.SetParent(transform);

        CreateFloor();
        CreateBackdrop();
        CreateShowcaseObjects();
        SetupFillLight();
        SetupAmbient();

        Debug.Log($"[UIShader] 스튜디오 환경 빌드 완료: " +
                  $"floor={floorSize}m, backdrop h={backdropHeight}m r={backdropRadius}m, " +
                  $"showcase={showcaseObjects.Count}개");
    }

    /// <summary>스튜디오 환경을 제거하고 리소스를 해제한다.</summary>
    public void CleanupStudio()
    {
        foreach (var obj in generatedObjects)
        {
            if (obj != null)
                SafeDestroy(obj);
        }
        generatedObjects.Clear();

        foreach (var mat in generatedMaterials)
        {
            if (mat != null)
                SafeDestroy(mat);
        }
        generatedMaterials.Clear();

        if (studioRoot != null)
            SafeDestroy(studioRoot);
    }

    // ═══════════════════════════════════════════════════
    // 바닥 생성
    // ═══════════════════════════════════════════════════

    private void CreateFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "StudioFloor";
        floor.transform.SetParent(studioRoot.transform);
        floor.transform.localPosition = Vector3.zero;
        floor.transform.localScale = new Vector3(floorSize / 10f, 1f, floorSize / 10f);

        // 콜라이더 제거 (시각 전용)
        RemoveCollider(floor);

        // HDRP/Lit 머티리얼 적용
        Material floorMat = CreateHDRPLitMaterial("M_StudioFloor",
            floorColor, 0f, floorSmoothness);
        floorMat.SetFloat("_CoatMask", floorClearCoatMask);

        floor.GetComponent<MeshRenderer>().material = floorMat;

        generatedObjects.Add(floor);
        generatedMaterials.Add(floorMat);
    }

    // ═══════════════════════════════════════════════════
    // 배경막 생성 (사이클로라마)
    // ═══════════════════════════════════════════════════

    private void CreateBackdrop()
    {
        Mesh backdropMesh = GenerateBackdropMesh();

        GameObject backdrop = new GameObject("StudioBackdrop");
        backdrop.transform.SetParent(studioRoot.transform);

        float centerZ = (config != null) ? config.screenPosition.z : 0f;
        backdrop.transform.localPosition = new Vector3(0f, 0f, centerZ);

        MeshFilter mf = backdrop.AddComponent<MeshFilter>();
        mf.mesh = backdropMesh;

        MeshRenderer mr = backdrop.AddComponent<MeshRenderer>();
        Material backdropMat = CreateHDRPLitMaterial("M_StudioBackdrop",
            backdropColor, 0f, backdropSmoothness);
        mr.material = backdropMat;

        generatedObjects.Add(backdrop);
        generatedMaterials.Add(backdropMat);
    }

    private Mesh GenerateBackdropMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "BackdropCyclorama";

        bool hasCove = coveRadius > 0.01f && coveSegments > 0;
        int coveRows = hasCove ? coveSegments + 1 : 0;
        int totalRows = coveRows + 2; // cove + wall bottom + wall top
        int cols = backdropSegments + 1;
        int vertexCount = cols * totalRows;

        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];

        float halfArc = backdropArcAngle * 0.5f;

        for (int i = 0; i < cols; i++)
        {
            float arcT = (float)i / backdropSegments;
            float theta = (-halfArc + arcT * backdropArcAngle) * Mathf.Deg2Rad;

            // 벽 위치 (원호 외곽)
            float wallX = backdropRadius * Mathf.Sin(theta);
            float wallZ = -backdropRadius * Mathf.Cos(theta);

            // 내향 법선 (원호 안쪽)
            float inX = -Mathf.Sin(theta);
            float inZ = Mathf.Cos(theta);

            int row = 0;

            // ── 코브 구간 (바닥 → 벽 하단 곡면 전환) ──
            if (hasCove)
            {
                for (int j = 0; j <= coveSegments; j++)
                {
                    float phi = (1f - (float)j / coveSegments) * Mathf.PI * 0.5f;

                    float y = coveRadius * (1f - Mathf.Sin(phi));
                    float effRadius = backdropRadius - coveRadius + coveRadius * Mathf.Cos(phi);

                    float vx = effRadius * Mathf.Sin(theta);
                    float vz = -effRadius * Mathf.Cos(theta);

                    // 법선: 수직 ↔ 수평 보간
                    Vector3 n = new Vector3(
                        Mathf.Cos(phi) * inX,
                        Mathf.Sin(phi),
                        Mathf.Cos(phi) * inZ
                    ).normalized;

                    int idx = row * cols + i;
                    vertices[idx] = new Vector3(vx, y, vz);
                    normals[idx] = n;
                    uvs[idx] = new Vector2(arcT, y / backdropHeight);
                    row++;
                }
            }

            // ── 벽 하단 (코브 끝 또는 바닥) ──
            {
                float wallBaseY = hasCove ? coveRadius : 0f;
                int idx = row * cols + i;
                vertices[idx] = new Vector3(wallX, wallBaseY, wallZ);
                normals[idx] = new Vector3(inX, 0f, inZ);
                uvs[idx] = new Vector2(arcT, wallBaseY / backdropHeight);
                row++;
            }

            // ── 벽 상단 ──
            {
                int idx = row * cols + i;
                vertices[idx] = new Vector3(wallX, backdropHeight, wallZ);
                normals[idx] = new Vector3(inX, 0f, inZ);
                uvs[idx] = new Vector2(arcT, 1f);
            }
        }

        // 삼각형 인덱스
        int quadCount = backdropSegments * (totalRows - 1);
        int[] triangles = new int[quadCount * 6];
        int ti = 0;

        for (int row = 0; row < totalRows - 1; row++)
        {
            for (int col = 0; col < backdropSegments; col++)
            {
                int bl = row * cols + col;
                int br = row * cols + col + 1;
                int tl = (row + 1) * cols + col;
                int tr = (row + 1) * cols + col + 1;

                // 내향면 (카메라 쪽) 렌더링
                triangles[ti++] = bl;
                triangles[ti++] = tl;
                triangles[ti++] = tr;

                triangles[ti++] = bl;
                triangles[ti++] = tr;
                triangles[ti++] = br;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    // ═══════════════════════════════════════════════════
    // 쇼케이스 오브젝트 생성
    // ═══════════════════════════════════════════════════

    private void CreateShowcaseObjects()
    {
        foreach (var objConfig in showcaseObjects)
        {
            PrimitiveType primitiveType = objConfig.type switch
            {
                ShowcaseType.Sphere => PrimitiveType.Sphere,
                ShowcaseType.Cube => PrimitiveType.Cube,
                ShowcaseType.Capsule => PrimitiveType.Capsule,
                ShowcaseType.Cylinder => PrimitiveType.Cylinder,
                _ => PrimitiveType.Sphere
            };

            GameObject obj = GameObject.CreatePrimitive(primitiveType);
            obj.name = $"Showcase_{objConfig.name}";
            obj.transform.SetParent(studioRoot.transform);
            obj.transform.localPosition = objConfig.position;
            obj.transform.localRotation = Quaternion.Euler(objConfig.rotation);
            obj.transform.localScale = objConfig.scale;

            RemoveCollider(obj);

            Material mat = CreateShowcaseMaterial(objConfig);
            obj.GetComponent<MeshRenderer>().material = mat;

            generatedObjects.Add(obj);
            generatedMaterials.Add(mat);
        }
    }

    private Material CreateShowcaseMaterial(ShowcaseObjectConfig objConfig)
    {
        switch (objConfig.materialPreset)
        {
            case MaterialPreset.ChromeMirror:
                return CreateHDRPLitMaterial($"M_{objConfig.name}",
                    Color.white, 1f, 0.99f);

            case MaterialPreset.DarkVehicle:
                var vehicleMat = CreateHDRPLitMaterial($"M_{objConfig.name}",
                    new Color(0.11f, 0.11f, 0.11f), 0.95f, 0.97f);
                vehicleMat.SetFloat("_CoatMask", 1f);
                return vehicleMat;

            case MaterialPreset.HumanSkin:
                return CreateHDRPLitMaterial($"M_{objConfig.name}",
                    new Color(0.78f, 0.72f, 0.66f), 0f, 0.35f);

            case MaterialPreset.GlossyPlastic:
                return CreateHDRPLitMaterial($"M_{objConfig.name}",
                    new Color(0.8f, 0.1f, 0.1f), 0f, 0.85f);

            case MaterialPreset.MatteWhite:
                return CreateHDRPLitMaterial($"M_{objConfig.name}",
                    new Color(0.9f, 0.9f, 0.9f), 0f, 0.15f);

            default: // Custom
                return CreateHDRPLitMaterial($"M_{objConfig.name}",
                    objConfig.baseColor, objConfig.metallic, objConfig.smoothness);
        }
    }

    // ═══════════════════════════════════════════════════
    // 조명
    // ═══════════════════════════════════════════════════

    private void SetupFillLight()
    {
        GameObject lightObj = new GameObject("StudioFillLight");
        lightObj.transform.SetParent(studioRoot.transform);
        lightObj.transform.localPosition = new Vector3(0f, 8f, 5f);
        lightObj.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);

        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = fillLightColor;
        light.shadows = LightShadows.Soft;

        // HDRP 설정
        var hdLight = lightObj.AddComponent<HDAdditionalLightData>();
        hdLight.SetIntensity(fillLightIntensity, LightUnit.Lux);
        hdLight.SetShadowResolution(512);

        generatedObjects.Add(lightObj);
    }

    private void SetupAmbient()
    {
        RenderSettings.ambientIntensity = ambientIntensity;
    }

    // ═══════════════════════════════════════════════════
    // 머티리얼 팩토리
    // ═══════════════════════════════════════════════════

    /// <summary>HDRP/Lit 머티리얼을 생성한다.</summary>
    public static Material CreateHDRPLitMaterial(string name, Color baseColor,
        float metallic, float smoothness)
    {
        Shader shader = Shader.Find("HDRP/Lit");
        if (shader == null)
        {
            Debug.LogWarning($"[UIShader] HDRP/Lit 셰이더를 찾을 수 없습니다. Standard 사용.");
            shader = Shader.Find("Standard");
        }

        Material mat = new Material(shader);
        mat.name = name;
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);

        return mat;
    }

    // ═══════════════════════════════════════════════════
    // 유틸리티
    // ═══════════════════════════════════════════════════

    private static void RemoveCollider(GameObject obj)
    {
        Collider col = obj.GetComponent<Collider>();
        if (col != null) SafeDestroy(col);
    }

    private static void SafeDestroy(Object obj)
    {
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
