using UnityEngine;

namespace Estuary
{
    public class EstuaryTestScript : MonoBehaviour
    {
        [SerializeField] private EstuaryCharacter character;
        [SerializeField] private EstuaryMicrophone microphone;

        void Start()
        {
            character.OnConnected += (session) =>{
                Debug.Log("Connected!");
                character.StartVoiceSession();  // Must start voice session first!
                microphone.StartRecording();
            };
            character.OnBotResponse += (response) => {
                if (response.IsFinal)
                    Debug.Log($"AI: {response.Text}");
            };
        }

        void Update()
        {
            // Press T to send a test message
            if (Input.GetKeyDown(KeyCode.T))
            {
                character.SendText("Tell me about yourself!");
            }
        }
    }
}
