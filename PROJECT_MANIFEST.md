# Gate2Reality — Project Manifest
**Version:** v1.0-fieldtest · Chapter I complete · Target: Android 15

---

## Overview

Immersive narrative AR game for Android. Physical objects in the real room (chair, book, cup) are semantic triggers that unlock horror effects, generative whispers, and ultimately a portal to an inverted mirror-world.

- **Engine:** Unity (URP) + AR Foundation 6 + ARCore
- **ML:** YOLOv8n int8 via Unity Sentis 2.x · MediaPipe Gemma (on-device)
- **Privacy:** zero network calls · camera frames never saved · person detection not logged

---

## File Map

### Gate2Reality.Narrative `Assets/Scripts/Narrative/`

| File | Purpose |
|------|---------|
| `ITriggerable.cs` | Effect contract; `Trigger(in Pose)` / `Cancel()` |
| `NarrativeCondition.cs` | Node conditions: semantic detection · proximity · gaze; enums `NarrativeLabel`, `ConditionType` |
| `NarrativeNode.cs` | Serializable graph node with dwell-time accumulator and edge list |
| `NarrativeManager.cs` | FSM controller; Guard Node cycle 45 s→beacon · 60 s→desaturation · 75 s→particles |
| `INarrativeGenerator.cs` | Generator interface + `NarrativeContext` struct (bitmask · light · room heuristic) |
| `NarrativeContextCollector.cs` | Aggregates AR light estimation + detection bitmask; updates generator every 2 s |
| `OnDeviceNarrativeGenerator.cs` | Kotlin bridge with 3 s timeout; fallback pools (2 per node); round-robin |

### Gate2Reality.Detection `Assets/Scripts/Detection/`

| File | Purpose |
|------|---------|
| `DetectionEvent.cs` | Readonly struct: `Label · WorldPose · Confidence · BoundsRadius` (zero-GC) |
| `YoloObjectDetector.cs` | CPU frame capture → Sentis GPU inference → pre-allocated NMS → `DetectionEvent` emit; person-only mode |
| `DepthPoseProjector.cs` | 2D→3D: ARCore Depth API → plane raycast → fixed-depth approximation |

### Gate2Reality.Effects `Assets/Scripts/Effects/`

| File | Purpose |
|------|---------|
| `TriggerableEffectBase.cs` | Abstract one-shot base: anchor snap · lifecycle guards · `MarkFinished()` |
| `ChairAwakeningEffect.cs` | Amber breathing light + shader distortion ramp + shadow-quad hint pointer |
| `BookMemoryEffect.cs` | 5-phase FSM: NoiseRise → Whispering → CupHint → NoiseFall → Done |
| `CupBreachEffect.cs` | Crack animation → shard burst → holo-map reveal |

### Gate2Reality.Scene `Assets/Scripts/Scene/`

| File | Purpose |
|------|---------|
| `SceneOneDirector.cs` | Wires detector → projector → narrative manager; guard events; person-mode switch at node 2 |

### Gate2Reality.SceneTwo `Assets/Scripts/SceneTwo/`

| File | Purpose |
|------|---------|
| `EchoZone.cs` | Anchor marker with surface metadata (`EchoSurface` enum) |
| `EchoZonePlacer.cs` | Greedy placement on classified ARCore planes; ring fallback; `PlacedZone` readonly struct |
| `HoloMapController.cs` | One-time top-down map: plane contour lines + zone pings + live player dot |
| `SceneTwoDirector.cs` | Echo whisper sequencing; zone placement trigger; node 6 crossing anchor |
| `PortalWindowEffect.cs` | Aperture animation via `easeOutBack` (~10 % overshoot); drives `_Aperture` shader property |
| `EchoSurfaceEffect.cs` | Floor ripple via aperture modulation + dust particles + bass hum |
| `CrossingTransitionEffect.cs` | Flash → world swap → `OnCrossedOver` event; Canvas Screen Space Overlay |

### Gate2Reality.Safety `Assets/Scripts/Safety/`

| File | Purpose |
|------|---------|
| `HorrorSafetyGovernor.cs` | Human presence → scale to 25 % in 0.5 s; global `_HorrorScale` shader keyword |
| `DeviceTuningProfile.cs` | Runtime tier detection (Flagship / Mid / Low); applies render scale + YOLO Hz; exec order −100 |

### Gate2Reality.UI `Assets/Scripts/UI/`

| File | Purpose |
|------|---------|
| `WhisperSubtitleController.cs` | Zero-GC typewriter via `maxVisibleCharacters`; fade-out; queue for rapid events |

### Shaders `Assets/Shaders/` — URP, half precision, mobile-optimised

