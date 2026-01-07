# Basic Chat Sample

This sample demonstrates how to set up a basic text and voice chat with an Estuary AI character.

## Setup Instructions

1. **Create an Estuary Config**
   - Right-click in the Project window
   - Select **Create > Estuary > Config**
   - Name it "EstuaryConfig"
   - Enter your API key from [app.estuary-ai.com](https://app.estuary-ai.com)
   - Set the Server URL (default is production)

2. **Set Up the Scene**
   - Create a new scene or use the provided `BasicChatDemo.unity`
   - Create an empty GameObject named "EstuaryManager"
   - Add the `EstuaryManager` component
   - Assign your EstuaryConfig asset to the Config field

3. **Add an AI Character**
   - Create a new GameObject for your NPC (e.g., "AICharacter")
   - Add the `EstuaryCharacter` component
   - Enter the Character ID from your Estuary dashboard
   - Set a unique Player ID (or leave blank for auto-generated)
   - Enable "Auto Connect" if you want it to connect on Start

4. **Add Voice Support (Optional)**
   - Add `EstuaryMicrophone` component to the character
   - Add `EstuaryAudioSource` component to the character
   - Link them in the EstuaryCharacter inspector

5. **Add the Demo Script**
   - Add the `BasicChatDemo` component to your character
   - Assign the EstuaryCharacter reference

## Testing

1. Enter Play Mode
2. The character will connect to Estuary automatically (if Auto Connect is enabled)
3. Type a message in the text field and click Send (or press Enter)
4. View the AI response in the console and on screen

## Voice Chat

1. Make sure microphone permissions are enabled in your player settings
2. Press 'V' to toggle voice mode (or the key configured in EstuaryMicrophone)
3. Speak into your microphone
4. The AI will respond with voice

## Customization

- Edit `BasicChatDemo.cs` to customize the UI and behavior
- Use the UnityEvents in the inspector to trigger animations or other effects
- Subscribe to C# events for more complex integrations

## Troubleshooting

- **"API key not set"**: Make sure your EstuaryConfig has a valid API key
- **"Character not found"**: Verify the Character ID matches one from your dashboard
- **No audio**: Check microphone permissions and AudioSource settings
- **Connection timeout**: Verify your server URL and network connectivity






