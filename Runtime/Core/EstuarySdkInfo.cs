namespace Estuary
{
    /// <summary>
    /// SDK identity. <see cref="Version"/> is the single runtime source of truth for the
    /// package version and must equal "version" in package.json
    /// (Tests/Editor/SdkVersionTests.cs fails if they drift).
    /// </summary>
    public static class EstuarySdkInfo
    {
        /// <summary>Package version. Keep in sync with package.json when releasing.</summary>
        public const string Version = "1.1.0";

        /// <summary>Header that identifies this SDK on every REST request to the Estuary gateway.</summary>
        public const string ClientHeaderName = "X-Estuary-Client";

        /// <summary>Value of <see cref="ClientHeaderName"/>: "estuary-unity-sdk/&lt;version&gt;".</summary>
        public const string ClientHeaderValue = "estuary-unity-sdk/" + Version;
    }
}