| Shader | Technique |
|--------|-----------|
| `RealityDistortion.shader` | Flow-noise UV distortion + crack mask driven by `_BreachProgress`; `_HorrorScale` global |
| `PortalWindow.shader` | Circular aperture cutout; stencil write 1 inside hole; rim glow |
| `InvertedWorld.shader` | Reads stencil 1; `ZTest Always`; front-cull + Z-flip for mirror interior; cold colour grade |
| `PortalRim.shader` | Additive rim ring at aperture edge; pulse via `sin(_Time.y)` |

### Native `Assets/Plugins/Android/`

| File | Purpose |
|------|---------|
| `NarrativeLlmBridge.kt` | MediaPipe LLM inference on background `HandlerThread`; `UnitySendMessage` callback; lazy model load |

### Models `Assets/Models/`
Place `yolov8n.onnx` (opset 15, imgsz 640, no built-in NMS) and optionally `gemma.task` here.
Gemma is also deliverable via Play Asset Delivery → `filesDir/models/`.

---

## Data Flow

```
ARCamera (YUV)
    │
    ▼
YoloObjectDetector
  ├─ GPU inference (Sentis, 5 Hz)
  ├─ NMS on pre-allocated Candidate[]
  ├─ OnRawDetection ──────────────────► SceneOneDirector
  │                                          │
  │                                          ▼
  │                                   DepthPoseProjector
  │                              (Depth API → plane → approx)
  │                                          │
  └─ OnHumanPresenceChanged ─► HorrorSafetyGovernor
                                        │
                               NarrativeManager.ReportDetection(DetectionEvent)
                                        │
                              ┌─────────┴──────────┐
                              │  Guard Node FSM     │
                              │  45 s→beacon        │
                              │  60 s→desaturate    │
                              │  75 s→particles     │
                              └─────────┬──────────┘
                                        │ OnNodeActivated
                              ┌─────────┴──────────────────────────┐
                              │         │              │            │
                         ChairEffect  BookEffect   CupEffect  SceneOneDirector
                                                        │        (mode switch)
                                                   HoloMapController
                                                   SceneTwoDirector
                                                   (EchoZonePlacer)
```

---

## Verification Summary (9 simulation runs)

| Scenario | Result |
|----------|--------|
| Guard cycle timings 45/60/75 s + 30 s repeat | ✅ |
| YOLO 2–3 frame dropout tolerance | ✅ |
| NMS cross-class overlap | ✅ |
| Subtitle prefetch latency (0.00 s) | ✅ |
| HorrorSafetyGovernor hysteresis | ✅ |
| Mixed semantic→proximity→gaze graph, 0.1° cone | ✅ |
| Zone placement: 4 room types incl. edge cases | ✅ |
| Whisper relay invariant (6 scenarios) | ✅ |
| Full chapter timeline −39 % energy profile | ✅ |

**10 bugs found and fixed** during simulation:
blocking `ReadbackAndClone`, double tensor disposal, Kotlin context leak,
MLLM↔UI text disconnect, portal wall theft, rim occlusion order,
orphaned `EchoZone` namespace, watch-mode freeze at scene boundary,
`easeOutBack` test artifact, zone drift without `ARAnchor`.

---

## Roadmap

### Stage A — Field Test on HONOR 90 (Immediate)
1. Development Build per `UNITY_SETUP_CHECKLIST.md` §1–13
2. 8 GB device → Gemma-270M; log tier, Depth API mode, YOLO timing
3. Three device-risk mitigations armed:
   - Frame orientation → `ConversionParams.transformation`
   - Sentis signatures → `ReadbackRequest` version check
   - MediaPipe genai → `LlmInferenceOptions` version validation

### Stage B — Asset Production (Parallel)
- Noise texture (RG flow, B crack detail) for `RealityDistortion.shader`
- Audio: whisper beds, white noise, chime, breach crunch, beacon, mirror ambience, portal tear, crossing stinger, inverse ambient
- Prefabs: shadow quad, crack phantom, page/fragment/conductor particles, inverted interior (−Z), holo-map material, subtitle Canvas (TMP), flash fullscreen

### Stage C — Stabilisation Post-Testing
- Field bug closure; raise inference intervals if thermal issues
- Fallback pool expansion (≥ 3 per node)
- Second test cycle → tag `v1.1-stable`

### Stage D — Chapter II Design
Entry point: `CrossingTransitionEffect.OnCrossedOver`.
Rule inversion: guard assists against player; familiar objects behave differently.

### Stage E — Production
- ScriptableObject graph editor with visual edge renderer
- Debug HUD (detections, node state, guard timer)
- Persistent ARCore anchors
- Play Store: Data Safety form, AR Required, Depth feature matrix
