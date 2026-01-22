using System;
using Estuary.Models;

namespace Estuary
{
    /// <summary>
    /// Event arguments for Estuary SDK events.
    /// </summary>
    public static class EstuaryEvents
    {
        /// <summary>
        /// Delegate for session connection events.
        /// </summary>
        public delegate void SessionConnectedHandler(SessionInfo sessionInfo);

        /// <summary>
        /// Delegate for disconnection events.
        /// </summary>
        public delegate void DisconnectedHandler(string reason);

        /// <summary>
        /// Delegate for bot text response events.
        /// </summary>
        public delegate void BotResponseHandler(BotResponse response);

        /// <summary>
        /// Delegate for bot voice audio events.
        /// </summary>
        public delegate void BotVoiceHandler(BotVoice voice);

        /// <summary>
        /// Delegate for speech-to-text events.
        /// </summary>
        public delegate void SttResponseHandler(SttResponse response);

        /// <summary>
        /// Delegate for interrupt events.
        /// </summary>
        public delegate void InterruptHandler(InterruptData data);

        /// <summary>
        /// Delegate for error events.
        /// </summary>
        public delegate void ErrorHandler(string errorMessage);

        /// <summary>
        /// Delegate for connection state change events.
        /// </summary>
        public delegate void ConnectionStateHandler(ConnectionState state);

        /// <summary>
        /// Delegate for action events parsed from bot responses.
        /// Actions are embedded using XML-style tags: &lt;action name="sit" /&gt;
        /// </summary>
        public delegate void ActionReceivedHandler(AgentAction action);

        /// <summary>
        /// Delegate for quota exceeded events.
        /// Fired when the API key owner has exceeded their monthly interaction quota.
        /// </summary>
        public delegate void QuotaExceededHandler(QuotaExceededData data);

        #region World Model Events

        /// <summary>
        /// Delegate for scene graph update events from the world model.
        /// </summary>
        public delegate void SceneGraphUpdateHandler(SceneGraphUpdate update);

        /// <summary>
        /// Delegate for room identified events from the world model.
        /// </summary>
        public delegate void RoomIdentifiedHandler(RoomIdentified room);

        #endregion

        #region LiveKit Events

        /// <summary>
        /// Delegate for when a LiveKit token is received from the server.
        /// </summary>
        public delegate void LiveKitTokenReceivedHandler(LiveKitTokenResponse tokenResponse);

        /// <summary>
        /// Delegate for when the LiveKit room is ready (bot has joined).
        /// </summary>
        public delegate void LiveKitReadyHandler(string roomName);

        /// <summary>
        /// Delegate for LiveKit error events.
        /// </summary>
        public delegate void LiveKitErrorHandler(string errorMessage);

        /// <summary>
        /// Delegate for when successfully connected to a LiveKit room.
        /// </summary>
        public delegate void LiveKitConnectedHandler(string roomName);

        /// <summary>
        /// Delegate for when disconnected from the LiveKit room.
        /// </summary>
        public delegate void LiveKitDisconnectedHandler(string reason);

        /// <summary>
        /// Delegate for when audio data is received via LiveKit from a remote participant.
        /// Parameters: PCM audio bytes, sample rate, number of channels.
        /// </summary>
        public delegate void LiveKitAudioReceivedHandler(byte[] pcmData, int sampleRate, int channels);

        /// <summary>
        /// Delegate for when a remote participant connects to the LiveKit room.
        /// </summary>
        public delegate void LiveKitParticipantConnectedHandler(string participantId);

        /// <summary>
        /// Delegate for when a remote participant disconnects from the LiveKit room.
        /// </summary>
        public delegate void LiveKitParticipantDisconnectedHandler(string participantId);

        #endregion
    }

    /// <summary>
    /// Connection states for the Estuary client.
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>
        /// Not connected to the server.
        /// </summary>
        Disconnected,

        /// <summary>
        /// Currently attempting to connect.
        /// </summary>
        Connecting,

        /// <summary>
        /// Connected and ready to communicate.
        /// </summary>
        Connected,

        /// <summary>
        /// Connection was lost, attempting to reconnect.
        /// </summary>
        Reconnecting,

        /// <summary>
        /// An error occurred during connection.
        /// </summary>
        Error
    }

    /// <summary>
    /// Connection states for LiveKit voice mode.
    /// </summary>
    public enum LiveKitConnectionState
    {
        /// <summary>
        /// Not connected to a LiveKit room.
        /// </summary>
        Disconnected,

        /// <summary>
        /// Requesting token from server.
        /// </summary>
        RequestingToken,

        /// <summary>
        /// Connecting to LiveKit room.
        /// </summary>
        Connecting,

        /// <summary>
        /// Connected to LiveKit room.
        /// </summary>
        Connected,

        /// <summary>
        /// Waiting for bot to join the room.
        /// </summary>
        WaitingForBot,

        /// <summary>
        /// Fully ready - bot has joined and audio is flowing.
        /// </summary>
        Ready,

        /// <summary>
        /// An error occurred.
        /// </summary>
        Error
    }
}






