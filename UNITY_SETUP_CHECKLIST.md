# Gate2Reality — Unity Setup Checklist
**Target:** Android 15 · Pixel 9 / Galaxy S26 · HONOR 90 field test

---

## §1 · Packages (Package Manager)

- [ ] **AR Foundation** 6.x (latest verified)
- [ ] **Google ARCore XR Plugin** 6.x (same major as ARF)
- [ ] **Unity Sentis** 2.x (YOLO inference)
- [ ] **Universal RP** (from current LTS)
- [ ] **TextMeshPro** (subtitle controller)
- [ ] **Input System** package
- [ ] Remove **ARKit XR Plugin** (iOS not target)

---

## §2 · Project Settings → Player (Android)

- [ ] Scripting Backend: **IL2CPP**
- [ ] Target Architectures: **ARM64 only**
- [ ] Graphics API: **Vulkan** (primary) + **GLES3** (fallback) — remove Vulkan auto-selection
- [ ] Minimum API Level: **29**
- [ ] Target API Level: **35** (Android 15)
- [ ] Active Input Handling: **Input System Package (New)**
- [ ] Optimized Frame Pacing: **ON**
- [ ] Incremental GC: **ON**
- [ ] Permissions: **CAMERA** only — no INTERNET runtime feature

---

## §3 · XR Plug-in Management

- [ ] Android tab → **Google ARCore: ON**
- [ ] Requirement: **Required**
- [ ] Depth: **Required**

---

## §4 · Scene Hierarchy

```
[AR Session]
  └─ ARSession
  └─ ARInputManager

[XR Origin]
  └─ Camera Offset
       └─ Main Camera
            ├─ ARCameraManager        (Light Est: Ambient Intensity + Color)
            ├─ ARCameraBackground
            ├─ AROcclusionManager     (Env Depth: Best, Prefer Env Occlusion)
            └─ DepthPoseProjector

[XR Origin (Plane)]
  ├─ ARRaycastManager
  └─ ARPlaneManager                  (plane visualization OFF)

[Gate2Reality Core]
  ├─ NarrativeManager                (nodes 0-2 Scene 1 / 3-6 Scene 2)
  ├─ YoloObjectDetector              (model: yolov8n.onnx, 5 Hz)
  ├─ SceneOneDirector
  ├─ NarrativeContextCollector
  ├─ OnDeviceNarrativeGenerator      (timeout 3s, fallback pools)
  ├─ HorrorSafetyGovernor
  └─ DeviceTuningProfile             (Script Exec Order: -100)

[Effects]
  ├─ ChairEffect
  │    ├─ ChairAwakeningEffect       (dwell 0.75s)
  │    ├─ Light (amber, culling: excl HorrorOverlay)
  │    ├─ shadowQuad (Layer: HorrorOverlay)
  │    └─ legOverlay
  ├─ BookEffect
  │    ├─ BookMemoryEffect           (dwell 0.75s)
  │    ├─ AudioSource ×3 (noise / whisper / chime → Horror mixer)
  │    └─ ParticleSystem (pages)
  ├─ CupEffect
  │    ├─ CupBreachEffect            (dwell 1.0s)
  │    ├─ crackOverlay (Layer: HorrorOverlay)
  │    ├─ shards ParticleSystem
  │    └─ holoMapRoot → HoloMapController.mapContentRoot
  └─ GuardFX
       ├─ beaconSource (3D AudioSource)
       ├─ guideParticles (ParticleSystem)
       └─ desaturationVolume (Volume, weight=0)

[UI Canvas]
  └─ WhisperSubtitleController (Screen Space - Overlay)
       └─ SubtitleText (TextMeshProUGUI)
```

Dwell times: Chair **0.75s** / Book **0.75s** / Cup **1.0s**

---

## §5 · ARCameraManager / AROcclusionManager

- [ ] Light Estimation Mode: **Ambient Intensity + Ambient Color**
- [ ] Environment Depth Mode: **Best** (Mid/Low devices: Fastest)
- [ ] Occlusion Preference: **Prefer Environment Occlusion**
- [ ] Cup shards material: **Opaque or Alpha-Cutout** (NOT transparent — occlusion conflict)

---

## §6 · URP Asset + Renderer

