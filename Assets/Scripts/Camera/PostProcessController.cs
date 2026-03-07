// Assets/Scripts/Camera/PostProcessController.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 5 — HDRP 후처리 컨트롤러
// ══════════════════════════════════════════════════════════════════════
//
// HDRP Volume Profile을 코드에서 생성하여 시네마틱 후처리를 적용한다.
// 카메라 거리에 따라 DOF를 자동 활성/비활성화한다.
//
// 후처리 구성:
//   - Bloom: 밝은 스크린 영역에서 빛 번짐
//   - Vignette: 시네마틱 프레이밍
//   - Tonemapping: ACES (HDR → LDR 자연 변환)
//   - Color Adjustments: 콘트라스트/채도 미세 조정
//   - Film Grain: 필름 질감 (은은하게)
//   - Depth of Field: 클로즈업 시에만 활성화

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[RequireComponent(typeof(Volume))]
public class PostProcessController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 참조
    // ═══════════════════════════════════════════════════

    [Header("References")]
    [Tooltip("카메라 컨트롤러 (DOF 거리 계산용)")]
    [SerializeField] private OrbitCameraController cameraController;

    // ═══════════════════════════════════════════════════
    // Bloom
    // ═══════════════════════════════════════════════════

    [Header("Bloom")]
    [SerializeField, Range(0f, 1f)] private float bloomIntensity = 0.15f;
    [SerializeField, Range(0f, 2f)] private float bloomThreshold = 0.9f;
    [SerializeField, Range(0f, 1f)] private float bloomScatter = 0.7f;

    // ═══════════════════════════════════════════════════
    // Vignette
    // ═══════════════════════════════════════════════════

    [Header("Vignette")]
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.3f;
    [SerializeField, Range(0f, 1f)] private float vignetteSmoothness = 0.5f;

    // ═══════════════════════════════════════════════════
    // Color Adjustments
    // ═══════════════════════════════════════════════════

    [Header("Color Adjustments")]
    [SerializeField, Range(-100f, 100f)] private float contrast = 10f;
    [SerializeField, Range(-100f, 100f)] private float saturation = 5f;

    // ═══════════════════════════════════════════════════
    // Film Grain
    // ═══════════════════════════════════════════════════

    [Header("Film Grain")]
    [SerializeField, Range(0f, 1f)] private float filmGrainIntensity = 0.08f;

    // ═══════════════════════════════════════════════════
    // Depth of Field
    // ═══════════════════════════════════════════════════

    [Header("Depth of Field")]
    [Tooltip("DOF가 활성화되는 카메라-타겟 거리 임계값 (이하일 때 활성화)")]
    [SerializeField] private float dofActivationDistance = 6f;

    [Tooltip("근접 블러 샘플 수")]
    [SerializeField, Range(3, 8)] private int dofNearSampleCount = 5;

    [Tooltip("원거리 블러 샘플 수")]
    [SerializeField, Range(3, 8)] private int dofFarSampleCount = 7;

    [Tooltip("원거리 최대 블러 반경")]
    [SerializeField, Range(0f, 16f)] private float dofFarMaxBlur = 8f;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private Volume volume;
    private VolumeProfile profile;

    private Bloom bloom;
    private Vignette vignette;
    private Tonemapping tonemapping;
    private ColorAdjustments colorAdjustments;
    private FilmGrain filmGrain;
    private DepthOfField depthOfField;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        SetupVolume();
    }

    void Update()
    {
        UpdateDepthOfField();
    }

    void OnDestroy()
    {
        if (profile != null)
        {
            if (Application.isPlaying)
                Destroy(profile);
            else
                DestroyImmediate(profile);
        }
    }

    // ═══════════════════════════════════════════════════
    // Volume 설정
    // ═══════════════════════════════════════════════════

    private void SetupVolume()
    {
        volume = GetComponent<Volume>();
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.profile = profile;
        volume.isGlobal = true;
        volume.priority = 1f;

        SetupBloom();
        SetupVignette();
        SetupTonemapping();
        SetupColorAdjustments();
        SetupFilmGrain();
        SetupDepthOfField();

        Debug.Log("[UIShader] 후처리 Volume 설정 완료");
    }

    private void SetupBloom()
    {
        bloom = profile.Add<Bloom>();
        bloom.intensity.Override(bloomIntensity);
        bloom.threshold.Override(bloomThreshold);
        bloom.scatter.Override(bloomScatter);
    }

    private void SetupVignette()
    {
        vignette = profile.Add<Vignette>();
        vignette.intensity.Override(vignetteIntensity);
        vignette.smoothness.Override(vignetteSmoothness);
    }

    private void SetupTonemapping()
    {
        tonemapping = profile.Add<Tonemapping>();
        tonemapping.mode.Override(TonemappingMode.ACES);
    }

    private void SetupColorAdjustments()
    {
        colorAdjustments = profile.Add<ColorAdjustments>();
        colorAdjustments.contrast.Override(contrast);
        colorAdjustments.saturation.Override(saturation);
    }

    private void SetupFilmGrain()
    {
        filmGrain = profile.Add<FilmGrain>();
        filmGrain.type.Override(FilmGrainLookup.Medium1);
        filmGrain.intensity.Override(filmGrainIntensity);
    }

    private void SetupDepthOfField()
    {
        depthOfField = profile.Add<DepthOfField>();
        depthOfField.focusMode.Override(DepthOfFieldMode.Manual);
        depthOfField.nearSampleCount = dofNearSampleCount;
        depthOfField.farSampleCount = dofFarSampleCount;
        depthOfField.farMaxBlur = dofFarMaxBlur;
        depthOfField.active = false; // 기본 비활성
    }

    // ═══════════════════════════════════════════════════
    // DOF 동적 제어
    // ═══════════════════════════════════════════════════

    private void UpdateDepthOfField()
    {
        if (depthOfField == null || cameraController == null) return;
        if (cameraController.target == null) return;

        float dist = Vector3.Distance(
            cameraController.transform.position,
            cameraController.target.position
        );

        bool shouldActivate = dist < dofActivationDistance;
        depthOfField.active = shouldActivate;

        if (shouldActivate)
        {
            depthOfField.focusDistance.Override(dist);
        }
    }

    // ═══════════════════════════════════════════════════
    // 공개 API
    // ═══════════════════════════════════════════════════

    /// <summary>블룸 강도를 런타임에서 변경한다.</summary>
    public void SetBloomIntensity(float intensity)
    {
        if (bloom != null)
            bloom.intensity.Override(intensity);
    }

    /// <summary>비네트 강도를 런타임에서 변경한다.</summary>
    public void SetVignetteIntensity(float intensity)
    {
        if (vignette != null)
            vignette.intensity.Override(intensity);
    }

    /// <summary>DOF 활성화 거리를 변경한다.</summary>
    public void SetDOFActivationDistance(float distance)
    {
        dofActivationDistance = distance;
    }
}
