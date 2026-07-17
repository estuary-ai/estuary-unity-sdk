using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Registers the glTFast-backed model loader with ModelLoaderBridge when the
    /// Estuary.glTFast assembly is loaded (i.e., when the glTFast package is installed).
    /// Uses RuntimeInitializeOnLoadMethod to run before any scene is loaded, mirroring
    /// LiveKitRegistrar.
    /// </summary>
    static class GltfastRegistrar
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            ModelLoaderBridge.RegisterFactory(() => new GltfastModelLoader());
            Debug.Log("[Estuary] glTFast model loader registered");
        }
    }
}
