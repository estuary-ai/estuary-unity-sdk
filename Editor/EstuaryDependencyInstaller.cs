using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Estuary.Editor
{
    /// <summary>
    /// Auto-detects missing LiveKit SDK dependency and offers to install it.
    /// Unity's UPM does not resolve git URL dependencies from a package's package.json,
    /// so users who install Estuary via git URL without pre-installing LiveKit get cascading
    /// compilation errors. This editor script catches that case and provides a one-click fix.
    /// </summary>
    [InitializeOnLoad]
    public static class EstuaryDependencyInstaller
    {
        private const string LogPrefix = "[Estuary]";
        private const string LiveKitPackageId = "io.livekit.livekit-sdk";
        private const string LiveKitGitUrl = "https://github.com/livekit/client-sdk-unity.git#v1.3.3";
        private const string SessionKey = "Estuary_LiveKitCheckDone";

        private static ListRequest _listRequest;
        private static AddRequest _addRequest;

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

            foreach (var package in _listRequest.Result)
            {
                if (package.name == LiveKitPackageId)
                {
                    Debug.Log($"{LogPrefix} LiveKit SDK found ({package.version}). No action needed.");
                    return;
                }
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

            Debug.Log($"{LogPrefix} Installing LiveKit SDK from {LiveKitGitUrl}...");
            _addRequest = Client.Add(LiveKitGitUrl);
            EditorApplication.update += OnAddRequestUpdate;
        }

        private static void OnAddRequestUpdate()
        {
            if (!_addRequest.IsCompleted)
                return;

            EditorApplication.update -= OnAddRequestUpdate;

            if (_addRequest.Status == StatusCode.Failure)
            {
                Debug.LogError($"{LogPrefix} Failed to install LiveKit SDK: {_addRequest.Error.message}");
                return;
            }

            Debug.Log($"{LogPrefix} LiveKit SDK installed successfully ({_addRequest.Result.version}). " +
                "Unity will recompile scripts automatically.");
        }
    }
}
