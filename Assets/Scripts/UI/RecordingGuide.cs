// Assets/Scripts/UI/RecordingGuide.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 6 — 녹화 가이드 오버레이
// ══════════════════════════════════════════════════════════════════════
//
// F5 키로 토글되는 녹화 가이드 오버레이.
// 영상 녹화 시 유용한 정보를 표시한다:
//   - 30초/60초 타임라인 가이드
//   - 현재 녹화 경과 시간
//   - 다음 키프레임까지 남은 시간
//   - 화면 구도 안전 영역 표시
//
// 컴포넌트 독립:
//   다른 시스템에 의존하지 않으므로 녹화 시에만 활성화하여 사용.

using UnityEngine;

public class RecordingGuide : MonoBehaviour
{
    // ═══════════════════════════════════════════════════
    // 설정
    // ═══════════════════════════════════════════════════

    [Header("Settings")]
    [Tooltip("녹화 가이드 표시 (F5 토글)")]
    [SerializeField] private bool showGuide = false;

    [Tooltip("타임라인 총 길이 (초)")]
    [SerializeField] private float timelineDuration = 30f;

    [Tooltip("안전 영역 마진 (화면 비율, 0~0.15)")]
    [SerializeField] [Range(0f, 0.15f)] private float safeAreaMargin = 0.05f;

    [Tooltip("안전 영역 표시")]
    [SerializeField] private bool showSafeArea = true;

    [Tooltip("3분할 격자 표시")]
    [SerializeField] private bool showThirdsGrid = false;

    // ═══════════════════════════════════════════════════
    // 타임라인 키프레임 (하이라이트 릴 구성)
    // ═══════════════════════════════════════════════════

    [System.Serializable]
    public class TimelineKeyframe
    {
        [Tooltip("키프레임 시작 시간 (초)")]
        public float time;

        [Tooltip("키프레임 설명")]
        public string description;

        public TimelineKeyframe(float time, string description)
        {
            this.time = time;
            this.description = description;
        }
    }

    [Header("Timeline")]
    [Tooltip("키프레임 목록")]
    [SerializeField] private TimelineKeyframe[] keyframes = new TimelineKeyframe[]
    {
        new TimelineKeyframe(0f,  "Start: Studio → URL Load"),
        new TimelineKeyframe(5f,  "Displacement Close-up"),
        new TimelineKeyframe(10f, "Floor Reflection + Colored Light"),
        new TimelineKeyframe(18f, "Modal Popup Interaction"),
        new TimelineKeyframe(23f, "Dark Mode Transition"),
        new TimelineKeyframe(28f, "Auto Cruise Cinematic")
    };

    // ═══════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════

    private bool isRecording;
    private float recordingStartTime;
    private float recordingElapsed;

    // 스타일 캐시
    private GUIStyle timerStyle;
    private GUIStyle keyframeStyle;
    private GUIStyle activeKeyframeStyle;
    private GUIStyle instructionStyle;
    private bool stylesInitialized;

    // 텍스처 (안전 영역 라인)
    private Texture2D lineTexture;

    /// <summary>녹화 중 여부</summary>
    public bool IsRecording => isRecording;

    /// <summary>녹화 경과 시간</summary>
    public float RecordingElapsed => recordingElapsed;

    // ═══════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════

    void Start()
    {
        lineTexture = new Texture2D(1, 1);
        lineTexture.SetPixel(0, 0, Color.white);
        lineTexture.Apply();
    }

    void Update()
    {
        // F5: 가이드 토글
        if (Input.GetKeyDown(KeyCode.F5))
            showGuide = !showGuide;

        // F6: 녹화 타이머 시작/정지
        if (Input.GetKeyDown(KeyCode.F6))
        {
            if (isRecording)
                StopRecording();
            else
                StartRecording();
        }

        // 녹화 타이머 업데이트
        if (isRecording)
            recordingElapsed = Time.realtimeSinceStartup - recordingStartTime;
    }

    void OnGUI()
    {
        if (!showGuide) return;
        EnsureStyles();

        if (showSafeArea)
            DrawSafeArea();

        if (showThirdsGrid)
            DrawThirdsGrid();

        DrawTimerDisplay();
        DrawTimelineBar();
        DrawInstructions();
    }

    void OnDestroy()
    {
        if (lineTexture != null)
            Destroy(lineTexture);
    }

    // ═══════════════════════════════════════════════════
    // 안전 영역 표시
    // ═══════════════════════════════════════════════════

    private void DrawSafeArea()
    {
        float w = Screen.width;
        float h = Screen.height;
        float mx = w * safeAreaMargin;
        float my = h * safeAreaMargin;

        Color safeColor = new Color(1f, 1f, 1f, 0.25f);

        // 상단
        DrawLine(mx, my, w - mx, my, safeColor);
        // 하단
        DrawLine(mx, h - my, w - mx, h - my, safeColor);
        // 좌측
        DrawLine(mx, my, mx, h - my, safeColor);
        // 우측
        DrawLine(w - mx, my, w - mx, h - my, safeColor);
    }

    // ═══════════════════════════════════════════════════
    // 3분할 격자
    // ═══════════════════════════════════════════════════

