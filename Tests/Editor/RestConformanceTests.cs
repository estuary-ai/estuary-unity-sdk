using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Estuary.Tests
{
    /// <summary>
    /// REST conformance (SCRUM-255): the routes and headers EstuaryHttpClient would send,
    /// observed through the EstuaryRestRoutes / BuildHeaders seam (there is no HTTP fake).
    /// Pure C# + Newtonsoft, runs headless under Mono.
    /// The pinned tests always run. The fixture-driven test reads the monorepo's
    /// sdk-conformance/rest.json and is ignored when the package lives outside the monorepo.
    /// </summary>
    [TestFixture]
    public class RestConformanceTests
    {
        const string CharacterId = "char_123";

        static readonly Dictionary<string, Func<string, EstuaryRestRoute>> Routes =
            new Dictionary<string, Func<string, EstuaryRestRoute>>
            {
                { "UploadImageToCharacter", _ => EstuaryRestRoutes.UploadImageToCharacter() },
                { "GetModelStatus", EstuaryRestRoutes.GetModelStatus },
                { "GenerateModel", EstuaryRestRoutes.GenerateModel },
                { "GetAgents", _ => EstuaryRestRoutes.GetAgents() },
                { "DeleteAgent", EstuaryRestRoutes.DeleteAgent },
            };

        // Listed in rest.json but deliberately still on a legacy route (see CLAUDE.md Parity Status).
        static readonly HashSet<string> KnownLegacy = new HashSet<string> { "GetAgents" };

        static void AssertRoute(EstuaryRestRoute route, string method, string path)
        {
            Assert.AreEqual(method, route.Method);
            Assert.AreEqual(path, route.Path);
        }

        [Test]
        public void Routes_ArePinned()
        {
            AssertRoute(EstuaryRestRoutes.UploadImageToCharacter(), "POST", "/api/v1/characters/from-image");
            AssertRoute(EstuaryRestRoutes.GetModelStatus(CharacterId), "GET", "/api/v1/characters/char_123/model");
            AssertRoute(EstuaryRestRoutes.GenerateModel(CharacterId), "POST", "/api/v1/characters/char_123/model");
            AssertRoute(EstuaryRestRoutes.DeleteAgent(CharacterId), "DELETE", "/api/v1/characters/char_123");
            AssertRoute(EstuaryRestRoutes.GetAgents(), "GET", "/api/agents");
        }

        [Test]
        public void Headers_IdentifyTheSdkAndCarryAuth()
        {
            var headers = EstuaryHttpClient.BuildHeaders(null, "est_test", false, "player_1");
            Assert.AreEqual("estuary-unity-sdk/" + EstuarySdkInfo.Version, headers["X-Estuary-Client"]);
            Assert.AreEqual("est_test", headers["X-API-Key"]);
            Assert.AreEqual("player_1", headers["X-Player-Id"]);

            var bearer = EstuaryHttpClient.BuildHeaders("tok", "est_test", true, null);
            Assert.AreEqual("Bearer tok", bearer["Authorization"]);
            Assert.IsFalse(bearer.ContainsKey("X-API-Key"));
            Assert.IsFalse(bearer.ContainsKey("X-Player-Id"));
            Assert.IsTrue(bearer.ContainsKey("X-Estuary-Client"));

            // A configured token provider that failed must not fall back to the API key.
            var failedProvider = EstuaryHttpClient.BuildHeaders(null, "est_test", true, null);
            Assert.IsFalse(failedProvider.ContainsKey("X-API-Key"));
            Assert.IsTrue(failedProvider.ContainsKey("X-Estuary-Client"));
        }

        [Test]
        public void Routes_MatchConformanceFixture()
        {
            var fixturePath = FindFixture();
            if (fixturePath == null)
                Assert.Ignore("sdk-conformance/rest.json not found above the package (not inside the monorepo).");

            var fixture = JObject.Parse(File.ReadAllText(fixturePath));
            var characterId = (string)fixture["args"]["characterId"];

            var pattern = (string)fixture["headers"]["X-Estuary-Client"]["pattern"];
            Assert.IsTrue(Regex.IsMatch(EstuarySdkInfo.ClientHeaderValue, pattern),
                $"{EstuarySdkInfo.ClientHeaderValue} does not match {pattern}");

            int checkedCases = 0;
            foreach (var c in fixture["cases"])
            {
                var method = (string)c["sdk"]?["unity"];
                if (method == null) continue;

                Assert.IsTrue(Routes.ContainsKey(method), $"case {c["id"]}: no route builder for {method}");
                if (KnownLegacy.Contains(method)) continue;

                var route = Routes[method](characterId);
                Assert.AreEqual((string)c["method"], route.Method, $"case {c["id"]} method");
                Assert.AreEqual((string)c["path"], route.Path, $"case {c["id"]} path");
                checkedCases++;
            }
            Assert.Greater(checkedCases, 0, "fixture lists no unity cases");
        }

        [Test]
        public void ParseCharacter_MapsV1SnakeCaseOntoAgentResponse()
        {
            const string json = @"{
                ""id"": ""c1"", ""name"": ""Nyx"", ""tagline"": ""oracle"", ""avatar"": ""pic.png"",
                ""appearance"": ""tall"", ""motive"": null,
                ""model_url"": ""m.glb"", ""model_preview_url"": ""p.glb"", ""model_status"": ""generating"",
                ""source_image_url"": ""src.jpg"", ""generated_voice_id"": ""v1"", ""model_rigged"": false }";

            var a = EstuaryHttpClient.ParseCharacter(json);
            Assert.AreEqual("c1", a.Id);
            Assert.AreEqual("Nyx", a.Name);
            Assert.AreEqual("tall", a.Appearance);
            Assert.AreEqual("m.glb", a.ModelUrl);
            Assert.AreEqual("p.glb", a.ModelPreviewUrl);
            Assert.AreEqual("generating", a.ModelStatus);
            Assert.AreEqual("src.jpg", a.SourceImageUrl);
            Assert.AreEqual("v1", a.GeneratedVoiceId);
            Assert.IsNull(a.ModelProvider);
        }

        [Test]
        public void ParseCharacter_StillAcceptsLegacyCamelCase()
        {
            var a = EstuaryHttpClient.ParseCharacter(
                @"{ ""id"": ""a1"", ""modelUrl"": ""m.glb"", ""modelStatus"": ""completed"", ""modelProvider"": ""tripo"" }");
            Assert.AreEqual("m.glb", a.ModelUrl);
            Assert.AreEqual("completed", a.ModelStatus);
            Assert.AreEqual("tripo", a.ModelProvider);
        }

        static string FindFixture()
        {
            var dir = new DirectoryInfo(SdkVersionTests.PackageRoot());
            for (; dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "sdk-conformance", "rest.json");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
