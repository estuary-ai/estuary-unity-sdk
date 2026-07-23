using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// IEstuaryModelLoader implementation backed by glTFast (com.unity.cloud.gltfast).
    /// Lives in the Estuary.glTFast assembly, which only compiles when the glTFast
    /// package is installed (defineConstraints: ESTUARY_GLTFAST). Registered with
    /// ModelLoaderBridge by GltfastRegistrar.
    /// </summary>
    public class GltfastModelLoader : IEstuaryModelLoader
    {
        public async Task<GameObject> LoadFromBytesAsync(byte[] glb, Transform parent, CancellationToken cancellationToken = default)
        {
            if (glb == null || glb.Length == 0)
            {
                Debug.LogError("[Estuary] GltfastModelLoader: empty GLB bytes.");
                return null;
            }

            var import = new GltfImport();
            GameObject root = null;
            try
            {
                // glTFast's generic Load handles GLB (binary) input; LoadGltfBinary is obsolete.
                bool loaded = await import.Load(glb, null, null, cancellationToken);
                if (!loaded)
                {
                    Debug.LogError("[Estuary] GltfastModelLoader: failed to parse GLB.");
                    import.Dispose();
                    return null;
                }

                // Root the instantiated hierarchy under a named, zeroed container so
                // callers get a single predictable transform to orient/scale/parent.
                root = new GameObject("EstuaryCharacterModel");
                if (parent != null)
                    root.transform.SetParent(parent, worldPositionStays: false);

                bool instantiated = await import.InstantiateMainSceneAsync(root.transform, cancellationToken);
                if (!instantiated)
                {
                    Debug.LogError("[Estuary] GltfastModelLoader: failed to instantiate glTF scene.");
                    Object.Destroy(root);
                    import.Dispose();
                    return null;
                }

                // Hand the import to a disposer on the root so its resources (meshes,
                // textures, materials, buffers) are freed whenever the hierarchy is
                // destroyed — model swap, scene unload, or app teardown.
                root.AddComponent<GltfImportDisposer>().Attach(import);
                return root;
            }
            catch
            {
                // Unexpected failure/cancellation mid-import: nothing owns the
                // import yet, so free it (and any orphaned root) here.
                if (root != null)
                    Object.Destroy(root);
                import.Dispose();
                throw;
            }
        }
    }
}
