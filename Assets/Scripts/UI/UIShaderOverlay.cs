// Assets/Scripts/UI/UIShaderOverlay.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 6 — 인게임 UI 오버레이
// ══════════════════════════════════════════════════════════════════════
//
// IMGUI 기반의 경량 인게임 제어 패널.
// Tab 키로 표시/숨김 토글, 드래그 가능한 윈도우.
//
// 섹션 구성:
//   1. URL 입력 (CEF 미구현 시 이벤트만 발생)
//   2. 깊이 가중치 프리셋 선택
//   3. 변위/광원 슬라이더
//   4. 카메라 프리셋 + 자동 순항
//   5. 데모 자동 재생
//   6. 고급 설정 (토글)
//   7. 키보드 단축키 안내
//
// 확장성 설계:
//   - CEFBridge 직접 의존 없음: OnURLRequested 이벤트로 느슨한 결합
//   - 각 UI 섹션은 독립 메서드로 분리 (오버라이드/확장 용이)
//   - GUIStyle 캐시로 GC 부담 최소화
//   - 모든 참조는 null-safe (선택적 컴포넌트 패턴)

using System;
using UnityEngine;

public class UIShaderOverlay : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 참조
    // ═══════════════════════════════════════════════════

    [Header("Core References")]
    [Tooltip("전역 설정")]
    [SerializeField] private UIShaderConfig config;

    [Tooltip("카메라 컨트롤러")]
    [SerializeField] private OrbitCameraController cameraController;

    [Tooltip("영역광 컨트롤러 (현재 강도/휘도 표시용)")]
    [SerializeField] private ScreenLightController lightController;

    [Tooltip("변위 컨트롤러 (카메라 거리/LOD 표시용)")]
    [SerializeField] private DisplacementController displacementController;

    [Tooltip("데모 자동 재생 컨트롤러")]
    [SerializeField] private DemoAutoPlay autoPlay;

    [Header("Depth Presets")]
    [Tooltip("전환 가능한 깊이 가중치 프리셋 배열")]
    [SerializeField] private DepthWeightPreset[] depthPresets;

    // ═══════════════════════════════════════════════════
    // UI 설정
    // ═══════════════════════════════════════════════════

    [Header("UI Settings")]
    [Tooltip("UI 표시 토글 키")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Tooltip("시작 시 UI 표시")]
    [SerializeField] private bool showOnStart = true;

    // ═══════════════════════════════════════════════════
    // 이벤트 (CEF 비의존 설계)
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// URL 로드 요청 이벤트.
    /// Phase 1 CEF 통합 시: overlay.OnURLRequested += cefBridge.LoadURL;
    /// </summary>
    public event Action<string> OnURLRequested;

    /// <summary>
    /// 깊이 가중치 프리셋 변경 이벤트.
    /// Phase 1 CEF 통합 시: JS 가중치 갱신에 활용.
    /// </summary>
    public event Action<DepthWeightPreset> OnDepthPresetChanged;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private bool showUI;
    private string urlInput = "";
    private int selectedPresetIndex;
    private bool showAdvanced;
    private bool showShortcuts;
    private Rect windowRect = new Rect(10, 10, 380, 0);

    // 스타일 캐시 (GC 방지)
    private GUIStyle headerStyle;
    private GUIStyle sectionLabelStyle;
    private GUIStyle urlFieldStyle;
    private GUIStyle valueStyle;
    private GUIStyle shortcutStyle;
    private GUIStyle compactButtonStyle;
    private bool stylesInitialized;

    // 윈도우 ID (다른 OnGUI 윈도우와 충돌 방지)
    private const int WINDOW_ID = 42;

    // ═══════════════════════════════════════════════════
    // 공개 프로퍼티
    // ═══════════════════════════════════════════════════

    /// <summary>UI 표시 여부</summary>
    public bool IsVisible
    {
        get => showUI;
        set => showUI = value;
    }

    /// <summary>현재 선택된 깊이 프리셋 인덱스</summary>
    public int SelectedPresetIndex => selectedPresetIndex;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        showUI = showOnStart;

        if (config != null)
            urlInput = config.defaultURL;

        // 기본 프리셋 인덱스: "Balanced" 탐색
        if (depthPresets != null)
        {
            for (int i = 0; i < depthPresets.Length; i++)
            {
                if (depthPresets[i] != null && depthPresets[i].presetName == "Balanced")
                {
                    selectedPresetIndex = i;
                    break;
                }
            }
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            showUI = !showUI;
    }

    void OnGUI()
    {
        if (!showUI) return;
        EnsureStyles();
        windowRect = GUILayout.Window(WINDOW_ID, windowRect, DrawWindow, "Depthweaver Controls");
    }

    // ═══════════════════════════════════════════════════
    // 윈도우 렌더링 (섹션 분리)
    // ═══════════════════════════════════════════════════

    private void DrawWindow(int id)
    {
        DrawURLSection();
        GUILayout.Space(6);
        DrawDepthPresetSection();
        GUILayout.Space(6);
        DrawDisplacementSection();
        GUILayout.Space(4);
        DrawLightSection();
        GUILayout.Space(6);
        DrawCameraSection();
        GUILayout.Space(6);
        DrawDemoSection();
        GUILayout.Space(6);
        DrawAdvancedSection();
        GUILayout.Space(4);
        DrawShortcutSection();

        GUI.DragWindow();
    }

    // ─── URL 입력 섹션 ───────────────────────────────

    private void DrawURLSection()
    {
        GUILayout.Label("URL", headerStyle);
        GUILayout.BeginHorizontal();

        urlInput = GUILayout.TextField(urlInput, urlFieldStyle, GUILayout.MinWidth(280));

        if (GUILayout.Button("Go", GUILayout.Width(36)))
        {
            if (!string.IsNullOrEmpty(urlInput))
            {
                OnURLRequested?.Invoke(urlInput);
                Debug.Log($"[UIShader] URL 요청: {urlInput}");
            }
        }

        GUILayout.EndHorizontal();
    }

    // ─── 깊이 프리셋 섹션 ───────────────────────────

    private void DrawDepthPresetSection()
    {
        if (depthPresets == null || depthPresets.Length == 0) return;

        GUILayout.Label("Depth Preset", headerStyle);

        // 프리셋 이름 배열 구성
        string[] names = new string[depthPresets.Length];
        for (int i = 0; i < depthPresets.Length; i++)
            names[i] = depthPresets[i] != null ? depthPresets[i].presetName : $"Preset {i}";

        int newIndex = GUILayout.SelectionGrid(selectedPresetIndex, names, names.Length);
        if (newIndex != selectedPresetIndex)
        {
            selectedPresetIndex = newIndex;
            ApplyDepthPreset(depthPresets[selectedPresetIndex]);
        }
    }

    // ─── 변위 슬라이더 섹션 ─────────────────────────

    private void DrawDisplacementSection()
    {
        if (config == null) return;

        GUILayout.Label($"Displacement Scale: {config.displacementScale:F2}", sectionLabelStyle);
        config.displacementScale = GUILayout.HorizontalSlider(config.displacementScale, 0f, 2f);
    }

    // ─── 광원 강도 섹션 ─────────────────────────────

    private void DrawLightSection()
    {
        if (config == null) return;

        GUILayout.Label($"Light Intensity: {config.cookieIntensityMultiplier:F2}x", sectionLabelStyle);
        config.cookieIntensityMultiplier = GUILayout.HorizontalSlider(config.cookieIntensityMultiplier, 0.1f, 5f);
    }

    // ─── 카메라 섹션 ────────────────────────────────

    private void DrawCameraSection()
    {
        if (cameraController == null) return;

        GUILayout.Label("Camera", headerStyle);

        // 프리셋 버튼 (최대 4개)
        GUILayout.BeginHorizontal();
        int count = Mathf.Min(cameraController.presets.Length, 4);
        for (int i = 0; i < count; i++)
        {
            string label = cameraController.presets[i].name;
            bool isActive = cameraController.ActivePresetIndex == i;

            // 활성 프리셋 강조
            if (isActive)
                GUI.color = new Color(0.7f, 0.9f, 1f);

            if (GUILayout.Button(label, compactButtonStyle))
                cameraController.TransitionToPreset(i);

            if (isActive)
                GUI.color = Color.white;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(2);

        // 자동 순항 + 데모 버튼
        GUILayout.BeginHorizontal();
        string cruiseLabel = cameraController.IsAutoCruising ? "Cruise: ON" : "Cruise: OFF";
        if (GUILayout.Button(cruiseLabel))
        {
            if (cameraController.IsAutoCruising)
                cameraController.StopCruise();
            else
                cameraController.StartCruise();
        }
        GUILayout.EndHorizontal();
    }

    // ─── 데모 섹션 ──────────────────────────────────

    private void DrawDemoSection()
    {
        if (autoPlay == null) return;

        GUILayout.BeginHorizontal();

        string demoLabel = autoPlay.IsAutoPlaying ? "Demo: ON" : "Demo: OFF";
        if (GUILayout.Button(demoLabel))
        {
            if (autoPlay.IsAutoPlaying)
                autoPlay.StopAutoPlay();
            else
                autoPlay.StartAutoPlay();
        }

        if (GUILayout.Button("Prev", GUILayout.Width(50)))
            autoPlay.PlayPrevious();

        if (GUILayout.Button("Next", GUILayout.Width(50)))
            autoPlay.PlayNext();

        GUILayout.EndHorizontal();

        // 현재 시나리오 표시
        if (autoPlay.IsAutoPlaying || autoPlay.CurrentScenarioIndex >= 0)
        {
            string scenarioName = autoPlay.CurrentScenarioName;
            if (!string.IsNullOrEmpty(scenarioName))
                GUILayout.Label($"  {scenarioName} [{autoPlay.CurrentScenarioIndex + 1}]", valueStyle);
        }
    }

    // ─── 고급 설정 섹션 ─────────────────────────────

    private void DrawAdvancedSection()
    {
        showAdvanced = GUILayout.Toggle(showAdvanced, "Advanced Settings");

        if (!showAdvanced) return;
        if (config == null) return;

        GUILayout.BeginVertical("box");

        // 채도 부스트
        GUILayout.Label($"Saturation Boost: {config.cookieSaturationBoost:F2}", valueStyle);
        config.cookieSaturationBoost = GUILayout.HorizontalSlider(config.cookieSaturationBoost, 0f, 3f);

        // 변위 바이어스
        GUILayout.Label($"Displacement Bias: {config.displacementBias:F2}", valueStyle);
        config.displacementBias = GUILayout.HorizontalSlider(config.displacementBias, -1f, 1f);

        // 경계 감쇠
        GUILayout.Label($"Edge Falloff: {config.edgeFalloff:F3}", valueStyle);
        config.edgeFalloff = GUILayout.HorizontalSlider(config.edgeFalloff, 0f, 0.2f);

        // 자체 발광
        GUILayout.Label($"Emission Intensity: {config.emissionIntensity:F2}", valueStyle);
        config.emissionIntensity = GUILayout.HorizontalSlider(config.emissionIntensity, 0f, 5f);

        // 기본 광원 강도
        GUILayout.Label($"Base Light (lm): {config.lightIntensity:F0}", valueStyle);
        config.lightIntensity = GUILayout.HorizontalSlider(config.lightIntensity, 500f, 20000f);

        GUILayout.Space(4);

        // 사분면 광원 토글
        config.enableQuadrantLights = GUILayout.Toggle(config.enableQuadrantLights, "Quadrant Lights (4x)");

        // 자동 노출 토글
        config.autoExposureEnabled = GUILayout.Toggle(config.autoExposureEnabled, "Auto Exposure");

        GUILayout.Space(4);

        // 실시간 정보 표시
        DrawInfoDisplay();

        GUILayout.EndVertical();
    }

    // ─── 실시간 정보 표시 ───────────────────────────

    private void DrawInfoDisplay()
    {
        GUILayout.Label("─── Info ───", valueStyle);

        if (lightController != null)
        {
            GUILayout.Label($"Avg Luminance:    {lightController.AverageLuminance:F3}", valueStyle);
            GUILayout.Label($"Current Intensity: {lightController.CurrentIntensity:F0} lm", valueStyle);
        }

        if (displacementController != null)
        {
            GUILayout.Label($"Camera Distance:  {displacementController.CurrentDistance:F1} m", valueStyle);
        }
    }

    // ─── 키보드 단축키 안내 ─────────────────────────

    private void DrawShortcutSection()
    {
        showShortcuts = GUILayout.Toggle(showShortcuts, "Keyboard Shortcuts");

        if (!showShortcuts) return;

        GUILayout.BeginVertical("box");
        GUILayout.Label("[Tab]    UI 토글", shortcutStyle);
        GUILayout.Label("[1-4]    카메라 프리셋", shortcutStyle);
        GUILayout.Label("[C]      자동 순항", shortcutStyle);
        GUILayout.Label("[P]      데모 자동 재생", shortcutStyle);
        GUILayout.Label("[← →]   시나리오 전환", shortcutStyle);
        GUILayout.Label("[F3]     성능 프로파일러", shortcutStyle);
        GUILayout.Label("[RMB]    카메라 궤도", shortcutStyle);
        GUILayout.Label("[Scroll] 줌", shortcutStyle);
        GUILayout.EndVertical();
    }

    // ═══════════════════════════════════════════════════
    // 깊이 프리셋 적용
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 깊이 가중치 프리셋을 적용한다.
    /// CEF가 활성화되어 있으면 JS 가중치 갱신 이벤트도 발생.
    /// </summary>
    private void ApplyDepthPreset(DepthWeightPreset preset)
    {
        if (preset == null) return;

        OnDepthPresetChanged?.Invoke(preset);
        Debug.Log($"[UIShader] 깊이 프리셋 변경: {preset.presetName}");
    }

    // ═══════════════════════════════════════════════════
    // 공개 API (외부 시스템 연동)
    // ═══════════════════════════════════════════════════

    /// <summary>URL 입력 필드를 프로그래밍적으로 설정한다.</summary>
    public void SetURL(string url)
    {
        urlInput = url ?? "";
    }

    /// <summary>프리셋을 프로그래밍적으로 선택한다.</summary>
    public void SelectPreset(int index)
    {
        if (depthPresets == null || index < 0 || index >= depthPresets.Length) return;
        selectedPresetIndex = index;
        ApplyDepthPreset(depthPresets[index]);
    }

    // ═══════════════════════════════════════════════════
    // 스타일 초기화 (한 번만 실행)
    // ═══════════════════════════════════════════════════

    private void EnsureStyles()
    {
        if (stylesInitialized) return;

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 13,
            normal = { textColor = Color.white }
        };

        sectionLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 12,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
        };

        urlFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 12
        };

        valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
        };

        shortcutStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.7f, 0.8f, 0.9f) }
        };

        compactButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            padding = new RectOffset(4, 4, 2, 2)
        };

        stylesInitialized = true;
    }
}
