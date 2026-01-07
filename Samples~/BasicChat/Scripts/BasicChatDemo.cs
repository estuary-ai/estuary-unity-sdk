using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Estuary;
using Estuary.Models;

namespace Estuary.Samples
{
    /// <summary>
    /// Basic demo script showing how to use the Estuary SDK for text and voice chat.
    /// Attach this to a GameObject with EstuaryCharacter, EstuaryMicrophone, and EstuaryAudioSource.
    /// </summary>
    public class BasicChatDemo : MonoBehaviour
    {
        #region Inspector Fields

        [Header("References")]
        [SerializeField]
        [Tooltip("The EstuaryCharacter component to use")]
        private EstuaryCharacter character;

        [SerializeField]
        [Tooltip("The EstuaryMicrophone component for voice input")]
        private EstuaryMicrophone microphone;

        [SerializeField]
        [Tooltip("The EstuaryAudioSource component for voice output")]
        private EstuaryAudioSource audioSource;

        [Header("UI (Optional)")]
        [SerializeField]
        [Tooltip("Input field for typing messages")]
        private InputField messageInput;

        [SerializeField]
        [Tooltip("Button to send messages")]
        private Button sendButton;

        [SerializeField]
        [Tooltip("Button to toggle voice mode")]
        private Button voiceButton;

        [SerializeField]
        [Tooltip("Text to display AI responses")]
        private Text responseText;

        [SerializeField]
        [Tooltip("Text to display connection status")]
        private Text statusText;

        [SerializeField]
        [Tooltip("Text to display speech transcription")]
        private Text transcriptText;

        [Header("Settings")]
        [SerializeField]
        [Tooltip("Key to hold for push-to-talk (if no UI button)")]
        private KeyCode pushToTalkKey = KeyCode.V;

        [SerializeField]
        [Tooltip("Maximum number of messages to keep in history")]
        private int maxHistoryCount = 50;

        #endregion

        #region Private Fields

        private readonly List<ChatMessage> _chatHistory = new List<ChatMessage>();
        private bool _isVoiceMode;
        private string _currentResponse = "";

        [Serializable]
        private class ChatMessage
        {
            public string Role;
            public string Content;
            public DateTime Timestamp;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Auto-find components if not assigned
            if (character == null)
                character = GetComponent<EstuaryCharacter>();

            if (microphone == null)
                microphone = GetComponent<EstuaryMicrophone>();

            if (audioSource == null)
                audioSource = GetComponent<EstuaryAudioSource>();

            // Validate required components
            if (character == null)
            {
                Debug.LogError("[BasicChatDemo] EstuaryCharacter component is required!");
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            // Subscribe to character events
            if (character != null)
            {
                character.OnConnected += HandleConnected;
                character.OnDisconnected += HandleDisconnected;
                character.OnBotResponse += HandleBotResponse;
                character.OnTranscript += HandleTranscript;
                character.OnVoiceReceived += HandleVoiceReceived;
                character.OnError += HandleError;
            }

            // Set up UI
            if (sendButton != null)
                sendButton.onClick.AddListener(OnSendClicked);

            if (voiceButton != null)
                voiceButton.onClick.AddListener(OnVoiceToggleClicked);

            if (messageInput != null)
                messageInput.onEndEdit.AddListener(OnInputEndEdit);
        }

        private void OnDisable()
        {
            // Unsubscribe from events
            if (character != null)
            {
                character.OnConnected -= HandleConnected;
                character.OnDisconnected -= HandleDisconnected;
                character.OnBotResponse -= HandleBotResponse;
                character.OnTranscript -= HandleTranscript;
                character.OnVoiceReceived -= HandleVoiceReceived;
                character.OnError -= HandleError;
            }

            // Clean up UI
            if (sendButton != null)
                sendButton.onClick.RemoveListener(OnSendClicked);

            if (voiceButton != null)
                voiceButton.onClick.RemoveListener(OnVoiceToggleClicked);

            if (messageInput != null)
                messageInput.onEndEdit.RemoveListener(OnInputEndEdit);
        }

