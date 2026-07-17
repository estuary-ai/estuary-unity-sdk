using System;

namespace Estuary
{
    /// <summary>
    /// Static service locator for the optional runtime glTF/GLB importer.
    /// The Estuary.glTFast assembly registers a concrete factory at runtime via
    /// RuntimeInitializeOnLoadMethod in GltfastRegistrar. EstuaryModelLoader uses this
    /// bridge to create a loader without directly referencing glTFast types.
    ///
    /// When the glTFast package is not installed, IsAvailable returns false and
    /// Create() returns null — EstuaryModelLoader then surfaces a clear "install a
    /// glTF importer" error instead of a compile break. Mirrors <see cref="LiveKitBridge"/>.
    /// </summary>
    public static class ModelLoaderBridge
    {
        private static Func<IEstuaryModelLoader> _factory;

        /// <summary>
        /// Whether a runtime glTF importer is available (a factory has been registered).
        /// </summary>
        public static bool IsAvailable => _factory != null;

        /// <summary>
        /// Register the factory for creating IEstuaryModelLoader instances.
        /// Called by GltfastRegistrar in the Estuary.glTFast assembly.
        /// </summary>
        public static void RegisterFactory(Func<IEstuaryModelLoader> factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Create a new IEstuaryModelLoader instance.
        /// Returns null if no glTF importer is installed.
        /// </summary>
        public static IEstuaryModelLoader Create()
        {
            return _factory?.Invoke();
        }
    }
}
