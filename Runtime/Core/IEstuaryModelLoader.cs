using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Abstraction over a runtime glTF/GLB importer. Implemented in the optional
    /// Estuary.glTFast assembly (compiles only when the glTFast package is installed)
    /// and registered with <see cref="ModelLoaderBridge"/> at runtime.
    ///
    /// Keeping this in the core assembly lets EstuaryModelLoader depend on the
    /// interface without referencing glTFast types directly — the same pattern used
    /// for the optional LiveKit integration (see <see cref="ILiveKitVoiceManager"/>).
    /// </summary>
    public interface IEstuaryModelLoader
    {
        /// <summary>
        /// Parse raw GLB bytes and instantiate the model under <paramref name="parent"/>.
        /// Returns the instantiated root GameObject, or null on failure (the
        /// implementation logs the reason).
        /// </summary>
        /// <param name="glb">Raw .glb (binary glTF) bytes.</param>
        /// <param name="parent">Transform to parent the instantiated model to (may be null).</param>
        /// <param name="cancellationToken">Cancels the import.</param>
        Task<GameObject> LoadFromBytesAsync(byte[] glb, Transform parent, CancellationToken cancellationToken = default);
    }
}
