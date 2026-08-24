# Estuary Unity SDK -- CLAUDE.md

## Overview

Unity SDK for the Estuary real-time AI conversation platform. Provides MonoBehaviour components and a core client library for integrating Estuary characters into Unity projects (AR, VR, desktop, mobile).

**Language:** C#
**Target:** Unity 2022.3+ (URP/Built-in)
**Package format:** Unity Package Manager (UPM) via `package.json`

## SDK Contract

This SDK implements the Estuary SDK API Contract defined in `SDK_CONTRACT.md` at the repository root. Always reference that file for the canonical API surface. When the contract changes, this SDK must be updated to match for all features within its platform capabilities.

## Platform Capabilities

```yaml
transport_websocket: true
transport_livekit_webrtc: true         # LiveKit Unity SDK (io.livekit.livekit-sdk) - optional
audio_recording: true                  # Unity Microphone API or LiveKit native mic
audio_playback: true                   # AudioSource component
push_to_talk: true                     # Server-side turn_mode (contract v1.11) + key/programmatic press-release
camera_capture: true                   # WebcamTexture
livekit_video: true                    # LiveKitVideoManager - continuous video streaming
scene_graph: true                      # Subscribe to world model updates
device_pose: true                      # Unity XR subsystem (XRInputSubsystem)
character_model_loading: true          # Runtime GLB import via glTFast (io.livekit-style optional dep)
simulation_api: true                   # Simulation v1 REST + /sim-v1 live streaming
min_audio_sample_rate: 16000
max_audio_sample_rate: 48000
default_playback_sample_rate: 24000    # TTS audio generated at 24kHz by default
```

## Parity Status

All `REQUIRED` features and the applicable `OPTIONAL` features from SDK_CONTRACT.md are implemented. Web-debug-only events such as `turn_metrics` are intentionally not consumed.

**Rows marked 📄 have substantial implementation notes in [`docs/PARITY_NOTES.md`](docs/PARITY_NOTES.md) — read the entry there before changing that feature.** Those notes record races that were actually hit and deliberate deviations from the other SDKs; the short note below is not sufficient context to change the behaviour safely.

| Feature | Status | Note |
|---|---|---|
| text_chat | Implemented | Full parity |
| voice_websocket | Implemented | |
| voice_livekit | Implemented | 📄 Requires LiveKit SDK. Warm-start token race + a bounded auto-unmute safety net for the first-chunk-muted race |
| voice_push_to_talk | Implemented | 📄 Contract v1.11. Server-side turn handling on both transports; **six deliberate behaviours** incl. phantom-press suppression and transport-gated teardown |
| interrupts | Implemented | |
| audio_playback_tracking | Implemented | |
| vision_camera | Implemented | 📄 Full VLM round-trip via `SendCameraImage(...)`; distinct from `EstuaryWebcam` continuous streaming |
| client_action | Implemented | 📄 Contract v1.10. **Requires the `capabilities.client_action` opt-in** or the server serves the retired XML tag path |
| video_streaming_livekit | Implemented | Requires LiveKit SDK |
| video_streaming_websocket | Implemented | Via `WebcamVideoSource` fallback |
| scene_graph | Implemented | |
| device_pose | Implemented | |
| preferences | Implemented | 📄 Server currently treats `enableVisionAcknowledgment` as a no-op; kept for contract parity |
| session_capabilities | Implemented | 📄 `client_action` is forced true on a copy so integrator config can't drop the session onto the retired path |
| memory_push | Implemented | Matches TS/Python |
| motive_push | Implemented | 📄 Contract v1.7. Legacy `/api/agents` LIST omits motive by design — expect null |
| voice_timeout | Implemented | 📄 Socket stays open, text keeps working. Never wire into reconnect suppression |
| session_timeout | Implemented | 📄 Suppressed at **every** reconnect-owning layer — socket-layer alone is insufficient (Lens lesson 7/8) |
| session_rejected | Implemented | 📄 Same suppression flag as session_timeout, else it reconnect-loops into the cap |
| character_model_loading | Implemented | 📄 Optional glTFast dependency; provider-aware orientation offset (Tripo GLBs face -X) |
| simulation (v1) | Implemented | 📄 REST + `/sim-v1` stream. Stream requires an API key; scheduled events do not stream |
| simulation motives | Implemented | 📄 Contract v1.7 |
| moderation events | Not implemented | 📄 Documented gap, contract v1.14. Ignoring is safe by design |
| input_limits / rate_limited | No SDK change required | 📄 Contract v1.13, server-side and backward compatible. Do **not** auto-retry if surfaced |
| turn_metrics | Not consumed | Web-debug only |
| animation_stream | Not implemented | 📄 `enable_animation` auth flag exists, but `bot_animation` frames are dropped. Experimental, no reference impl in any SDK |
| stt_config | Not applicable | 📄 STT runs entirely gateway-side (SCRUM-232) |
| encounter | Not implemented | Lens-Studio-only at MVP per SDK_CONTRACT.md |

