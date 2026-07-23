using System;
using System.Reflection;
using NUnit.Framework;

namespace Estuary.Tests
{
    /// <summary>
    /// Wire-shape tests for EstuaryClient's camera_image payload. The payload is a
    /// private nested [Serializable] class serialized by JsonUtility, so its FIELD
    /// NAMES are the wire keys (snake_case per SDK_CONTRACT.md) — these tests lock
    /// them via reflection (JsonUtility itself needs the Unity native runtime, so
    /// serializing here would not run headless under plain Mono).
    /// </summary>
    [TestFixture]
    public class CameraImagePayloadTests
    {
        private static Type PayloadType =>
            typeof(EstuaryClient).GetNestedType("CameraImagePayload", BindingFlags.NonPublic);

        private static FieldInfo Field(string name) =>
            PayloadType.GetField(name, BindingFlags.Public | BindingFlags.Instance);

        [Test]
        public void CameraImagePayload_HasSnakeCaseWireFields()
        {
            Assert.IsNotNull(PayloadType, "EstuaryClient.CameraImagePayload nested class not found");
            Assert.IsNotNull(Field("image"), "wire key 'image' missing");
            Assert.IsNotNull(Field("mime_type"), "wire key 'mime_type' missing");
            Assert.IsNotNull(Field("request_id"), "wire key 'request_id' missing");
            Assert.IsNotNull(Field("text"), "wire key 'text' missing");
        }

        [Test]
        public void CameraImagePayload_CarriesSampleRate_AsInt()
        {
            // Without sample_rate the gateway defaults to 16000, giving VLM replies
            // 16kHz TTS in a (typically) 48kHz session.
            var field = Field("sample_rate");
            Assert.IsNotNull(field, "wire key 'sample_rate' missing");
            Assert.AreEqual(typeof(int), field.FieldType,
                "'sample_rate' must be an int (JsonUtility serializes it as a JSON number)");
        }
    }
}
