using UnityEngine;
using Estuary;
using Estuary.Models;

namespace Estuary.Samples
{
    /// <summary>
    /// Minimal example of voice chat with an Estuary AI character.
    /// This script shows the simplest possible setup for voice conversations.
    /// </summary>
    public class SimpleVoiceChatDemo : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField]
        [Tooltip("Character ID from Estuary dashboard")]
        private string characterId = "";

        [SerializeField]
        [Tooltip("Your player ID (leave empty for auto-generated)")]
        private string playerId = "";

        [SerializeField]
        [Tooltip("Estuary config asset")]
        private EstuaryConfig config;

        [Header("Voice Settings")]
        [SerializeField]
        [Tooltip("Key to hold for push-to-talk")]
        private KeyCode talkKey = KeyCode.Space;

        // Components (created at runtime)
        private EstuaryCharacter _character;
        private EstuaryMicrophone _microphone;
        private EstuaryAudioSource _audioSource;

        private bool _isTalking;

        private void Start()
        {
            // Validate configuration
            if (string.IsNullOrEmpty(characterId))
            {
                Debug.LogError("[SimpleVoiceChatDemo] Character ID is required!");
                enabled = false;
                return;
            }

            if (config == null)
            {
                Debug.LogError("[SimpleVoiceChatDemo] Estuary Config is required!");
                enabled = false;
                return;
            }

            // Set up EstuaryManager
            EstuaryManager.Instance.Config = config;
            EstuaryManager.Instance.DebugLogging = true;

            // Create character component
            _character = gameObject.AddComponent<EstuaryCharacter>();
            _character.CharacterId = characterId;
            _character.PlayerId = string.IsNullOrEmpty(playerId) 
                ? $"player_{SystemInfo.deviceUniqueIdentifier.Substring(0, 8)}" 
                : playerId;
            _character.AutoConnect = true;

            // Create audio components
            _audioSource = gameObject.AddComponent<EstuaryAudioSource>();
            _microphone = gameObject.AddComponent<EstuaryMicrophone>();
            _microphone.TargetCharacter = _character;

            // Subscribe to events
            _character.OnConnected += OnConnected;
            _character.OnDisconnected += OnDisconnected;
            _character.OnBotResponse += OnBotResponse;
            _character.OnTranscript += OnTranscript;
            _character.OnError += OnError;

            Debug.Log("[SimpleVoiceChatDemo] Initialized. Hold SPACE to talk.");
        }

        private void Update()
        {
            // Push-to-talk
            if (Input.GetKeyDown(talkKey) && !_isTalking)
            {
                StartTalking();
            }
            else if (Input.GetKeyUp(talkKey) && _isTalking)
            {
                StopTalking();
            }
        }

        private void OnDestroy()
        {
            if (_character != null)
            {
                _character.OnConnected -= OnConnected;
                _character.OnDisconnected -= OnDisconnected;
                _character.OnBotResponse -= OnBotResponse;
                _character.OnTranscript -= OnTranscript;
                _character.OnError -= OnError;
            }
        }

        private void StartTalking()
        {
            if (!_character.IsConnected)
            {
                Debug.Log("[SimpleVoiceChatDemo] Not connected yet!");
                return;
            }

            _isTalking = true;
            _character.StartVoiceSession();
            _microphone.StartRecording();
            Debug.Log("[SimpleVoiceChatDemo] Recording started...");
        }

        private void StopTalking()
        {
            _isTalking = false;
            _microphone.StopRecording();
            _character.EndVoiceSession();
            Debug.Log("[SimpleVoiceChatDemo] Recording stopped.");
        }

        #region Event Handlers

        private void OnConnected(SessionInfo session)
        {
            Debug.Log($"[SimpleVoiceChatDemo] Connected! Session: {session.SessionId}");
            Debug.Log("[SimpleVoiceChatDemo] Hold SPACE to talk to the AI.");
        }

        private void OnDisconnected()
        {
            Debug.Log("[SimpleVoiceChatDemo] Disconnected from server.");
            _isTalking = false;
        }

        private void OnBotResponse(BotResponse response)
        {
            if (response.IsFinal)
            {
                Debug.Log($"[SimpleVoiceChatDemo] AI: {response.Text}");
            }
        }

        private void OnTranscript(SttResponse response)
        {
            if (response.IsFinal)
            {
                Debug.Log($"[SimpleVoiceChatDemo] You: {response.Text}");
            }
            else
            {
                Debug.Log($"[SimpleVoiceChatDemo] (transcribing) {response.Text}...");
            }
        }

        private void OnError(string error)
        {
            Debug.LogError($"[SimpleVoiceChatDemo] Error: {error}");
        }

        #endregion
    }
}






