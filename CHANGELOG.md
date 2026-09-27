# Changelog

All notable changes to the Estuary Unity SDK will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Remove the `EstuaryCharacter` **Strip Actions From Text** Inspector option. Typed `client_action` events handle actions, and response text now passes through unchanged. `ActionParser.StripActions` remains available for legacy text.
- Make `PushToTalkEnabled` the sole PTT switch. The Inspector disables `PushToTalkKey` while PTT is off, and runtime ignores a previously assigned key; existing key-only scenes must enable the checkbox.
- Remove local microphone VAD settings, WebSocket silence gating, and duplicate LiveKit microphone capture. Server-confirmed speech now drives automatic interrupts; push-to-talk still gates audio explicitly.
- Remove `IsSpeechDetected`, `OnSpeechDetected`, `OnSilenceDetected`, and `EstuaryAudioSource.SetMicrophoneReference`, along with the microphone-based auto-interrupt Inspector setting. Use `EstuaryCharacter.OnTranscript` and `OnInterrupt` for speech and barge-in events.
- Migrate generation/delete/model status to canonical v1 REST paths and identify every API request with `X-Estuary-Client`; retain the unpaginated `GetAgents` compatibility route.
- Disable executable XML action parsing; typed `client_action` remains the action source.
- Track WebSocket playback by final packet, message ID and local render deadline; reset on disconnect and isolate streaming text by message ID.


- Skip optional dependency prompts during batch-mode tests/builds; clarify that WebSocket voice works without LiveKit.
- Updated the LiveKit Editor auto-installer to v2.1.0. LiveKit voice now requires Unity 2022.3 or newer.
- Handle LiveKit v2.1.0 client disconnect callbacks once, preserving the local disconnect reason during voice teardown.

### Added

- Character CRUD/transfer/motives/tool testing, avatar/model uploads, conversations/history, memory reads/search/writes, shares, and HTTP turns with incremental NDJSON streaming.
- Delegation, structured API results, moderation and structured server errors through all client layers. Moderation redacts matching content and suppresses automatic reconnect on termination.
- Rigged model generation/status/fallback metadata and `EstuaryClipPlayer` for imported glTF skeletal clips. Audio2Face remains excluded.
- Optional LiveKit speaking-state/message-ID attributes.
- Simulation motive override and seed motive parameter; shared REST auth/headers/errors.
- Shared REST conformance fixtures and Unity regression tests for events, playback, streaming, auth and model behavior.


- **Character Simulation (v1)**: run the Estuary simulation on your own characters
  - **EstuarySimulation**: component for streaming + triggering conversations on a world instance
  - **EstuarySimulationApi**: coroutine REST client for `/api/v1/simulation/*` (worlds, instances, conversation triggers, events, lore, world view, transcripts)
  - **EstuarySimulationStream**: live `/sim-v1` Socket.IO stream (messages, tool calls, lore, world-view updates)
  - Simulation data models in `Estuary.Models` (`SimulationWorld`, `SimulationInstance`, `SimulationEvent`, `SimulationWorldView`, ...)
  - **Session isolation** (contract v1.6): `Destroy World On End` inspector toggle + `EndWorld()` on `EstuarySimulation` delete the world server-side when the session ends — the next world starts fresh with no bleed-through memories; `ClearWorldMemories()` (component + `EstuarySimulationApi`) soft-resets a world by deleting every memory its simulation created while keeping the world, instances, lore, and transcripts

## [1.0.0] - 2024-12-16

### Added

- Initial release of the Estuary Unity SDK
- **EstuaryManager**: Singleton manager for SDK initialization and connection management
- **EstuaryCharacter**: Component for making GameObjects AI-powered characters
- **EstuaryMicrophone**: Component for capturing and streaming microphone audio
- **EstuaryAudioSource**: Component for playing back AI voice responses
- **EstuaryConfig**: ScriptableObject for storing SDK configuration
- **EstuaryClient**: Low-level Socket.IO client for server communication
- Support for text chat with AI characters
- Support for real-time voice conversations
- Automatic conversation persistence via player IDs
- Push-to-talk and voice activity detection modes
- Audio interrupt handling (stop AI when user speaks)
- Streaming text responses for low latency
- Streaming audio responses for smooth playback
- Unity Events for inspector-based event handling
- C# Events for code-based event handling
- Sample scripts demonstrating basic text and voice chat

### Technical Details

- Uses Socket.IO for WebSocket communication
- Compatible with Unity 2021.3+
- Supports IL2CPP builds
- Audio format: 16kHz, 16-bit PCM for recording, 24kHz for playback
- Includes built-in audio conversion utilities

### Known Limitations

- Socket.IO implementation is basic; for production, integrate with [SocketIOClient](https://github.com/doghappy/socket.io-client-csharp)
- MP3 decoding not included; server should send PCM or WAV format
- No built-in UI; use sample scripts as reference for your own implementation


