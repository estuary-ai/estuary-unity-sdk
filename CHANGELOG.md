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






