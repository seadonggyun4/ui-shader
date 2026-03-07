// Assets/Scripts/Rendering/CookieProcessor.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — GPU 기반 쿠키 텍스처 후처리기
// ══════════════════════════════════════════════════════════════════════
//
// CookieProcess.shader를 통해 GPU에서 쿠키 텍스처를 후처리한다.
// CPU GetPixels/SetPixels 루프를 대체하여 ~100x 성능 향상.
//
// 책임 범위:
//   - 소스 텍스처 → 후처리된 RenderTexture 변환 (채도, 콘트라스트, 강도)
//   - AnimationCurve → 1D LUT 텍스처 베이킹
//   - 평균 휘도 계산 (프로그레시브 다운샘플링)
//   - 리소스 생명주기 관리
//
// ScreenLightController가 소유하며, QuadrantLightSystem이 결과를 소비한다.

using System;
using UnityEngine;

public class CookieProcessor : IDisposable
{
    // ═══════════════════════════════════════════════════
    // GPU 리소스
    // ═══════════════════════════════════════════════════

    private readonly RenderTexture processedRT;
    private readonly Material processingMaterial;
    private readonly Texture2D contrastLUT;
    private readonly Texture2D luminanceReadback;
    private readonly int resolution;

    // 셰이더 프로퍼티 ID 캐싱
    private static readonly int SaturationBoostId = Shader.PropertyToID("_SaturationBoost");
    private static readonly int IntensityMultiplierId = Shader.PropertyToID("_IntensityMultiplier");
    private static readonly int ContrastLUTId = Shader.PropertyToID("_ContrastLUT");

    // ═══════════════════════════════════════════════════
    // 상태
    // ═══════════════════════════════════════════════════

    private float averageLuminance = 0.5f;
    private bool isDisposed;
    private bool lutDirty = true;
    private AnimationCurve cachedCurve;

    // ═══════════════════════════════════════════════════
    // 공개 프로퍼티
    // ═══════════════════════════════════════════════════

    /// <summary>후처리된 쿠키 RenderTexture (밉맵 포함, HDRP 쿠키에 직접 할당 가능)</summary>
    public RenderTexture ProcessedTexture => processedRT;

    /// <summary>마지막 프레임의 평균 휘도 (0~1, BT.709 가중)</summary>
    public float AverageLuminance => averageLuminance;

    /// <summary>리소스 해제 여부</summary>
    public bool IsDisposed => isDisposed;

    // ═══════════════════════════════════════════════════
    // 생성 / 해제
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// CookieProcessor를 생성한다.
    /// </summary>
    /// <param name="resolution">쿠키 텍스처 해상도 (e.g., 512)</param>
    /// <param name="cookieProcessShader">CookieProcess.shader 참조</param>
    public CookieProcessor(int resolution, Shader cookieProcessShader)
    {
        if (cookieProcessShader == null)
            throw new ArgumentNullException(nameof(cookieProcessShader),
                "[UIShader] CookieProcessor: CookieProcess 셰이더가 null입니다.");

        this.resolution = resolution;

        // ─── 후처리 결과 RT (밉맵 활성화: 거리 기반 쿠키 블러링) ───
        processedRT = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32);
        processedRT.useMipMap = true;
        processedRT.autoGenerateMips = false;
        processedRT.filterMode = FilterMode.Trilinear;
        processedRT.wrapMode = TextureWrapMode.Clamp;
        processedRT.name = "UIShader_ProcessedCookie";
        processedRT.Create();

        // ─── 후처리 머티리얼 ───
        processingMaterial = new Material(cookieProcessShader);
        processingMaterial.name = "UIShader_CookieProcessMat";

        // ─── 콘트라스트 LUT (256x1, AnimationCurve → GPU 룩업) ───
        contrastLUT = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        contrastLUT.filterMode = FilterMode.Bilinear;
        contrastLUT.wrapMode = TextureWrapMode.Clamp;
        contrastLUT.name = "UIShader_ContrastLUT";

        // 기본값: 항등 커브 (input = output)
        BakeContrastCurve(AnimationCurve.Linear(0, 0, 1, 1));

        // ─── 휘도 리드백용 1x1 텍스처 ───
        luminanceReadback = new Texture2D(1, 1, TextureFormat.ARGB32, false);

