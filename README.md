# Depthweaver

> *Weaving light from the invisible depth of the web.*

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B%20LTS-000000?style=flat&logo=unity&logoColor=white)](https://unity.com)
[![HDRP](https://img.shields.io/badge/HDRP-High%20Definition-blueviolet?style=flat)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@latest)
[![CEF](https://img.shields.io/badge/Chromium-Embedded%20Framework-4285F4?style=flat&logo=googlechrome&logoColor=white)](https://bitbucket.org/chromiumembedded/cef)
[![C#](https://img.shields.io/badge/C%23-10.0-239120?style=flat&logo=csharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![C++](https://img.shields.io/badge/C%2B%2B-17-00599C?style=flat&logo=cplusplus&logoColor=white)](https://isocpp.org/)
[![HLSL](https://img.shields.io/badge/HLSL-Shader-ff6600?style=flat)](https://docs.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl)
[![JavaScript](https://img.shields.io/badge/JavaScript-ES6-F7DF1E?style=flat&logo=javascript&logoColor=black)](https://developer.mozilla.org/en-US/docs/Web/JavaScript)
[![Platform](https://img.shields.io/badge/Platform-Windows%20|%20macOS-lightgrey?style=flat)](https://github.com)
[![License](https://img.shields.io/badge/License-Proprietary-red?style=flat)](LICENSE)

---

## Abstract

Depthweaver is a real-time rendering pipeline that transforms live web pages into **2.5D displacement surfaces** and **area light cookies** within a Unity 3D scene. By introducing a novel **Composite Depth Scoring System** that extracts implicit elevation semantics from CSS/DOM signals (e.g., `box-shadow`, `z-index`, `transform: translateZ()`), the system reconstructs 3D depth information from inherently 2D HTML/CSS content. The extracted depth map drives vertex displacement on a subdivided screen mesh while the color output simultaneously serves as an HDRP `RectAreaLight` cookie, projecting colored indirect illumination onto surrounding 3D geometry.

This approach differs from prior work (CineShader, CSS3D Renderer, MiDaS) by operating directly on CSS design-system semantics rather than pixel-level inference, achieving event-driven depth updates with zero per-frame computational cost when the DOM is static.

---

## Table of Contents

- [System Architecture](#system-architecture)
- [Composite Depth Scoring Algorithm](#composite-depth-scoring-algorithm)
- [Dual Texture Pipeline](#dual-texture-pipeline)
- [Feature Overview](#feature-overview)
  - [CEF Integration and Browser Abstraction](#1-cef-integration-and-browser-abstraction)
  - [Depth Extraction Engine](#2-depth-extraction-engine)
  - [Vertex Displacement Pipeline](#3-vertex-displacement-pipeline)
  - [Area Light Cookie System](#4-area-light-cookie-system)
  - [Studio Environment](#5-studio-environment)
  - [Cinematic Camera System](#6-cinematic-camera-system)
  - [Demo and Tooling](#7-demo-and-tooling)
- [Project Structure](#project-structure)
- [Technical Specifications](#technical-specifications)
- [Build Instructions](#build-instructions)
- [Keyboard Controls](#keyboard-controls)
- [Acknowledgments](#acknowledgments)

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  UNITY PROCESS (C#)                                                 │
│                                                                     │
│  ┌─────────────────────┐     ┌─────────────────────────────────┐   │
│  │  CEF Browser         │     │  HDRP Rendering Engine          │   │
│  │  (Chromium Offscreen)│     │                                 │   │
│  │                      │     │  ┌─────────────┐ ┌───────────┐ │   │
│  │  ┌────────────────┐  │     │  │ Screen Mesh  │ │ Rect Area │ │   │
│  │  │  Web Page       │  │     │  │ (Displaced)  │ │   Light   │ │   │
│  │  │  HTML / CSS / JS│  │     │  └──────┬──────┘ └─────┬─────┘ │   │
│  │  └───────┬────────┘  │     │         │              │       │   │
│  │          │           │     │     Depth Map      Cookie Tex  │   │
│  │    ┌─────┴─────┐    │     │       (Alpha)        (RGB)     │   │
│  │    │  Offscreen │    │     │         └───────┬───────┘      │   │
│  │    │  Render    │    │     │                 │              │   │
│  │    └─────┬─────┘    │     │  ┌──────────────┴───────────┐  │   │
│  │          │           │     │  │      3D Environment      │  │   │
│  │    ┌─────┴─────┐    │     │  │  Terrain / Vegetation /  │  │   │
│  │    │Color Buffer│────╂─────╂──│  Studio / Showcases      │  │   │
│  │    └───────────┘    │     │  └──────────────────────────┘  │   │
│  │    ┌───────────┐    │     │                                 │   │
│  │    │Depth Canvas│────╂─────╂──→ Displacement Map            │   │
│  │    └───────────┘    │     │                                 │   │
│  └─────────────────────┘     └─────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Composite Depth Scoring Algorithm

The core contribution of this project is the **Composite Depth Score**, a weighted aggregation of six CSS/DOM signals that recovers designer-intended elevation from web page structure:

```
VisualDepth(element) =
    w1 × NormalizedDOMDepth(element)
  + w2 × NormalizedStackingContext(element)
  + w3 × BoxShadowElevation(element)
  + w4 × TransformZ(element)
  + w5 × OpacityHint(element)
  + w6 × PositionType(element)
```

| Signal | Default Weight | Extraction Method | Rationale |
|--------|---------------|-------------------|-----------|
| DOM Nesting Depth | w1 = 0.25 | `parentNode` chain traversal | Hierarchical baseline from document structure |
| Stacking Context | w2 = 0.25 | `getComputedStyle().zIndex` | CSS-specified visual ordering |
| Box Shadow Elevation | w3 = 0.30 | `box-shadow` blur/spread parsing | Material Design elevation encodes depth intent |
| Transform Z-axis | w4 = 0.10 | `transform: translateZ()` parsing | Explicit 3D positioning |
| Opacity Hint | w5 = 0.05 | `opacity < 1` detection | Background layer / overlay detection |
| Position Type | w6 = 0.05 | `position: fixed/sticky` analysis | Navigation bars, floating elements |

Box Shadow receives the highest weight (0.30) because modern design systems (Material Design, Tailwind CSS) systematically encode elevation through shadow values:

```
box-shadow: 0 1px 2px     →  elevation ~ 1   (subtle surface lift)
box-shadow: 0 4px 8px     →  elevation ~ 3   (card level)
box-shadow: 0 12px 24px   →  elevation ~ 6   (modal / dropdown)
box-shadow: 0 24px 48px   →  elevation ~ 12  (top-level popup)
```

---

## Dual Texture Pipeline

The system separates color and depth texture production into two independent update paths with distinct refresh rates:

```
Web Page (CEF Offscreen Render)
│
├── [Every Frame] Color Buffer (RGB)
│   ├──→ Screen Mesh Albedo Texture
│   └──→ RectAreaLight Cookie Texture
│
└── [DOM Mutation Only] Depth Canvas (Grayscale)
│
    └──→ Screen Mesh Displacement Map
         vertex += normal * depth.r * displacementScale
```

The depth canvas is updated only when the DOM changes (via `MutationObserver`), while the color buffer is captured every frame. This decoupled architecture yields zero per-frame depth computation cost for static pages.

---

## Feature Overview

### 1. CEF Integration and Browser Abstraction

The system embeds a full Chromium browser via a C++ native plugin for offscreen rendering, with a **Strategy Pattern** abstraction (`IBrowserBackend`) enabling runtime browser engine replacement.

**Native Plugin (C++17):**
- Offscreen rendering with configurable resolution and frame rate
- Double-buffered pixel transfer with mutex-guarded front/back swap
- Dirty rectangle tracking for partial-update optimization
- Full input forwarding: mouse (move, click, wheel), keyboard (key down/up, char)
- JavaScript execution and evaluation bridge
- Cross-platform build system (Windows x64 / macOS ARM64 via CMake)

**Unity Bridge (C#):**
- `IBrowserBackend` — Abstract interface decoupling browser engine from the pipeline
- `CEFNativeBackend` — P/Invoke-based implementation wrapping 25+ native API calls
- `CEFTextureSource` — `ITextureSource` implementation with three transfer modes:
  - **Standard**: `LoadRawTextureData()` single-buffer upload
  - **DoubleBuffered**: Pingpong `Texture2D[2]` for GPU stall prevention
  - **NativePointer**: Direct GPU texture write (platform-specific)
- `CEFBridge` — High-level facade (URL navigation, JS injection, depth weight sync)
- `CEFInputHandler` — Raycast-based screen mesh hit detection with UV-to-pixel coordinate mapping

**Extensibility:** Adding a new browser backend (e.g., Vuplex, Ultralight) requires implementing a single interface (`IBrowserBackend`) with no changes to the texture pipeline.

### 2. Depth Extraction Engine

A JavaScript module (`depth-extractor.js`) injected into the CEF browser that generates a 512x512 grayscale depth canvas from live DOM analysis.

**Architecture:**
- `SignalRegistry` — Plugin-pattern signal extractor registry; new depth signals can be registered at runtime via `window.__UIShader.registerSignal()` without modifying core code
- `DepthScorer` — Weighted aggregation of registered signals into a normalized depth value per element
- `DepthRenderer` — DOM tree traversal with bounding rect projection onto the depth canvas
- `DOMWatcher` — `MutationObserver` + event listeners (`transitionend`, `animationend`, `scroll`, `resize`) with configurable debounce (DOM: 50ms, scroll: 100ms)
- CSS animation active-state polling for continuous animation detection

**Presets:** Three built-in weight configurations — `balanced`, `materialDesign`, `flatDesign` — optimized for different design systems.

**Depth Texture Post-Processing (GPU):**
- `DepthBlur.compute` — Separable Gaussian blur (horizontal + vertical, 2-pass) on the depth map
- `DepthTextureProcessor.cs` — Pingpong buffer management with configurable iteration count

**Weight Tuning:**
- `DepthWeightPreset` (ScriptableObject) — JSON serialization, interpolation between presets, factory methods
- `DepthWeightTuner` (Custom Editor) — Real-time sliders with color bars, sum visualization, normalization, preset buttons

### 3. Vertex Displacement Pipeline

The depth map drives physical vertex displacement on a subdivided screen mesh, producing a 2.5D surface where UI elements protrude from the screen plane.

**Screen Mesh (Procedural Generation):**
- Three-level LOD pool pre-generated at startup:
  - **High**: 511x511 subdivision (262,144 vertices)
  - **Medium**: 255x255 subdivision (65,536 vertices)
  - **Low**: 63x63 subdivision (4,096 vertices)
- 32-bit index buffer support for high-density meshes
- Tangent vectors for normal mapping compatibility
- `OnLODChanged` event for external system notification
- Screen frame generator: 4-edge extruded border with per-face flat shading

**HLSL Displacement Shader:**
- 3-pass structure: ForwardOnly (color + displacement + normals), ShadowCaster (displaced shadows), DepthForwardOnly (HDRP depth prepass)
- Central-difference normal reconstruction: 4-directional adjacent depth samples for accurate surface gradient
- Edge feathering: `smoothstep`-based displacement attenuation at mesh boundaries
- Self-emission output for screen glow effect
- Shader Graph Custom Function Node interface (`DisplaceVertex.hlsl`) with `float`/`half` precision variants

**Runtime Control:**
- `DisplacementController` — Real-time shader parameter synchronization (scale, bias, edge falloff, emission intensity)
- Camera-distance-based automatic LOD switching with hysteresis to prevent boundary flickering

### 4. Area Light Cookie System

The web page color output serves as an HDRP `RectAreaLight` cookie, projecting colored indirect illumination onto surrounding 3D geometry via Unity's native Linearly Transformed Cosines (LTC) integration.

**GPU Cookie Processing:**
- `CookieProcess.shader` — Single-pass GPU pipeline: saturation boost (BT.709 luminance lerp) → contrast curve (1D LUT) → intensity multiplier
- `AnimationCurve` → 256x1 LUT texture auto-baking with half-texel offset correction
- ~100x faster than equivalent CPU `GetPixels` loop

**Auto Exposure:**
- Progressive downsampling (512 → 64 → 4 → 1) for average luminance computation (BT.709 weighted)
- Configurable min/max intensity multipliers with exponential decay interpolation
- Prevents oversaturation from bright web content

**Quadrant Light System:**
- 2x2 subdivided `RectAreaLight` array for improved spatial accuracy
- GPU Blit with UV scale/offset for per-quadrant cookie cropping (zero CPU cost)
- Automatic main light ↔ quadrant light switching to prevent double illumination
- Shared auto-exposure intensity synchronization

### 5. Studio Environment

Two interchangeable environment modes for different presentation contexts:

**Natural Environment (Procedural):**
- `ProceduralTerrainModule` — Heightmap-based terrain generation
- `HDRPSkyModule` — Physically-based sky with configurable exposure
- `SunLightModule` — Directional sun with shadow cascades
- `ProceduralGrassModule` — Terrain-conforming grass instances
- `AtmosphereModule` — Volumetric fog and atmospheric scattering
- Modular architecture via `IEnvironmentModule` interface

**Studio Environment (Production):**
- Reflective floor: HDRP/Lit with ClearCoat (Smoothness = 0.88)
- Cyclorama backdrop: Procedurally generated arc mesh with configurable arc angle, radius, and segment count
- Floor-to-wall cove transition: Quarter-circle smooth junction with configurable radius
- Showcase objects: Extensible `List<ShowcaseObjectConfig>` with 5 material presets (ChromeMirror, DarkVehicle, HumanSkin, GlossyPlastic, MatteWhite)
- Fill light: Low-intensity directional (50 lux, cool blue-white)

### 6. Cinematic Camera System

**Orbit Camera Controller:**
- Manual mode: Right-click orbit + scroll wheel zoom with configurable sensitivity and clamped pitch
- Auto-cruise mode (C key): Continuous horizontal rotation + sinusoidal vertical oscillation
- Preset transitions: `AnimationCurve`-based smooth interpolation between camera presets
- Automatic cruise interruption on manual input detection (right-click, scroll)
- Three-state machine: Manual → AutoCruise → Transition

**Post-Processing Controller:**
- HDRP Volume auto-generation with code-configured overrides
- Effects: Bloom, Vignette, ACES Tonemapping, Color Adjustments, Film Grain, Depth of Field
- Distance-based auto DOF: Activates within 6m, dynamically updates focus distance

### 7. Demo and Tooling

**Demo Auto-Play System:**
- `DemoScenarios` (ScriptableObject) — Data-driven scenario definitions (URL, weight preset, camera preset, displacement scale)
- 5 built-in scenarios: Material Design modal, dark mode toggle, animated hero, hover card grid, dashboard
- Coroutine-based scenario cycling with configurable dwell time
- CEF-independent design: `event Action<string> OnLoadURL` for loose coupling

**In-Game UI Overlay (Tab key):**
- Draggable IMGUI window with sectioned layout
- URL input, depth preset selection grid, displacement/lighting/camera sliders
- Quadrant light toggle, auto-exposure toggle, keyboard shortcut reference
- GC-optimized GUIStyle caching

**Performance Profiler (F3 key):**
- 120-frame history ring buffer
- FPS average + 1% low (top-percentile sorted)
- Memory: `Profiler.GetTotalAllocatedMemoryLong()` / `GetMonoUsedSizeLong()`
- Editor-only: draw calls, triangles, batches via `UnityStats`
- Pipeline state: color/depth update rates, camera mode, demo scenario

**Recording Guide (F5/F6 keys):**
- 30-second timeline with 6 keyframe markers for highlight reel composition
- Recording timer with elapsed/remaining display
- Safe area margins and rule-of-thirds grid overlays

**Build Automation:**
- Menu: `UIShader > Build > Windows x64`, `macOS (Apple Silicon)`, `All Platforms`
- `BuildReport`-based result reporting (size, duration, errors/warnings)
- CI/CD support: `BuildFromCommandLine()` with `-platform windows/macos/all`
- Automatic CEF binary copying (activates when native plugin is available)

---

## Project Structure

```
depthweaver/
├── Assets/
│   ├── Scripts/
│   │   ├── Core/                          # Pipeline orchestration
│   │   │   ├── UIShaderConfig.cs          # Central ScriptableObject configuration
│   │   │   ├── TexturePipelineManager.cs  # Texture distribution orchestrator
│   │   │   ├── BootstrapManager.cs        # Coroutine-based initialization sequence
│   │   │   ├── ITextureSource.cs          # Texture provider abstraction
│   │   │   ├── StaticTextureSource.cs     # Phase 0 PNG source
│   │   │   └── UIShaderBootstrap.cs       # Editor bootstrap utility
│   │   │
│   │   ├── CEF/                           # Browser integration (Phase 1)
│   │   │   ├── IBrowserBackend.cs         # Browser engine abstraction (Strategy)
│   │   │   ├── NativePluginInterop.cs     # P/Invoke declarations (25+ APIs)
│   │   │   ├── CEFNativeBackend.cs        # Native backend implementation
│   │   │   ├── CEFTextureSource.cs        # ITextureSource for live web pages
│   │   │   ├── CEFBridge.cs               # High-level facade
│   │   │   └── CEFInputHandler.cs         # Raycast input forwarding
│   │   │
│   │   ├── Rendering/                     # Displacement + lighting
│   │   │   ├── ScreenMeshGenerator.cs     # 3-level LOD mesh pool
│   │   │   ├── ScreenFrameGenerator.cs    # Extruded border frame
│   │   │   ├── DisplacementController.cs  # Runtime shader parameter control
│   │   │   ├── ScreenLightController.cs   # Main RectAreaLight + auto-exposure
│   │   │   ├── CookieProcessor.cs         # GPU cookie post-processing
│   │   │   └── QuadrantLightSystem.cs     # 2x2 subdivided area lights
│   │   │
│   │   ├── DepthMap/                      # Depth processing (Phase 2)
│   │   │   ├── DepthTextureProcessor.cs   # GPU Gaussian blur (pingpong)
│   │   │   └── DepthWeightPreset.cs       # Weight presets (ScriptableObject)
│   │   │
│   │   ├── Environment/                   # Natural environment modules
│   │   │   ├── IEnvironmentModule.cs      # Module interface
│   │   │   ├── NaturalEnvironmentBuilder.cs
│   │   │   ├── ProceduralTerrainModule.cs
│   │   │   ├── HDRPSkyModule.cs
│   │   │   ├── SunLightModule.cs
│   │   │   ├── ProceduralGrassModule.cs
│   │   │   └── AtmosphereModule.cs
│   │   │
│   │   ├── Studio/                        # Production studio environment
│   │   │   └── StudioEnvironment.cs
│   │   │
│   │   ├── Camera/                        # Camera + post-processing
│   │   │   ├── OrbitCameraController.cs   # Orbit / cruise / transition
│   │   │   └── PostProcessController.cs   # HDRP Volume auto-setup
│   │   │
│   │   ├── Demo/                          # Demo scenarios
│   │   │   ├── DemoScenarios.cs           # ScriptableObject data
│   │   │   └── DemoAutoPlay.cs            # Auto-play controller
│   │   │
│   │   └── UI/                            # Overlays and tools
│   │       ├── UIShaderOverlay.cs         # In-game control panel
│   │       ├── PerformanceProfiler.cs     # F3 performance overlay
│   │       └── RecordingGuide.cs          # F5/F6 recording guide
│   │
│   ├── Shaders/
│   │   ├── UIShaderDisplacement.shader    # 3-pass HLSL displacement
│   │   ├── UIShaderDisplacement_Simple.shader  # Phase 0 simplified
│   │   ├── DisplaceVertex.hlsl            # Shader Graph custom function
│   │   ├── DepthBlur.compute              # Separable Gaussian blur
│   │   └── CookieProcess.shader           # GPU cookie processing
│   │
│   ├── JavaScript/
│   │   └── depth-extractor.js             # Composite Depth Score engine
│   │
│   └── Editor/
│       ├── HDRPSettingsValidator.cs        # HDRP asset validation
│       ├── ProjectStructureSetup.cs        # Folder structure generator
│       ├── StudioSceneSetupWizard.cs       # Scene builder wizard
│       ├── DepthWeightTuner.cs             # Weight tuning inspector
│       └── BuildHelper.cs                  # Cross-platform build automation
│
└── NativePlugin/
    ├── CMakeLists.txt                      # Cross-platform build (VS 2022 / Xcode)
    └── src/
        ├── cef_plugin.h                    # C API declarations
        ├── cef_plugin.cpp                  # API implementation + global state
        ├── render_handler.h/cpp            # Offscreen CefRenderHandler
        ├── browser_client.h/cpp            # CefClient + load events
        └── browser_app.h/cpp               # CefApp process handler
```

---

## Technical Specifications

| Metric | Value |
|--------|-------|
| Total Source Files | 58 |
| Total Lines of Code | ~12,300 |
| Languages | C# (45%), HLSL (25%), JavaScript (20%), C++ (10%) |
| Render Resolution | 512 x 512 (configurable) |
| Max Mesh Subdivision | 511 x 511 (262,144 vertices) |
| LOD Levels | 3 (High / Medium / Low) |
| Depth Signals | 6 (extensible via plugin registry) |
| Depth Update Trigger | DOM Mutation (event-driven, 0 cost when static) |
| Cookie Processing | GPU Blit (single pass) |
| Auto-Exposure | Progressive downsample (512 → 1) |
| Target Performance | 60 FPS at 1080p (RTX 3060 tier) |
| Platform Support | Windows x64, macOS ARM64 |
| Unity Version | 2022.3+ LTS |
| Render Pipeline | HDRP (High Definition Render Pipeline) |

---

## Build Instructions

### Prerequisites

- Unity 2022.3 LTS or later with HDRP package
- CMake 3.20+ (for native plugin)
- Windows: Visual Studio 2022 with C++ desktop workload
- macOS: Xcode 14+ with Command Line Tools
- CEF binary distribution (matching platform)

### Native Plugin Build

```bash
# Windows
cd NativePlugin
cmake -B build -G "Visual Studio 17 2022" -A x64 \
      -DCEF_ROOT="path/to/cef_binary"
cmake --build build --config Release

# macOS
cd NativePlugin
cmake -B build -G Xcode \
      -DCMAKE_OSX_ARCHITECTURES=arm64 \
      -DCEF_ROOT="path/to/cef_binary"
cmake --build build --config Release
```

### Unity Project Setup

1. Open the project in Unity 2022.3+ with HDRP
2. Verify HDRP Asset settings: Area Lights, Shadows, SSR, SSAO enabled
3. Create `UIShaderConfig` asset: `Create > UIShader > Config`
4. Build studio scene: `UIShader > Build Studio Scene`
5. Place native plugin binaries in `Assets/Plugins/` (platform-specific subfolders)

### Standalone Build

```
Unity Menu → UIShader > Build > Windows x64
Unity Menu → UIShader > Build > macOS (Apple Silicon)
```

---

## Keyboard Controls

| Key | Action |
|-----|--------|
| Tab | Toggle in-game UI overlay |
| C | Toggle auto-cruise camera |
| 1-4 | Camera preset transitions |
| P | Toggle demo auto-play |
| Left/Right Arrow | Previous/next demo scenario |
| F3 | Toggle performance profiler |
| F5 | Toggle recording guide |
| F6 | Start/stop recording timer |
| Right-click + Drag | Orbit camera |
| Scroll Wheel | Zoom camera |

---

## Acknowledgments

This project was inspired by [CineShader](https://cineshader.com) by Lusion, which demonstrated the rendering technique of using Shadertoy GLSL output as both a displacement map and area light cookie in a virtual studio environment. Depthweaver extends this concept to arbitrary web pages through the introduction of the Composite Depth Scoring System.
