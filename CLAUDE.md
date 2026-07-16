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
camera_capture: true                   # WebcamTexture
livekit_video: true                    # LiveKitVideoManager - continuous video streaming
scene_graph: true                      # Subscribe to world model updates
device_pose: true                      # Unity XR subsystem (XRInputSubsystem)
min_audio_sample_rate: 16000
max_audio_sample_rate: 48000
default_playback_sample_rate: 24000    # TTS audio generated at 24kHz by default
```

## Parity Status

All `REQUIRED` features and the applicable `OPTIONAL` features from SDK_CONTRACT.md are implemented (web-debug-only events like `turn_metrics` are intentionally not consumed — see below):

- text_chat: Implemented
- voice_websocket: Implemented
- voice_livekit: Implemented (requires LiveKit SDK). **Warm start:** the gateway allocates its bot pre-join + STT on a `livekit_token` request (voice intent), never at auth. `EstuaryManager` therefore emits the `livekit_token` request at voice intent even when an embedded session_info token is held: the embedded token still wins the race for the client-side room join (no round-trip penalty), while the request pre-warms the server's bot join. The duplicate token response is absorbed by an idle-state guard in `HandleLiveKitTokenReceived` (only auto-connects from Disconnected/RequestingToken, so the room is never bounced).
- interrupts: Implemented
- audio_playback_tracking: Implemented
- vision_camera: Implemented — full VLM round-trip. `SendCameraImage(imageBase64, mimeType, requestId?, text?)` (client → manager → character) emits `camera_image`; the server's proactive `camera_capture` request surfaces as `OnCameraCaptureRequested(CameraCaptureRequest)`. (Distinct from `EstuaryWebcam`, which streams continuous world-model video; this is the on-demand vision path. Added 2026-07-15 for parity with the TS/Python/Lens SDKs — the prior "Implemented" claim was stale, there was no code.)
- video_streaming_livekit: Implemented (requires LiveKit SDK)
- video_streaming_websocket: Implemented (via WebcamVideoSource fallback)
- scene_graph: Implemented
- device_pose: Implemented
- preferences: Implemented — `UpdatePreferences(enableVisionAcknowledgment)` (client + manager) emits `update_preferences`. NOTE: the server currently treats `enableVisionAcknowledgment` as a no-op (vision acknowledgment moved into the worker's agentic tool system); the event + field are retained for contract parity and forward-compat. (Added 2026-07-15 — the prior "Implemented" claim was stale, there was no code.)
- session_capabilities: Implemented — the auth payload carries `capabilities {version, camera, microphone, speaker}` (per-session device declaration; server hides tools that need an absent capability). Configured via the EstuaryConfig `deviceHas*` toggles; defaults all-true (identical to omitting them). The SDK always sends a non-null object because Unity's JsonUtility serializes a null nested object as all-false. Matches the TS SDK; Python/Lens do not send it.
- memory_push: Implemented — handles `memory_updated`, fires `OnMemoryUpdated(MemoryUpdatedEvent)` (client → manager → character). `MemoryData` uses camelCase keys (matching `Memory.to_dict()`). Matches TS/Python.
- voice_timeout: Implemented — server voice-lane idle release (SDK_CONTRACT.md): after no user speech for `VOICE_IDLE_TIMEOUT_S` the server emits `voice_timeout`, deletes the LiveKit room / closes STT, and KEEPS the socket (text keeps working; no disconnect follows — never wired into reconnect suppression). `EstuaryClient` clears the voice-mode gate and fires `OnVoiceTimeout(VoiceTimeoutData)`; `EstuaryManager` disposes the local LiveKit room WITHOUT `livekit_leave` (the room is already gone server-side; its Disconnected event during this teardown is expected, not a call failure) and forwards to `EstuaryCharacter`, which stops the mic and clears `IsVoiceSessionActive` so the next `StartVoiceSession()` is a fresh session. Recommended UX is the auto-mute illusion (mic shows muted; unmute = `StartVoiceSession()`). Belt-and-braces: any non-client LiveKit room disconnect also clears stale voice state via `HandleVoiceTransportClosed`.
- session_timeout: Implemented at EVERY reconnect-owning layer (Lens lesson 7/8: socket-layer suppression alone is insufficient) — `EstuaryClient.HandleSessionTimeout` fires `OnSessionTimeout(SessionTimeoutData)` and flags the disconnect that follows so the client's auto-reconnect is suppressed; the event is forwarded client → manager → character, and `EstuaryCharacter` sets its own `_serverEndedSession` flag so its component-level `autoReconnect` also skips the reap disconnect (it previously looped: reconnect → re-auth → billed voice resources → reaped again). Both flags clear on explicit connect. Resuming requires an explicit `ConnectAsync`/`Connect()` driven by user intent, per SDK_CONTRACT.md.
- session_rejected: Implemented — handles `session_rejected` {reason, cap, share_token_id}, fires `OnSessionRejected(SessionRejectedData)` (client → manager → character). The server disconnects immediately after, so the SDK sets the same `_serverEndedSession` suppression flag as session_timeout at BOTH the client and character layers — otherwise the trailing disconnect would auto-reconnect straight back into the concurrent-session cap in a loop. (Added 2026-07-15, upgrading the prior "impl deferred" status; the reconnect-loop was a real latent bug — no other SDK handles this event either.)
- turn_metrics: Not consumed (web-debug only) — backend emits this OPTIONAL/debug event (SDK_CONTRACT.md contract v1.2) carrying per-response voice-latency timings for the web chat latency gizmo; no Unity handler needed. Contract v1.2 adds an optional/nullable `speech_end_ms` field (predictive-turn head-start metric); it is additive and does not change the decision to ignore the event.
- animation_stream (`bot_animation`): Not implemented (auth opt-in only). The auth payload now carries the `enable_animation` flag (EstuaryConfig toggle, default false) so a developer CAN request ARKit-52 blendshape frames, but the Unity SDK does not yet consume/render `bot_animation` — the frames would be dropped. This matches every other SDK: the feature is EXPERIMENTAL and has no reference implementation anywhere (not even the TS SDK). Render support is a future item; requires a 16 kHz connect + server `ENABLE_A2F=true`.
- encounter: Not implemented — the 2-character agent-to-agent Encounter (`subscribe_encounter` / `encounter_*` + `POST /api/encounters`) is Lens-Studio-only at MVP per SDK_CONTRACT.md. Add here if/when Encounter is promoted beyond the Lens SDK.

## Architecture

```
Runtime/
+-- Components/          # Unity MonoBehaviour components (user-facing)
|   +-- EstuaryManager       - Singleton, coordinates connection lifecycle
|   +-- EstuaryCharacter     - Per-character instance, manages conversation
|   +-- EstuaryMicrophone    - Audio capture (Unity Mic or LiveKit native)
|   +-- EstuaryAudioSource   - TTS playback via AudioSource
|   +-- EstuaryWebcam        - Video streaming (LiveKit or WebSocket)
|   +-- EstuaryActionManager - Parses XML action tags from bot responses
+-- Core/                # Low-level client logic (no LiveKit dependency)
|   +-- EstuaryClient        - Socket.IO v4 client (manual protocol impl)
|   +-- EstuaryConfig        - ScriptableObject configuration asset
|   +-- EstuaryEvents        - Event definitions, enums, LiveKitTokenResponse
|   +-- ILiveKitVoiceManager - Interface for voice manager abstraction
|   +-- ILiveKitVideoManager - Interface for video manager abstraction
|   +-- LiveKitBridge        - Service locator for optional LiveKit integration
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
- **Auto-installer:** `Editor/EstuaryDependencyInstaller.cs` is an `[InitializeOnLoad]` script in the `Estuary.Editor` assembly (which has `"references": []` and compiles independently of the Runtime assembly). On domain reload it checks if `io.livekit.livekit-sdk` is installed and offers to add it via `PackageManager.Client.Add()`. Uses `SessionState` to avoid repeated prompts per Editor session. Because the Editor assembly has no reference to Runtime or LiveKit, it ALWAYS runs even when LiveKit is missing.
- The `ESTUARY_LIVEKIT` scripting define is auto-set via `versionDefines` when `io.livekit.livekit-sdk` is detected. Do not manually define it.

## Documentation Maintenance

- When modifying SDK features, installation steps, or dependencies: update both `README.md` and `estuary-docs/docs/unity-sdk/` docs to keep them in sync
- LiveKit is an **optional** dependency -- the SDK works for text-only chat without it. The auto-installer prompts users to install it on first import.
