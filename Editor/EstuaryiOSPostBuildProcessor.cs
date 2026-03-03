#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Estuary.Editor
{
    /// <summary>
    /// Fixes iOS build crash caused by Unity's libiPhone-lib.a shipping outdated CELT/libvpx
    /// symbols that shadow LiveKit's liblivekit_ffi.a Opus 1.5+ implementations.
    /// Automates the manual Xcode fix documented in the LiveKit Unity SDK README.
    /// </summary>
    public static class EstuaryiOSPostBuildProcessor
    {
        private const string LogPrefix = "[Estuary]";

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            Debug.Log($"{LogPrefix} Running iOS post-build processor...");

            var pbxProjectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var pbxProject = new PBXProject();
            pbxProject.ReadFromFile(pbxProjectPath);

            var unityFrameworkGuid = pbxProject.GetUnityFrameworkTargetGuid();

            AddRequiredFrameworks(pbxProject, unityFrameworkGuid);
            AddObjCLinkerFlag(pbxProject, unityFrameworkGuid);
            ReorderLibraries(pbxProject, unityFrameworkGuid, pathToBuiltProject);

            pbxProject.WriteToFile(pbxProjectPath);

            EnsureMicrophoneUsageDescription(pathToBuiltProject);

            Debug.Log($"{LogPrefix} iOS post-build processor completed successfully.");
        }

        private static void AddRequiredFrameworks(PBXProject pbxProject, string targetGuid)
        {
            string[] frameworks =
            {
                "OpenGLES.framework",
                "MetalKit.framework",
                "GLKit.framework",
                "VideoToolbox.framework",
                "Network.framework"
            };

            foreach (var framework in frameworks)
            {
                pbxProject.AddFrameworkToProject(targetGuid, framework, false);
            }

            Debug.Log($"{LogPrefix} Added {frameworks.Length} required iOS frameworks.");
        }

        private static void AddObjCLinkerFlag(PBXProject pbxProject, string targetGuid)
        {
            pbxProject.AddBuildProperty(targetGuid, "OTHER_LDFLAGS", "-ObjC");
            Debug.Log($"{LogPrefix} Added -ObjC linker flag.");
        }

        private static void EnsureMicrophoneUsageDescription(string pathToBuiltProject)
        {
            var plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            if (!plist.root.values.ContainsKey("NSMicrophoneUsageDescription"))
            {
                plist.root.SetString("NSMicrophoneUsageDescription",
                    "Microphone access is required for voice conversation with AI characters.");
                plist.WriteToFile(plistPath);
                Debug.Log($"{LogPrefix} Added NSMicrophoneUsageDescription to Info.plist");
            }
        }

        private static void ReorderLibraries(PBXProject pbxProject, string targetGuid,
            string pathToBuiltProject)
        {
            // Find libiPhone-lib.a — Unity's core library with outdated CELT symbols
            var iphoneLibGuid = pbxProject.FindFileGuidByProjectPath("Libraries/libiPhone-lib.a");
            if (string.IsNullOrEmpty(iphoneLibGuid))
            {
                Debug.LogWarning($"{LogPrefix} Could not find libiPhone-lib.a — skipping library reorder.");
                return;
            }

            // Find liblivekit_ffi.a — path varies depending on Unity version and UPM resolution.
            // Unity places ARM64 native plugins under Libraries/ARM64/ with the full package path.
            string[] livekitCandidatePaths =
            {
                "Libraries/ARM64/Packages/io.livekit.livekit-sdk/Runtime/Plugins/ffi-ios-arm64/liblivekit_ffi.a",
                "Libraries/Packages/io.livekit.livekit-sdk/Runtime/Plugins/ffi-ios-arm64/liblivekit_ffi.a",
                "Libraries/io.livekit.livekit-sdk/Plugins/iOS/liblivekit_ffi.a",
                "Frameworks/io.livekit.livekit-sdk/Plugins/iOS/liblivekit_ffi.a",
            };

            string livekitLibGuid = null;
            string foundPath = null;
            foreach (var path in livekitCandidatePaths)
            {
                livekitLibGuid = pbxProject.FindFileGuidByProjectPath(path);
                if (!string.IsNullOrEmpty(livekitLibGuid))
                {
                    foundPath = path;
                    break;
                }
            }

            // Fallback: search the Xcode project directory for the file and derive the project path
            if (string.IsNullOrEmpty(livekitLibGuid))
            {
                var matches = Directory.GetFiles(pathToBuiltProject, "liblivekit_ffi.a",
                    SearchOption.AllDirectories);
                foreach (var match in matches)
                {
                    // Convert absolute filesystem path to project-relative path
                    var relativePath = match
                        .Substring(pathToBuiltProject.Length + 1)
                        .Replace('\\', '/');
                    livekitLibGuid = pbxProject.FindFileGuidByProjectPath(relativePath);
                    if (!string.IsNullOrEmpty(livekitLibGuid))
                    {
                        foundPath = relativePath;
                        Debug.Log($"{LogPrefix} Found liblivekit_ffi.a via filesystem search at: {relativePath}");
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(livekitLibGuid))
            {
                Debug.LogWarning(
                    $"{LogPrefix} Could not find liblivekit_ffi.a in any expected path or via " +
                    "filesystem search — skipping library reorder. LiveKit voice may crash on iOS.");
                return;
            }

            Debug.Log($"{LogPrefix} Found liblivekit_ffi.a at: {foundPath}");

            // Remove libiPhone-lib.a from build phase, then re-add it.
            // This places it at the END of the link order, after liblivekit_ffi.a,
            // so the linker resolves CELT symbols from LiveKit's Opus 1.5+ first.
            pbxProject.RemoveFileFromBuild(targetGuid, iphoneLibGuid);
            pbxProject.AddFileToBuild(targetGuid, iphoneLibGuid);

            Debug.Log($"{LogPrefix} Reordered libiPhone-lib.a to link after liblivekit_ffi.a.");
        }
    }
}
#endif
