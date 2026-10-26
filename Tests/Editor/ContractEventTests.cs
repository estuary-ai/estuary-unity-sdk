using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Estuary.Models;
using NUnit.Framework;
using UnityEngine;

namespace Estuary.Tests
{
    public class ContractEventTests
    {
        private EstuaryClient _client;
        private FakeSocketIOConnection _socket;
        private GameObject _object;
        private EstuaryCharacter _character;

        [SetUp]
        public void SetUp()
        {
            _client = new EstuaryClient(); _socket = new FakeSocketIOConnection();
            _client.AttachSocketForTest(_socket);
            _object = new GameObject("contract-test");
            _character = _object.AddComponent<EstuaryCharacter>();
            _character.AutoConnect = false;
        }

        [TearDown]
        public void TearDown() { _client.Dispose(); Object.DestroyImmediate(_object); }

        [Test]
        public void RichEventsDispatchOnMainThreadWithoutLosingPayloadFields()
        {
            var received = new List<string>();
            _client.OnDelegationUpdate += e => { Assert.AreEqual("https://provider.invalid/authorize", e.AuthorizationUrl); received.Add(e.Status); };
            _client.OnApiEndpointResult += e => { Assert.AreEqual("image/png", e.Media[0].MimeType); received.Add(e.Citations[0].Title); };
            Task.Run(() =>
            {
                _socket.Receive("delegation_update", "{\"status\":\"authorization_required\",\"authorization_url\":\"https://provider.invalid/authorize\",\"message_id\":\"m\"}");
                _socket.Receive("api_endpoint_result", "{\"media\":[{\"url\":\"https://proxy.invalid/image\",\"mimeType\":\"image/png\"}],\"citations\":[{\"title\":\"Source\"}],\"message_id\":\"m\"}");
            }).GetAwaiter().GetResult();
            Assert.IsEmpty(received);
            _client.ProcessMainThreadQueue();
            CollectionAssert.AreEqual(new[] { "authorization_required", "Source" }, received);
        }

        [Test]
        public void RedactionDropsQueuedAndLateContentButPreservesOtherMessages()
        {
            var text = new List<string>(); int voice = 0, action = 0, media = 0, flags = 0;
            _client.OnBotResponse += e => text.Add(e.MessageId);
            _client.OnBotVoice += _ => voice++;
            _client.OnClientAction += _ => action++;
            _client.OnApiEndpointResult += _ => media++;
            _client.OnModerationFlag += _ => flags++;
            _socket.Receive("bot_response", "{\"text\":\"old\",\"message_id\":\"a\"}");
            _socket.Receive("moderation_flag", "{\"action\":\"redacted\",\"message_id\":\"a\",\"message\":\"Removed\"}");
            _socket.Receive("bot_voice", "{\"audio\":\"AAAA\",\"message_id\":\"a\"}");
            _socket.Receive("client_action", "{\"name\":\"wave\",\"message_id\":\"a\"}");
            _socket.Receive("api_endpoint_result", "{\"media\":[],\"message_id\":\"a\"}");
            _socket.Receive("bot_response", "{\"text\":\"new\",\"message_id\":\"b\"}");
            _client.ProcessMainThreadQueue();
            CollectionAssert.AreEqual(new[] { "b" }, text);
            Assert.AreEqual(0, voice + action + media); Assert.AreEqual(1, flags);
        }