- [ ] Opaque Texture: **ON**, Downsampling: **2x Bilinear**
- [ ] HDR: **ON**
- [ ] Renderer Feature: **AR Background Renderer Feature**
- [ ] Post-processing: **Bloom** (threshold 1.0, intensity ~0.6)
- [ ] Color Adjustments Volume (Saturation **−100**) on separate Volume, **weight = 0** (controlled by HorrorSafetyGovernor / guard)
- [ ] MSAA: **4x**
- [ ] Render Scale: **1.0** (Mid: 0.9, Low: 0.75 — applied by DeviceTuningProfile)
- [ ] Main Light Shadows: **OFF**

---

## §7 · Layers & Culling Matrix

| Layer | Contents | Notes |
|-------|----------|-------|
| `ARSurfaces` | planes, depth meshes | raycast mask, not rendered |
| `HorrorOverlay` | distortion / crack overlays | excluded from Amber light |
| `Holograms` | room map, crack ghost | excluded from desaturation volume |

- [ ] Physics collision matrix: **disable ALL** checks
- [ ] Auto Sync Transforms: **OFF**
- [ ] Physics.simulationMode: **Script**
- [ ] Amber Light Culling Mask: everything **EXCEPT HorrorOverlay**

---

## §8 · Sentis / YOLO

- [ ] Model: **yolov8n.onnx** (opset 15, imgsz 640, no built-in NMS)
- [ ] Path: `Assets/Models/yolov8n.onnx`
- [ ] Target inference latency: **≤15ms** (Vulkan)
- [ ] Fallback: raise `inferenceIntervalMs` to **300** if latency >25ms
- [ ] COCO class mapping wired in `YoloObjectDetector`: 56→Chair, 73→Book, 41→Cup

---

## §9 · MLLM (Kotlin Plugin)

- [ ] `NarrativeLlmBridge.kt` → `Assets/Plugins/Android/`
- [ ] Gradle dependency: `com.google.mediapipe:tasks-genai`
- [ ] Model delivery: **Play Asset Delivery** (install-time) → `filesDir/models/gemma.task`
- [ ] 8GB RAM device: **Gemma-270M** only; 12GB: Gemma-2B int4 ok
- [ ] Fallback mode: game runs with preset whisper pools if model absent

---

## §10 · Audio

- [ ] AudioMixer: **"Horror"** group, exposed parameter `HorrorVolumeDb`
- [ ] All narrative AudioSources → Output: **Horror** group
- [ ] `spatialBlend`: **1** (except beacon fallback mode)
- [ ] DSP Buffer Size: **Best Performance**
- [ ] Required audio assets:
  - Whisper beds (×6 per node min), white noise, porcelain chime, breach crunch
  - Beacon tone, mirror-side ambience, portal tear, crossing stinger
  - Inverse-side ambient, chapter stinger

---

## §11 · Performance / Thermal

- [ ] `Application.targetFrameRate`: **30**
- [ ] GC Alloc target in Update hot-path: **0 B**
- [ ] Profiler validation: 10-minute gameplay session
- [ ] Throttling response: on `THROTTLING_SEVERE` → multiply `inferenceIntervalMs ×2`
- [ ] Privacy audit: **zero network calls**, no frame saving, no person-detection logging

---

## §12 · Scene 1 Smoke Test

1. Plane detection → chair in frame 0.75s → amber light + leg shimmer starts ✓
2. Brief book detection → shadow quad rotates toward book ✓
3. Hold book in frame 0.75s → pages, noise rise, whisper (or fallback), chime → crack ghost appears ✓
4. Cup in frame 1.0s → blue crack animation → shards burst → holo-map reveals ✓
5. Friend enters frame at any step → horror fades to 25% in **0.5s** ✓
6. Idle 45s → beacon tone at last known anchor ✓
7. Idle 60s → desaturation volume weight=1 ✓
8. Idle 75s → guide particles at anchor ✓

---

## §13 · Scene 2 "Cartographer" — Delta Settings

