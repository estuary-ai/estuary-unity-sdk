using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Estuary.Editor
{
    [CustomEditor(typeof(EstuaryMicrophone))]
    [CanEditMultipleObjects]
    internal sealed class EstuaryMicrophoneEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var script = new PropertyField(serializedObject.FindProperty("m_Script"));
            script.SetEnabled(false);
            root.Add(script);

            root.Add(new PropertyField(serializedObject.FindProperty("targetCharacter")));
            root.Add(new PropertyField(serializedObject.FindProperty("sampleRate")));
            root.Add(new PropertyField(serializedObject.FindProperty("chunkDurationMs")));
            root.Add(new PropertyField(serializedObject.FindProperty("microphoneDevice")));

            var pushToTalkEnabled = serializedObject.FindProperty("pushToTalkEnabled");
            var enabledField = new PropertyField(pushToTalkEnabled)
            {
                name = "pushToTalkEnabled"
            };
            var keyField = new PropertyField(serializedObject.FindProperty("pushToTalkKey"))
            {
                name = "pushToTalkKey"
            };
            root.Add(enabledField);
            root.Add(keyField);

            void UpdateKeyField(SerializedProperty property)
            {
                keyField.SetEnabled(property.boolValue && !property.hasMultipleDifferentValues);
            }

            UpdateKeyField(pushToTalkEnabled);
            root.TrackPropertyValue(pushToTalkEnabled, UpdateKeyField);

            root.Add(new PropertyField(serializedObject.FindProperty("onRecordingStarted")));
            root.Add(new PropertyField(serializedObject.FindProperty("onRecordingStopped")));
            root.Add(new PropertyField(serializedObject.FindProperty("onVolumeChanged")));

            return root;
        }
    }
}
