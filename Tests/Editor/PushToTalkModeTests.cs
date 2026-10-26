using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Estuary.Tests
{
    [TestFixture]
    public class PushToTalkModeTests
    {
        private GameObject _gameObject;
        private EstuaryMicrophone _microphone;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("Push-to-talk test");
            _microphone = _gameObject.AddComponent<EstuaryMicrophone>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void AssignedKey_DoesNotEnablePushToTalkWhenCheckboxIsOff()
        {
            _microphone.PushToTalkKey = KeyCode.Space;

            Assert.IsFalse(_microphone.IsPushToTalkMode);

            _microphone.PushToTalkEnabled = true;
            Assert.IsTrue(_microphone.IsPushToTalkMode);

            _microphone.PushToTalkEnabled = false;
            Assert.IsFalse(_microphone.IsPushToTalkMode);
        }

        [Test]
        public void PushToTalkCanBeEnabledWithoutAKeyForCustomInput()
        {
            _microphone.PushToTalkEnabled = true;

            Assert.AreEqual(KeyCode.None, _microphone.PushToTalkKey);
            Assert.IsTrue(_microphone.IsPushToTalkMode);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InspectorKeyField_IsAvailableOnlyWhenPushToTalkIsEnabled(bool enabled)
        {
            _microphone.PushToTalkKey = KeyCode.Space;
            _microphone.PushToTalkEnabled = enabled;

            var editor = UnityEditor.Editor.CreateEditor(_microphone);
            try
            {
                var root = editor.CreateInspectorGUI();
                var enabledField = root.Q<PropertyField>("pushToTalkEnabled");
                var keyField = root.Q<PropertyField>("pushToTalkKey");

                Assert.IsNotNull(enabledField);
                Assert.IsNotNull(keyField);
                var fields = root.Children().OfType<PropertyField>().ToList();
                Assert.Less(fields.IndexOf(enabledField), fields.IndexOf(keyField));
                Assert.AreEqual(enabled, keyField.enabledInHierarchy);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }
    }
}
