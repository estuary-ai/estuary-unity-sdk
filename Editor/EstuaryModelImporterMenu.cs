using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Estuary.Editor
{
    /// <summary>
    /// Menu helper to install the optional glTF importer (glTFast) that
    /// EstuaryModelLoader needs to instantiate downloaded character models at runtime.
    ///
    /// Unlike LiveKit (required for the runtime assembly to compile), glTFast is fully
    /// optional — the SDK compiles without it — so this is an on-demand menu item rather
    /// than an auto-nagging InitializeOnLoad check.
    /// </summary>
    public static class EstuaryModelImporterMenu
    {
        private const string LogPrefix = "[Estuary]";
        private const string GltfastPackageId = "com.unity.cloud.gltfast";

        private static AddRequest _addRequest;

        [MenuItem("Estuary/Install glTF Importer (glTFast)", priority = 20)]
        private static void InstallGltfast()
        {
            Debug.Log($"{LogPrefix} Installing glTF importer {GltfastPackageId}...");
            _addRequest = Client.Add(GltfastPackageId);
            EditorApplication.update += OnAddRequestUpdate;
        }

        private static void OnAddRequestUpdate()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= OnAddRequestUpdate;

            if (_addRequest.Status == StatusCode.Failure)
            {
                Debug.LogError($"{LogPrefix} Failed to install {GltfastPackageId}: {_addRequest.Error.message}. " +
                    "Install it manually via Package Manager (Add package by name: com.unity.cloud.gltfast).");
                _addRequest = null;
                return;
            }

            Debug.Log($"{LogPrefix} glTF importer installed ({_addRequest.Result.version}). " +
                "Unity will recompile; the Estuary model loader is now available.");
            _addRequest = null;
        }
    }
}
