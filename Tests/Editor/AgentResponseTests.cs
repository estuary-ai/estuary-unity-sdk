using Newtonsoft.Json;
using NUnit.Framework;
using Estuary.Models;

namespace Estuary.Tests
{
    /// <summary>
    /// Tests for AgentResponse / ModelStatusResponse (Runtime/Models/AgentResponse.cs):
    /// camelCase mapping (incl. the v1.7 private motive and the modelProvider that
    /// drives the GLB orientation fix) and the computed model-loading helpers.
    /// Pure C# + Newtonsoft — runs headless under Mono.
    /// </summary>
    [TestFixture]
    public class AgentResponseTests
    {
        [Test]
        public void AgentResponse_DeserializesCamelCaseFields()
        {
            const string json = @"{
                ""id"": ""a1"", ""name"": ""Nyx"", ""tagline"": ""oracle"",
                ""personality"": ""cryptic"", ""background"": ""sea-born"",
                ""motive"": ""warn the city"",
                ""avatar"": ""pic.png"", ""modelUrl"": ""m.glb"",
                ""modelPreviewUrl"": ""p.glb"", ""modelStatus"": ""completed"",
                ""modelProvider"": ""tripo"", ""generatedVoiceId"": ""v1"" }";

            var a = JsonConvert.DeserializeObject<AgentResponse>(json);
            Assert.AreEqual("a1", a.Id);
            Assert.AreEqual("Nyx", a.Name);
            Assert.AreEqual("warn the city", a.Motive);
            Assert.AreEqual("tripo", a.ModelProvider);
            Assert.AreEqual("v1", a.GeneratedVoiceId);
        }

        [Test]
        public void AgentResponse_MotiveNull_WhenOmittedByLegacyAgentsList()
        {
            // The legacy /api/agents LIST payload omits motive (privacy).
            var a = JsonConvert.DeserializeObject<AgentResponse>(
                @"{ ""id"": ""a1"", ""name"": ""Nyx"" }");
            Assert.IsNull(a.Motive);
            Assert.IsNull(a.ModelProvider);
        }

        [TestCase("completed", true)]
        [TestCase("texture_failed", true)]
        [TestCase("generating", false)]
        [TestCase("failed", false)]
        [TestCase(null, false)]
        public void AgentResponse_HasLoadableModel(string status, bool expected)
        {
            var a = new AgentResponse { ModelStatus = status };
            Assert.AreEqual(expected, a.HasLoadableModel);
        }

        [Test]
        public void AgentResponse_BestModelUrl_PrefersTexturedOverPreview()
        {
            Assert.AreEqual("m.glb",
                new AgentResponse { ModelUrl = "m.glb", ModelPreviewUrl = "p.glb" }.BestModelUrl);
        }

        [Test]
        public void AgentResponse_BestModelUrl_FallsBackToPreviewWhenModelMissing()
        {
            Assert.AreEqual("p.glb",
                new AgentResponse { ModelUrl = null, ModelPreviewUrl = "p.glb" }.BestModelUrl);
            Assert.AreEqual("p.glb",
                new AgentResponse { ModelUrl = "", ModelPreviewUrl = "p.glb" }.BestModelUrl);
        }

        [Test]
        public void ModelStatusResponse_DeserializesAndFlags()
        {
            var r = JsonConvert.DeserializeObject<ModelStatusResponse>(
                @"{ ""modelStatus"": ""generating"", ""progress"": 40, ""modelPreviewUrl"": ""p.glb"" }");
            Assert.AreEqual(40, r.Progress);
            Assert.IsTrue(r.IsInProgress);
            Assert.IsFalse(r.IsCompleted);
        }

        [TestCase("generating", true, false, false, false)]
        [TestCase("preview_ready", true, false, false, false)]
        [TestCase("completed", false, true, false, false)]
        [TestCase("failed", false, false, true, false)]
        [TestCase("texture_failed", false, false, false, true)]
        public void ModelStatusResponse_StateFlags(
            string status, bool inProgress, bool completed, bool failed, bool textureFailed)
        {
            var r = new ModelStatusResponse { ModelStatus = status };
            Assert.AreEqual(inProgress, r.IsInProgress);
            Assert.AreEqual(completed, r.IsCompleted);
            Assert.AreEqual(failed, r.IsFailed);
            Assert.AreEqual(textureFailed, r.IsTextureFailed);
        }
    }
}
