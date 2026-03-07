// Assets/Scripts/Rendering/QuadrantLightSystem.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — 2×2 사분면 다중 영역광 시스템
// ══════════════════════════════════════════════════════════════════════
//
// 웹 페이지를 4개 사분면으로 분할하여 각 사분면이 별도의 영역광을
// 구동함으로써, 공간적으로 정확한 유색 조명 투사를 실현한다.
//
// 예: 좌측 사이드바(파랑) → 좌측에 파란 빛 집중
//     우측 콘텐츠(흰색) → 우측에 흰 빛 집중
//
// ScreenLightController의 ProcessedCookieTexture를 입력으로 받아
// GPU Blit (UV scale/offset)으로 사분면을 크롭한다.
//
// 동작 모드:
//   비활성(기본) — 메인 단일 영역광만 사용
//   활성        — 4개 사분면 광원 사용, 메인 광원 자동 비활성화

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public class QuadrantLightSystem : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 인스펙터 참조
    // ═══════════════════════════════════════════════════

    [Header("Configuration")]
    [SerializeField] private UIShaderConfig config;

    // ═══════════════════════════════════════════════════
    // 상수
    // ═══════════════════════════════════════════════════

    private const int QUADRANT_COUNT = 4;

    // 사분면 UV 크롭 정의 (scale, offset)
    // Graphics.Blit(src, dst, scale, offset) 형태로 사용
    //   uv_output = uv_input * scale + offset
    private static readonly Vector2 QuadScale = new Vector2(0.5f, 0.5f);
    private static readonly Vector2[] QuadOffsets =
    {
        new Vector2(0.0f, 0.5f),  // 0: 좌상 (TL)
        new Vector2(0.5f, 0.5f),  // 1: 우상 (TR)
        new Vector2(0.0f, 0.0f),  // 2: 좌하 (BL)
        new Vector2(0.5f, 0.0f),  // 3: 우하 (BR)
    };

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private Light[] quadLights;
    private HDAdditionalLightData[] hdLightDataArray;
    private RenderTexture[] quadCookieRTs;
    private GameObject[] lightObjects;

    private bool isInitialized;
    private bool wasEnabled;

    // ═══════════════════════════════════════════════════
    // 공개 프로퍼티
    // ═══════════════════════════════════════════════════

    /// <summary>사분면 광원 활성 여부 (config 연동)</summary>
    public bool IsEnabled => config != null && config.enableQuadrantLights && isInitialized;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        if (config != null && config.enableQuadrantLights)
            Initialize();
    }

    void Update()
    {
        if (config == null) return;

        // 런타임 활성/비활성 토글 감지
        bool currentEnabled = config.enableQuadrantLights;
        if (currentEnabled != wasEnabled)
        {
            if (currentEnabled && !isInitialized)
                Initialize();

            SetLightsActive(currentEnabled);
            wasEnabled = currentEnabled;
        }
    }

    void OnDestroy()
    {
        Cleanup();
    }

    // ═══════════════════════════════════════════════════
    // 초기화 / 해제
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 4개의 사분면 영역광을 생성하고 구성한다.
    /// </summary>
    private void Initialize()
    {
        if (isInitialized) return;
        if (config == null)
        {
            Debug.LogError("[UIShader] QuadrantLightSystem: UIShaderConfig가 할당되지 않았습니다.");
            return;
        }

        float screenSize = config.screenWorldSize;
        float quadSize = screenSize * 0.5f;
        int quadResolution = config.screenResolution / 2;
        float spacing = config.quadrantLightSpacing;

        quadLights = new Light[QUADRANT_COUNT];
        hdLightDataArray = new HDAdditionalLightData[QUADRANT_COUNT];
        quadCookieRTs = new RenderTexture[QUADRANT_COUNT];
        lightObjects = new GameObject[QUADRANT_COUNT];

        // 사분면별 로컬 오프셋 (스크린 메시 중앙 기준)
        Vector3[] localPositions =
        {
            new Vector3(-quadSize * 0.5f,  quadSize * 0.5f, 0.01f), // 좌상
            new Vector3( quadSize * 0.5f,  quadSize * 0.5f, 0.01f), // 우상
            new Vector3(-quadSize * 0.5f, -quadSize * 0.5f, 0.01f), // 좌하
            new Vector3( quadSize * 0.5f, -quadSize * 0.5f, 0.01f), // 우하
        };

        string[] labels = { "TL", "TR", "BL", "BR" };

        for (int i = 0; i < QUADRANT_COUNT; i++)
        {
            // ─── GameObject ───
            GameObject obj = new GameObject($"QuadrantLight_{labels[i]}");
            obj.transform.SetParent(transform, false);
            obj.transform.localPosition = localPositions[i];
            obj.transform.localRotation = Quaternion.identity;
            lightObjects[i] = obj;

            // ─── Light ───
            Light light = obj.AddComponent<Light>();
            light.type = LightType.Area;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
            light.range = config.lightRange;

            // ─── HDRP 확장 데이터 ───
            HDAdditionalLightData hdData = obj.AddComponent<HDAdditionalLightData>();
            float effectiveSize = quadSize - spacing;
            hdData.SetAreaLightSize(new Vector2(effectiveSize, effectiveSize));
            hdData.lightUnit = LightUnit.Lumen;
            hdData.intensity = config.lightIntensity / QUADRANT_COUNT;
            hdData.shadowResolution.level = 0; // Low (4광원이므로 개별 그림자 해상도 절감)

            // ─── 쿠키 RenderTexture ───
            RenderTexture cookieRT = new RenderTexture(
                quadResolution, quadResolution, 0, RenderTextureFormat.ARGB32);
            cookieRT.useMipMap = true;
            cookieRT.autoGenerateMips = false;
            cookieRT.filterMode = FilterMode.Trilinear;
            cookieRT.wrapMode = TextureWrapMode.Clamp;
            cookieRT.name = $"UIShader_QuadCookie_{labels[i]}";
            cookieRT.Create();
            hdData.SetCookie(cookieRT);

            quadLights[i] = light;
            hdLightDataArray[i] = hdData;
            quadCookieRTs[i] = cookieRT;
        }

        isInitialized = true;
        wasEnabled = config.enableQuadrantLights;

        Debug.Log($"[UIShader] QuadrantLightSystem 초기화: " +
                  $"4×{quadResolution}x{quadResolution} 쿠키, " +
                  $"사분면 크기 {quadSize - spacing:F2} 유닛");
    }

    /// <summary>
    /// 사분면 광원 리소스를 해제한다.
    /// </summary>
    private void Cleanup()
    {
        if (!isInitialized) return;

        for (int i = 0; i < QUADRANT_COUNT; i++)
        {
            if (quadCookieRTs[i] != null)
            {
                quadCookieRTs[i].Release();
                Destroy(quadCookieRTs[i]);
            }
            if (lightObjects[i] != null)
                Destroy(lightObjects[i]);
        }

        quadLights = null;
        hdLightDataArray = null;
        quadCookieRTs = null;
        lightObjects = null;
        isInitialized = false;
    }

    // ═══════════════════════════════════════════════════
    // 쿠키 갱신 (외부 API)
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 후처리된 전체 쿠키 텍스처에서 사분면을 크롭하여 각 광원 쿠키를 갱신한다.
    /// GPU Blit (UV scale/offset) 기반이므로 CPU 부하가 없다.
    /// </summary>
    /// <param name="processedFullTexture">ScreenLightController.ProcessedCookieTexture</param>
    public void UpdateQuadrantCookies(RenderTexture processedFullTexture)
    {
        if (!isInitialized || processedFullTexture == null) return;

        for (int i = 0; i < QUADRANT_COUNT; i++)
        {
            // GPU Blit으로 사분면 크롭: source UV = uv * scale + offset
            Graphics.Blit(processedFullTexture, quadCookieRTs[i], QuadScale, QuadOffsets[i]);

            // 밉맵 생성 (거리 기반 블러링)
            quadCookieRTs[i].GenerateMips();

            // HDRP 쿠키 갱신
            hdLightDataArray[i].SetCookie(quadCookieRTs[i]);
        }
    }

    /// <summary>
    /// 자동 노출에 의한 총 강도를 4개 광원에 균등 분배한다.
    /// ScreenLightController.CurrentIntensity를 전달받아 적용한다.
    /// </summary>
    /// <param name="totalIntensity">자동 노출 적용 후 총 강도 (루멘)</param>
    public void SyncIntensity(float totalIntensity)
    {
        if (!isInitialized) return;

        float perQuadrant = totalIntensity / QUADRANT_COUNT;
        for (int i = 0; i < QUADRANT_COUNT; i++)
        {
            if (hdLightDataArray[i] != null)
                hdLightDataArray[i].intensity = perQuadrant;
        }
    }

    /// <summary>
    /// 사분면 광원의 활성/비활성을 전환한다.
    /// </summary>
    public void SetLightsActive(bool active)
    {
        if (!isInitialized) return;

        for (int i = 0; i < QUADRANT_COUNT; i++)
        {
            if (quadLights[i] != null)
                quadLights[i].enabled = active;
        }
    }

    /// <summary>
    /// config 변경을 반영한다 (크기, 간격, 범위).
    /// </summary>
    public void ApplyConfigChanges()
    {
        if (!isInitialized || config == null) return;

        float quadSize = config.screenWorldSize * 0.5f;
        float effectiveSize = quadSize - config.quadrantLightSpacing;

        for (int i = 0; i < QUADRANT_COUNT; i++)
        {
            if (hdLightDataArray[i] != null)
            {
                hdLightDataArray[i].SetAreaLightSize(new Vector2(effectiveSize, effectiveSize));
                quadLights[i].range = config.lightRange;
            }
        }
    }
}
