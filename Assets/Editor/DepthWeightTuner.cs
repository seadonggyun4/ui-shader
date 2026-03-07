// Assets/Editor/DepthWeightTuner.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — 깊이 가중치 실시간 튜닝 에디터
// ══════════════════════════════════════════════════════════════════════
//
// DepthWeightPreset ScriptableObject의 커스텀 인스펙터.
// 6개 가중치를 슬라이더로 조정하고, 가중치 합계 시각화,
// 1.0 정규화 버튼, 실시간 CEF 적용(Phase 1+) 기능을 제공한다.
//
// 확장:
//   - 프리셋 간 보간 (A/B 블렌딩)
//   - 가중치 히스토리 (Undo 지원)
//   - 비주얼 바 차트로 가중치 분포 시각화

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DepthWeightPreset))]
public class DepthWeightTunerEditor : Editor
{
    // ═══════════════════════════════════════════════════
    // 직렬화 프로퍼티
    // ═══════════════════════════════════════════════════

    private SerializedProperty propPresetName;
    private SerializedProperty propDescription;
    private SerializedProperty propDomDepth;
    private SerializedProperty propStackContext;
    private SerializedProperty propBoxShadow;
    private SerializedProperty propTransformZ;
    private SerializedProperty propOpacity;
    private SerializedProperty propPosition;

    // 신호 표시 정보
    private static readonly SignalInfo[] signalInfos = new SignalInfo[]
    {
        new SignalInfo("domDepth",     "DOM 중첩 깊이",    "w1", new Color(0.2f, 0.6f, 1f)),
        new SignalInfo("stackContext", "쌓임 맥락 (z-index)", "w2", new Color(0.4f, 0.8f, 0.4f)),
        new SignalInfo("boxShadow",    "Box Shadow 고도",  "w3", new Color(1f, 0.6f, 0.2f)),
        new SignalInfo("transformZ",   "Transform Z",      "w4", new Color(0.8f, 0.4f, 0.8f)),
        new SignalInfo("opacity",      "불투명도 힌트",     "w5", new Color(0.6f, 0.6f, 0.6f)),
        new SignalInfo("position",     "배치 유형",         "w6", new Color(1f, 0.8f, 0.3f)),
    };

    private struct SignalInfo
    {
        public string propName;
        public string label;
        public string tag;
        public Color color;

        public SignalInfo(string propName, string label, string tag, Color color)
        {
            this.propName = propName;
            this.label = label;
            this.tag = tag;
            this.color = color;
        }
    }

    // ═══════════════════════════════════════════════════
    // 초기화
    // ═══════════════════════════════════════════════════

    void OnEnable()
    {
        propPresetName = serializedObject.FindProperty("presetName");
        propDescription = serializedObject.FindProperty("description");
        propDomDepth = serializedObject.FindProperty("domDepth");
        propStackContext = serializedObject.FindProperty("stackContext");
        propBoxShadow = serializedObject.FindProperty("boxShadow");
        propTransformZ = serializedObject.FindProperty("transformZ");
        propOpacity = serializedObject.FindProperty("opacity");
        propPosition = serializedObject.FindProperty("position");
    }

    // ═══════════════════════════════════════════════════
    // 인스펙터 GUI
    // ═══════════════════════════════════════════════════

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var preset = (DepthWeightPreset)target;

        // ─── 헤더 ───
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Depth Weight Tuner", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        // ─── 프리셋 정보 ───
        EditorGUILayout.PropertyField(propPresetName, new GUIContent("Preset Name"));
        EditorGUILayout.PropertyField(propDescription, new GUIContent("Description"));
        EditorGUILayout.Space(8);

        // ─── 가중치 슬라이더 + 시각적 바 ───
        EditorGUILayout.LabelField("Signal Weights", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        SerializedProperty[] weightProps = {
            propDomDepth, propStackContext, propBoxShadow,
            propTransformZ, propOpacity, propPosition
        };

        for (int i = 0; i < signalInfos.Length; i++)
        {
            DrawWeightSlider(signalInfos[i], weightProps[i]);
        }

        // ─── 합계 표시 ───
        EditorGUILayout.Space(8);
        DrawSumBar(preset);

        // ─── 액션 버튼 ───
        EditorGUILayout.Space(8);
        DrawActionButtons(preset);

        // ─── 프리셋 빠른 적용 ───
        EditorGUILayout.Space(8);
        DrawPresetButtons(preset);

        // ─── JSON 미리보기 ───
        EditorGUILayout.Space(8);
        DrawJSONPreview(preset);

        serializedObject.ApplyModifiedProperties();
    }

    // ═══════════════════════════════════════════════════
    // 개별 UI 요소
    // ═══════════════════════════════════════════════════