    private void DrawThirdsGrid()
    {
        float w = Screen.width;
        float h = Screen.height;
        Color gridColor = new Color(1f, 1f, 1f, 0.15f);

        // 수직선 2개
        DrawLine(w / 3f, 0, w / 3f, h, gridColor);
        DrawLine(w * 2f / 3f, 0, w * 2f / 3f, h, gridColor);

        // 수평선 2개
        DrawLine(0, h / 3f, w, h / 3f, gridColor);
        DrawLine(0, h * 2f / 3f, w, h * 2f / 3f, gridColor);
    }

    // ═══════════════════════════════════════════════════
    // 타이머 표시
    // ═══════════════════════════════════════════════════

    private void DrawTimerDisplay()
    {
        float y = Screen.height - 90;

        if (isRecording)
        {
            // 녹화 중 타이머 (빨간색)
            string elapsed = FormatTime(recordingElapsed);
            string remaining = FormatTime(Mathf.Max(0, timelineDuration - recordingElapsed));

            GUI.color = new Color(1f, 0.3f, 0.3f);
            GUI.Label(new Rect(20, y, 300, 30), $"REC  {elapsed} / {FormatTime(timelineDuration)}", timerStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(20, y + 24, 300, 20), $"Remaining: {remaining}", keyframeStyle);
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(20, y, 300, 30), $"STANDBY  ({FormatTime(timelineDuration)})", timerStyle);
            GUI.color = Color.white;
        }
    }

    // ═══════════════════════════════════════════════════
    // 타임라인 바
    // ═══════════════════════════════════════════════════

    private void DrawTimelineBar()
    {
        if (keyframes == null || keyframes.Length == 0) return;

        float barX = 20;
        float barY = Screen.height - 40;
        float barW = Screen.width - 40;
        float barH = 4;

        // 배경 바
        GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), lineTexture);

        // 진행 바 (녹화 중일 때)
        if (isRecording)
        {
            float progress = Mathf.Clamp01(recordingElapsed / timelineDuration);
            GUI.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            GUI.DrawTexture(new Rect(barX, barY, barW * progress, barH), lineTexture);
        }

        GUI.color = Color.white;

        // 키프레임 마커
        for (int i = 0; i < keyframes.Length; i++)
        {
            float t = keyframes[i].time / timelineDuration;
            float markerX = barX + barW * t;

            bool isCurrent = isRecording &&
                recordingElapsed >= keyframes[i].time &&
                (i + 1 >= keyframes.Length || recordingElapsed < keyframes[i + 1].time);

            // 마커 점
            GUI.color = isCurrent ? new Color(1f, 0.8f, 0.2f) : new Color(0.8f, 0.8f, 0.8f, 0.7f);
            GUI.DrawTexture(new Rect(markerX - 2, barY - 4, 4, 12), lineTexture);

            // 라벨
            GUIStyle style = isCurrent ? activeKeyframeStyle : keyframeStyle;
            GUI.Label(new Rect(markerX - 40, barY - 22, 120, 18), keyframes[i].description, style);
        }

        GUI.color = Color.white;
    }

    // ═══════════════════════════════════════════════════
    // 조작 안내
    // ═══════════════════════════════════════════════════

    private void DrawInstructions()
    {
        float x = Screen.width - 220;
        float y = Screen.height - 90;

        GUI.Label(new Rect(x, y, 200, 18), "[F5] Guide Toggle", instructionStyle);
        GUI.Label(new Rect(x, y + 16, 200, 18), "[F6] Record Start/Stop", instructionStyle);
        GUI.Label(new Rect(x, y + 32, 200, 18), "[Tab] Hide UI", instructionStyle);
    }

    // ═══════════════════════════════════════════════════
    // 녹화 제어
    // ═══════════════════════════════════════════════════

    /// <summary>녹화 타이머를 시작한다.</summary>
    public void StartRecording()
    {
        isRecording = true;
        recordingStartTime = Time.realtimeSinceStartup;
        recordingElapsed = 0f;
        Debug.Log("[UIShader] 녹화 가이드 타이머 시작");
    }

    /// <summary>녹화 타이머를 정지한다.</summary>
    public void StopRecording()
    {
        isRecording = false;
        Debug.Log($"[UIShader] 녹화 가이드 타이머 정지: {FormatTime(recordingElapsed)}");
    }

    // ═══════════════════════════════════════════════════
    // 유틸리티
    // ═══════════════════════════════════════════════════

    private static string FormatTime(float seconds)
    {
        int min = (int)(seconds / 60f);
        int sec = (int)(seconds % 60f);
        return $"{min:D2}:{sec:D2}";
    }

    private void DrawLine(float x0, float y0, float x1, float y1, Color color)
    {
        GUI.color = color;

        float dx = x1 - x0;
        float dy = y1 - y0;

        if (Mathf.Abs(dx) > Mathf.Abs(dy))
        {
            // 수평에 가까운 선
            GUI.DrawTexture(new Rect(Mathf.Min(x0, x1), y0, Mathf.Abs(dx), 1), lineTexture);
        }
        else
        {
            // 수직에 가까운 선
            GUI.DrawTexture(new Rect(x0, Mathf.Min(y0, y1), 1, Mathf.Abs(dy)), lineTexture);
        }

        GUI.color = Color.white;
    }

    private void EnsureStyles()
    {
        if (stylesInitialized) return;

        timerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        keyframeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
        };

        activeKeyframeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 9,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.8f, 0.2f) }
        };

        instructionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 0.6f) }
        };

        stylesInitialized = true;
    }
}