## Architecture

```
Runtime/
+-- Components/          # Unity MonoBehaviour components (user-facing)
|   +-- EstuaryManager       - Singleton, coordinates connection lifecycle
|   +-- EstuaryCharacter     - Per-character instance, manages conversation
|   +-- EstuaryMicrophone    - Audio capture (Unity Mic or LiveKit native)
|   +-- EstuaryAudioSource   - TTS playback via AudioSource
|   +-- EstuaryWebcam        - Video streaming (LiveKit or WebSocket)
|   +-- EstuaryModelLoader   - Downloads a character's GLB and instantiates it as a GameObject
|   +-- EstuarySimulation    - Simulation v1: REST + /sim-v1 live stream for one world instance
|   +-- EstuaryActionManager - Dispatches named action bindings (typed client_action events + dormant legacy XML tags)
+-- Core/                # Low-level client logic (no LiveKit dependency)
|   +-- EstuaryClient        - Socket.IO v4 client (manual protocol impl)
|   +-- EstuaryConfig        - ScriptableObject configuration asset
|   +-- EstuaryEvents        - Event definitions, enums, LiveKitTokenResponse
|   +-- EstuaryHttpClient    - REST client (agents list, model generate/status, GLB download)
|   +-- EstuarySimulationApi - REST client for /api/v1/simulation/* (worlds, instances, triggers, lore, world view)
|   +-- EstuarySimulationStream - /sim-v1 Socket.IO namespace client (live conversation streaming)
|   +-- ILiveKitVoiceManager - Interface for voice manager abstraction
|   +-- ILiveKitVideoManager - Interface for video manager abstraction
|   +-- LiveKitBridge        - Service locator for optional LiveKit integration
|   +-- IEstuaryModelLoader  - Interface for the optional runtime glTF importer
|   +-- ModelLoaderBridge    - Service locator for optional glTF import (glTFast)
+-- glTFast/             # glTFast-dependent code (separate assembly, only compiles with glTFast)
|   +-- Estuary.glTFast.asmdef - defineConstraints: [ESTUARY_GLTFAST]
|   +-- GltfastModelLoader     - Runtime GLB import via GLTFast.GltfImport (implements IEstuaryModelLoader)
|   +-- GltfastRegistrar       - Auto-registers the loader factory on app start
+-- LiveKit/             # LiveKit-dependent code (separate assembly, only compiles with LiveKit SDK)
|   +-- Estuary.LiveKit.asmdef - defineConstraints: [ESTUARY_LIVEKIT]
|   +-- LiveKitRegistrar      - Auto-registers factories on app start
|   +-- LiveKitVoiceManager   - WebRTC voice via LiveKit (implements ILiveKitVoiceManager)
|   +-- LiveKitVideoManager   - WebRTC video streaming (implements ILiveKitVideoManager)
|   +-- DirectMicrophoneSource - Cross-platform mic via RtcAudioSource
|   +-- DirectWebcamVideoSource - Texture video for LiveKit
|   +-- WebcamVideoSource     - Legacy webcam wrapper
|   +-- AndroidMicrophoneSource - Android mic (inherits RtcAudioSource)
|   +-- VPIOAudioSource       - iOS native audio via RtcAudioSource
+-- Models/              # Data models matching SDK_CONTRACT.md shapes
+-- Utilities/           # AudioConverter, Base64Helper, ActionParser
```

### Optional LiveKit Dependency Pattern

LiveKit is an **optional** dependency detected via `versionDefines` in the assembly definitions:

1. When `io.livekit.livekit-sdk` is installed, `ESTUARY_LIVEKIT` is auto-defined
2. The `Estuary.LiveKit` assembly only compiles when this define is present (`defineConstraints`)
3. `LiveKitRegistrar` auto-registers concrete implementations with `LiveKitBridge` via `[RuntimeInitializeOnLoadMethod]`
4. Core components use `ILiveKitVoiceManager`/`ILiveKitVideoManager` interfaces via `LiveKitBridge`
5. When LiveKit is not installed, `LiveKitBridge.IsAvailable` returns false and components gracefully degrade to WebSocket-only mode

This design ensures the core `Estuary.asmdef` compiles with zero errors even when LiveKit is not installed, unblocking the Editor auto-installer.

## Key Events (C# delegates)

