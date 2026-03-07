// Assets/Scripts/DepthMap/DepthWeightPreset.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — 깊이 가중치 프리셋 ScriptableObject
// ══════════════════════════════════════════════════════════════════════
//
// 6종 CSS/DOM 깊이 신호의 가중치를 에셋으로 관리한다.
// 디자인 시스템별 최적 가중치를 프리셋으로 저장하고,
// 에디터 인스펙터 또는 런타임에서 실시간 전환/조정한다.
//
// depth-extractor.js의 SignalRegistry.updateWeights()와
// JSON 직렬화를 통해 양방향 동기화된다.

using UnityEngine;

[CreateAssetMenu(fileName = "DepthWeightPreset", menuName = "UIShader/Depth Weight Preset")]
public class DepthWeightPreset : ScriptableObject
{
    // ═══════════════════════════════════════════════════
    // 프리셋 메타데이터
    // ═══════════════════════════════════════════════════

    [Header("Preset Info")]
    [Tooltip("프리셋 식별 이름")]
    public string presetName = "Custom";

    [Tooltip("프리셋 설명")]
    [TextArea(2, 4)]
    public string description = "";

    // ═══════════════════════════════════════════════════
    // 가중치 (합계 = 1.0 권장)
    // ═══════════════════════════════════════════════════

    [Header("Signal Weights (sum = 1.0)")]

    [Tooltip("w1: DOM 중첩 깊이 — 문서 구조의 계층적 기저선")]
    [Range(0f, 1f)]
    public float domDepth = 0.25f;

    [Tooltip("w2: 쌓임 맥락 (z-index) — CSS가 명시한 시각적 순서")]
    [Range(0f, 1f)]
    public float stackContext = 0.25f;

    [Tooltip("w3: Box Shadow 고도값 — Material Design elevation (최고 가중치 권장)")]
    [Range(0f, 1f)]
    public float boxShadow = 0.30f;

    [Tooltip("w4: Transform Z축 — 명시적 3차원 위치 지정")]
    [Range(0f, 1f)]
    public float transformZ = 0.10f;

    [Tooltip("w5: 불투명도 힌트 — 오버레이/배경막 탐지")]
    [Range(0f, 1f)]
    public float opacity = 0.05f;

    [Tooltip("w6: 배치 유형 — fixed/sticky 내비게이션, absolute 팝업")]
    [Range(0f, 1f)]
    public float position = 0.05f;

    // ═══════════════════════════════════════════════════
    // 유틸리티
    // ═══════════════════════════════════════════════════

    /// <summary>가중치 합계</summary>
    public float Sum => domDepth + stackContext + boxShadow + transformZ + opacity + position;

    /// <summary>가중치 합계가 1.0에 근접한지 확인</summary>
    public bool IsNormalized => Mathf.Abs(Sum - 1f) < 0.01f;

    /// <summary>
    /// 가중치를 1.0으로 정규화한다.
    /// </summary>
    public void Normalize()
    {
        float sum = Sum;
        if (sum <= 0f) return;

        float factor = 1f / sum;
        domDepth *= factor;
        stackContext *= factor;
        boxShadow *= factor;
        transformZ *= factor;
        opacity *= factor;
        position *= factor;
    }

    /// <summary>
    /// 가중치를 JSON 문자열로 변환한다.
    /// depth-extractor.js의 updateWeights()에 전달하기 위한 형식.
    /// </summary>
    public string ToJSON()
    {
        return string.Format(
            "{{\"domDepth\":{0:F4},\"stackContext\":{1:F4},\"boxShadow\":{2:F4}," +
            "\"transformZ\":{3:F4},\"opacity\":{4:F4},\"position\":{5:F4}}}",
            domDepth, stackContext, boxShadow, transformZ, opacity, position
        );
    }

    /// <summary>
    /// JSON 문자열로부터 가중치를 로드한다.
    /// </summary>
    public void FromJSON(string json)
    {
        // 간단한 JSON 파싱 (JsonUtility 대신 수동 파싱으로 의존성 최소화)
        domDepth = ExtractFloat(json, "domDepth", domDepth);
        stackContext = ExtractFloat(json, "stackContext", stackContext);
        boxShadow = ExtractFloat(json, "boxShadow", boxShadow);
        transformZ = ExtractFloat(json, "transformZ", transformZ);
        opacity = ExtractFloat(json, "opacity", opacity);
        position = ExtractFloat(json, "position", position);
    }