        [Test]
        public void TerminationSuppressesBothReconnectOwnersEvenBeforeQueueIsPumped()
        {
            _client.OnModerationWarning += _character.HandleModerationWarning;
            _client.OnDisconnected += _character.HandleDisconnected;
            _socket.Receive("moderation_warning", "{\"level\":\"terminated\",\"message\":\"Session ended\"}");
            _socket.ServerDisconnect();
            Assert.AreEqual(ConnectionState.Disconnected, _client.State);
            _client.ProcessMainThreadQueue();
            Assert.IsTrue((bool)typeof(EstuaryCharacter).GetField("_serverEndedSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_character));
            // Multiple transport-close notifications must not resurrect a terminated session.
            _socket.ServerDisconnect();
            _client.ProcessMainThreadQueue();
            Assert.AreEqual(ConnectionState.Disconnected, _client.State);
        }

        [Test]
        public void DisconnectDiscardsUnplayedContentAlreadyQueuedForMainThread()
        {
            int voices = 0;
            _client.OnBotVoice += _ => voices++;
            _socket.Receive("bot_voice", "{\"audio\":\"AAAA\",\"message_id\":\"old\"}");
            _socket.ServerDisconnect("client disconnect");
            _client.ProcessMainThreadQueue();
            Assert.AreEqual(0, voices);
        }

        [Test]
        public void OldSocketCallbacksCannotAffectAReplacementConnection()
        {
            var received = new List<string>();
            _client.OnBotResponse += response => received.Add(response.MessageId);
            _socket.Receive("bot_response", "{\"text\":\"queued\",\"message_id\":\"a\"}");
            var replacement = new FakeSocketIOConnection();
            _client.AttachSocketForTest(replacement);
            _socket.Receive("bot_response", "{\"text\":\"late\",\"message_id\":\"a\"}");
            _socket.ServerDisconnect();
            replacement.Receive("bot_response", "{\"text\":\"current\",\"message_id\":\"b\"}");
            _client.ProcessMainThreadQueue();
            CollectionAssert.AreEqual(new[] { "b" }, received);
            Assert.AreEqual(ConnectionState.Connected, _client.State);
        }

        [Test]
        public void TextAccumulatesPerMessageEvenIfAudioMetadataArrivedFirst()
        {
            _character.HandleBotResponse(new BotResponse("First", false, "a"));
            _character.TrackMessageId("b");
            _character.HandleBotResponse(new BotResponse("Second", false, "b"));
            _character.HandleBotResponse(new BotResponse(" part", false, "b"));
            Assert.AreEqual("Second part", _character.CurrentPartialResponse);
            _character.HandleModerationFlag(new ModerationFlag { MessageId = "a", Message = "Removed" });
            Assert.AreEqual("Second part", _character.CurrentPartialResponse);
            _character.HandleModerationFlag(new ModerationFlag { MessageId = "b", Message = "Removed" });
            Assert.AreEqual("Removed", _character.CurrentPartialResponse);
        }

        [Test]
        public void XmlTagsRemainTextAndTypedActionsFireOnce()
        {
            int actions = 0;
            _character.OnActionReceived += _ => actions++;
            _character.HandleBotResponse(new BotResponse("Hello <action name=\"wave\"/>", true, "m"));
            Assert.AreEqual(0, actions);
            StringAssert.Contains("<action", _character.CurrentPartialResponse);
            _character.HandleClientAction(ClientActionEvent.FromJson("{\"name\":\"wave\",\"arguments\":{},\"message_id\":\"m\"}"));
            Assert.AreEqual(1, actions);
        }

        [Test]
        public void PlaybackCompletionCarriesMessageCorrelation()
        {
            _client.NotifyAudioPlaybackCompleteAsync("m").GetAwaiter().GetResult();
            Assert.AreEqual("audio_playback_complete", _socket.Emitted[0].Name);
            StringAssert.Contains("\"message_id\":\"m\"", JsonUtility.ToJson(_socket.Emitted[0].Data));
        }

        [Test]
        public void ErrorsKeepMachineCodeAndHumanMessageWithoutRetry()
        {
            ServerError error = null;
            _client.OnServerError += e => error = e;
            _socket.Receive("error", "{\"error\":\"rate_limited\",\"message\":\"Try again later\"}");
            _client.ProcessMainThreadQueue();
            Assert.AreEqual("rate_limited", error.Code); Assert.AreEqual("Try again later", error.Message);
            Assert.IsEmpty(_socket.Emitted);
        }

        [Test]
        public void LiveKitAttributesTolerateMissingMessageIdAndExposeIdleOwner()
        {
            var attrs = new Dictionary<string, string> { ["estuary.state"] = "speaking" };
            Assert.AreEqual("", BotSpeakingState.FromAttributes("bot-c", attrs).MessageId);
            attrs["estuary.message_id"] = "m"; attrs["estuary.state"] = "idle";
            var state = BotSpeakingState.FromAttributes("bot-c", attrs);
            Assert.IsFalse(state.IsSpeaking); Assert.AreEqual("m", state.MessageId);
            Assert.IsNull(BotSpeakingState.FromAttributes("bot-c", new Dictionary<string, string>()));
        }

        [TestCase("rig_failed", true)] [TestCase("animation_failed", true)] [TestCase("failed", false)]
        public void RiggingFailuresRetainUsableStaticModel(string status, bool usable)
        {
            var result = new ModelStatusResponse { ModelStatus = status, ModelUrl = "https://assets.invalid/model.glb" };
            Assert.AreEqual(usable, result.IsPartialSuccess);
            result.ModelUrl = null;
            Assert.IsTrue(result.IsFailed);
        }

        [TestCase("posing")] [TestCase("rig_checking")] [TestCase("rigging")] [TestCase("animating")]
        public void RiggingStagesContinuePolling(string status)
            => Assert.IsTrue(new ModelStatusResponse { ModelStatus = status }.IsInProgress);

        [Test]
        public void ClipNamesUseExactMatchOrUniqueSuffix()
        {
            Assert.AreEqual("preset:biped:wave", EstuaryClipPlayer.ResolveClipName(new[] { "preset:biped:wave", "idle" }, "wave"));
            Assert.IsNull(EstuaryClipPlayer.ResolveClipName(new[] { "a:wave", "b:wave" }, "wave"));
            Assert.AreEqual("wave", EstuaryClipPlayer.ResolveClipName(new[] { "a:wave", "b:wave", "wave" }, "wave"));
        }
    }
}
