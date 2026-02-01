using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Estuary.Models;

namespace Estuary
{
    /// <summary>
    /// Listens to one or more EstuaryCharacter instances and routes parsed actions
    /// to configurable handlers. Use this to drive animations, gameplay logic, or
    /// UI from AI-emitted actions (e.g. &lt;action name="sit" /&gt;, &lt;action name="wave" /&gt;).
    /// </summary>
    [AddComponentMenu("Estuary/Estuary Action Manager")]
    public class EstuaryActionManager : MonoBehaviour
    {
        #region Action binding

        [Serializable]
        public class ActionBinding
        {
            [Tooltip("Action name from the character response (e.g. sit, wave, follow). Case-insensitive.")]
            public string actionName;

            [Tooltip("Invoked when this action is received from any watched character.")]
            public AgentActionEvent onAction = new AgentActionEvent();
        }

        [Serializable]
        public class AgentActionEvent : UnityEvent<AgentAction> { }

        #endregion

        #region Inspector fields

        [Header("Characters")]
        [SerializeField]
        [Tooltip("Characters to listen to for actions. If empty, uses the EstuaryManager active character.")]
        private List<EstuaryCharacter> characters = new List<EstuaryCharacter>();

        [Header("Action bindings")]
        [SerializeField]
        [Tooltip("Bind specific action names to handlers. Invoked when a matching action is received.")]
        private List<ActionBinding> actionBindings = new List<ActionBinding>();

        [Header("Events")]
        [SerializeField]
        [Tooltip("Fired for any action from any watched character (before named bindings).")]
        private AgentActionEvent onAnyActionReceived = new AgentActionEvent();

        #endregion

        #region Public API

        /// <summary>
        /// Fired when any action is received from any watched character.
        /// </summary>
        public event Action<AgentAction> OnAnyActionReceived;

        /// <summary>
        /// Characters this manager is currently listening to.
        /// </summary>
        public IReadOnlyList<EstuaryCharacter> Characters => characters;

        /// <summary>
        /// Add a character to listen to at runtime. Safe to call multiple times for the same character.
        /// </summary>
        public void AddCharacter(EstuaryCharacter character)
        {
            if (character == null || characters.Contains(character))
                return;
            characters.Add(character);
            Subscribe(character);
        }

        /// <summary>
        /// Remove a character from the watch list.
        /// </summary>
        public void RemoveCharacter(EstuaryCharacter character)
        {
            if (character == null)
                return;
            Unsubscribe(character);
            characters.Remove(character);
        }

        /// <summary>
        /// Invoke the handler(s) for an action by name. Use from code to trigger the same path as character actions.
        /// </summary>
        /// <param name="actionName">Name of the action (e.g. sit, wave)</param>
        /// <param name="payload">Optional full action; if null, a new AgentAction with the given name is used</param>
        public void InvokeAction(string actionName, AgentAction payload = null)
        {
            var action = payload ?? new AgentAction(actionName);
            InvokeActionInternal(action);
        }

        #endregion

        #region Unity lifecycle

        private void Start()
        {
            // If no characters assigned, use active character from EstuaryManager
            if (characters.Count == 0 && EstuaryManager.HasInstance)
            {
                var active = EstuaryManager.Instance.ActiveCharacter;
                if (active != null)
                {
                    characters.Add(active);
                    Subscribe(active);
                }
                else
                {
                    EstuaryManager.Instance.OnActiveCharacterChanged += OnActiveCharacterChanged;
                }
            }
            else
            {
                for (int i = 0; i < characters.Count; i++)
                {
                    if (characters[i] != null)
                        Subscribe(characters[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if (EstuaryManager.HasInstance)
                EstuaryManager.Instance.OnActiveCharacterChanged -= OnActiveCharacterChanged;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] != null)
                    Unsubscribe(characters[i]);
            }
        }

        #endregion

        #region Event handling

        private void OnActiveCharacterChanged(EstuaryCharacter previous, EstuaryCharacter current)
        {
            if (previous != null)
            {
                Unsubscribe(previous);
                characters.Remove(previous);
            }

            if (current != null && !characters.Contains(current))
            {
                characters.Add(current);
                Subscribe(current);
            }
        }

        private void Subscribe(EstuaryCharacter character)
        {
            if (character == null) return;
            character.OnActionReceived += HandleActionReceived;
        }

        private void Unsubscribe(EstuaryCharacter character)
        {
            if (character == null) return;
            character.OnActionReceived -= HandleActionReceived;
        }

        private void HandleActionReceived(AgentAction action)
        {
            if (action == null || string.IsNullOrEmpty(action.Name))
                return;

            InvokeActionInternal(action);
        }

        private void InvokeActionInternal(AgentAction action)
        {
            var name = action.Name;

            // Global "any action" event
            OnAnyActionReceived?.Invoke(action);
            onAnyActionReceived?.Invoke(action);

            // Named bindings (case-insensitive)
            if (actionBindings != null)
            {
                foreach (var binding in actionBindings)
                {
                    if (string.Equals(binding?.actionName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        binding.onAction?.Invoke(action);
                        break;
                    }
                }
            }
        }

        #endregion
    }
}
