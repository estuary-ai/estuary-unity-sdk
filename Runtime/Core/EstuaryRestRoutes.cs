namespace Estuary
{
    /// <summary>
    /// Method + path of one REST call, relative to the server URL.
    /// </summary>
    internal readonly struct EstuaryRestRoute
    {
        public readonly string Method;
        public readonly string Path;

        public EstuaryRestRoute(string method, string path)
        {
            Method = method;
            Path = path;
        }
    }

    /// <summary>
    /// The REST routes EstuaryHttpClient calls, one builder per public method.
    /// Kept apart from the UnityWebRequest plumbing so EditMode tests can check them
    /// against sdk-conformance/rest.json without an HTTP fake
    /// (Tests/Editor/RestConformanceTests.cs).
    /// </summary>
    internal static class EstuaryRestRoutes
    {
        public static EstuaryRestRoute UploadImageToCharacter()
            => new EstuaryRestRoute("POST", "/api/v1/characters/from-image");

        public static EstuaryRestRoute GetModelStatus(string agentId)
            => new EstuaryRestRoute("GET", $"/api/v1/characters/{agentId}/model");

        public static EstuaryRestRoute GenerateModel(string agentId)
            => new EstuaryRestRoute("POST", $"/api/v1/characters/{agentId}/model");

        // Still on the legacy route: the v1 list is paginated and its CharacterResponse
        // has no modelProvider, which EstuaryModelLoader needs for the GLB orientation fix.
        public static EstuaryRestRoute GetAgents()
            => new EstuaryRestRoute("GET", "/api/agents");

        public static EstuaryRestRoute DeleteAgent(string agentId)
            => new EstuaryRestRoute("DELETE", $"/api/v1/characters/{agentId}");
    }
}