    /// <summary>
    /// 다른 프리셋의 가중치를 복사한다.
    /// </summary>
    public void CopyFrom(DepthWeightPreset other)
    {
        if (other == null) return;
        domDepth = other.domDepth;
        stackContext = other.stackContext;
        boxShadow = other.boxShadow;
        transformZ = other.transformZ;
        opacity = other.opacity;
        position = other.position;
    }

    /// <summary>
    /// 두 프리셋 사이를 보간한다.
    /// 프리셋 전환 애니메이션에 활용.
    /// </summary>
    public static DepthWeightPreset Lerp(DepthWeightPreset a, DepthWeightPreset b, float t)
    {
        var result = CreateInstance<DepthWeightPreset>();
        t = Mathf.Clamp01(t);
        result.domDepth = Mathf.Lerp(a.domDepth, b.domDepth, t);
        result.stackContext = Mathf.Lerp(a.stackContext, b.stackContext, t);
        result.boxShadow = Mathf.Lerp(a.boxShadow, b.boxShadow, t);
        result.transformZ = Mathf.Lerp(a.transformZ, b.transformZ, t);
        result.opacity = Mathf.Lerp(a.opacity, b.opacity, t);
        result.position = Mathf.Lerp(a.position, b.position, t);
        return result;
    }

    // ═══════════════════════════════════════════════════
    // 팩토리 메서드 — 기본 프리셋 3종
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Material Design 프리셋 (box-shadow 중심).
    /// Google Material Design, Vuetify, MUI 등 그림자 기반 디자인 시스템에 최적화.
    /// </summary>
    public static DepthWeightPreset CreateMaterialDesign()
    {
        var preset = CreateInstance<DepthWeightPreset>();
        preset.presetName = "Material Design";
        preset.description = "Box-shadow 중심. Google Material Design, MUI, Vuetify 등 그림자 기반 디자인에 최적화.";
        preset.domDepth = 0.15f;
        preset.stackContext = 0.20f;
        preset.boxShadow = 0.40f;
        preset.transformZ = 0.10f;
        preset.opacity = 0.05f;
        preset.position = 0.10f;
        return preset;
    }

    /// <summary>
    /// Flat Design 프리셋 (DOM 구조 중심).
    /// Apple, 미니멀 디자인 등 그림자 없는 UI에 최적화.
    /// </summary>
    public static DepthWeightPreset CreateFlatDesign()
    {
        var preset = CreateInstance<DepthWeightPreset>();
        preset.presetName = "Flat Design";
        preset.description = "DOM 구조 중심. Apple, 미니멀 디자인 등 그림자 없는 UI에 최적화.";
        preset.domDepth = 0.45f;
        preset.stackContext = 0.25f;
        preset.boxShadow = 0.05f;
        preset.transformZ = 0.05f;
        preset.opacity = 0.10f;
        preset.position = 0.10f;
        return preset;
    }

    /// <summary>
    /// Balanced 프리셋 (균형 가중치).
    /// 범용 목적. 대부분의 웹사이트에 합리적인 결과.
    /// </summary>
    public static DepthWeightPreset CreateBalanced()
    {
        var preset = CreateInstance<DepthWeightPreset>();
        preset.presetName = "Balanced";
        preset.description = "범용 균형 가중치. 대부분의 웹사이트에서 합리적인 결과.";
        preset.domDepth = 0.25f;
        preset.stackContext = 0.25f;
        preset.boxShadow = 0.30f;
        preset.transformZ = 0.10f;
        preset.opacity = 0.05f;
        preset.position = 0.05f;
        return preset;
    }

    // ═══════════════════════════════════════════════════
    // 내부 유틸
    // ═══════════════════════════════════════════════════

    private static float ExtractFloat(string json, string key, float fallback)
    {
        string searchKey = "\"" + key + "\":";
        int idx = json.IndexOf(searchKey);
        if (idx < 0) return fallback;

        int valueStart = idx + searchKey.Length;
        int valueEnd = json.IndexOfAny(new char[] { ',', '}' }, valueStart);
        if (valueEnd < 0) valueEnd = json.Length;

        string valueStr = json.Substring(valueStart, valueEnd - valueStart).Trim();
        if (float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float result))
        {
            return result;
        }
        return fallback;
    }

    private void OnValidate()
    {
        domDepth = Mathf.Clamp01(domDepth);
        stackContext = Mathf.Clamp01(stackContext);
        boxShadow = Mathf.Clamp01(boxShadow);
        transformZ = Mathf.Clamp01(transformZ);
        opacity = Mathf.Clamp01(opacity);
        position = Mathf.Clamp01(position);
    }
}