        private void Update()
        {
            // Handle keyboard input
            if (Input.GetKeyDown(pushToTalkKey))
            {
                StartVoiceInput();
            }
            else if (Input.GetKeyUp(pushToTalkKey))
            {
                StopVoiceInput();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Send a text message to the AI character.
        /// </summary>
        /// <param name="message">The message to send</param>
        public void SendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (!character.IsConnected)
            {
                Debug.LogWarning("[BasicChatDemo] Not connected to server");
                UpdateStatus("Not connected");
                return;
            }

            // Add to history
            AddToHistory("user", message);

            // Clear current response
            _currentResponse = "";
            UpdateResponseDisplay();

            // Send to character
            character.SendText(message);

            Debug.Log($"[BasicChatDemo] Sent: {message}");

            // Clear input field
            if (messageInput != null)
            {
                messageInput.text = "";
                messageInput.ActivateInputField();
            }
        }

        /// <summary>
        /// Toggle voice input mode.
        /// </summary>
        public void ToggleVoiceMode()
        {
            _isVoiceMode = !_isVoiceMode;

            if (_isVoiceMode)
            {
                StartVoiceInput();
            }
            else
            {
                StopVoiceInput();
            }
        }

        /// <summary>
        /// Start voice input.
        /// </summary>
        public void StartVoiceInput()
        {
            if (microphone == null)
            {
                Debug.LogWarning("[BasicChatDemo] No microphone component available");
                return;
            }

            if (!character.IsConnected)
            {
                Debug.LogWarning("[BasicChatDemo] Not connected to server");
                return;
            }

            _isVoiceMode = true;
            character.StartVoiceSession();
            microphone.StartRecording();

            UpdateStatus("Listening...");
            Debug.Log("[BasicChatDemo] Voice input started");
        }

        /// <summary>
        /// Stop voice input.
        /// </summary>
        public void StopVoiceInput()
        {
            if (microphone == null)
                return;

            _isVoiceMode = false;
            microphone.StopRecording();
            character.EndVoiceSession();

            UpdateStatus("Voice stopped");
            Debug.Log("[BasicChatDemo] Voice input stopped");
        }

        /// <summary>
        /// Get the chat history.
        /// </summary>
        /// <returns>List of chat messages</returns>
        public List<(string role, string content)> GetChatHistory()
        {
            var result = new List<(string, string)>();
            foreach (var msg in _chatHistory)
            {
                result.Add((msg.Role, msg.Content));
            }
            return result;
        }

        /// <summary>
        /// Clear the chat history.
        /// </summary>
        public void ClearHistory()
        {
            _chatHistory.Clear();
            _currentResponse = "";
            UpdateResponseDisplay();
        }

        #endregion

        #region Event Handlers

        private void HandleConnected(SessionInfo sessionInfo)
        {
            Debug.Log($"[BasicChatDemo] Connected! Session: {sessionInfo.SessionId}");
            UpdateStatus($"Connected to {character.CharacterId}");
        }

        private void HandleDisconnected()
        {
            Debug.Log("[BasicChatDemo] Disconnected");
            UpdateStatus("Disconnected");
            _isVoiceMode = false;
        }

        private void HandleBotResponse(BotResponse response)
        {
            if (response.IsFinal)
            {
                // Final response - add complete message to history
                _currentResponse = response.Text;
                AddToHistory("assistant", _currentResponse);
                Debug.Log($"[BasicChatDemo] AI (final): {_currentResponse}");
            }
            else
            {
                // Streaming response - accumulate text
                _currentResponse += response.Text;
                Debug.Log($"[BasicChatDemo] AI (streaming): {response.Text}");
            }

            UpdateResponseDisplay();
        }

        private void HandleTranscript(SttResponse response)
        {
            if (response.IsFinal)
            {
                // User finished speaking
                AddToHistory("user", response.Text);
                Debug.Log($"[BasicChatDemo] You (voice): {response.Text}");
            }

            UpdateTranscriptDisplay(response.Text, response.IsFinal);
        }

        private void HandleVoiceReceived(BotVoice voice)
        {
            Debug.Log($"[BasicChatDemo] Received voice chunk: {voice.DecodedAudio.Length} bytes");
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[BasicChatDemo] Error: {error}");
            UpdateStatus($"Error: {error}");
        }

        #endregion

        #region UI Handlers

        private void OnSendClicked()
        {
            if (messageInput != null && !string.IsNullOrWhiteSpace(messageInput.text))
            {
                SendMessage(messageInput.text);
            }
        }

        private void OnVoiceToggleClicked()
        {
            ToggleVoiceMode();
        }

        private void OnInputEndEdit(string text)
        {
            // Send on Enter key
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                SendMessage(text);
            }
        }

        #endregion

        #region Private Methods

        private void AddToHistory(string role, string content)
        {
            _chatHistory.Add(new ChatMessage
            {
                Role = role,
                Content = content,
                Timestamp = DateTime.Now
            });

            // Trim history if needed
            while (_chatHistory.Count > maxHistoryCount)
            {
                _chatHistory.RemoveAt(0);
            }
        }

        private void UpdateResponseDisplay()
        {
            if (responseText != null)
            {
                responseText.text = _currentResponse;
            }
        }

        private void UpdateTranscriptDisplay(string text, bool isFinal)
        {
            if (transcriptText != null)
            {
                transcriptText.text = isFinal ? "" : $"You: {text}...";
            }
        }

        private void UpdateStatus(string status)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }
        }

        #endregion
    }
}






