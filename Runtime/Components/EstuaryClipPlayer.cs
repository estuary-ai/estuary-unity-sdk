using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Estuary.Models;

namespace Estuary
{
    /// <summary>Play imported glTF skeletal clips from typed character actions. No Audio2Face dependency.</summary>
    [AddComponentMenu("Estuary/Estuary Clip Player")]
    public class EstuaryClipPlayer : MonoBehaviour
    {
        [SerializeField] private EstuaryCharacter character;
        [SerializeField] private EstuaryModelLoader modelLoader;
        [SerializeField] private string idleAction = "idle";
        [SerializeField] private float crossFadeSeconds = 0.15f;
        private Animation[] _animations = Array.Empty<Animation>();
        private Coroutine _returnToIdle;

        /// <summary>Fired with the action name when no unique matching clip is available.</summary>
        public event Action<string> OnClipNotFound;

        private void OnEnable()
        {
            if (character == null) character = GetComponent<EstuaryCharacter>();
            if (modelLoader == null) modelLoader = GetComponent<EstuaryModelLoader>();
            if (character != null) character.OnActionReceived += HandleAction;
            if (modelLoader != null)
            {
                modelLoader.OnModelLoaded += SetModel;
                if (modelLoader.CurrentModel != null) SetModel(modelLoader.CurrentModel);
            }
        }

        private void OnDisable()
        {
            if (character != null) character.OnActionReceived -= HandleAction;
            if (modelLoader != null) modelLoader.OnModelLoaded -= SetModel;
            if (_returnToIdle != null) StopCoroutine(_returnToIdle);
            _returnToIdle = null;
        }

        /// <summary>Bind an imported model hierarchy, including models loaded by a custom importer.</summary>
        public void SetModel(GameObject model)
        {
            if (_returnToIdle != null) StopCoroutine(_returnToIdle);
            _returnToIdle = null;
            _animations = model == null ? Array.Empty<Animation>() : model.GetComponentsInChildren<Animation>(true);
            foreach (var animation in _animations) { animation.playAutomatically = false; animation.Stop(); }
            if (!string.IsNullOrEmpty(idleAction)) PlayAction(idleAction);
        }

        private void HandleAction(AgentAction action) => PlayAction(action.Name);

        /// <summary>Resolve an exact clip name or a unique colon-delimited suffix (fold_arms → preset:biped:fold_arms).</summary>
        public bool PlayAction(string actionName)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var animation in _animations)
                if (animation != null)
                    foreach (AnimationState state in animation) names.Add(state.name);
            var clip = ResolveClipName(names, actionName);
            if (clip == null) { OnClipNotFound?.Invoke(actionName); return false; }
            if (_returnToIdle != null) StopCoroutine(_returnToIdle);
            _returnToIdle = null;
            bool loop = actionName == idleAction || actionName == "walk" || actionName == "run";
            float length = 0;
            foreach (var animation in _animations)
            {
                if (animation == null || animation[clip] == null) continue;
                var state = animation[clip];
                state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
                length = Mathf.Max(length, state.length / Mathf.Max(Mathf.Abs(state.speed), 0.01f));
                animation.CrossFade(clip, Mathf.Max(0, crossFadeSeconds));
            }
            if (!loop && !string.IsNullOrEmpty(idleAction) && isActiveAndEnabled)
                _returnToIdle = StartCoroutine(ReturnToIdle(length));
            return true;
        }

        private IEnumerator ReturnToIdle(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _returnToIdle = null;
            PlayAction(idleAction);
        }

        internal static string ResolveClipName(IEnumerable<string> names, string action)
        {
            if (string.IsNullOrEmpty(action)) return null;
            string candidate = null;
            bool ambiguous = false;
            foreach (var name in names)
            {
                if (name == action) return name;
                if (name == null || !name.EndsWith(":" + action, StringComparison.Ordinal)) continue;
                if (candidate != null && candidate != name) ambiguous = true;
                candidate = name;
            }
            return ambiguous ? null : candidate;
        }
    }
}
