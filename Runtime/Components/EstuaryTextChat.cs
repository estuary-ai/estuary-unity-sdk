using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Estuary;

public class EstuaryTextChat : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EstuaryCharacter character;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button sendButton;

    [Header("Behavior")]
    [SerializeField] private bool sendOnEnter = true;
    [SerializeField] private bool clearAfterSend = true;

    private void Awake()
    {
        if (character == null)
            character = GetComponent<EstuaryCharacter>();
    }

    private void OnEnable()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(SendCurrentText);

        if (inputField != null)
        {
            if (sendOnEnter)
                inputField.onSubmit.AddListener(OnInputSubmit);
        }
    }

    private void OnDisable()
    {
        if (sendButton != null)
            sendButton.onClick.RemoveListener(SendCurrentText);

        if (inputField != null)
        {
            if (sendOnEnter)
                inputField.onSubmit.RemoveListener(OnInputSubmit);
        }
    }

    public void SendCurrentText()
    {
        if (inputField == null)
            return;

        SendText(inputField.text);
    }

    public void SendText(string message, bool textOnly = true)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (character == null)
        {
            Debug.LogWarning("[EstuaryTextChat] No EstuaryCharacter assigned.");
            return;
        }

        if (!character.IsConnected)
        {
            Debug.LogWarning("[EstuaryTextChat] Character is not connected.");
            return;
        }

        character.SendText(message);

        if (inputField != null && clearAfterSend)
        {
            inputField.text = "";
            inputField.ActivateInputField();
        }
    }

    private void OnInputSubmit(string text)
    {
        SendText(text);
    }
}
