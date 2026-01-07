# Changelog

All notable changes to the Estuary Unity SDK will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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






