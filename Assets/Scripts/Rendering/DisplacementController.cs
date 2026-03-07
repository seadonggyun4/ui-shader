// Assets/Scripts/Rendering/DisplacementController.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — 변위 파이프라인 런타임 제어기
// ══════════════════════════════════════════════════════════════════════
//
// 깊이 맵 기반 변위의 런타임 파라미터를 관리하며,
// 카메라 거리에 따른 자동 LOD 전환을 수행한다.
//
// 책임 범위:
//   - 셰이더 파라미터 동기화 (edgeFalloff, emissionIntensity)
//   - 카메라 거리 기반 자동 LOD 전환
//   - LOD 히스테리시스 (불필요한 전환 방지)

using System;
using UnityEngine;

public class DisplacementController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 인스펙터 참조
    // ═══════════════════════════════════════════════════

    [Header("References")]
    [SerializeField] private UIShaderConfig config;
    [SerializeField] private ScreenMeshGenerator screenMesh;
    [SerializeField] private Camera mainCamera;

    [Header("Auto LOD")]
    [Tooltip("카메라 거리에 따른 자동 LOD 전환")]
    [SerializeField] private bool autoLOD = true;

    [Tooltip("High LOD 최대 거리")]
    [SerializeField] private float highLODDistance = 8f;

    [Tooltip("Medium LOD 최대 거리 (초과 시 Low)")]
    [SerializeField] private float mediumLODDistance = 15f;

    [Tooltip("LOD 전환 히스테리시스 (경계에서 떨림 방지)")]
    [SerializeField] private float lodHysteresis = 0.5f;

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private Material screenMaterial;

    // 셰이더 프로퍼티 ID 캐싱
    private static readonly int EdgeFalloffId = Shader.PropertyToID("_EdgeFalloff");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int DisplacementScaleId = Shader.PropertyToID("_DisplacementScale");
    private static readonly int DisplacementBiasId = Shader.PropertyToID("_DisplacementBias");

    // ═══════════════════════════════════════════════════
    // 공개 프로퍼티
    // ═══════════════════════════════════════════════════

    /// <summary>자동 LOD 활성 여부</summary>
    public bool AutoLOD
    {
        get => autoLOD;
        set => autoLOD = value;
    }

    /// <summary>현재 카메라~스크린 거리</summary>
    public float CurrentDistance { get; private set; }

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        if (!ValidateReferences()) return;

        screenMaterial = screenMesh.GetComponent<MeshRenderer>().material;
        ApplyShaderParameters();
    }

    void Update()
    {
        if (screenMaterial == null || config == null) return;

        ApplyShaderParameters();
        UpdateAutoLOD();
    }

    // ═══════════════════════════════════════════════════
    // 셰이더 파라미터 동기화
    // ═══════════════════════════════════════════════════

    private void ApplyShaderParameters()
    {
        screenMaterial.SetFloat(DisplacementScaleId, config.displacementScale);
        screenMaterial.SetFloat(DisplacementBiasId, config.displacementBias);
        screenMaterial.SetFloat(EdgeFalloffId, config.edgeFalloff);
        screenMaterial.SetFloat(EmissionIntensityId, config.emissionIntensity);
    }

    // ═══════════════════════════════════════════════════
    // 자동 LOD
    // ═══════════════════════════════════════════════════

    private void UpdateAutoLOD()
    {
        if (!autoLOD || mainCamera == null || screenMesh == null) return;

        CurrentDistance = Vector3.Distance(
            mainCamera.transform.position,
            screenMesh.transform.position
        );

        ScreenMeshGenerator.LODLevel targetLOD = EvaluateLODForDistance(CurrentDistance);

        if (targetLOD != screenMesh.CurrentLOD)
        {
            screenMesh.SetLOD(targetLOD);
        }
    }

    /// <summary>
    /// 거리에 따른 LOD 레벨을 히스테리시스 적용하여 결정한다.
    /// 현재 LOD보다 높은 LOD로 전환하려면 (hysteresis)만큼 더 가까워야 한다.
    /// 이로써 경계 거리에서 프레임마다 LOD가 오가는 현상을 방지한다.
    /// </summary>
    private ScreenMeshGenerator.LODLevel EvaluateLODForDistance(float distance)
    {
        ScreenMeshGenerator.LODLevel currentLOD = screenMesh.CurrentLOD;
        float h = lodHysteresis;

        // 현재 LOD 기준으로 전환 임계값 결정
        switch (currentLOD)
        {
            case ScreenMeshGenerator.LODLevel.High:
                // High → Medium: 멀어져야 전환
                if (distance > highLODDistance + h)
                {
                    if (distance > mediumLODDistance + h)
                        return ScreenMeshGenerator.LODLevel.Low;
                    return ScreenMeshGenerator.LODLevel.Medium;
                }
                return ScreenMeshGenerator.LODLevel.High;

            case ScreenMeshGenerator.LODLevel.Medium:
                // Medium → High: 가까워져야 전환 (더 엄격)
                if (distance < highLODDistance - h)
                    return ScreenMeshGenerator.LODLevel.High;
                // Medium → Low: 멀어져야 전환
                if (distance > mediumLODDistance + h)
                    return ScreenMeshGenerator.LODLevel.Low;
                return ScreenMeshGenerator.LODLevel.Medium;

            case ScreenMeshGenerator.LODLevel.Low:
                // Low → Medium: 가까워져야 전환 (더 엄격)
                if (distance < mediumLODDistance - h)
                {
                    if (distance < highLODDistance - h)
                        return ScreenMeshGenerator.LODLevel.High;
                    return ScreenMeshGenerator.LODLevel.Medium;
                }
                return ScreenMeshGenerator.LODLevel.Low;
        }

        return currentLOD;
    }

    // ═══════════════════════════════════════════════════
    // 검증
    // ═══════════════════════════════════════════════════

    private bool ValidateReferences()
    {
        bool valid = true;

        if (config == null)
        {
            Debug.LogError("[UIShader] DisplacementController: UIShaderConfig가 할당되지 않았습니다.");
            valid = false;
        }
        if (screenMesh == null)
        {
            Debug.LogError("[UIShader] DisplacementController: ScreenMeshGenerator가 할당되지 않았습니다.");
            valid = false;
        }
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("[UIShader] DisplacementController: 메인 카메라를 찾을 수 없습니다. 자동 LOD가 비활성화됩니다.");
                autoLOD = false;
            }
        }

        return valid;
    }
}
