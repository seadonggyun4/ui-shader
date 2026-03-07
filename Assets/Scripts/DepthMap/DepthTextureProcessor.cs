// Assets/Scripts/DepthMap/DepthTextureProcessor.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — 깊이 텍스처 후처리 파이프라인
// ══════════════════════════════════════════════════════════════════════
//
// 깊이 추출기(depth-extractor.js)가 생산한 원시 깊이 텍스처에
// 가우시안 블러를 적용하여 변위 메시의 시각적 품질을 향상시킨다.
//
// 설계:
//   - 분리 가능 가우시안 블러 (수평+수직 2패스)로 O(2n) 성능
//   - 다중 반복(iteration)으로 더 넓은 블러 반경 달성
//   - 핑퐁 버퍼링으로 중간 RenderTexture 재활용
//   - UIShaderConfig의 블러 파라미터에 실시간 반응

using UnityEngine;

public class DepthTextureProcessor : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 인스펙터 참조
    // ═══════════════════════════════════════════════════

    [Header("References")]
    [SerializeField] private ComputeShader depthBlurShader;
    [SerializeField] private UIShaderConfig config;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private RenderTexture pingRT;  // 핑퐁 버퍼 A
    private RenderTexture pongRT;  // 핑퐁 버퍼 B
    private int kernelH;           // 수평 블러 커널 인덱스
    private int kernelV;           // 수직 블러 커널 인덱스
    private bool isInitialized;

    // 셰이더 프로퍼티 ID 캐싱
    private static readonly int InputId = Shader.PropertyToID("Input");
    private static readonly int ResultId = Shader.PropertyToID("Result");
    private static readonly int TexelSizeId = Shader.PropertyToID("_TexelSize");
    private static readonly int KernelRadiusId = Shader.PropertyToID("_KernelRadius");

    /// <summary>블러 처리된 깊이 텍스처 (읽기 전용)</summary>
    public RenderTexture BlurredDepthTexture => pingRT;

    /// <summary>초기화 완료 여부</summary>
    public bool IsInitialized => isInitialized;

    // ═══════════════════════════════════════════════════
    // 생명주기
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 명시적 초기화. UIShaderBootstrap에서 호출하거나 Start에서 자동 호출.
    /// </summary>
    public void Initialize()
    {
        if (isInitialized) return;

        if (depthBlurShader == null)
        {
            Debug.LogWarning("[UIShader] DepthTextureProcessor: ComputeShader가 할당되지 않았습니다. " +
                           "블러 없이 원시 깊이 텍스처를 사용합니다.");
            return;
        }

        if (config == null)
        {
            Debug.LogError("[UIShader] DepthTextureProcessor: UIShaderConfig가 할당되지 않았습니다.");
            return;
        }

        // 커널 인덱스 조회
        kernelH = depthBlurShader.FindKernel("BlurHorizontal");
        kernelV = depthBlurShader.FindKernel("BlurVertical");

        // 핑퐁 RenderTexture 생성
        CreateBuffers();

        isInitialized = true;
        Debug.Log($"[UIShader] DepthTextureProcessor 초기화: {config.screenResolution}x{config.screenResolution}, " +
                  $"블러 반복={config.depthBlurIterations}, 커널={config.depthBlurKernelSize}");
    }

    void Start()
    {
        Initialize();
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    // ═══════════════════════════════════════════════════
    // 버퍼 관리
    // ═══════════════════════════════════════════════════

    private void CreateBuffers()
    {
        int res = config.screenResolution;

        pingRT = CreateBlurRT(res, "UIShader_DepthBlur_Ping");
        pongRT = CreateBlurRT(res, "UIShader_DepthBlur_Pong");
    }

    private RenderTexture CreateBlurRT(int resolution, string name)
    {
        var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32);
        rt.enableRandomWrite = true;
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.name = name;
        rt.Create();
        return rt;
    }

    private void ReleaseBuffers()
    {
        if (pingRT != null)
        {
            pingRT.Release();
            pingRT = null;
        }
        if (pongRT != null)
        {
            pongRT.Release();
            pongRT = null;
        }
        isInitialized = false;
    }

    /// <summary>
    /// 해상도 변경 시 버퍼를 재생성한다.
    /// </summary>
    public void ResizeBuffers(int newResolution)
    {
        ReleaseBuffers();
        CreateBuffers();
        isInitialized = true;
    }

    // ═══════════════════════════════════════════════════
    // 블러 적용
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 원시 깊이 텍스처에 가우시안 블러를 적용한다.
    /// 반환값: 블러 처리된 RenderTexture.
    /// 블러 반복 횟수가 0이면 원본을 그대로 Blit한다.
    /// </summary>
    /// <param name="sourceDepth">원시 깊이 텍스처 (Texture2D 또는 RenderTexture)</param>
    /// <returns>블러 처리된 RenderTexture</returns>
    public RenderTexture ApplyBlur(Texture sourceDepth)
    {
        if (sourceDepth == null) return null;

        // 컴퓨트 셰이더 없으면 원본 복사 후 반환
        if (!isInitialized || depthBlurShader == null || config.depthBlurIterations <= 0)
        {
            Graphics.Blit(sourceDepth, pingRT);
            return pingRT;
        }

        int res = config.screenResolution;
        float texelSize = 1f / res;
        int kernelRadius = Mathf.Clamp(config.depthBlurKernelSize / 2, 1, 5);
        int threadGroups = Mathf.CeilToInt(res / 8f);

        // 공통 파라미터 설정
        depthBlurShader.SetFloat(TexelSizeId, texelSize);
        depthBlurShader.SetInt(KernelRadiusId, kernelRadius);

        // 첫 반복: 소스 → ping (수평) → pong (수직)
        // 이후 반복: pong → ping (수평) → pong (수직)

        // 수평 패스: source → ping
        depthBlurShader.SetTexture(kernelH, InputId, sourceDepth);
        depthBlurShader.SetTexture(kernelH, ResultId, pingRT);
        depthBlurShader.Dispatch(kernelH, threadGroups, threadGroups, 1);

        // 수직 패스: ping → pong
        depthBlurShader.SetTexture(kernelV, InputId, pingRT);
        depthBlurShader.SetTexture(kernelV, ResultId, pongRT);
        depthBlurShader.Dispatch(kernelV, threadGroups, threadGroups, 1);

        // 추가 반복
        for (int i = 1; i < config.depthBlurIterations; i++)
        {
            // 수평: pong → ping
            depthBlurShader.SetTexture(kernelH, InputId, pongRT);
            depthBlurShader.SetTexture(kernelH, ResultId, pingRT);
            depthBlurShader.Dispatch(kernelH, threadGroups, threadGroups, 1);

            // 수직: ping → pong
            depthBlurShader.SetTexture(kernelV, InputId, pingRT);
            depthBlurShader.SetTexture(kernelV, ResultId, pongRT);
            depthBlurShader.Dispatch(kernelV, threadGroups, threadGroups, 1);
        }

        // 최종 결과는 pongRT에 있음 → pingRT에 복사하여 일관된 출력
        Graphics.Blit(pongRT, pingRT);

        return pingRT;
    }

    /// <summary>
    /// 블러 적용 후 결과를 Texture2D로 읽어온다.
    /// GPU→CPU 전송이므로 성능 비용이 있으며, 디버그 목적으로만 사용.
    /// </summary>
    public Texture2D ApplyBlurAndReadback(Texture sourceDepth)
    {
        var blurredRT = ApplyBlur(sourceDepth);
        if (blurredRT == null) return null;

        int res = config.screenResolution;
        var result = new Texture2D(res, res, TextureFormat.ARGB32, false);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = blurredRT;
        result.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        result.Apply(false);
        RenderTexture.active = prev;

        return result;
    }
}