### Graph nodes
| Node | Kind | Condition | Dwell | Effect |
|------|------|-----------|-------|--------|
| 3 | WallEcho | Proximity 1.2m | 0.5s | PortalWindowEffect d=0.6m |
| 4 | SurfaceEcho | Proximity 1.2m | 0.5s | EchoSurfaceEffect |
| 5 | PortalWall | Gaze 12° / 6m | 1.0s | PortalWindowEffect d=2.0m |
| 6 | Crossing | Proximity 0.5m | 0.4s | CrossingTransitionEffect |

### Scene additions
- [ ] `EchoZonePlacer`, `HoloMapController`, `SceneTwoDirector` on **[Gate2Reality Core]**
- [ ] `ARAnchorManager` on **XR Origin**
- [ ] Portal window quad: materials array `[PortalWindow, PortalRim]`
- [ ] Floor ripple quad: `[PortalRim]`
- [ ] InvertedWorld interior shell: `renderQueue = Geometry+20`
- [ ] Interior props: `renderQueue` 2021–2025 (painter's order)
- [ ] All portal prefabs authored local **−Z** of anchor
- [ ] `CrossingTransitionEffect.snapToAnchor`: **OFF**
- [ ] Subtitle Canvas: **Screen Space - Overlay**
- [ ] `HoloMapController.mapContentRoot` = child of `CupBreachEffect.holoMapRoot`

### Scene 2 Smoke Test
1. Cup finale → holo-map shows room contour + 3 violet pulsing markers + yellow player dot ✓
2. Approach wall marker → window aperture ruptures with ~10% overshoot, cold mirror interior ✓
3. Mirror whisper subtitle appears ~1.2s after reveal (or preset fallback) ✓
4. Approach floor ripples → circles radiate, dust column, bass hum ✓
5. Gaze large wall 1s → 2m door opens ✓
6. Enter door → flash, cold grade shift, stinger at peak, `OnCrossedOver` fires ✓

---

## §14 · Final Optimization Passport

| Stage | YOLO Mode | Hz | Est. Power |
|-------|-----------|----|-----------|
| Nodes 0–2 (Scene 1) | Full 5 Hz | 5 | ~1W |
| Cup activation onwards | Person-only | 1 | ~0.2W |
| Scene 2 zone placement | PlaneDetectionMode.None | — | — |
| Scene completed | Detector disabled | 0 | ~0W |

### Pre-release checklist
- [ ] Full chapter profiler: **GC Alloc = 0 B** outside node activation
- [ ] Battery Historian / `adb power`: consumption step visible at Cup trigger
- [ ] Privacy watch: person enters frame in Scene 2 → horror fades in 0.5s at 1 Hz
- [ ] Seam test: person in frame at final flash → intensity returns to 1.0 post-`OnCrossedOver`
- [ ] Complete Scene 1 (§12) + Scene 2 (§13) in single run

---

## §15 · Field Test — HONOR 90 (REA-NX9)

**Device:** Snapdragon 7 Gen 1 · Adreno 644 · 8/12 GB RAM · Android 14+

| Setting | Value |
|---------|-------|
| `minSdkVersion` | 29 |
| Device tier (DeviceTuningProfile) | Mid |
| YOLO interval | 300ms (3.3 Hz) |
| Environment Depth | Fastest |
| `renderScale` | 0.9 |
| Frame rate cap | 30 fps |
| Gemma model | 8GB→270M · 12GB→2B int4 |

### Setup
- [ ] `DeviceTuningProfile` on **[Gate2Reality Core]**, Script Execution Order: **−100**
- [ ] Development Build: **enabled**
- [ ] Logcat filter: `Gate2Reality|Exception`

### Expected metrics

| Metric | Green | Red |
|--------|-------|-----|
| YOLO int8 GPU 640² | 20–35ms | >45ms |
| FPS | stable 30 | <27 |
| Gemma-2B latency | 1.5–3s (timeout fallback) | >5s |
| Battery 15min chapter | 4–6% | >9% |
| Privacy reaction (2nd person) | ≤1.5s | >2s |

### Fallback
- [ ] If YOLO >45ms → raise `midIntervalMs` to **400**
- [ ] 15-minute thermal test validation completed
- [ ] Three device-risk mitigations verified:
  - Frame orientation → `ConversionParams.transformation`
  - Sentis signatures → `ReadbackRequest` version check
  - MediaPipe genai API → `LlmInferenceOptions` version validation
