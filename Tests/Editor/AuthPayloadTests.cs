using System;
using System.Reflection;
using NUnit.Framework;
using Estuary.Models;

namespace Estuary.Tests
{
    /// <summary>
    /// Wire-shape tests for the Socket.IO connect auth payload (SCRUM-141,
    /// SDK_CONTRACT §Connection & Authentication). AuthenticateData is a private
    /// nested [Serializable] class serialized by JsonUtility, so its FIELD NAMES
    /// are the wire keys — locked here via reflection (JsonUtility needs the
    /// Unity native runtime and cannot run headless under plain Mono).
    /// </summary>
    [TestFixture]
    public class AuthPayloadTests
    {
        private static Type PayloadType =>
            typeof(EstuaryClient).GetNestedType("AuthenticateData", BindingFlags.NonPublic);

        private static FieldInfo Field(string name) =>
            PayloadType.GetField(name, BindingFlags.Public | BindingFlags.Instance);

        [Test]
        public void AuthenticateData_HasSnakeCaseWireFields()
        {
            Assert.IsNotNull(PayloadType, "EstuaryClient.AuthenticateData nested class not found");
            Assert.IsNotNull(Field("api_key"), "wire key 'api_key' missing");
            Assert.IsNotNull(Field("character_id"), "wire key 'character_id' missing");
            Assert.IsNotNull(Field("player_id"), "wire key 'player_id' missing");
            Assert.IsNotNull(Field("token"), "wire key 'token' missing");
        }

        [Test]
        public void AuthenticateData_AudioSampleRate_IsInt()
        {
            var field = Field("audio_sample_rate");
            Assert.IsNotNull(field, "wire key 'audio_sample_rate' missing");
            Assert.AreEqual(typeof(int), field.FieldType,
                "'audio_sample_rate' must be an int (JSON number on the wire)");
        }

        [Test]
        public void AuthenticateData_EnableAnimation_IsBool()
        {
            var field = Field("enable_animation");
            Assert.IsNotNull(field, "wire key 'enable_animation' missing");
            Assert.AreEqual(typeof(bool), field.FieldType);
        }

        [Test]
        public void AuthenticateData_CarriesTypedCapabilities()
        {
            var field = Field("capabilities");
            Assert.IsNotNull(field, "wire key 'capabilities' missing");
            Assert.AreEqual(typeof(SessionCapabilities), field.FieldType,
                "'capabilities' must be the typed SessionCapabilities model");
        }

        [Test]
        public void SessionCapabilities_DefaultsMatchContractV1()
        {
            // SCRUM-141 declaration: {version:"1", camera:true, microphone:true,
            // speaker:true} — defaults must equal the server's omit-behavior so a
            // default instance can never RESTRICT the device.
            var caps = new SessionCapabilities();
            Assert.AreEqual("1", caps.version);
            Assert.IsTrue(caps.camera);
            Assert.IsTrue(caps.microphone);
            Assert.IsTrue(caps.speaker);
            Assert.IsTrue(caps.client_action,
                "client_action must default true — the server defaults it FALSE when absent (v1.10)");
        }
    }
}