    private void DrawWeightSlider(SignalInfo info, SerializedProperty prop)
    {
        EditorGUILayout.BeginHorizontal();

        // 태그 라벨 (w1~w6)
        var tagStyle = new GUIStyle(EditorStyles.miniLabel);
        tagStyle.fixedWidth = 24;
        tagStyle.alignment = TextAnchor.MiddleCenter;
        EditorGUILayout.LabelField(info.tag, tagStyle, GUILayout.Width(24));

        // 슬라이더
        EditorGUILayout.Slider(prop, 0f, 1f, new GUIContent(info.label));

        // 컬러 바 (비중 시각화)
        Rect barRect = GUILayoutUtility.GetRect(60, 16, GUILayout.Width(60));
        float fill = prop.floatValue;
        EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width, barRect.height),
                          new Color(0.15f, 0.15f, 0.15f));
        EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height),
                          info.color);

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSumBar(DepthWeightPreset preset)
    {
        float sum = preset.Sum;
        bool isNormalized = preset.IsNormalized;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Weight Sum:", GUILayout.Width(80));

        // 합계 값 (색상 코딩)
        Color prevColor = GUI.color;
        GUI.color = isNormalized ? new Color(0.3f, 0.9f, 0.3f) : new Color(1f, 0.8f, 0.3f);
        EditorGUILayout.LabelField(sum.ToString("F4"), EditorStyles.boldLabel, GUILayout.Width(60));
        GUI.color = prevColor;

        // 상태 아이콘
        string status = isNormalized ? "OK" : (sum > 1f ? "Over" : "Under");
        EditorGUILayout.LabelField(status, GUILayout.Width(40));

        EditorGUILayout.EndHorizontal();

        if (!isNormalized)
        {
            EditorGUILayout.HelpBox(
                $"가중치 합계가 {sum:F3}입니다. 1.0으로 정규화를 권장합니다.\n" +
                "정규화하지 않아도 동작하지만, 깊이 범위가 0~1을 벗어날 수 있습니다.",
                MessageType.Warning);
        }
    }

    private void DrawActionButtons(DepthWeightPreset preset)
    {
        EditorGUILayout.BeginHorizontal();

        // 정규화 버튼
        if (GUILayout.Button("Normalize to 1.0", GUILayout.Height(24)))
        {
            Undo.RecordObject(preset, "Normalize Weights");
            preset.Normalize();
            EditorUtility.SetDirty(preset);
        }

        // 리셋 (Balanced) 버튼
        if (GUILayout.Button("Reset to Balanced", GUILayout.Height(24)))
        {
            Undo.RecordObject(preset, "Reset to Balanced");
            var balanced = DepthWeightPreset.CreateBalanced();
            preset.CopyFrom(balanced);
            DestroyImmediate(balanced);
            EditorUtility.SetDirty(preset);
        }

        EditorGUILayout.EndHorizontal();

        // 실시간 CEF 적용 버튼 (Phase 1+)
        EditorGUILayout.Space(4);
        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("Apply to Runtime (CEF)", GUILayout.Height(28)))
        {
            ApplyToRuntime(preset);
        }
        GUI.enabled = true;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "런타임 적용은 Play 모드에서만 가능합니다. " +
                "CEF 브라우저가 활성화된 상태에서 사용하세요.",
                MessageType.Info);
        }
    }

    private void DrawPresetButtons(DepthWeightPreset preset)
    {
        EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Material Design"))
        {
            Undo.RecordObject(preset, "Apply Material Design Preset");
            var p = DepthWeightPreset.CreateMaterialDesign();
            preset.CopyFrom(p);
            preset.presetName = p.presetName;
            preset.description = p.description;
            DestroyImmediate(p);
            EditorUtility.SetDirty(preset);
        }

        if (GUILayout.Button("Flat Design"))
        {
            Undo.RecordObject(preset, "Apply Flat Design Preset");
            var p = DepthWeightPreset.CreateFlatDesign();
            preset.CopyFrom(p);
            preset.presetName = p.presetName;
            preset.description = p.description;
            DestroyImmediate(p);
            EditorUtility.SetDirty(preset);
        }

        if (GUILayout.Button("Balanced"))
        {
            Undo.RecordObject(preset, "Apply Balanced Preset");
            var p = DepthWeightPreset.CreateBalanced();
            preset.CopyFrom(p);
            preset.presetName = p.presetName;
            preset.description = p.description;
            DestroyImmediate(p);
            EditorUtility.SetDirty(preset);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawJSONPreview(DepthWeightPreset preset)
    {
        EditorGUILayout.LabelField("JSON Preview (JS Injection)", EditorStyles.boldLabel);
        string json = preset.ToJSON();

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextArea(json, GUILayout.Height(36));
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Copy JSON to Clipboard", GUILayout.Height(20)))
        {
            EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log("[UIShader] JSON 클립보드에 복사됨: " + json);
        }
    }

    // ═══════════════════════════════════════════════════
    // 런타임 적용
    // ═══════════════════════════════════════════════════

    private void ApplyToRuntime(DepthWeightPreset preset)
    {
        // Phase 1+: CEFBridge가 존재하면 가중치를 JS로 전송
        // Phase 0: CEFBridge 없이도 에러 없이 경고만 출력

        // CEFBridge 타입을 동적으로 찾기 (Phase 1 미구현 시 null)
        var bridgeType = System.Type.GetType("CEFBridge");
        if (bridgeType != null)
        {
            var bridge = FindObjectOfType(bridgeType) as MonoBehaviour;
            if (bridge != null)
            {
                // 리플렉션으로 ExecuteJavaScript 호출 (Phase 1 의존성 제거)
                var method = bridgeType.GetMethod("ExecuteJavaScript",
                    new System.Type[] { typeof(string) });

                if (method != null)
                {
                    string js = "window.__UIShader.updateWeights(" + preset.ToJSON() + ");" +
                                "window.__UIShader.renderDepthMap();";
                    method.Invoke(bridge, new object[] { js });
                    Debug.Log($"[UIShader] 가중치 적용됨: {preset.presetName}");
                    return;
                }
            }
        }

        Debug.LogWarning("[UIShader] CEFBridge를 찾을 수 없습니다. " +
                        "Phase 1 구현 후 실시간 적용이 가능합니다. " +
                        "JSON 값은 정상 생성됩니다: " + preset.ToJSON());
    }
}
