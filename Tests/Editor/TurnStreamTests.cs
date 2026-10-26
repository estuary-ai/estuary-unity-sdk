using System.Collections.Generic;
using System.Text;
using Estuary.Models;
using NUnit.Framework;
using UnityEngine;

namespace Estuary.Tests
{
    public class TurnStreamTests
    {
        [Test]
        public void Utf8CharactersMaySpanPacketsAndLastLineMayLackNewline()
        {
            var reader = new TurnStreamReader();
            var bytes = Encoding.UTF8.GetBytes("\n{\"type\":\"text_delta\",\"data\":{\"text\":\"Hi 🌊\"}}\r\n{\"type\":\"turn_completed\",\"data\":{\"text\":\"Hi 🌊\"}}");
            foreach (var b in bytes) reader.Append(new[] { b }, 1);
            reader.Finish();
            Assert.IsNull(reader.Error);
            Assert.IsTrue(reader.Events.TryDequeue(out var delta));
            Assert.AreEqual("Hi 🌊", (string)delta.Data["text"]);
            Assert.IsTrue(reader.Events.TryDequeue(out var done));
            Assert.AreEqual("turn_completed", done.Type);
            Assert.IsTrue(reader.Events.IsEmpty);
        }

        [TestCase("not json\n")]
        [TestCase("{\"text\":\"missing envelope\"}\n")]
        public void MalformedEventsSurfaceErrors(string text)
        {
            var reader = new TurnStreamReader();
            var bytes = Encoding.UTF8.GetBytes(text);
            reader.Append(bytes, bytes.Length);
            Assert.IsNotNull(reader.Error);
        }

        [Test]
        public void TruncatedUtf8FailsAtEndOfStream()
        {
            var reader = new TurnStreamReader();
            reader.Append(new byte[] { 0xf0, 0x9f }, 2);
            reader.Finish();
            Assert.IsNotNull(reader.Error);
        }

        [TestCase("{\"type\":\"text_delta\",\"data\":{\"text\":\"Hello\"}}\n", "without turn_completed")]
        [TestCase("{\"type\":\"error\",\"data\":{\"message\":\"rate_limited\"}}\n", "rate_limited")]
        [TestCase("{\"type\":\"turn_completed\",\"data\":{\"actions\":\"invalid\"}}\n", "Invalid turn completion")]
        public void FailedStreamsNeverReportSuccessOrRetry(string body, string expectedError)
        {
            var config = ScriptableObject.CreateInstance<EstuaryConfig>();
            try
            {
                var http = new EstuaryHttpClient(config);
                int requests = 0, completions = 0;
                string error = null;
                http.TransportForTest = _ => { requests++; return new RestResponse { Status = 200, Body = body }; };
                RestConformanceTests.Run(http.StreamTurn("c", new CharacterTurnRequest { Message = "Hi", PlayerId = "p" },
                    null, _ => completions++, e => error = e));
                Assert.AreEqual(1, requests); Assert.AreEqual(0, completions);
                StringAssert.Contains(expectedError, error);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void StreamDeliversToolEventsInOrderBeforeCompletion()
        {
            var config = ScriptableObject.CreateInstance<EstuaryConfig>();
            try
            {
                var http = new EstuaryHttpClient(config);
                http.TransportForTest = _ => new RestResponse { Status = 200, Body =
                    "{\"type\":\"text_delta\",\"data\":{\"text\":\"Hello\"}}\n" +
                    "{\"type\":\"api_endpoint_result\",\"data\":{\"message_id\":\"m\",\"media\":[]}}\n" +
                    "{\"type\":\"turn_completed\",\"data\":{\"text\":\"Hello\",\"actions\":[{\"name\":\"wave\",\"arguments\":{\"count\":2}}]}}\n" };
                var events = new List<string>();
                CharacterTurnResponse result = null;
                RestConformanceTests.Run(http.StreamTurn("c", new CharacterTurnRequest { Message = "Hi", PlayerId = "p" },
                    e => events.Add(e.Type), r => { result = r; events.Add("callback"); }, Assert.Fail));
                CollectionAssert.AreEqual(new[] { "text_delta", "api_endpoint_result", "turn_completed", "callback" }, events);
                Assert.AreEqual(2, (int)result.Actions[0].Arguments["count"]);
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
