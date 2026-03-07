// Assets/Editor/BuildHelper.cs
// ══════════════════════════════════════════════════════════════════════
// Depthweaver Phase 6 — 빌드 자동화 헬퍼
// ══════════════════════════════════════════════════════════════════════
//
// 메뉴 기반 빌드 자동화:
//   UIShader > Build > Windows x64
//   UIShader > Build > macOS (Apple Silicon)
//   UIShader > Build > All Platforms
//
// 빌드 프로세스:
//   1. 씬 목록 검증
//   2. 빌드 옵션 구성
//   3. BuildPipeline.BuildPlayer 실행
//   4. CEF 바이너리 복사 (존재 시)
//   5. 빌드 크기 보고
//
// 확장:
//   - 추가 플랫폼: BuildHelper에 새 메뉴 아이템 추가
//   - 빌드 후처리: PostBuildStep() 가상 메서드 패턴 (필요 시)
//   - CI/CD: 커맨드 라인 빌드 지원 (BuildFromCommandLine)

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System.Linq;

public static class BuildHelper
{
    // ═══════════════════════════════════════════════════
    // 빌드 설정 상수
    // ═══════════════════════════════════════════════════

    private const string BUILD_ROOT = "Builds";
    private const string WINDOWS_DIR = "Windows";
    private const string MACOS_DIR = "macOS";
    private const string PRODUCT_NAME = "Depthweaver";

    // CEF 바이너리 소스 경로 (Phase 1 구현 시 실제 바이너리 배치)
    private const string CEF_WINDOWS_SRC = "Assets/Plugins/CEF/Windows/x86_64";
    private const string CEF_MACOS_SRC = "Assets/Plugins/CEF/macOS/arm64";

    // ═══════════════════════════════════════════════════
    // 메뉴 아이템
    // ═══════════════════════════════════════════════════

    [MenuItem("UIShader/Build/Windows x64", false, 100)]
    public static void BuildWindows()
    {
        ExecuteBuild(
            BuildTarget.StandaloneWindows64,
            Path.Combine(BUILD_ROOT, WINDOWS_DIR, $"{PRODUCT_NAME}.exe"),
            WINDOWS_DIR
        );
    }

    [MenuItem("UIShader/Build/macOS (Apple Silicon)", false, 101)]
    public static void BuildMacOS()
    {
        // Apple Silicon 전용 아키텍처 설정
        EditorUserBuildSettings.SetPlatformSettings(
            "Standalone", "OSXUniversal", "Architecture", "ARM64"
        );

        ExecuteBuild(
            BuildTarget.StandaloneOSX,
            Path.Combine(BUILD_ROOT, MACOS_DIR, $"{PRODUCT_NAME}.app"),
            MACOS_DIR
        );
    }

    [MenuItem("UIShader/Build/All Platforms", false, 120)]
    public static void BuildAll()
    {
        Debug.Log("[UIShader Build] 전체 플랫폼 빌드 시작...");
        BuildWindows();
        BuildMacOS();
        Debug.Log("[UIShader Build] 전체 플랫폼 빌드 완료");
    }

    // ═══════════════════════════════════════════════════
    // 빌드 실행
    // ═══════════════════════════════════════════════════

    private static void ExecuteBuild(BuildTarget target, string outputPath, string platformName)
    {
        Debug.Log($"[UIShader Build] {platformName} 빌드 시작...");

        // ─── 씬 목록 구성 ───
        string[] scenes = GetBuildScenes();
        if (scenes.Length == 0)
        {
            Debug.LogError("[UIShader Build] 빌드 씬을 찾을 수 없습니다. " +
                          "Build Settings에 씬을 추가하거나, Assets/Scenes/에 씬 파일을 배치하세요.");
            return;
        }

        // ─── 출력 디렉토리 확보 ───
        string outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // ─── 빌드 옵션 ───
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = target,
            options = BuildOptions.None
        };

        // ─── 빌드 실행 ───
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            // CEF 바이너리 복사 (존재 시)
            CopyCEFBinaries(target, outputPath);

