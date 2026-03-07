// Assets/Scripts/Demo/DemoAutoPlay.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 5 — 데모 자동 재생 컨트롤러
// ══════════════════════════════════════════════════════════════════════
//
// 데모 시나리오를 자동으로 순환하거나 수동 탐색하는 컨트롤러.
//
// 입력:
//   - P 키: 자동 재생 토글
//   - ← → 화살표: 이전/다음 시나리오
//
// CEF 통합:
//   Phase 1에서 CEFBridge 구현 시, OnLoadURL 이벤트를 연결한다:
//     demoAutoPlay.OnLoadURL += cefBridge.LoadURL;
//
// 시나리오 로드 시:
//   1. URL 로드 요청 (OnLoadURL 이벤트)
//   2. config.displacementScale 변경
//   3. 카메라 프리셋 전환 (TransitionToPreset)

using System;
using System.Collections;
using UnityEngine;

public class DemoAutoPlay : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 참조
    // ═══════════════════════════════════════════════════

    [Header("References")]
    [Tooltip("데모 시나리오 에셋")]
    [SerializeField] private DemoScenarios scenarios;

    [Tooltip("카메라 컨트롤러")]
    [SerializeField] private OrbitCameraController cameraController;

    [Tooltip("전역 설정 (displacementScale 변경용)")]
    [SerializeField] private UIShaderConfig config;

    // ═══════════════════════════════════════════════════
    // 자동 재생 설정
    // ═══════════════════════════════════════════════════

    [Header("Auto Play")]
    [Tooltip("시작 시 자동 재생")]
    [SerializeField] private bool autoPlayOnStart = false;

    [Tooltip("시나리오당 체류 시간 (초)")]
    [SerializeField] private float scenarioDuration = 15f;

    [Tooltip("시나리오 전환 대기 시간 (초)")]
    [SerializeField] private float transitionDelay = 2f;

    // ═══════════════════════════════════════════════════
    // 이벤트
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// URL 로드 요청 이벤트.
    /// Phase 1 CEF 통합 시: demoAutoPlay.OnLoadURL += cefBridge.LoadURL;
    /// </summary>
    public event Action<string> OnLoadURL;

    /// <summary>시나리오 변경 시 발생. (시나리오 인덱스, 시나리오 데이터)</summary>
    public event Action<int, DemoScenarios.Scenario> OnScenarioChanged;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private int currentScenarioIndex;
    private bool isAutoPlaying;
    private Coroutine autoPlayCoroutine;

    /// <summary>현재 시나리오 인덱스</summary>
    public int CurrentScenarioIndex => currentScenarioIndex;

    /// <summary>자동 재생 중 여부</summary>
    public bool IsAutoPlaying => isAutoPlaying;

    /// <summary>현재 시나리오 이름 (UI 표시용)</summary>
    public string CurrentScenarioName =>
        scenarios != null && currentScenarioIndex < scenarios.Count
            ? scenarios.GetScenario(currentScenarioIndex)?.name ?? ""
            : "";

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        if (scenarios == null)
        {
            Debug.LogWarning("[UIShader] DemoAutoPlay: DemoScenarios 에셋이 할당되지 않았습니다.");
            return;
        }

        if (autoPlayOnStart && scenarios.Count > 0)
            StartAutoPlay();
    }

    void Update()
    {
        if (scenarios == null || scenarios.Count == 0) return;

        // P 키: 자동 재생 토글
        if (Input.GetKeyDown(KeyCode.P))
        {
            if (isAutoPlaying)
                StopAutoPlay();
            else
                StartAutoPlay();
        }

        // ← → 화살표: 수동 시나리오 전환
        if (Input.GetKeyDown(KeyCode.LeftArrow))
            PlayPrevious();

        if (Input.GetKeyDown(KeyCode.RightArrow))
            PlayNext();
    }

    void OnDestroy()
    {
        StopAutoPlay();
    }

    // ═══════════════════════════════════════════════════
    // 공개 API
    // ═══════════════════════════════════════════════════

    /// <summary>자동 재생을 시작한다. 카메라 자동 순항도 함께 활성화.</summary>
    public void StartAutoPlay()
    {
        if (scenarios == null || scenarios.Count == 0) return;

        isAutoPlaying = true;

        if (cameraController != null)
            cameraController.StartCruise();

        autoPlayCoroutine = StartCoroutine(AutoPlayCoroutine());

        Debug.Log("[UIShader] 데모 자동 재생: ON");
    }

    /// <summary>자동 재생을 중단한다.</summary>
    public void StopAutoPlay()
    {
        isAutoPlaying = false;

        if (autoPlayCoroutine != null)
        {
            StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = null;
        }

        if (cameraController != null)
            cameraController.StopCruise();

        Debug.Log("[UIShader] 데모 자동 재생: OFF");
    }

    /// <summary>다음 시나리오로 전환한다.</summary>
    public void PlayNext()
    {
        if (scenarios == null || scenarios.Count == 0) return;
        LoadScenario((currentScenarioIndex + 1) % scenarios.Count);
    }

    /// <summary>이전 시나리오로 전환한다.</summary>
    public void PlayPrevious()
    {
        if (scenarios == null || scenarios.Count == 0) return;
        LoadScenario((currentScenarioIndex - 1 + scenarios.Count) % scenarios.Count);
    }

    /// <summary>지정된 인덱스의 시나리오를 로드한다.</summary>
    public void LoadScenario(int index)
    {
        if (scenarios == null) return;

        var scenario = scenarios.GetScenario(index);
        if (scenario == null) return;

        currentScenarioIndex = index;

        // URL 로드 요청
        if (!string.IsNullOrEmpty(scenario.url))
        {
            OnLoadURL?.Invoke(scenario.url);
        }

        // 변위 스케일 변경
        if (config != null)
        {
            config.displacementScale = scenario.displacementScale;
        }

        // 카메라 프리셋 전환
        if (cameraController != null && scenario.cameraPresetIndex >= 0)
        {
            cameraController.TransitionToPreset(scenario.cameraPresetIndex);
        }

        OnScenarioChanged?.Invoke(index, scenario);

        Debug.Log($"[UIShader] 시나리오 로드: [{index + 1}/{scenarios.Count}] {scenario.name}");
    }

    // ═══════════════════════════════════════════════════
    // 코루틴
    // ═══════════════════════════════════════════════════

    private IEnumerator AutoPlayCoroutine()
    {
        while (isAutoPlaying)
        {
            LoadScenario(currentScenarioIndex);
            yield return new WaitForSeconds(scenarioDuration);

            currentScenarioIndex = (currentScenarioIndex + 1) % scenarios.Count;
            yield return new WaitForSeconds(transitionDelay);
        }
    }
}
