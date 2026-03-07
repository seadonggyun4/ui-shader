// Assets/Scripts/Demo/DemoScenarios.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 5 — 데모 시나리오 데이터
// ══════════════════════════════════════════════════════════════════════
//
// ScriptableObject 기반 데모 시나리오 목록.
// 각 시나리오는 URL, 카메라 앵글, 변위 스케일, 가중치 프리셋을 포함한다.
//
// 사용법:
//   1. Create > UIShader > Demo Scenarios 로 에셋 생성
//   2. DemoAutoPlay 컴포넌트에 할당
//   3. 시나리오 추가/편집은 인스펙터에서 수행
//
// 확장:
//   - 새 시나리오 추가: 인스펙터에서 리스트에 항목 추가
//   - 커스텀 가중치: DepthWeightPreset 에셋을 생성하여 할당
//   - 프리셋별 후처리 파라미터는 DemoAutoPlay에서 처리

using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "DemoScenarios", menuName = "UIShader/Demo Scenarios")]
public class DemoScenarios : ScriptableObject
{
    [System.Serializable]
    public class Scenario
    {
        [Tooltip("시나리오 이름 (UI 표시용)")]
        public string name;

        [Tooltip("로드할 웹 페이지 URL")]
        public string url;

        [Tooltip("시나리오 설명")]
        [TextArea(2, 4)]
        public string description;

        [Tooltip("최적 깊이 가중치 프리셋 (null이면 현재 유지)")]
        public DepthWeightPreset weightPreset;

        [Tooltip("권장 카메라 프리셋 인덱스 (-1이면 현재 유지)")]
        public int cameraPresetIndex = 0;

        [Tooltip("권장 변위 스케일")]
        [Range(0f, 2f)]
        public float displacementScale = 0.5f;
    }

    [Tooltip("데모 시나리오 목록")]
    public List<Scenario> scenarios = new List<Scenario>
    {
        new Scenario
        {
            name = "Material Design 모달",
            url = "https://mui.com/material-ui/react-dialog/",
            description = "클릭 시 모달 팝업 → 메시 돌출 + 그림자 심화 + 조명 변화",
            cameraPresetIndex = 0,
            displacementScale = 0.6f
        },
        new Scenario
        {
            name = "다크 모드 토글",
            url = "https://tailwindcss.com",
            description = "다크/라이트 모드 전환 → 전체 조명 환경 극적 변화",
            cameraPresetIndex = 0,
            displacementScale = 0.4f
        },
        new Scenario
        {
            name = "애니메이션 히어로",
            url = "https://stripe.com",
            description = "CSS 그라데이션 애니메이션 → 지속적인 색상/조명 변화",
            cameraPresetIndex = 1,
            displacementScale = 0.3f
        },
        new Scenario
        {
            name = "호버 카드 그리드",
            url = "https://mui.com/material-ui/react-card/",
            description = "카드 호버 → 개별 카드가 들려올라감 + 그림자 변화",
            cameraPresetIndex = 3,
            displacementScale = 0.7f
        },
        new Scenario
        {
            name = "대시보드",
            url = "https://mui.com/material-ui/getting-started/templates/dashboard/",
            description = "차트/그래프의 다색 조명 투사",
            cameraPresetIndex = 2,
            displacementScale = 0.5f
        }
    };

    /// <summary>시나리오 수</summary>
    public int Count => scenarios.Count;

    /// <summary>인덱스로 시나리오 접근 (범위 검증 포함)</summary>
    public Scenario GetScenario(int index)
    {
        if (index < 0 || index >= scenarios.Count) return null;
        return scenarios[index];
    }
}
