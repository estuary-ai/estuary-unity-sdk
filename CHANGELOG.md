# Changelog

All notable changes to the Estuary Unity SDK will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Character Simulation (v1)**: run the Estuary simulation on your own characters
  - **EstuarySimulation**: component for streaming + triggering conversations on a world instance
  - **EstuarySimulationApi**: coroutine REST client for `/api/v1/simulation/*` (worlds, instances, conversation triggers, events, lore, world view, transcripts)
  - **EstuarySimulationStream**: live `/sim-v1` Socket.IO stream (messages, tool calls, lore, world-view updates)
  - Simulation data models in `Estuary.Models` (`SimulationWorld`, `SimulationInstance`, `SimulationEvent`, `SimulationWorldView`, ...)
  - **Session isolation** (contract v1.6): `Destroy World On End` inspector toggle + `EndWorld()` on `EstuarySimulation` delete the world server-side when the session ends — the next world starts fresh with no bleed-through memories; `ClearWorldMemories()` (component + `EstuarySimulationApi`) soft-resets a world by deleting every memory its simulation created while keeping the world, instances, lore, and transcripts
- **Client identification** (SCRUM-255): every Estuary REST request now sends `X-Estuary-Client: estuary-unity-sdk/<version>`. The version comes from the new `EstuarySdkInfo.Version` constant, which an EditMode test keeps equal to `package.json`. Not sent on GLB downloads or on socket connections
- **REST conformance tests**: `Tests/Editor/RestConformanceTests.cs` checks the SDK's REST routes and headers against the monorepo's `sdk-conformance/rest.json`

### Changed

- **Canonical REST routes** (SCRUM-255): `UploadImageToCharacter` now calls `POST /api/v1/characters/from-image`, `GenerateModel` calls `POST /api/v1/characters/{id}/model`, `GetModelStatus`/`PollModelStatus` call `GET /api/v1/characters/{id}/model`, and `DeleteAgent` calls `DELETE /api/v1/characters/{id}`. Method signatures and result types are unchanged. `AgentResponse.ModelProvider` is null on the `UploadImageToCharacter` result because the v1 character shape does not carry it. `GetAgents` stays on `GET /api/agents` for now (the v1 list is paginated and omits `modelProvider`). Requires a gateway that serves these `/api/v1` routes

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






