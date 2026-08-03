using System.Reflection;
using NUnit.Framework;

namespace Estuary.Tests
{
    /// <summary>
    /// Wire-emission tests for server-side push-to-talk (SDK_CONTRACT v1.11,
    /// SCRUM-220), driven through a fake socket injected via EstuaryClient's
    /// internal test seam. Serializing is JsonUtility's job (not headless-safe);
    /// these tests assert event names + typed payload objects instead.
    /// </summary>
    [TestFixture]
    public class PushToTalkEmissionTests
    {
        private EstuaryClient _client;
        private FakeSocketIOConnection _socket;

        [SetUp]
        public void SetUp()
        {
            _client = new EstuaryClient();
            _socket = new FakeSocketIOConnection();
            _client.AttachSocketForTest(_socket);
        }

        [Test]
        public void AttachSocketForTest_MarksClientConnected()
        {
            Assert.IsTrue(_client.IsConnected,
                "the seam must mark the client Connected so emit paths run");
        }

        [Test]
        public void SessionTurnMode_DefaultsToContinuous()
        {
            Assert.AreEqual(TurnMode.Continuous, _client.SessionTurnMode);
        }

        [Test]
        public void LiveKitToken_Continuous_EmitsNullPayload()
        {
            _client.RequestLiveKitTokenAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, _socket.Emitted.Count);
            Assert.AreEqual("livekit_token", _socket.Emitted[0].Name);
            Assert.IsNull(_socket.Emitted[0].Data,
                "continuous mode must stay wire-identical to pre-v1.11 (null payload)");
        }

        [Test]
        public void LiveKitToken_Ptt_EmitsTurnModePayload()
        {
            _client.SessionTurnMode = TurnMode.PushToTalk;

            _client.RequestLiveKitTokenAsync().GetAwaiter().GetResult();

            Assert.AreEqual("livekit_token", _socket.Emitted[0].Name);
            var payload = _socket.Emitted[0].Data as EstuaryClient.TurnModePayload;
            Assert.IsNotNull(payload, "PTT must declare turn_mode on livekit_token");
            Assert.AreEqual("push_to_talk", payload.turn_mode);
        }

        [Test]
        public void LiveKitJoin_Ptt_EmitsTurnModePayload()
        {
            _client.SessionTurnMode = TurnMode.PushToTalk;

            _client.NotifyLiveKitJoinedAsync().GetAwaiter().GetResult();

            Assert.AreEqual("livekit_join", _socket.Emitted[0].Name);
            Assert.IsInstanceOf<EstuaryClient.TurnModePayload>(_socket.Emitted[0].Data);
        }

        [Test]
        public void LiveKitJoin_Continuous_EmitsNullPayload()
        {
            _client.NotifyLiveKitJoinedAsync().GetAwaiter().GetResult();

            Assert.AreEqual("livekit_join", _socket.Emitted[0].Name);
            Assert.IsNull(_socket.Emitted[0].Data);
        }

        [Test]
        public void StartVoice_Ptt_EmitsTurnModePayload()
        {
            // WebSocket transport: the session-start start_voice IS the
            // declaration (opens the per-turn STT stream with PTT options).
            _client.SessionTurnMode = TurnMode.PushToTalk;

            _client.StartVoiceModeAsync().GetAwaiter().GetResult();

            Assert.AreEqual("start_voice", _socket.Emitted[0].Name);
            Assert.IsInstanceOf<EstuaryClient.TurnModePayload>(_socket.Emitted[0].Data);
        }

        [Test]
        public void StartVoice_Continuous_EmitsNullPayload()
        {
            _client.StartVoiceModeAsync().GetAwaiter().GetResult();

            Assert.AreEqual("start_voice", _socket.Emitted[0].Name);
            Assert.IsNull(_socket.Emitted[0].Data);
        }

        [Test]
        public void TurnModePayload_WireShape()
        {
            var field = typeof(EstuaryClient.TurnModePayload)
                .GetField("turn_mode", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field, "wire key 'turn_mode' missing");
            Assert.AreEqual(typeof(string), field.FieldType);
            Assert.AreEqual("push_to_talk", new EstuaryClient.TurnModePayload().turn_mode);
        }
    }
}
