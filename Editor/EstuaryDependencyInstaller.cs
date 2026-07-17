using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Estuary.Editor
{
    /// <summary>
    /// Auto-detects missing dependencies the Estuary SDK needs and offers to install them.
    /// Unity's UPM does not resolve git URL dependencies from a package's package.json,
    /// so users who install Estuary via git URL without pre-installing LiveKit get cascading
    /// compilation errors. This editor script catches that case and provides a one-click fix.
    ///
    /// It also ensures the built-in <c>com.unity.modules.screencapture</c> module is present:
    /// LiveKit's CameraVideoSource / ScreenVideoSource use <c>UnityEngine.ScreenCapture</c>,
    /// which lives in that module. On a lean/URP project without it, installing LiveKit alone
    /// still fails to compile ("The name 'ScreenCapture' does not exist").
    /// </summary>
    [InitializeOnLoad]
    public static class EstuaryDependencyInstaller
    {
        private const string LogPrefix = "[Estuary]";
        private const string LiveKitPackageId = "io.livekit.livekit-sdk";
        private const string LiveKitGitUrl = "https://github.com/livekit/client-sdk-unity.git#v1.3.3";
        // Built-in module required by LiveKit's video sources (UnityEngine.ScreenCapture).
        private const string ScreenCaptureModuleId = "com.unity.modules.screencapture";
        private const string SessionKey = "Estuary_LiveKitCheckDone";

        private static ListRequest _listRequest;
        private static AddAndRemoveRequest _addRequest;

        static EstuaryDependencyInstaller()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            SessionState.SetBool(SessionKey, true);

            _listRequest = Client.List(offlineMode: true);
            EditorApplication.update += OnListRequestUpdate;
        }

        private static void OnListRequestUpdate()
        {
            if (!_listRequest.IsCompleted)
                return;

            EditorApplication.update -= OnListRequestUpdate;

            if (_listRequest.Status == StatusCode.Failure)
            {
                Debug.LogWarning($"{LogPrefix} Could not query installed packages: {_listRequest.Error.message}");
                return;
            }

            bool hasLiveKit = false;
            bool hasScreenCapture = false;
            foreach (var package in _listRequest.Result)
            {
                if (package.name == LiveKitPackageId) hasLiveKit = true;
                else if (package.name == ScreenCaptureModuleId) hasScreenCapture = true;
            }

            // LiveKit is already installed: just make sure the module it needs is too,
            // otherwise the LiveKit assembly fails to compile.
            if (hasLiveKit)
            {
                Debug.Log($"{LogPrefix} LiveKit SDK found.");
                if (!hasScreenCapture)
                {
                    Debug.Log($"{LogPrefix} Adding required module {ScreenCaptureModuleId} " +
                        "(needed by LiveKit video capture)...");
                    Install(new[] { ScreenCaptureModuleId });
                }
                return;
            }

            Debug.LogWarning($"{LogPrefix} LiveKit SDK ({LiveKitPackageId}) is not installed. " +
                "The Estuary SDK requires it for compilation.");

            var install = EditorUtility.DisplayDialog(
                "Estuary SDK — Missing Dependency",
                "The Estuary SDK requires the LiveKit Unity SDK (io.livekit.livekit-sdk) " +
                "but it is not installed.\n\n" +
                "Without it, the Estuary runtime assembly cannot compile and you will see " +
                "errors about missing types (SessionInfo, BotResponse, etc.).\n\n" +
                "Would you like to install it now?",
                "Install LiveKit SDK",
                "Not Now");

            if (!install)
            {
                Debug.Log($"{LogPrefix} LiveKit SDK installation skipped by user. " +
                    "Add it manually via Package Manager: " + LiveKitGitUrl);
                return;
            }

            // Install LiveKit and, if missing, the screencapture module it depends on —
            // in a single request so the project compiles cleanly on the first resolve.
            var toAdd = new List<string> { LiveKitGitUrl };
            if (!hasScreenCapture) toAdd.Add(ScreenCaptureModuleId);

            Debug.Log($"{LogPrefix} Installing {string.Join(", ", toAdd)}...");
            Install(toAdd.ToArray());
        }

        private static void Install(string[] packagesToAdd)
        {
            _addRequest = Client.AddAndRemove(packagesToAdd, null);
            EditorApplication.update += OnAddRequestUpdate;
        }

        private static void OnAddRequestUpdate()
        {
            if (!_addRequest.IsCompleted)
                return;

            EditorApplication.update -= OnAddRequestUpdate;

            if (_addRequest.Status == StatusCode.Failure)
            {
                Debug.LogError($"{LogPrefix} Failed to install dependencies: {_addRequest.Error.message}");
                return;
            }

            Debug.Log($"{LogPrefix} Dependencies installed successfully. " +
                "Unity will recompile scripts automatically.");
        }
    }
}
