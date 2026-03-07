// Assets/Scripts/Rendering/ScreenLightController.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — Phase 4 HDRP RectAreaLight 컨트롤러
// ══════════════════════════════════════════════════════════════════════
//
// HDRP RectAreaLight를 관리하여 웹 페이지 색상 텍스처를 영역광 쿠키로
// 실시간 투사한다. GPU 기반 CookieProcessor를 활용하여 쿠키 후처리
// (채도, 콘트라스트, 강도) 및 자동 노출을 수행한다.
//
// 책임 범위:
//   - HDRP RectAreaLight 구성 (크기, 강도, 범위, 그림자)
//   - CookieProcessor를 통한 GPU 쿠키 후처리
//   - 자동 노출 (평균 휘도 기반 강도 자동 조절)
//   - 메인 광원 활성/비활성 (QuadrantLightSystem 연동)
//
// TexturePipelineManager → UpdateCookie() → CookieProcessor → HDRP Cookie

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[RequireComponent(typeof(Light))]
public class ScreenLightController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 인스펙터 참조
    // ═══════════════════════════════════════════════════

    [Header("Configuration")]
    [SerializeField] private UIShaderConfig config;

    [Header("Cookie Processing")]
    [Tooltip("CookieProcess.shader 참조 (빌드 시 셰이더 스트리핑 방지)")]
    [SerializeField] private Shader cookieProcessShader;

    // ═══════════════════════════════════════════════════
    // 내부 컴포넌트
    // ═══════════════════════════════════════════════════

    private Light areaLight;
    private HDAdditionalLightData hdLightData;
    private CookieProcessor cookieProcessor;

    // 자동 노출
    private float targetIntensity;
    private float smoothedIntensity;

    // 상태
    private bool isConfigured;
    private bool mainLightActive = true;

    // ═══════════════════════════════════════════════════
    // 공개 프로퍼티
    // ═══════════════════════════════════════════════════

    /// <summary>광원 구성 완료 여부</summary>
    public bool IsConfigured => isConfigured;

    /// <summary>후처리된 쿠키 RenderTexture (QuadrantLightSystem이 소비)</summary>
    public RenderTexture ProcessedCookieTexture =>
        cookieProcessor != null ? cookieProcessor.ProcessedTexture : null;

    /// <summary>현재 자동 노출 적용된 광원 강도 (루멘)</summary>
    public float CurrentIntensity => smoothedIntensity;

    /// <summary>현재 평균 휘도 (0~1)</summary>
    public float AverageLuminance =>
        cookieProcessor != null ? cookieProcessor.AverageLuminance : 0.5f;

    /// <summary>메인 광원 활성 상태</summary>
    public bool MainLightActive => mainLightActive;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Awake()
    {
        areaLight = GetComponent<Light>();
        hdLightData = GetComponent<HDAdditionalLightData>();
        if (hdLightData == null)
            hdLightData = gameObject.AddComponent<HDAdditionalLightData>();
    }

    void Start()
    {
        ConfigureAreaLight();
        InitializeCookieProcessor();
    }

    void Update()
    {
        if (!isConfigured || config == null) return;

        UpdateAutoExposure();
        SyncLightParameters();
    }

    void OnDestroy()
    {
        cookieProcessor?.Dispose();
        cookieProcessor = null;
    }

    // ═══════════════════════════════════════════════════
    // 초기화
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// RectAreaLight를 UIShaderConfig 사양에 맞게 구성한다.
    /// </summary>
    private void ConfigureAreaLight()
    {
        if (config == null)
        {
            Debug.LogError("[UIShader] ScreenLightController: UIShaderConfig가 할당되지 않았습니다.");
            return;
        }

        // ─── 광원 유형: Area (Rectangle) ───
        areaLight.type = LightType.Area;
        hdLightData.SetAreaLightSize(new Vector2(config.screenWorldSize, config.screenWorldSize));

        // ─── 강도 ───
        hdLightData.lightUnit = LightUnit.Lumen;
        hdLightData.intensity = config.lightIntensity;
        smoothedIntensity = config.lightIntensity;
        targetIntensity = config.lightIntensity;

        // ─── 색상: 흰색 (쿠키가 색상을 제어) ───
        areaLight.color = Color.white;

        // ─── 범위 ───
        areaLight.range = config.lightRange;

        // ─── 그림자 ───
        areaLight.shadows = LightShadows.Soft;
        hdLightData.shadowResolution.level = 1; // Medium

        isConfigured = true;

        Debug.Log($"[UIShader] RectAreaLight 구성 완료: " +
                  $"{config.screenWorldSize}x{config.screenWorldSize} 유닛, " +
                  $"{config.lightIntensity} 루멘, 범위 {config.lightRange}m");
    }

    /// <summary>
    /// GPU 기반 CookieProcessor를 초기화한다.
    /// </summary>
    private void InitializeCookieProcessor()
    {
        if (config == null) return;

        // 셰이더 자동 탐색 (인스펙터 미할당 시)
        if (cookieProcessShader == null)
            cookieProcessShader = Shader.Find("Hidden/UIShader/CookieProcess");

        if (cookieProcessShader == null)
        {
            Debug.LogError("[UIShader] ScreenLightController: CookieProcess 셰이더를 찾을 수 없습니다. " +
                          "인스펙터에서 직접 할당하거나 셰이더가 프로젝트에 포함되었는지 확인하세요.");
            return;
        }

        cookieProcessor = new CookieProcessor(config.screenResolution, cookieProcessShader);
        cookieProcessor.BakeContrastCurve(config.cookieContrastCurve);

        // 초기 쿠키 설정 (순백색 RT)
        hdLightData.SetCookie(cookieProcessor.ProcessedTexture);

        Debug.Log("[UIShader] CookieProcessor 초기화 완료");
    }

    // ═══════════════════════════════════════════════════
    // 쿠키 갱신 (외부 API)
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 매 프레임 호출: 웹 페이지 색상 텍스처를 GPU에서 후처리하여 쿠키를 갱신한다.
    /// TexturePipelineManager가 ITextureSource 이벤트 수신 시 호출.
    /// </summary>
    public void UpdateCookie(Texture2D sourceTexture)
    {
        if (sourceTexture == null || cookieProcessor == null || cookieProcessor.IsDisposed) return;

        // GPU Blit 기반 후처리 (채도 + 콘트라스트 LUT + 강도)
        cookieProcessor.Process(
            sourceTexture,
            config.cookieSaturationBoost,
            config.cookieIntensityMultiplier,
            config.cookieContrastCurve
        );

        // HDRP 쿠키 갱신
        hdLightData.SetCookie(cookieProcessor.ProcessedTexture);
    }

    // ═══════════════════════════════════════════════════
    // 자동 노출
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 자동 노출: 페이지 밝기에 따라 광원 강도를 자동 조절.
    ///   밝은 페이지(avgLum≈0.8) → 강도 감소 (과포화 방지)
    ///   어두운 페이지(avgLum≈0.2) → 강도 증가 (가시성 확보)
    /// 매 프레임 부드럽게 보간하여 깜박임을 방지한다.
    /// </summary>
    private void UpdateAutoExposure()
    {
        if (!config.autoExposureEnabled || cookieProcessor == null) return;

        // 목표 강도: 평균 휘도가 높을수록 승수가 낮아짐
        float avgLum = cookieProcessor.AverageLuminance;
        float exposureMultiplier = Mathf.Lerp(
            config.autoExposureMaxMultiplier,  // 어두운 페이지용 (1.5x)
            config.autoExposureMinMultiplier,  // 밝은 페이지용 (0.6x)
            avgLum
        );
        targetIntensity = config.lightIntensity * exposureMultiplier;

        // 부드러운 전환 (지수 감쇠 보간)
        smoothedIntensity = Mathf.Lerp(
            smoothedIntensity,
            targetIntensity,
            Time.deltaTime * config.autoExposureSmoothSpeed
        );

        // 메인 광원이 활성일 때만 강도 적용
        if (mainLightActive)
            hdLightData.intensity = smoothedIntensity;
    }

    // ═══════════════════════════════════════════════════
    // 광원 제어
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 런타임에서 config 변경을 반영한다.
    /// </summary>
    public void ApplyConfigChanges()
    {
        if (config == null || hdLightData == null) return;

        hdLightData.SetAreaLightSize(new Vector2(config.screenWorldSize, config.screenWorldSize));
        areaLight.range = config.lightRange;

        // 자동 노출 비활성 시 기본 강도로 복원
        if (!config.autoExposureEnabled)
        {
            hdLightData.intensity = config.lightIntensity;
            smoothedIntensity = config.lightIntensity;
            targetIntensity = config.lightIntensity;
        }

        // 콘트라스트 LUT 재베이킹
        cookieProcessor?.MarkLUTDirty();
    }

    /// <summary>
    /// 광원 강도를 직접 설정한다. 자동 노출이 활성이면 다음 프레임에 덮어쓰인다.
    /// </summary>
    public void SetIntensity(float lumens)
    {
        if (hdLightData != null)
        {
            hdLightData.intensity = lumens;
            smoothedIntensity = lumens;
        }
    }

    /// <summary>
    /// 메인 광원의 활성/비활성을 전환한다.
    /// QuadrantLightSystem 활성 시 메인 광원을 비활성화하여 이중 조명을 방지한다.
    /// </summary>
    public void SetMainLightActive(bool active)
    {
        mainLightActive = active;
        areaLight.enabled = active;
    }

    // ═══════════════════════════════════════════════════
    // 파라미터 동기화
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// config 변경에 반응하여 광원 파라미터를 매 프레임 동기화한다.
    /// </summary>
    private void SyncLightParameters()
    {
        // 범위 동기화
        if (Mathf.Abs(areaLight.range - config.lightRange) > 0.01f)
            areaLight.range = config.lightRange;
    }
}
