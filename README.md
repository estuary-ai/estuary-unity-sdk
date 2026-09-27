# Estuary Unity SDK

Unity SDK for integrating Estuary AI characters with real-time voice and text chat capabilities into Unity games and applications.

> **New here?** The quickest way to start is the [**Estuary Unity SDK Template**](https://github.com/estuary-ai/estuary-unity-sdk-template) — a ready-made Unity 6 project wired for voice chat. Clone it, add your API key + character, and press Play.

## Features

- **Real-time Voice Chat**: Two modes available:
  - **LiveKit Mode** (Recommended): Low-latency WebRTC voice with native AEC (Acoustic Echo Cancellation)
  - **WebSocket Mode**: Fallback mode using Socket.IO for audio streaming
- **Text Chat**: Send and receive text messages with AI characters
- **Streaming Responses**: Receive bot responses as they're generated
- **Scripted Lines**: Make the character say prewritten lines via `SayLine` (skips the LLM)
- **Vision (VLM)**: Send camera images for the character to "see" (`SendCameraImage`), and respond to the server's proactive capture requests (`OnCameraCaptureRequested`)
- **Conversation Persistence**: Conversations are persisted per player-character pair
- **Memory Push**: Receive newly extracted memories in real time after a conversation (`OnMemoryUpdated`)
- **Typed Actions**: Receive validated action events and route them to gameplay or imported animation clips
- **Session Lifecycle**: First-class handling of idle `session_timeout` / `voice_timeout` reaps and policy `session_rejected` (concurrent-session cap) — all with correct no-auto-reconnect suppression
- **Device Capabilities**: Declare camera/mic/speaker availability per session so the character only offers tools the device supports
- **World Model Integration**: Stream webcam video (LiveKit or WebSocket) for spatial awareness + scene-graph updates
- **3D Character Models**: Download a character's Estuary-generated 3D model (GLB) and instantiate it as a GameObject at runtime (`EstuaryModelLoader`). Requires the optional glTFast importer.
- **Character Simulation**: Build worlds of your characters, trigger character-to-character conversations, and stream them live — including the evolving per-instance world-view document (`EstuarySimulation`)

## Requirements

- Unity 2022.3 LTS or newer
- .NET Standard 2.1 or .NET 4.x
- [LiveKit Unity SDK](https://github.com/livekit/client-sdk-unity) v2.1.0+

## Installation

### Option 1: Unity Package Manager (Recommended)

Unity Package Manager does not resolve transitive Git URL dependencies, so you must add the LiveKit SDK first.

1. Open the Package Manager window (`Window > Package Manager`)
2. Click the `+` button in the top-left corner
3. Select `Add package from git URL...`
4. Add the **LiveKit SDK** first:
   ```
   https://github.com/livekit/client-sdk-unity.git#v2.1.0
   ```
5. Click `+` > `Add package from git URL...` again
6. Add the **Estuary SDK**:
   ```
   https://github.com/Estuary-AI/estuary-unity-sdk.git
   ```

If your project already has an older LiveKit version, update its Git URL in Package Manager to the v2.1.0 tag above.

> **Auto-installer:** If you skip step 4, the SDK includes an Editor auto-installer that will detect the missing LiveKit dependency and offer to install it for you — along with the built-in `com.unity.modules.screencapture` module that LiveKit's video capture requires (without it you'd hit `The name 'ScreenCapture' does not exist` errors on a lean/URP project). Adding LiveKit first (as shown above) is still the recommended approach.

### Option 2: Manual Installation

1. Clone or download this repository
2. Copy the contents into your Unity project's `Packages/com.estuary.sdk/` directory
3. Ensure the [LiveKit Unity SDK](https://github.com/livekit/client-sdk-unity) is also installed

## Quick Start

> **Fastest start:** clone the ready-made [**estuary-unity-sdk-template**](https://github.com/estuary-ai/estuary-unity-sdk-template) — a Unity 6 project already wired for desktop LiveKit voice (scene, config, and an on-screen HUD). Otherwise follow the manual steps below.
>
> **Tip:** `GameObject > Estuary > AI Character` creates a fully-wired character (EstuaryCharacter + EstuaryAudioSource + EstuaryMicrophone, cross-referenced). Adding an `EstuaryCharacter` via *Add Component* auto-wires the same voice stack.

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

Speech detection, turn completion, and speech-triggered interrupts run on the server for
both LiveKit and WebSocket voice. The microphone sends continuous audio while unmuted,
or only while the button is held in push-to-talk mode. There is no local VAD setting.

| Property | Description |
|----------|-------------|
| `IsRecording` | Whether currently recording |
| `IsMuted` | Whether microphone is muted |
| `IsLiveKitMode` | Using LiveKit native capture |
| `PushToTalkKey` | Optional key to hold while PTT is enabled (`None` = no key binding) |
| `PushToTalkEnabled` | Turns PTT on or off; with no key, drive `PushToTalkPress()`/`PushToTalkRelease()` yourself |
| `IsPushToTalkMode` | Whether PTT is enabled |
| `IsPushToTalkHeld` | Whether the talk button is currently held |

#### Push-to-Talk

Hold-to-talk with server-side turn handling (contract v1.11): while the button is
held the server buffers speech instead of answering over you; release dispatches
exactly one turn. The talk button only works during an active voice session
(`StartVoiceSession()`); presses outside one are ignored. Tick `Push To Talk Enabled`
in the Inspector, then optionally set `Push To Talk Key` (e.g. Space). Without a key,
drive it from your own input (touch/XR):

```csharp
microphone.PushToTalkPress();    // button down — interrupts the bot, opens the mic
microphone.PushToTalkRelease();  // button up — server finalizes and dispatches the turn
```

Works on both transports: LiveKit (track unmute/mute) and WebSocket (chunk gating).
The key field is disabled when PTT is off, and a previously assigned key is ignored.
Existing scenes that relied on a key alone must also enable the checkbox.

### EstuaryAudioSource

Plays bot voice audio responses.

Server-confirmed interrupts stop playback and clear queued audio. Use
`EstuaryCharacter.OnInterrupt` to respond to a barge-in.

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

## Typed Actions

Characters invoke declared actions through `client_action` events. The SDK negotiates this protocol automatically; XML tags in prose never execute. Actions fire on arrival, independently of TTS playback.

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

The server keeps legacy action tags out of response text on the typed action path.
`EstuaryCharacter` passes response text through unchanged; use
`ActionParser.StripActions` only when displaying text from an older source.

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

## 3D Character Models

`EstuaryModelLoader` downloads a character's Estuary-generated 3D model (a GLB produced by the
image-to-character → generate-model pipeline) and instantiates it as a GameObject at runtime.

### Requirements

Runtime GLB import needs a glTF importer. This is an **optional** dependency (the rest of the SDK
compiles and runs without it), wired the same way as LiveKit:

- Install via the menu: **`Estuary > Install glTF Importer (glTFast)`**, or add
  `com.unity.cloud.gltfast` in Package Manager (Add package by name).
- With glTFast absent, `EstuaryModelLoader` fails loads with a clear "install a glTF importer"
  message rather than a compile error.

### Usage

```csharp
public class CharacterModelExample : MonoBehaviour
{
    public EstuaryConfig config;
    public Transform spawnPoint;

    void Start()
    {
        var loader = gameObject.AddComponent<EstuaryModelLoader>();
        loader.SetConfig(config);

        // (a) Load by agent id — resolves the ready model URL + provider automatically:
        loader.LoadForAgent(
            "your-agent-uuid",
            onSuccess: go => Debug.Log($"Loaded model: {go.name}"),
            onError:   msg => Debug.LogError(msg));

        // (b) Or load directly from a known URL (e.g. AgentResponse.BestModelUrl):
        // loader.LoadFromUrl(agent.BestModelUrl, agent.ModelProvider);
    }
}
```

Only characters whose `modelStatus` is `completed` (or `texture_failed`, which falls back to the
untextured preview) have a loadable model. To generate one first, call
`EstuaryHttpClient.GenerateModel` then `PollModelStatus` — or enable the loader's
`pollUntilReady` flag.

### EstuaryModelLoader Properties

| Property | Default | Description |
|----------|---------|-------------|
| `config` | (required) | EstuaryConfig for the REST calls |
| `spawnParent` | this transform | Where the instantiated model is parented |
| `normalizeHeight` | `0` | If > 0, uniformly scale so the model is this many meters tall (`0` = native scale) |
| `tripoRotationOffset` | `(0,-90,0)` | Extra local rotation for Tripo models (they import facing -X in Unity; -90° yaw faces +Z — verified live) |
| `meshyRotationOffset` | `(0,0,0)` | Extra local rotation for Meshy models (already face the camera) |
| `pollUntilReady` | `false` | If the model isn't ready, poll `model-status` until it completes before loading |
| `replaceExisting` | `true` | Destroy the previously loaded model before instantiating a new one |

| Member | Description |
|--------|-------------|
| `CurrentModel` | The most recently instantiated model root |
| `OnModelLoaded(GameObject)` | Fired on a successful load |
| `OnModelLoadFailed(string)` | Fired with an error message on failure |

To generate an animated humanoid, call `GenerateModel(id, onSuccess, onError, rigged: true)`.
`ModelStatusResponse` exposes `Rigged`, `Animations`, and all intermediate stages. A
`rig_failed` or `animation_failed` result with `ModelUrl` is usable as a static fallback.

Add **Estuary Clip Player** beside `EstuaryCharacter` and `EstuaryModelLoader` to play
imported skeletal clips from actions. It selects an exact clip name or a unique suffix
(`wave` → `preset:biped:wave`); ambiguous names are rejected. Idle/walk/run loop; other
clips return to idle. Audio2Face is not part of this adapter.

## REST, rich results, and moderation

`EstuaryHttpClient` now covers character CRUD/transfer/motives and uploads, conversation
history, memory reads/search/writes, shares, and single or streaming text-only HTTP turns.
Start methods as Unity coroutines; callbacks run as the coroutine is pumped. Use
`ListCharacters` for paginated v1 reads. `GetAgents` retains its unpaginated legacy behavior.
REST calls send `X-Estuary-Client`, honor `TokenProvider`, `PlayerId` and `OrgId`, and return
HTTP status plus server details on failure. A failing token provider never falls back to
an API key. Asset downloads and anonymous share redemption do not forward your credentials.

```csharp
var http = new EstuaryHttpClient(config) { PlayerId = playerId };
StartCoroutine(http.GetMessages(characterId, playerId,
    page => Debug.Log($"Loaded {page.Messages.Length} messages"), Debug.LogError));
StartCoroutine(http.SearchMemories(characterId, playerId, "favorite tea",
    result => Debug.Log($"Found {result.Total} memories"), Debug.LogError));
StartCoroutine(http.StreamTurn(characterId,
    new CharacterTurnRequest { Message = "Hello", PlayerId = playerId },
    evt => Debug.Log(evt.Type), reply => Debug.Log(reply.Text), Debug.LogError));
```

New events on `EstuaryCharacter`, `EstuaryManager`, and `EstuaryClient`:

| Event | Purpose |
|---|---|
| `OnDelegationUpdate` | Task progress and optional authorization URL |
| `OnApiEndpointResult` | Structured images/citations, separate from prose |
| `OnModerationWarning` | Warning or termination; termination requires explicit reconnect |
| `OnModerationFlag` | Redact the matching message’s historical UI/media by `MessageId` |
| `OnServerError` | Machine `Code` and human `Message`, preserving existing `OnError` |

All callbacks use the Unity main thread. URLs in results are data; your app chooses when
to open authorization links and validates media URLs before fetching without credentials.
The SDK stops matching audio and filters late redacted packets, but your app owns its UI
history. `BasicChatDemo` demonstrates scoped text replacement.

`OnBotSpeakingStateChanged` on the manager/character exposes LiveKit `estuary.state` and
optional `estuary.message_id`. The low-level optional `ILiveKitBotStateSource` provides
the current snapshot. Empty IDs are supported; these attributes are not a playback clock.

WebSocket audio acknowledges a message after `BotVoice.IsFinal` and local rendering have
finished. `OnMessagePlaybackComplete` identifies it. Raw PCM callers must explicitly call
`audioSource.MarkAudioComplete(messageId)` after their last chunk.

## Character Simulation

Run the Estuary character simulation on your own characters: group them into a
**world** with seed relationships and lore, fork a per-player **instance**, then
trigger character-to-character (pair or group) **conversations**. Immediate
triggers stream live over the `/sim-v1` Socket.IO namespace; each completed
conversation also updates the instance's **world view** — a living markdown
document of what is going on in the world.

> Simulation streaming requires an **API key** on your `EstuaryConfig` (REST
> calls also work with a `TokenProvider`). Simulation LLM usage bills to your
> monthly interaction quota.

```csharp
using Estuary;
using Estuary.Models;
using UnityEngine;

public class SimulationExample : MonoBehaviour
{
    [SerializeField] private EstuarySimulation sim;   // EstuaryConfig assigned in Inspector

    private void Start()
    {
        // 1. One-time setup: world + instance (persist the ids — instances survive restarts)
        StartCoroutine(sim.Api.CreateWorld(
            new SimulationWorldCreateRequest
            {
                Name = "Harbor Town",
                Characters = new() {
                    new SimulationCharacterSpec("character-uuid-1", "innkeeper"),
                    new SimulationCharacterSpec("character-uuid-2", "fisherman"),
                },
                SeedLore = new() { "A storm wrecked the pier last winter." },
            },
            world => StartCoroutine(sim.Api.CreateInstance(
                world.Id, "player-123",
                instance =>
                {
                    // 2. Stream + trigger
                    sim.OnSimulationMessage += m => Debug.Log($"{m.AgentName}: {m.Text}");
                    sim.OnWorldViewUpdated += md => Debug.Log($"World view:\n{md}");
                    sim.OnSimulationComplete += () => Debug.Log("Conversation finished");
                    sim.ConnectStream(instance.Id);

                    sim.TriggerConversation(
                        "character-uuid-1", "character-uuid-2",
                        intent: "Discuss rebuilding the pier before market day");
                },
                err => Debug.LogError(err))),
            err => Debug.LogError(err)));
    }
}
```

Everything else on the API surface (`sim.Api`): `ListWorlds` / `GetWorld` /
`UpdateWorld` / `DeleteWorld` / `ClearWorldMemories`, `AddCharacter` /
`RemoveCharacter` / `UpsertRelationship`, `ListInstances` / `GetInstance` /
`SetInstanceStatus` / `DeleteInstance`, `ListEvents` / `ListLore` /
`GetWorldView` / `GetConversation`. All are coroutines with
`onSuccess`/`onError` callbacks.

Notes:

- **Instances start paused.** Triggered conversations always work; activating an
  instance (`SetInstanceStatus(id, "active")`) additionally opts in to
  autonomous event generation (billable).
- **Scheduled triggers don't stream.** Set `ScheduledAt` (UTC, ≤7 days out) on a
  `SimulationConversationTrigger` to queue one for the engine, then poll
  `ListEvents` — only immediate triggers stream over `/sim-v1`.
- **World view**: fetch anytime with `sim.FetchWorldView(...)` (its `Markdown`
  is `null` until the first conversation completes) or listen to
  `OnWorldViewUpdated` for the rewritten document after each conversation.
- The `EstuarySimulation` component targets one instance at a time; call
  `ConnectStream(otherInstanceId)` to switch.
- **Fresh sessions / memory isolation.** Lore, relationships, transcripts, and
  the world view are per-instance, but memories characters save (the `remember`
  tool) are keyed by `(character, playerId)` and deliberately shared with that
  player's live chats — they would carry into a re-created world. To end a
  session cleanly, set the component's **World Id** and enable
  **Destroy World On End** in the inspector, then call `sim.EndWorld()` (also
  fired best-effort if the component is destroyed at runtime): the world is
  deleted server-side together with every memory its conversations created, so
  the next world starts with no bleed-through. To keep the world but wipe its
  memories, use `sim.ClearWorldMemories(...)` instead.

## Configuration Reference

### EstuaryConfig

| Field | Default | Description |
|-------|---------|-------------|
| `ServerUrl` | `https://api.estuary-ai.com` | Estuary server URL |
| `ApiKey` | (required) | Your API key (starts with `est_`) |
| `CharacterId` | (required) | Character UUID |
| `PlayerId` | (required) | Player identifier |
| `VoiceMode` | `LiveKit` | Voice communication mode |
| `deviceHasCamera` | `true` | Declare device camera; when off, camera/vision tools are hidden from the character |
| `deviceHasMicrophone` | `true` | Declare device microphone |
| `deviceHasSpeaker` | `true` | Declare device speaker |
| `enableAnimation` | `false` | Opt in to `bot_animation` blendshape frames (auth flag only — frames are not yet rendered; requires 16 kHz connect + server A2F) |
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

> **Keep your API key out of source control.** The `API Key` on an `EstuaryConfig`
> asset is a serialized field — committing the asset commits the key. For anything
> shared or shipped, leave the asset's key blank and inject it at runtime with
> `SetApiKeyRuntime(...)` (or a `TokenProvider` for Firebase/Bearer auth) from a
> source that isn't committed.

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

1. **"LiveKit SDK not available"**: Install the LiveKit Unity SDK via Package Manager (see [Installation](#installation))
2. **`The name 'ScreenCapture' does not exist` (compile error after adding LiveKit)**: your project is missing the built-in `com.unity.modules.screencapture` module that LiveKit's video sources need. The Estuary auto-installer adds it for you; to add it manually, use Package Manager (`+ > Add package by name...` → `com.unity.modules.screencapture`).
3. **Connection failed**: Ensure LiveKit server is running and accessible

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
void SayLine(string text, bool textOnly = false)   // scripted line (skips the LLM)

// Vision
void SendCameraImage(string imageBase64, string mimeType = "image/jpeg", string requestId = null, string text = null)

// Voice
void StartVoiceSession()
void EndVoiceSession()
void Interrupt()

// Events (C#): OnCameraCaptureRequested, OnMemoryUpdated, OnSessionRejected,
//              OnSessionTimeout, OnVoiceTimeout (plus OnBotResponse, OnVoiceReceived, ...)
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

// Vision & Preferences
Task SendCameraImageAsync(string imageBase64, string mimeType = "image/jpeg", string requestId = null, string text = null)
Task UpdatePreferencesAsync(bool enableVisionAcknowledgment)

// LiveKit
Task RequestLiveKitTokenAsync()
Task ConnectLiveKitAsync()
Task DisconnectLiveKitAsync()
Task StartLiveKitPublishingAsync()
Task StopLiveKitPublishingAsync()
```

### EstuarySimulation Methods

```csharp
// Live stream (/sim-v1) — one world instance at a time
void ConnectStream(string instanceId = null)
void DisconnectStream()

// Conveniences (coroutines are started for you)
Coroutine TriggerConversation(string sourceCharacterId, string targetCharacterId, string intent, ...)
Coroutine TriggerGroupConversation(IList<string> participantIds, string intent, ...)
Coroutine FetchWorldView(Action<SimulationWorldView> onSuccess, ...)
Coroutine FetchLore(Action<SimulationLoreList> onSuccess, ...)
Coroutine ClearWorldMemories(Action<SimulationWorldMemoriesCleared> onSuccess = null, ...)

// World lifecycle — with the Destroy World On End toggle, ends the session by
// deleting the world (and all memories it created) server-side
void EndWorld(Action onDestroyed = null, Action<string> onError = null)

// Full REST surface (run with StartCoroutine)
EstuarySimulationApi Api { get; }

// Events (C#): OnSimulationStarted, OnSimulationMessage, OnSimulationToolCall,
//              OnSimulationLore, OnWorldViewUpdated, OnSimulationComplete,
//              OnSimulationError, OnWorldDestroyed, OnStreamConnected/Disconnected/Error
```

## License

MIT License - see [LICENSE](LICENSE) for details.

## Support

- Documentation: [docs.estuary-ai.com](https://docs.estuary-ai.com)
- Issues: [GitHub Issues](https://github.com/Estuary-AI/estuary-unity-sdk/issues)
- Email: support@estuary-ai.com
