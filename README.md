# Estuary Unity SDK

Unity SDK for integrating Estuary AI characters with real-time voice and text chat capabilities into Unity games and applications.

## Features

- **Real-time Voice Chat**: Two modes available:
  - **LiveKit Mode** (Recommended): Low-latency WebRTC voice with native AEC (Acoustic Echo Cancellation)
  - **WebSocket Mode**: Fallback mode using Socket.IO for audio streaming
- **Text Chat**: Send and receive text messages with AI characters
- **Streaming Responses**: Receive bot responses as they're generated
- **Conversation Persistence**: Conversations are persisted per player-character pair
- **Action Parsing**: Parse XML-style action tags from bot responses (e.g., `<action name="wave" />`)
- **World Model Integration**: Stream webcam video for spatial awareness (coming soon)

## Requirements

- Unity 2021.3 LTS or newer
- .NET Standard 2.1 or .NET 4.x
- For LiveKit mode: [LiveKit Unity SDK](https://github.com/livekit/client-sdk-unity)

## Installation

### Option 1: Unity Package Manager (Recommended)

1. Open the Package Manager window (`Window > Package Manager`)
2. Click the `+` button in the top-left corner
3. Select `Add package from git URL...`
4. Enter the following URL:
   ```
   https://github.com/Estuary-AI/estuary-unity-sdk.git
   ```

### Option 2: Manual Installation

1. Clone or download this repository
2. Copy the contents into your Unity project's `Packages/com.estuary.sdk/` directory

### Installing LiveKit (Optional but Recommended)

For low-latency voice with echo cancellation:

1. Add the LiveKit Unity SDK via Package Manager:
   ```
   https://github.com/livekit/client-sdk-unity.git
   ```
2. Add the `LIVEKIT_AVAILABLE` scripting define symbol:
   - Go to `Edit > Project Settings > Player`
   - Find `Scripting Define Symbols`
   - Add `LIVEKIT_AVAILABLE`

## Quick Start

### 1. Create Configuration

1. Right-click in your Project window
2. Select `Create > Estuary > Config`
3. Fill in your configuration:
   - **Server URL**: `https://api.estuary-ai.com` (or your self-hosted server)
   - **API Key**: Your Estuary API key (get one at [app.estuary-ai.com](https://app.estuary-ai.com))
   - **Character ID**: The UUID of your AI character
   - **Player ID**: A unique identifier for the player (for conversation persistence)

### 2. Set Up Your Scene

Add the following components to your scene:

```
GameObject: "EstuaryManager"
├── EstuaryManager (Component)
│   └── Config: [Your EstuaryConfig asset]

GameObject: "AI Character" (e.g., your NPC)
├── EstuaryCharacter (Component)
│   ├── Character ID: [Your character UUID]
│   └── Player ID: [Unique player ID]
├── EstuaryAudioSource (Component)
│   └── Audio Source: [AudioSource component]
└── EstuaryMicrophone (Component) [Optional - for voice]
```

### 3. Basic Usage

```csharp
using Estuary;
using Estuary.Models;
using UnityEngine;

public class ChatExample : MonoBehaviour
{
    public EstuaryCharacter character;

    void Start()
    {
        // Subscribe to events
        character.OnConnected += OnConnected;
        character.OnBotResponse += OnBotResponse;
        character.OnError += OnError;
        
        // Connect (automatic if AutoConnect is enabled)
        character.Connect();
    }

    void OnConnected(SessionInfo session)
    {
        Debug.Log($"Connected! Session: {session.SessionId}");
        
        // Send a greeting
        character.SendText("Hello!");
    }

    void OnBotResponse(BotResponse response)
    {
        Debug.Log($"Bot: {response.Text}");
        
        // Check if this is the final response
        if (response.IsFinal)
        {
            Debug.Log("Response complete!");
        }
    }

    void OnError(string error)
    {
        Debug.LogError($"Error: {error}");
    }
}
```

### 4. Voice Chat

```csharp
// Start a voice session (enables microphone)
character.StartVoiceSession();

// End voice session
character.EndVoiceSession();

// Check if voice is active
if (character.IsVoiceSessionActive)
{
    // Voice chat is running
}
```

## Components

### EstuaryManager

Singleton manager that handles the global connection to Estuary servers.

| Property | Description |
|----------|-------------|
| `Config` | The EstuaryConfig asset to use |
| `IsConnected` | Whether connected to the server |
| `IsLiveKitReady` | Whether LiveKit voice is ready |
| `DebugLogging` | Enable debug log output |

### EstuaryCharacter

Attach to any GameObject that should be an AI character.

| Property | Description |
|----------|-------------|
| `CharacterId` | UUID of the character from your dashboard |
| `PlayerId` | Unique player identifier for persistence |
| `AutoConnect` | Automatically connect on Start |
| `AutoReconnect` | Automatically reconnect on disconnect |

| Event | Description |
|-------|-------------|
| `OnConnected` | Session established |
| `OnDisconnected` | Connection lost |
| `OnBotResponse` | Bot text response received |
| `OnVoiceReceived` | Bot voice audio received |
| `OnTranscript` | Speech-to-text result |
| `OnInterrupt` | Interrupt signal received |
| `OnActionReceived` | Action tag parsed from response |

### EstuaryMicrophone

Captures microphone audio for voice chat.

| Property | Description |
|----------|-------------|
| `IsRecording` | Whether currently recording |
| `IsMuted` | Whether microphone is muted |
| `IsLiveKitMode` | Using LiveKit native capture |
| `PushToTalkKey` | Key for push-to-talk (None = always on) |

### EstuaryAudioSource

Plays bot voice audio responses.

| Property | Description |
|----------|-------------|
| `IsPlaying` | Whether audio is currently playing |
| `Volume` | Playback volume (0-1) |

## Voice Modes

### LiveKit Mode (Recommended)

LiveKit provides:
- Low-latency WebRTC audio streaming
- Native Acoustic Echo Cancellation (AEC)
- Automatic Gain Control (AGC)
- Noise Suppression

```csharp
// Configuration will automatically use LiveKit if available
config.VoiceMode = VoiceMode.LiveKit;
config.AutoConnectLiveKit = true;
```

### WebSocket Mode

Fallback mode that streams audio over Socket.IO:
- Works on all platforms including WebGL
- Higher latency than LiveKit
- No native AEC (may have echo issues with speakers)

```csharp
config.VoiceMode = VoiceMode.WebSocket;
```

## Action Parsing

Bot responses can include XML-style action tags:

```xml
Hello! <action name="wave" /> Nice to meet you!
```

Subscribe to action events:

```csharp
character.OnActionReceived += (AgentAction action) =>
{
    switch (action.Name)
    {
        case "wave":
            animator.SetTrigger("Wave");
            break;
        case "sit":
            animator.SetTrigger("Sit");
            break;
    }
};
```

Action tags are automatically stripped from `CurrentPartialResponse` if `StripActionsFromText` is enabled.

## World Model / Webcam Streaming

The `EstuaryWebcam` component enables spatial awareness by streaming webcam video to the world model service.

### Setup

```csharp
public class WorldModelExample : MonoBehaviour
{
    public EstuaryCharacter character;
    public EstuaryWebcam webcam;

    void Start()
    {
        character.OnConnected += OnConnected;
        webcam.OnSceneGraphUpdated += OnSceneGraphUpdated;
    }

    void OnConnected(SessionInfo session)
    {
        // Start webcam streaming with the session ID
        webcam.StartStreaming(session.SessionId);
    }

    void OnSceneGraphUpdated(SceneGraph graph)
    {
        Debug.Log($"Scene: {graph.Summary}");
        Debug.Log($"Entities: {graph.EntityCount}");
        
        foreach (var entity in graph.Entities)
        {
            Debug.Log($"  - {entity.Label} at {entity.Position}");
        }
    }
}
```

### Streaming Modes

#### LiveKit Mode (Recommended)

Uses LiveKit WebRTC video tracks for lower latency streaming:
- Requires LiveKit SDK installed
- Uses existing LiveKit room connection (from voice chat)
- Native video codec for better quality/bandwidth
- Desktop/Mobile only (not WebGL)

```csharp
webcam.StreamMode = WebcamStreamMode.LiveKit;
webcam.StartStreaming(session.SessionId);
```

#### WebSocket Mode (Fallback)

Sends base64-encoded JPEG frames over Socket.IO:
- Works on all platforms including WebGL
- Higher latency than LiveKit
- Configurable JPEG quality

```csharp
webcam.StreamMode = WebcamStreamMode.WebSocket;
webcam.StartStreaming(session.SessionId);
```

### EstuaryWebcam Properties

| Property | Default | Description |
|----------|---------|-------------|
| `StreamMode` | `LiveKit` | Streaming method (LiveKit or WebSocket) |
| `AutoFallback` | `true` | Fall back to WebSocket if LiveKit unavailable |
| `TargetFps` | `10` | Target frames per second |
| `TargetWidth` | `1280` | Capture resolution width |
| `TargetHeight` | `720` | Capture resolution height |
| `JpegQuality` | `75` | JPEG quality for WebSocket mode |
| `UseFrontCamera` | `false` | Prefer front-facing camera |
| `SendPose` | `false` | Send camera pose with frames (AR) |
| `AutoSubscribeSceneGraph` | `true` | Auto-subscribe to scene updates |

### Scene Graph Events

```csharp
webcam.OnSceneGraphUpdated += (SceneGraph graph) =>
{
    // Access detected entities
    foreach (var entity in graph.Entities)
    {
        string name = entity.Label;
        Vector3 pos = entity.Position;
        float distance = entity.DistanceFromUser;
    }
    
    // Access spatial relationships
    foreach (var rel in graph.Relationships)
    {
        // e.g., "cup on table", "person next_to chair"
        Debug.Log($"{rel.SubjectId} {rel.Predicate} {rel.ObjectId}");
    }
    
    // Scene summary
    Debug.Log(graph.Summary);  // "Indoor office with person at desk"
};

webcam.OnRoomIdentified += (RoomIdentified room) =>
{
    Debug.Log($"Location: {room.RoomName}");  // "Living Room"
};
```

## Configuration Reference

### EstuaryConfig

| Field | Default | Description |
|-------|---------|-------------|
| `ServerUrl` | `https://api.estuary-ai.com` | Estuary server URL |
| `ApiKey` | (required) | Your API key (starts with `est_`) |
| `CharacterId` | (required) | Character UUID |
| `PlayerId` | (required) | Player identifier |
| `VoiceMode` | `LiveKit` | Voice communication mode |
| `AutoConnectLiveKit` | `true` | Auto-connect LiveKit on session |
| `RecordingSampleRate` | `16000`/`48000` | Microphone sample rate |
| `PlaybackSampleRate` | `24000` | Voice playback sample rate |
| `AudioChunkDurationMs` | `100` | Audio chunk size (WebSocket mode) |
| `AutoReconnect` | `true` | Auto-reconnect on disconnect |
| `MaxReconnectAttempts` | `5` | Max reconnection attempts |
| `ReconnectDelayMs` | `2000` | Delay between reconnects |
| `DebugLogging` | `false` | Enable debug logging |

### Runtime Configuration

```csharp
// Set API key at runtime (for builds)
config.SetApiKeyRuntime("est_your_api_key");

// Set server URL at runtime
config.SetServerUrlRuntime("https://your-server.com");

// Create config programmatically
var config = EstuaryConfig.CreateForDevelopment("est_your_key", "http://localhost:4001");
```

## Platform Support

| Platform | Text Chat | Voice (LiveKit) | Voice (WebSocket) |
|----------|-----------|-----------------|-------------------|
| Windows | Yes | Yes | Yes |
| macOS | Yes | Yes | Yes |
| Linux | Yes | Yes | Yes |
| iOS | Yes | Yes | Yes |
| Android | Yes | Yes | Yes |
| WebGL | Yes | No | Yes |

## Troubleshooting

### Connection Issues

1. **"Authentication failed"**: Check your API key is correct and starts with `est_`
2. **"Character not found"**: Verify the character UUID exists in your dashboard
3. **"Quota exceeded"**: Your monthly interaction limit has been reached

### Voice Issues

1. **No audio output**: Ensure `EstuaryAudioSource` has a valid `AudioSource` component
2. **Echo/feedback**: Use LiveKit mode for native AEC, or use headphones
3. **Microphone not working**: Check microphone permissions in player settings

### LiveKit Issues

1. **"LiveKit SDK not available"**: Add `LIVEKIT_AVAILABLE` to scripting defines
2. **Connection failed**: Ensure LiveKit server is running and accessible

## Sample Projects

See the `Samples~` folder for example implementations:

- **BasicChat**: Simple text and voice chat demo

Import samples via Package Manager:
1. Select the Estuary SDK package
2. Click `Samples` tab
3. Click `Import` next to the sample you want

## API Reference

### EstuaryCharacter Methods

```csharp
// Connection
void Connect()
void Disconnect()

// Text Chat
void SendText(string message)
Task SendTextAsync(string message)

// Voice
void StartVoiceSession()
void EndVoiceSession()
void Interrupt()
```

### EstuaryManager Methods

```csharp
// Connection
void Connect()
void Disconnect()
Task ConnectAsync()
Task DisconnectAsync()

// Character Management
void RegisterCharacter(EstuaryCharacter character)
void UnregisterCharacter(EstuaryCharacter character)
void SetActiveCharacter(EstuaryCharacter character)

// LiveKit
Task RequestLiveKitTokenAsync()
Task ConnectLiveKitAsync()
Task DisconnectLiveKitAsync()
Task StartLiveKitPublishingAsync()
Task StopLiveKitPublishingAsync()
```

## License

MIT License - see [LICENSE](LICENSE) for details.

## Support

- Documentation: [docs.estuary-ai.com](https://docs.estuary-ai.com)
- Issues: [GitHub Issues](https://github.com/Estuary-AI/estuary-unity-sdk/issues)
- Email: support@estuary-ai.com