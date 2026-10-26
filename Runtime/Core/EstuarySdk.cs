namespace Estuary
{
    /// <summary>Runtime package identification. The package-version test prevents drift from package.json.</summary>
    public static class EstuarySdk
    {
        public const string Version = "1.2.0";
        public const string ClientIdentification = "estuary-unity-sdk/" + Version;
    }
}
