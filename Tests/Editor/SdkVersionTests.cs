using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Estuary.Tests
{
    /// <summary>
    /// EstuarySdkInfo.Version is what the SDK reports in X-Estuary-Client, so it must
    /// not drift from the package manifest. Pure C# + Newtonsoft, runs headless under Mono.
    /// </summary>
    [TestFixture]
    public class SdkVersionTests
    {
        // Tests/Editor/<caller file> -> package root
        internal static string PackageRoot([CallerFilePath] string thisFile = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), "..", ".."));

        [Test]
        public void Version_MatchesPackageJson()
        {
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot(), "package.json")));
            Assert.AreEqual((string)manifest["version"], EstuarySdkInfo.Version,
                "EstuarySdkInfo.Version must equal package.json \"version\"");
        }

        [Test]
        public void ClientHeaderValue_IsNameSlashVersion()
        {
            Assert.AreEqual("X-Estuary-Client", EstuarySdkInfo.ClientHeaderName);
            Assert.AreEqual("estuary-unity-sdk/" + EstuarySdkInfo.Version, EstuarySdkInfo.ClientHeaderValue);
        }
    }
}