```csharp
OnSessionConnected(SessionInfo)
OnBotResponse(BotResponse)           // Streaming text chunks
OnBotVoice(BotVoice)                 // Streaming TTS audio chunks
OnSttResponse(SttResponse)           // Speech-to-text results
OnInterrupt(InterruptData)
OnLiveKitTokenReceived(LiveKitTokenResponse)
OnSceneGraphUpdate(SceneGraphUpdate)
OnQuotaExceeded(QuotaExceededData)
OnSessionTimeout(SessionTimeoutData)  // Server idle-timeout; no auto-reconnect (any layer) — resume via ConnectAsync
OnVoiceTimeout(VoiceTimeoutData)      // Server voice-idle release; socket stays, text continues — restart voice on user intent
OnCameraCaptureRequested(CameraCaptureRequest) // Server asks for an image — respond with SendCameraImage(...)
OnMemoryUpdated(MemoryUpdatedEvent)   // Newly extracted memories pushed after a conversation ends
OnSessionRejected(SessionRejectedData) // Policy cap hit (e.g. concurrent-session limit); disconnect follows, no auto-reconnect
OnClientAction(ClientActionEvent)     // Typed in-world action (contract v1.9); character layer re-fires it as OnActionReceived(AgentAction)
```

Outbound methods added for parity: `SendCameraImage(imageBase64, mimeType, requestId?, text?)` on
`EstuaryManager` and `EstuaryCharacter` (`SendCameraImageAsync` on the client), and
`UpdatePreferencesAsync(enableVisionAcknowledgment)` on `EstuaryManager` and `EstuaryClient` only
(session-level — intentionally NOT on the per-character component). The auth payload additionally
carries `capabilities` (from EstuaryConfig `deviceHas*` toggles) and `enable_animation`.

## Code Style

- C# conventions: PascalCase for public members, camelCase for private fields with `_` prefix
- One class per file, filename matches class name
- Use Unity-idiomatic patterns: ScriptableObject for config, MonoBehaviour for components, events via C# delegates
- XML doc comments on all public API surface

## Platform Notes

- Socket.IO v4 is implemented manually (Engine.IO framing + Socket.IO packet parsing) -- no third-party Socket.IO library
- LiveKit uses the official Unity SDK package `io.livekit.livekit-sdk` when installed
- Audio format: PCM 16-bit, sample rate configurable (recording: 16kHz for STT, playback: 24kHz preferred TTS default)
- Works across Unity platforms (Editor, Android, iOS, Windows, macOS) but LiveKit availability depends on platform support
- **Auto-installer:** `Editor/EstuaryDependencyInstaller.cs` is an `[InitializeOnLoad]` script in the `Estuary.Editor` assembly (which has `"references": []` and compiles independently of the Runtime assembly). On domain reload it checks if `io.livekit.livekit-sdk` is installed and, if missing, offers to add it — plus the built-in `com.unity.modules.screencapture` module LiveKit's video sources need — via `PackageManager.Client.AddAndRemove()`. It also adds `screencapture` when LiveKit is already present but that module is missing; otherwise the LiveKit assembly fails to compile with `The name 'ScreenCapture' does not exist` on a lean/URP project. Uses `SessionState` to avoid repeated prompts per Editor session. Because the Editor assembly has no reference to Runtime or LiveKit, it ALWAYS runs even when LiveKit is missing.
- **Setup helpers (editor):** `EstuaryCharacter.Reset()` auto-wires the voice stack (adds/links `EstuaryAudioSource` + `EstuaryMicrophone`, sets `microphone.TargetCharacter`) when the component is added in the Editor. `GameObject > Estuary > AI Character` (`Editor/Tools/EstuaryCreateMenu.cs`, in a separate `Estuary.Editor.Tools` asmdef that DOES reference the runtime — kept apart from the independent `Estuary.Editor` installer assembly) spawns a fully-wired character and ensures an `EstuaryManager`. The starter project lives at `estuary-ai/estuary-unity-sdk-template`.
- The `ESTUARY_LIVEKIT` scripting define is auto-set via `versionDefines` when `io.livekit.livekit-sdk` is detected. Do not manually define it.

## Documentation Maintenance

- When modifying SDK features, installation steps, or dependencies: update both `README.md` and `estuary-docs/docs/unity-sdk/` docs to keep them in sync
- **When a feature's behaviour changes, update its entry in `docs/PARITY_NOTES.md`, not just the status table in this file.** The table is the index; the notes are the record of why each feature works the way it does. A status flip with no note update loses the rationale.
- LiveKit is an **optional** dependency -- the SDK works for text-only chat without it. The auto-installer prompts users to install it on first import.