        Debug.Log($"[UIShader] CookieProcessor 생성: {resolution}x{resolution}, " +
                  $"밉맵 레벨 {Mathf.FloorToInt(Mathf.Log(resolution, 2)) + 1}");
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (processedRT != null) { processedRT.Release(); UnityEngine.Object.Destroy(processedRT); }
        if (processingMaterial != null) UnityEngine.Object.Destroy(processingMaterial);
        if (contrastLUT != null) UnityEngine.Object.Destroy(contrastLUT);
        if (luminanceReadback != null) UnityEngine.Object.Destroy(luminanceReadback);
    }

    // ═══════════════════════════════════════════════════
    // LUT 베이킹
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// AnimationCurve를 256x1 LUT 텍스처로 베이크한다.
    /// 에디터에서 커브 변경 시 호출해야 한다.
    /// </summary>
    public void BakeContrastCurve(AnimationCurve curve)
    {
        if (curve == null) return;

        cachedCurve = curve;
        Color[] pixels = new Color[256];

        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            float v = Mathf.Clamp01(curve.Evaluate(t));
            pixels[i] = new Color(v, v, v, 1f);
        }

        contrastLUT.SetPixels(pixels);
        contrastLUT.Apply(false);
        lutDirty = false;
    }

    /// <summary>LUT 재베이킹이 필요함을 표시한다.</summary>
    public void MarkLUTDirty()
    {
        lutDirty = true;
    }

    // ═══════════════════════════════════════════════════
    // 핵심 처리
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 소스 텍스처를 후처리하여 ProcessedTexture에 결과를 저장한다.
    /// GPU Blit 기반이므로 CPU 부하가 거의 없다.
    /// </summary>
    /// <param name="source">원본 웹 페이지 색상 텍스처</param>
    /// <param name="saturationBoost">채도 부스트 (1.0 = 변경 없음)</param>
    /// <param name="intensityMultiplier">밝기 승수 (1.0 = 변경 없음)</param>
    /// <param name="contrastCurve">콘트라스트 커브 (변경 시 LUT 재베이킹)</param>
    public void Process(Texture source, float saturationBoost, float intensityMultiplier,
                        AnimationCurve contrastCurve = null)
    {
        if (isDisposed || source == null) return;

        // 커브가 변경되었으면 LUT 재베이킹
        if (contrastCurve != null && (lutDirty || cachedCurve != contrastCurve))
            BakeContrastCurve(contrastCurve);

        // ─── GPU 처리 파라미터 설정 ───
        processingMaterial.SetFloat(SaturationBoostId, saturationBoost);
        processingMaterial.SetFloat(IntensityMultiplierId, intensityMultiplier);
        processingMaterial.SetTexture(ContrastLUTId, contrastLUT);

        // ─── GPU Blit: 소스 → 후처리 RT ───
        Graphics.Blit(source, processedRT, processingMaterial);

        // ─── 밉맵 생성 (HDRP LTC 거리 기반 블러링용) ───
        processedRT.GenerateMips();

        // ─── 평균 휘도 계산 ───
        CalculateAverageLuminance();
    }

    // ═══════════════════════════════════════════════════
    // 평균 휘도 계산
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 프로그레시브 다운샘플링으로 평균 색상을 구한 뒤 BT.709 가중 휘도를 계산한다.
    /// 512→64→4→1 (3단계 Blit, GPU bilinear 필터링 활용)
    /// </summary>
    private void CalculateAverageLuminance()
    {
        // 프로그레시브 다운샘플: 큰 비율의 단일 Blit보다 정확한 평균값 제공
        int[] downsampleSizes = { 64, 4, 1 };
        RenderTexture current = processedRT;
        RenderTexture[] temps = new RenderTexture[downsampleSizes.Length];

        for (int i = 0; i < downsampleSizes.Length; i++)
        {
            int size = downsampleSizes[i];
            temps[i] = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            temps[i].filterMode = FilterMode.Bilinear;
            Graphics.Blit(current, temps[i]);
            current = temps[i];
        }

        // 1x1 RT에서 평균 색상 리드백
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = temps[downsampleSizes.Length - 1];
        luminanceReadback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
        luminanceReadback.Apply(false);
        RenderTexture.active = prev;

        // BT.709 가중 평균 휘도
        Color avg = luminanceReadback.GetPixel(0, 0);
        averageLuminance = avg.r * 0.2126f + avg.g * 0.7152f + avg.b * 0.0722f;

        // 임시 RT 반환
        for (int i = 0; i < temps.Length; i++)
            RenderTexture.ReleaseTemporary(temps[i]);
    }
}
