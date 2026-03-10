# Estuary Unity SDK — CLAUDE.md

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
transport_livekit_webrtc: true         # LiveKit Unity SDK (io.livekit.livekit-sdk)
audio_recording: true                  # Unity Microphone API or LiveKit native mic
audio_playback: true                   # AudioSource component
camera_capture: true                   # WebcamTexture
livekit_video: true                    # LiveKitVideoManager — continuous video streaming
scene_graph: true                      # Subscribe to world model updates
device_pose: true                      # Unity XR subsystem (XRInputSubsystem)
min_audio_sample_rate: 16000
max_audio_sample_rate: 48000
```

## Parity Status

All `REQUIRED` and `OPTIONAL` features from SDK_CONTRACT.md are implemented:

- text_chat: Implemented
- voice_websocket: Implemented
- voice_livekit: Implemented
- interrupts: Implemented
- audio_playback_tracking: Implemented
- vision_camera: Implemented
- video_streaming_livekit: Implemented
- video_streaming_websocket: Implemented (via WebcamVideoSource fallback)
- scene_graph: Implemented
- device_pose: Implemented
- preferences: Implemented

## Architecture

```
Runtime/
├── Components/          # Unity MonoBehaviour components (user-facing)
│   ├── EstuaryManager       — Singleton, coordinates connection lifecycle
│   ├── EstuaryCharacter     — Per-character instance, manages conversation
│   ├── EstuaryMicrophone    — Audio capture (Unity Mic or LiveKit native)
│   ├── EstuaryAudioSource   — TTS playback via AudioSource
│   ├── EstuaryWebcam        — Video streaming (LiveKit or WebSocket)
│   └── EstuaryActionManager — Parses XML action tags from bot responses
├── Core/                # Low-level client logic (not MonoBehaviours)
│   ├── EstuaryClient        — Socket.IO v4 client (manual protocol impl)
│   ├── EstuaryConfig        — ScriptableObject configuration asset
│   ├── EstuaryEvents        — Event definitions and delegates
│   ├── LiveKitVoiceManager  — WebRTC voice via LiveKit
│   ├── LiveKitVideoManager  — WebRTC video streaming
│   └── WebcamVideoSource    — WebSocket-based video fallback
├── Models/              # Data models matching SDK_CONTRACT.md shapes
└── Utilities/           # AudioConverter, Base64Helper, ActionParser
```

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
```

## Code Style

- C# conventions: PascalCase for public members, camelCase for private fields with `_` prefix
- One class per file, filename matches class name
- Use Unity-idiomatic patterns: ScriptableObject for config, MonoBehaviour for components, events via C# delegates
- XML doc comments on all public API surface

## Platform Notes

- Socket.IO v4 is implemented manually (Engine.IO framing + Socket.IO packet parsing) — no third-party Socket.IO library
- LiveKit uses the official Unity SDK package `io.livekit.livekit-sdk`
- Audio format: PCM 16-bit, sample rate configurable (default 16kHz for STT compatibility)
- Works across Unity platforms (Editor, Android, iOS, Windows, macOS) but LiveKit availability depends on platform support

## Documentation Maintenance

- When modifying SDK features, installation steps, or dependencies: update both `README.md` and `estuary-docs/docs/unity-sdk/` docs to keep them in sync
- LiveKit is a **required** dependency — never document it as optional
- Do not reference the `LIVEKIT_AVAILABLE` scripting define — it does not exist in the codebase
