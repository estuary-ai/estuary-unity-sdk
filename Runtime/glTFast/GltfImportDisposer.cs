using GLTFast;
using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Holds the GltfImport that produced an instantiated model hierarchy and
    /// releases its native/managed resources (meshes, textures, materials,
    /// buffers) when the root GameObject is destroyed. Attached by
    /// GltfastModelLoader to every instantiated root, so a plain
    /// Destroy(CurrentModel) (EstuaryModelLoader's replaceExisting path), a
    /// scene unload, or app teardown all free the import — without changing
    /// EstuaryModelLoader's API. Internal to the Estuary.glTFast assembly.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class GltfImportDisposer : MonoBehaviour
    {
        private GltfImport _import;

        /// <summary>Take ownership of the import; it is disposed in OnDestroy.</summary>
        internal void Attach(GltfImport import)
        {
            _import = import;
        }

        private void OnDestroy()
        {
            _import?.Dispose();
            _import = null;
        }
    }
}
