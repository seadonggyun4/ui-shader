// Assets/Scripts/UI/PerformanceProfiler.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 5 — 성능 프로파일러 오버레이
// ══════════════════════════════════════════════════════════════════════
//
// F3 키로 토글되는 OnGUI 기반 성능 모니터링 오버레이.
//
// 표시 항목:
//   - FPS (평균, 1% Low)
//   - 프레임 타임 (ms)
//   - 메모리 사용량 (총 할당, GC)
//   - 텍스처 파이프라인 상태 (색상/깊이 갱신률)
//   - Draw Calls / 삼각형 수 (에디터 전용)
//
// 성능 예산 (PHASE-5.md 기준):
//   CEF 전송 < 8ms, 깊이 생성 < 2ms, 변위 < 1ms,
//   HDRP 렌더 < 10ms, 후처리 < 3ms → 합계 < 16.6ms (60fps)

using UnityEngine;
using UnityEngine.Profiling;

public class PerformanceProfiler : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 설정
    // ═══════════════════════════════════════════════════

    [Header("Settings")]
    [Tooltip("프로파일러 표시 (F3 토글)")]
    [SerializeField] private bool showProfiler = false;

    [Tooltip("FPS 히스토리 프레임 수 (평균 계산용)")]
    [SerializeField] private int historySize = 120;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private float[] frameTimes;
    private int frameIndex;
    private TexturePipelineManager pipelineManager;
    private DemoAutoPlay demoAutoPlay;
    private OrbitCameraController cameraController;

    // 캐시 (매 프레임 GC 방지)
    private GUIStyle headerStyle;
    private GUIStyle normalStyle;
    private GUIStyle warningStyle;
    private bool stylesInitialized;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        frameTimes = new float[historySize];
        pipelineManager = FindObjectOfType<TexturePipelineManager>();
        demoAutoPlay = FindObjectOfType<DemoAutoPlay>();
        cameraController = FindObjectOfType<OrbitCameraController>();
    }

    void Update()
    {
        // 프레임 타임 기록
        frameTimes[frameIndex] = Time.unscaledDeltaTime * 1000f;
        frameIndex = (frameIndex + 1) % frameTimes.Length;

        // F3: 프로파일러 토글
        if (Input.GetKeyDown(KeyCode.F3))
            showProfiler = !showProfiler;
    }

    // ═══════════════════════════════════════════════════
    // OnGUI 렌더링
    // ═══════════════════════════════════════════════════

    void OnGUI()
    {
        if (!showProfiler) return;
        EnsureStyles();

        float avgMs = CalculateAverage();
        float percentile1Ms = CalculatePercentile(0.99f);
        float avgFps = avgMs > 0.001f ? 1000f / avgMs : 0f;
        float lowFps = percentile1Ms > 0.001f ? 1000f / percentile1Ms : 0f;

        long totalMemMB = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
        long gcMemMB = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);

        GUILayout.BeginArea(new Rect(10, 10, 420, 400));
        GUILayout.BeginVertical("box");

        GUILayout.Label("UIShader Performance", headerStyle);
        GUILayout.Label("─────────────────────────────────", normalStyle);

        // FPS
        GUIStyle fpsStyle = avgFps >= 55f ? normalStyle : warningStyle;
        GUILayout.Label($"FPS:   {avgFps:F0}  (avg {avgMs:F1} ms)", fpsStyle);
        GUILayout.Label($"1% Low: {lowFps:F0}  ({percentile1Ms:F1} ms)", normalStyle);

        GUILayout.Label("─────────────────────────────────", normalStyle);

        // 메모리
        GUILayout.Label($"Memory (Total):  {totalMemMB} MB", normalStyle);
        GUILayout.Label($"Memory (GC):     {gcMemMB} MB", normalStyle);

        // 에디터 전용 통계
