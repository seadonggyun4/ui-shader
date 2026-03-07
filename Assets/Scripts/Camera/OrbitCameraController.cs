// Assets/Scripts/Camera/OrbitCameraController.cs
// ══════════════════════════════════════════════════════════════════════
// 스크린 메시를 중심으로 궤도 회전하는 시네마틱 카메라 컨트롤러.
// ══════════════════════════════════════════════════════════════════════
//
// 입력:
//   - 마우스 우클릭 드래그: 궤도 회전
//   - 스크롤: 줌
//   - 1~4 키: 프리셋 전환 (AnimationCurve 기반 부드러운 전환)
//   - C 키: 자동 순항 모드 토글
//
// Phase 5 확장:
//   - 자동 순항 모드 (느린 회전 + 수직 진동)
//   - AnimationCurve 기반 프리셋 전환 애니메이션
//   - 공개 API: TransitionToPreset(), StartCruise(), StopCruise()

using UnityEngine;

public class OrbitCameraController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 궤도 설정
    // ═══════════════════════════════════════════════════

    [Header("Orbit")]
    [Tooltip("궤도 중심 (미할당 시 ScreenMeshGenerator 자동 탐색)")]
    public Transform target;

    [Tooltip("초기 궤도 거리")]
    public float distance = 12f;

    [Tooltip("최소 줌 거리")]
    public float minDistance = 3f;

    [Tooltip("최대 줌 거리")]
    public float maxDistance = 35f;

    [Tooltip("마우스 회전 감도")]
    public float rotationSpeed = 5f;

    [Tooltip("스크롤 줌 감도")]
    public float zoomSpeed = 3f;

    [Tooltip("보간 속도 (부드러운 이동)")]
    public float smoothSpeed = 8f;

    // ═══════════════════════════════════════════════════
    // 각도 제한
    // ═══════════════════════════════════════════════════

    [Header("Angle Limits")]
    [Tooltip("수직 최소 각도 (아래쪽)")]
    public float minVerticalAngle = -5f;

    [Tooltip("수직 최대 각도 (위쪽)")]
    public float maxVerticalAngle = 70f;

    // ═══════════════════════════════════════════════════
    // 프리셋 카메라 위치
    // ═══════════════════════════════════════════════════

    [Header("Presets")]
    public CameraPreset[] presets = new CameraPreset[]
    {
        new CameraPreset("정면 와이드",   0f,   8f, 12f),
        new CameraPreset("좌측 45도",   -45f, 15f, 10f),
        new CameraPreset("우측 상단",    30f, 35f, 13f),
        new CameraPreset("클로즈업",      5f,   5f,  5f)
    };

    [System.Serializable]
    public class CameraPreset
    {
        public string name;
        public float horizontalAngle;
        public float verticalAngle;
        public float distance;

        public CameraPreset(string name, float h, float v, float d)
        {
            this.name = name;
            horizontalAngle = h;
            verticalAngle = v;
            distance = d;
        }
    }

    // ═══════════════════════════════════════════════════
    // 자동 순항 모드
    // ═══════════════════════════════════════════════════

    [Header("Auto Cruise")]
    [Tooltip("자동 순항 활성화 (C 키로 토글)")]
    public bool autoCruise = false;

    [Tooltip("초당 수평 회전 각도")]
    public float cruiseSpeed = 3f;

    [Tooltip("수직 진동 진폭 (도)")]
    public float cruiseVerticalAmplitude = 2f;

    [Tooltip("수직 진동 주파수 (Hz)")]
    public float cruiseVerticalFrequency = 0.3f;

    // ═══════════════════════════════════════════════════
    // 전환 애니메이션
    // ═══════════════════════════════════════════════════

    [Header("Transition")]
    [Tooltip("프리셋 전환 소요 시간 (초)")]
    public float presetTransitionDuration = 2f;

    [Tooltip("전환 보간 커브 (Ease In/Out)")]
    public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private float currentHAngle;
    private float currentVAngle = 8f;
    private float currentDist;

    private float targetHAngle;
    private float targetVAngle = 8f;
    private float targetDist;

    // 전환 상태
    private bool isTransitioning;
    private float transitionProgress;
    private float transFromH, transFromV, transFromDist;
    private float transToH, transToV, transToDist;
    private int transitionTargetPreset = -1;

    // 자동 순항 상태
    private float cruiseBaseVerticalAngle;

    /// <summary>현재 활성 프리셋 인덱스 (-1이면 수동 조작 중)</summary>
    public int ActivePresetIndex { get; private set; } = -1;

    /// <summary>자동 순항 활성 여부</summary>
    public bool IsAutoCruising => autoCruise;

    /// <summary>프리셋 전환 진행 중 여부</summary>
    public bool IsTransitioning => isTransitioning;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        if (target == null)
        {
            ScreenMeshGenerator screenMesh = FindObjectOfType<ScreenMeshGenerator>();
            if (screenMesh != null)
                target = screenMesh.transform;
            else
                Debug.LogWarning("[UIShader] OrbitCamera: 궤도 타겟을 찾을 수 없습니다.");
        }

        currentDist = distance;
        targetDist = distance;
        targetHAngle = currentHAngle;
        targetVAngle = currentVAngle;
        cruiseBaseVerticalAngle = currentVAngle;

        ApplyPositionDirect();
    }

    void LateUpdate()
    {
        if (target == null) return;

        HandleGlobalInput();

        if (isTransitioning)
            UpdateTransition();
        else if (autoCruise)
            UpdateAutoCruise();
        else
            UpdateManual();

        ApplyPosition();
    }

    // ═══════════════════════════════════════════════════
    // 전역 입력 (모든 모드에서 작동)
    // ═══════════════════════════════════════════════════

    private void HandleGlobalInput()
    {
        // 프리셋 키 (1~4)
        for (int i = 0; i < presets.Length && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                TransitionToPreset(i);
        }

        // 자동 순항 토글 (C 키)
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (autoCruise)
                StopCruise();
            else
                StartCruise();
        }

        // 수동 입력 시 자동 순항 중단
        if (autoCruise && !isTransitioning)
        {
            if (Input.GetMouseButton(1) ||
                Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.001f)
            {
                StopCruise();
            }
        }
    }

    // ═══════════════════════════════════════════════════
    // 수동 조작 모드
    // ═══════════════════════════════════════════════════

    private void UpdateManual()
    {
        // 마우스 우클릭 드래그: 궤도 회전
        if (Input.GetMouseButton(1))
        {
            targetHAngle += Input.GetAxis("Mouse X") * rotationSpeed;
            targetVAngle -= Input.GetAxis("Mouse Y") * rotationSpeed;
            targetVAngle = Mathf.Clamp(targetVAngle, minVerticalAngle, maxVerticalAngle);
            ActivePresetIndex = -1;
        }

        // 스크롤: 줌
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            targetDist -= scroll * zoomSpeed;
            targetDist = Mathf.Clamp(targetDist, minDistance, maxDistance);
            ActivePresetIndex = -1;
        }

        // 부드러운 보간
        float lerpFactor = Time.deltaTime * smoothSpeed;
        currentHAngle = Mathf.Lerp(currentHAngle, targetHAngle, lerpFactor);
        currentVAngle = Mathf.Lerp(currentVAngle, targetVAngle, lerpFactor);
        currentDist = Mathf.Lerp(currentDist, targetDist, lerpFactor);
    }

    // ═══════════════════════════════════════════════════
    // 자동 순항 모드
    // ═══════════════════════════════════════════════════

    private void UpdateAutoCruise()
    {
        targetHAngle += cruiseSpeed * Time.deltaTime;
        targetVAngle = cruiseBaseVerticalAngle +
            Mathf.Sin(Time.time * cruiseVerticalFrequency * Mathf.PI * 2f)
            * cruiseVerticalAmplitude;

        // 시네마틱 느낌: 느린 보간
        float lerpFactor = Time.deltaTime * smoothSpeed * 0.5f;
        currentHAngle = Mathf.Lerp(currentHAngle, targetHAngle, lerpFactor);
        currentVAngle = Mathf.Lerp(currentVAngle, targetVAngle, lerpFactor);
        currentDist = Mathf.Lerp(currentDist, targetDist, lerpFactor);
    }

    // ═══════════════════════════════════════════════════
    // 프리셋 전환 애니메이션
    // ═══════════════════════════════════════════════════

    private void UpdateTransition()
    {
        transitionProgress += Time.deltaTime / presetTransitionDuration;
        float t = transitionCurve.Evaluate(Mathf.Clamp01(transitionProgress));

        currentHAngle = Mathf.Lerp(transFromH, transToH, t);
        currentVAngle = Mathf.Lerp(transFromV, transToV, t);
        currentDist = Mathf.Lerp(transFromDist, transToDist, t);

        // target도 동기화 (전환 후 Lerp 점프 방지)
        targetHAngle = currentHAngle;
        targetVAngle = currentVAngle;
        targetDist = currentDist;

        if (transitionProgress >= 1f)
        {
            isTransitioning = false;
            ActivePresetIndex = transitionTargetPreset;
            Debug.Log($"[UIShader] 카메라 전환 완료: " +
                      $"{(transitionTargetPreset >= 0 ? presets[transitionTargetPreset].name : "사용자 지정")}");
        }
    }

    // ═══════════════════════════════════════════════════
    // 위치 적용
    // ═══════════════════════════════════════════════════

    private void ApplyPosition()
    {
        float hRad = currentHAngle * Mathf.Deg2Rad;
        float vRad = currentVAngle * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            Mathf.Sin(hRad) * Mathf.Cos(vRad) * currentDist,
            Mathf.Sin(vRad) * currentDist,
            Mathf.Cos(hRad) * Mathf.Cos(vRad) * currentDist
        );

        transform.position = target.position + offset;
        transform.LookAt(target.position);
    }

    private void ApplyPositionDirect()
    {
        currentHAngle = targetHAngle;
        currentVAngle = targetVAngle;
        currentDist = targetDist;
        ApplyPosition();
    }

    // ═══════════════════════════════════════════════════
    // 공개 API
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// AnimationCurve 기반 부드러운 프리셋 전환을 시작한다.
    /// 전환 중에도 호출하면 현재 위치에서 새 프리셋으로 재전환한다.
    /// </summary>
    public void TransitionToPreset(int index)
    {
        if (index < 0 || index >= presets.Length) return;

        CameraPreset preset = presets[index];

        isTransitioning = true;
        transitionProgress = 0f;
        transitionTargetPreset = index;

        transFromH = currentHAngle;
        transFromV = currentVAngle;
        transFromDist = currentDist;

        transToH = preset.horizontalAngle;
        transToV = preset.verticalAngle;
        transToDist = preset.distance;

        Debug.Log($"[UIShader] 카메라 전환 시작: {preset.name} ({presetTransitionDuration}s)");
    }

    /// <summary>지정된 프리셋을 즉시 적용한다 (전환 애니메이션 없음).</summary>
    public void ApplyPreset(int index)
    {
        if (index < 0 || index >= presets.Length) return;

        CameraPreset preset = presets[index];
        targetHAngle = preset.horizontalAngle;
        targetVAngle = preset.verticalAngle;
        targetDist = preset.distance;
        ActivePresetIndex = index;

        Debug.Log($"[UIShader] 카메라 프리셋: {preset.name}");
    }

    /// <summary>카메라를 즉시 지정 위치로 이동한다 (보간 없음).</summary>
    public void SetPositionImmediate(float hAngle, float vAngle, float dist)
    {
        targetHAngle = hAngle;
        targetVAngle = vAngle;
        targetDist = dist;
        currentHAngle = hAngle;
        currentVAngle = vAngle;
        currentDist = dist;
        ActivePresetIndex = -1;
        isTransitioning = false;

        if (target != null)
            ApplyPosition();
    }

    /// <summary>자동 순항을 시작한다. 현재 수직 각도를 기준으로 진동한다.</summary>
    public void StartCruise()
    {
        autoCruise = true;
        cruiseBaseVerticalAngle = currentVAngle;
        ActivePresetIndex = -1;
        Debug.Log("[UIShader] 자동 순항: ON");
    }

    /// <summary>자동 순항을 중단한다.</summary>
    public void StopCruise()
    {
        autoCruise = false;
        Debug.Log("[UIShader] 자동 순항: OFF");
    }
}
