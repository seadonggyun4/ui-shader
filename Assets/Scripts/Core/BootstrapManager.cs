// Assets/Scripts/Core/BootstrapManager.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 6 — 부트스트랩 매니저
// ══════════════════════════════════════════════════════════════════════
//
// 애플리케이션 시작 시 초기화 시퀀스를 오케스트레이션한다.
// 코루틴 기반 단계적 초기화로, 각 단계의 준비 완료를 대기한 후 진행한다.
//
// 초기화 순서:
//   1. Application.targetFrameRate 설정
//   2. 텍스처 소스 준비 대기 (Phase 0: StaticTextureSource, Phase 1: CEF)
//   3. 기본 URL 로드 (CEF 활성 시)
//   4. 자동 순항 모드 시작
//   5. 자동 데모 모드 시작 (configurable 지연)
//
// CEF 비의존 설계:
//   - ITextureSource.IsReady를 폴링하여 소스 유형에 무관하게 작동
//   - CEF 미구현 시 StaticTextureSource의 IsReady=true로 즉시 통과
//   - OnURLRequested 이벤트를 UIShaderOverlay에 연결하여 CEF 연동 준비
//
// 확장:
//   - 부트스트랩 이벤트를 통해 외부 시스템이 초기화 완료를 감지 가능
//   - 커맨드 라인 인수 파싱 (향후 확장 지점)

using System;
using System.Collections;
using UnityEngine;

public class BootstrapManager : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 참조
    // ═══════════════════════════════════════════════════

    [Header("Core")]
    [SerializeField] private UIShaderConfig config;

    [Header("Subsystems")]
    [Tooltip("텍스처 파이프라인 매니저 (활성 상태 확인용)")]
    [SerializeField] private TexturePipelineManager pipelineManager;

    [Tooltip("카메라 컨트롤러")]
    [SerializeField] private OrbitCameraController cameraController;

    [Tooltip("데모 자동 재생 컨트롤러")]
    [SerializeField] private DemoAutoPlay autoPlay;

    [Tooltip("UI 오버레이 (URL 이벤트 연결용)")]
    [SerializeField] private UIShaderOverlay overlay;

    [Tooltip("CEF 브릿지 (Phase 1, URL 로드 이벤트 수신)")]
    [SerializeField] private CEFBridge cefBridge;

    // ═══════════════════════════════════════════════════
    // 시작 설정
    // ═══════════════════════════════════════════════════

    [Header("Startup")]
    [Tooltip("시작 시 자동 데모 모드 활성화")]
    [SerializeField] private bool autoStartDemo = true;

    [Tooltip("자동 순항 모드 자동 시작")]
    [SerializeField] private bool autoStartCruise = true;

    [Tooltip("파이프라인 준비 대기 최대 시간 (초)")]
    [SerializeField] private float maxWaitTime = 10f;

    [Tooltip("데모 시작 전 대기 시간 (초, 사용자에게 초기 화면을 보여줌)")]
    [SerializeField] private float demoStartDelay = 8f;

    // ═══════════════════════════════════════════════════
    // 이벤트
    // ═══════════════════════════════════════════════════

    /// <summary>부트스트랩 완료 시 발생. 모든 서브시스템이 준비된 상태.</summary>
    public event Action OnBootstrapComplete;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private bool isBootstrapped;

    /// <summary>부트스트랩 완료 여부</summary>
    public bool IsBootstrapped => isBootstrapped;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        if (config == null)
        {
            Debug.LogError("[UIShader] BootstrapManager: UIShaderConfig가 할당되지 않았습니다.");
            return;
        }

        // 프레임레이트 설정
        Application.targetFrameRate = config.targetFrameRate;
        QualitySettings.vSyncCount = 0;

        StartCoroutine(BootstrapSequence());
    }

    // ═══════════════════════════════════════════════════
    // 부트스트랩 시퀀스
    // ═══════════════════════════════════════════════════

    private IEnumerator BootstrapSequence()
    {
        Debug.Log("[UIShader] 부트스트랩 시작...");

        // ─── 1단계: 파이프라인 준비 대기 ───
        yield return WaitForPipeline();

        // ─── 2단계: 이벤트 연결 ───
        ConnectEvents();

        // ─── 3단계: 자동 순항 시작 ───
        if (autoStartCruise && cameraController != null)
        {
            cameraController.StartCruise();
            Debug.Log("[UIShader] 자동 순항 모드 활성화");
        }

        isBootstrapped = true;
        OnBootstrapComplete?.Invoke();

        Debug.Log("[UIShader] 부트스트랩 완료");

        // ─── 4단계: 데모 모드 지연 시작 ───
        if (autoStartDemo && autoPlay != null)
        {
            Debug.Log($"[UIShader] 데모 모드 {demoStartDelay}초 후 시작 예정");
            yield return new WaitForSeconds(demoStartDelay);
            autoPlay.StartAutoPlay();
        }
    }

    /// <summary>
    /// 텍스처 파이프라인이 활성화될 때까지 대기한다.
    /// maxWaitTime 초과 시 타임아웃 경고 후 진행.
    /// </summary>
    private IEnumerator WaitForPipeline()
    {
        if (pipelineManager == null)
        {
            Debug.LogWarning("[UIShader] BootstrapManager: TexturePipelineManager 미할당, 대기 생략");
            yield break;
        }

        float elapsed = 0f;
        while (!pipelineManager.IsActive && elapsed < maxWaitTime)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (pipelineManager.IsActive)
            Debug.Log($"[UIShader] 파이프라인 준비 완료 ({elapsed:F1}s)");
        else
            Debug.LogWarning($"[UIShader] 파이프라인 준비 타임아웃 ({maxWaitTime}s). 진행합니다.");
    }

    /// <summary>
    /// UI 오버레이와 데모 자동 재생의 이벤트를 CEF 브릿지에 연결한다.
    /// CEFBridge가 할당되지 않은 경우 (Phase 0 모드) 안전하게 건너뛴다.
    /// </summary>
    private void ConnectEvents()
    {
        if (cefBridge != null)
        {
            // UI 오버레이 URL 요청 → CEF 브릿지
            if (overlay != null)
            {
                overlay.OnURLRequested += cefBridge.LoadURL;
                Debug.Log("[UIShader] overlay.OnURLRequested → cefBridge.LoadURL 연결");
            }

            // 데모 자동 재생 URL 로드 → CEF 브릿지
            if (autoPlay != null)
            {
                autoPlay.OnLoadURL += cefBridge.LoadURL;
                Debug.Log("[UIShader] autoPlay.OnLoadURL → cefBridge.LoadURL 연결");
            }

            Debug.Log("[UIShader] CEF 이벤트 연결 완료");
        }
        else
        {
            Debug.Log("[UIShader] CEFBridge 미할당 — Phase 0 모드 (이벤트 연결 생략)");
        }
    }

    void OnDestroy()
    {
        // 이벤트 연결 해제 (메모리 누수 방지)
        if (cefBridge != null)
        {
            if (overlay != null)
                overlay.OnURLRequested -= cefBridge.LoadURL;
            if (autoPlay != null)
                autoPlay.OnLoadURL -= cefBridge.LoadURL;
        }
    }

    // ═══════════════════════════════════════════════════
    // 공개 API
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 부트스트랩을 수동으로 다시 실행한다.
    /// 씬 리로드 없이 시스템을 재초기화할 때 사용.
    /// </summary>
    public void Rebootstrap()
    {
        isBootstrapped = false;
        StopAllCoroutines();
        StartCoroutine(BootstrapSequence());
    }
}