#if UNITY_EDITOR
        GUILayout.Label("─────────────────────────────────", normalStyle);
        GUILayout.Label($"Draw Calls:  {UnityStats.drawCalls}", normalStyle);
        GUILayout.Label($"Triangles:   {UnityStats.triangles:N0}", normalStyle);
        GUILayout.Label($"Batches:     {UnityStats.batches}", normalStyle);
#endif

        // 파이프라인 상태
        if (pipelineManager != null)
        {
            GUILayout.Label("─────────────────────────────────", normalStyle);
            GUILayout.Label($"Pipeline:        {(pipelineManager.IsActive ? "Active" : "Inactive")}",
                pipelineManager.IsActive ? normalStyle : warningStyle);
            GUILayout.Label($"Color Updates/s: {pipelineManager.ColorUpdatesPerSecond}", normalStyle);
            GUILayout.Label($"Depth Updates/s: {pipelineManager.DepthUpdatesPerSecond}", normalStyle);
        }

        // 카메라 상태
        if (cameraController != null)
        {
            GUILayout.Label("─────────────────────────────────", normalStyle);
            string cameraMode = cameraController.IsAutoCruising ? "Auto Cruise" :
                                cameraController.IsTransitioning ? "Transitioning" : "Manual";
            GUILayout.Label($"Camera: {cameraMode}", normalStyle);

            if (cameraController.ActivePresetIndex >= 0 &&
                cameraController.ActivePresetIndex < cameraController.presets.Length)
            {
                GUILayout.Label($"Preset: {cameraController.presets[cameraController.ActivePresetIndex].name}",
                    normalStyle);
            }
        }

        // 데모 상태
        if (demoAutoPlay != null && demoAutoPlay.IsAutoPlaying)
        {
            GUILayout.Label("─────────────────────────────────", normalStyle);
            GUILayout.Label($"Demo: {demoAutoPlay.CurrentScenarioName} " +
                            $"[{demoAutoPlay.CurrentScenarioIndex + 1}]", normalStyle);
        }

        GUILayout.Label("─────────────────────────────────", normalStyle);
        GUILayout.Label("[F3] Toggle  [C] Cruise  [P] Demo  [1-4] Presets", normalStyle);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    // ═══════════════════════════════════════════════════
    // 통계 계산
    // ═══════════════════════════════════════════════════

    private float CalculateAverage()
    {
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < frameTimes.Length; i++)
        {
            if (frameTimes[i] > 0f)
            {
                sum += frameTimes[i];
                count++;
            }
        }
        return count > 0 ? sum / count : 0f;
    }

    /// <summary>
    /// 상위 percentile의 프레임 타임을 계산한다.
    /// percentile=0.99 → 상위 1%의 느린 프레임 평균 (1% Low FPS)
    /// </summary>
    private float CalculatePercentile(float percentile)
    {
        // 유효 프레임 수집
        int validCount = 0;
        for (int i = 0; i < frameTimes.Length; i++)
        {
            if (frameTimes[i] > 0f) validCount++;
        }
        if (validCount < 10) return CalculateAverage();

        // 정렬 (복사본 사용, GC 부담이지만 F3 디버그용이므로 허용)
        float[] sorted = new float[validCount];
        int si = 0;
        for (int i = 0; i < frameTimes.Length; i++)
        {
            if (frameTimes[i] > 0f)
                sorted[si++] = frameTimes[i];
        }
        System.Array.Sort(sorted);

        // 상위 (1-percentile) 프레임의 평균
        int startIdx = Mathf.FloorToInt(validCount * percentile);
        float sum = 0f;
        int count = 0;
        for (int i = startIdx; i < validCount; i++)
        {
            sum += sorted[i];
            count++;
        }
        return count > 0 ? sum / count : 0f;
    }

    // ═══════════════════════════════════════════════════
    // GUI 스타일
    // ═══════════════════════════════════════════════════

    private void EnsureStyles()
    {
        if (stylesInitialized) return;

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            normal = { textColor = Color.white }
        };

        normalStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
        };

        warningStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.7f, 0.2f) }
        };

        stylesInitialized = true;
    }
}