            // 빌드 크기 보고
            long sizeMB = summary.totalSize / (1024 * 1024);
            Debug.Log($"[UIShader Build] {platformName} 빌드 성공: {outputPath} ({sizeMB} MB, {summary.totalTime:F1}s)");
        }
        else
        {
            Debug.LogError($"[UIShader Build] {platformName} 빌드 실패: {summary.result} " +
                          $"(에러 {summary.totalErrors}개, 경고 {summary.totalWarnings}개)");
        }
    }

    // ═══════════════════════════════════════════════════
    // 씬 목록 관리
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 빌드에 포함할 씬 목록을 구성한다.
    /// Build Settings의 씬 목록을 우선 사용하고,
    /// 비어 있으면 Assets/Scenes/ 에서 자동 탐색한다.
    /// </summary>
    private static string[] GetBuildScenes()
    {
        // Build Settings에 등록된 씬 우선
        string[] buildSettingsScenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();

        if (buildSettingsScenes.Length > 0)
            return buildSettingsScenes;

        // 대안: Assets/Scenes/ 에서 자동 탐색
        string[] scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .ToArray();

        if (scenePaths.Length > 0)
        {
            Debug.LogWarning($"[UIShader Build] Build Settings에 씬이 없어 자동 탐색 사용: " +
                           string.Join(", ", scenePaths.Select(Path.GetFileNameWithoutExtension)));
        }

        return scenePaths;
    }

    // ═══════════════════════════════════════════════════
    // CEF 바이너리 복사
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// CEF 네이티브 바이너리를 빌드 출력에 복사한다.
    /// Phase 1 CEF 통합 전에는 소스 디렉토리가 비어 있으므로 건너뛴다.
    /// </summary>
    private static void CopyCEFBinaries(BuildTarget target, string buildPath)
    {
        string srcPath;
        string destPath;

        switch (target)
        {
            case BuildTarget.StandaloneWindows64:
                srcPath = CEF_WINDOWS_SRC;
                // Windows: 데이터 폴더 내 Plugins/
                string dataFolder = Path.ChangeExtension(buildPath, null) + "_Data";
                destPath = Path.Combine(dataFolder, "Plugins");
                break;

            case BuildTarget.StandaloneOSX:
                srcPath = CEF_MACOS_SRC;
                // macOS: .app/Contents/Frameworks/
                destPath = Path.Combine(buildPath, "Contents", "Frameworks");
                break;

            default:
                return;
        }

        if (!Directory.Exists(srcPath))
        {
            Debug.Log("[UIShader Build] CEF 바이너리 미발견 (Phase 1 미구현). 복사 생략.");
            return;
        }

        string[] files = Directory.GetFiles(srcPath, "*", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            Debug.Log("[UIShader Build] CEF 바이너리 디렉토리가 비어 있습니다. 복사 생략.");
            return;
        }

        if (!Directory.Exists(destPath))
            Directory.CreateDirectory(destPath);

        int copied = 0;
        foreach (string file in files)
        {
            string destFile = Path.Combine(destPath, Path.GetFileName(file));
            File.Copy(file, destFile, true);
            copied++;
        }

        // locales 하위 디렉토리 복사
        string localesSrc = Path.Combine(srcPath, "locales");
        if (Directory.Exists(localesSrc))
        {
            string localesDest = Path.Combine(destPath, "locales");
            if (!Directory.Exists(localesDest))
                Directory.CreateDirectory(localesDest);

            foreach (string file in Directory.GetFiles(localesSrc))
            {
                File.Copy(file, Path.Combine(localesDest, Path.GetFileName(file)), true);
                copied++;
            }
        }

        Debug.Log($"[UIShader Build] CEF 바이너리 {copied}개 파일 복사 완료: {destPath}");
    }

    // ═══════════════════════════════════════════════════
    // CI/CD 지원 (커맨드 라인 빌드)
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 커맨드 라인에서 호출 가능한 빌드 엔트리포인트.
    /// Unity -batchmode -executeMethod BuildHelper.BuildFromCommandLine -platform windows
    /// </summary>
    public static void BuildFromCommandLine()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string platform = "windows"; // 기본값

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-platform" && i + 1 < args.Length)
                platform = args[i + 1].ToLower();
        }

        switch (platform)
        {
            case "windows":
                BuildWindows();
                break;
            case "macos":
                BuildMacOS();
                break;
            case "all":
                BuildAll();
                break;
            default:
                Debug.LogError($"[UIShader Build] 알 수 없는 플랫폼: {platform}. " +
                              "지원: windows, macos, all");
                break;
        }
    }
}
#endif
