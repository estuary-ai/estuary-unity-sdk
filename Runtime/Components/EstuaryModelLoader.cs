using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using Estuary.Models;

namespace Estuary
{
    /// <summary>
    /// Downloads a character's Estuary-generated 3D model (GLB) and instantiates it as a
    /// GameObject at runtime.
    ///
    /// Pipeline (mirrors the web frontend's Model3DViewer flow):
    ///   resolve the ready model URL (GET /api/agents or /model-status)
    ///     -> download the GLB (EstuaryHttpClient.DownloadGlb)
    ///     -> import + instantiate via the optional glTF importer (ModelLoaderBridge / glTFast)
    ///     -> apply a provider-specific orientation offset and optional height normalization.
    ///
    /// Requires a runtime glTF importer (glTFast). If none is installed, loads fail with a
    /// clear message (Estuary > Install glTF Importer, or Package Manager: com.unity.cloud.gltfast).
    /// </summary>
    [AddComponentMenu("Estuary/Estuary Model Loader")]
    public class EstuaryModelLoader : MonoBehaviour
    {
        #region Inspector

        [SerializeField]
        [Tooltip("Estuary configuration (server URL + API key). Required.")]
        private EstuaryConfig config;

        [SerializeField]
        [Tooltip("Where to parent the instantiated model. Defaults to this component's transform.")]
        private Transform spawnParent;

        [Header("Normalization")]
        [SerializeField]
        [Tooltip("If > 0, uniformly scale the model so its world-space height is this many meters. 0 = keep the model's native scale.")]
        private float normalizeHeight = 0f;

        [Header("Orientation offset (local Euler, degrees)")]
        [Tooltip("Extra rotation applied to Tripo-generated models. Tripo GLBs import facing -X in Unity; a -90° yaw turns them to face +Z (toward a default camera). Verified live against a Tripo character.")]
        [SerializeField]
        private Vector3 tripoRotationOffset = new Vector3(0f, -90f, 0f);

        [SerializeField]
        [Tooltip("Extra rotation applied to Meshy-generated models. Meshy exports already face the camera (identity); untested in Unity, corroborated by the web frontend.")]
        private Vector3 meshyRotationOffset = Vector3.zero;

        [Header("Behavior")]
        [SerializeField]
        [Tooltip("If the agent's model isn't ready yet, poll model-status until it completes before loading. Otherwise a not-ready agent fails immediately.")]
        private bool pollUntilReady = false;

        [SerializeField]
        [Tooltip("Destroy any previously loaded model before instantiating a new one.")]
        private bool replaceExisting = true;

        #endregion

        #region Public surface

        /// <summary>The most recently instantiated model root, or null.</summary>
        public GameObject CurrentModel { get; private set; }

        /// <summary>Fired with the instantiated root GameObject on a successful load.</summary>
        public event Action<GameObject> OnModelLoaded;

        /// <summary>Fired with an error message on a failed load.</summary>
        public event Action<string> OnModelLoadFailed;

        /// <summary>Assign or replace the config used for REST calls.</summary>
        public void SetConfig(EstuaryConfig cfg)
        {
            config = cfg;
            _http = null;
        }

        /// <summary>Scope REST results to a player id (sent as X-Player-Id).</summary>
        public void SetPlayerId(string playerId)
        {
            Http.PlayerId = playerId;
        }

        /// <summary>
        /// Load a character's model by agent id. Resolves the ready GLB URL + provider from
        /// the agents list, then downloads and instantiates it.
        /// </summary>
        public Coroutine LoadForAgent(string agentId,
            Action<GameObject> onSuccess = null, Action<string> onError = null)
            => StartCoroutine(LoadForAgentRoutine(agentId, onSuccess, onError));

        /// <summary>
        /// Load directly from a known model URL (e.g. AgentResponse.ModelUrl / BestModelUrl).
        /// <paramref name="provider"/> ("tripo" | "meshy" | null) selects the orientation offset.
        /// </summary>
        public Coroutine LoadFromUrl(string modelUrl, string provider = null,
            Action<GameObject> onSuccess = null, Action<string> onError = null)
            => StartCoroutine(LoadFromUrlRoutine(modelUrl, provider, onSuccess, onError));

        #endregion

        #region Internals

        private EstuaryHttpClient _http;
        private EstuaryHttpClient Http => _http ??= new EstuaryHttpClient(config);

        private IEnumerator LoadForAgentRoutine(string agentId,
            Action<GameObject> onSuccess, Action<string> onError)
        {
            if (!EnsurePrereqs(onError)) yield break;

            AgentResponse agent = null;
            string err = null;
            yield return Http.GetAgents(
                list =>
                {
                    if (list != null)
                        agent = list.Find(a => a != null && a.Id == agentId);
                },
                e => err = e);

            if (err != null) { Fail(onError, $"Could not fetch agents: {err}"); yield break; }
            if (agent == null) { Fail(onError, $"Agent '{agentId}' not found (or not visible to this key/player)."); yield break; }

            if (!agent.HasLoadableModel)
            {
                if (!pollUntilReady)
                {
                    Fail(onError, $"Agent '{agentId}' has no ready model (modelStatus={agent.ModelStatus ?? "null"}). " +
                                  "Trigger generation with EstuaryHttpClient.GenerateModel, or enable 'pollUntilReady'.");
                    yield break;
                }

                ModelStatusResponse final = null;
                yield return Http.PollModelStatus(agentId,
                    onStatusChanged: null,
                    onCompleted: s => final = s,
                    onError: e => err = e);

                if (err != null) { Fail(onError, $"Model generation did not complete: {err}"); yield break; }
                string polledUrl = !string.IsNullOrEmpty(final.ModelUrl) ? final.ModelUrl : final.ModelPreviewUrl;
                yield return LoadFromUrlRoutine(polledUrl, agent.ModelProvider, onSuccess, onError);
                yield break;
            }

            yield return LoadFromUrlRoutine(agent.BestModelUrl, agent.ModelProvider, onSuccess, onError);
        }

        private IEnumerator LoadFromUrlRoutine(string modelUrl, string provider,
            Action<GameObject> onSuccess, Action<string> onError)
        {
            if (!EnsurePrereqs(onError)) yield break;
            if (string.IsNullOrEmpty(modelUrl)) { Fail(onError, "modelUrl is empty."); yield break; }

            // 1. Download GLB bytes (handles both presigned S3 URLs and relative /static paths).
            byte[] glb = null;
            string err = null;
            yield return Http.DownloadGlb(modelUrl, b => glb = b, e => err = e);
            if (err != null) { Fail(onError, $"GLB download failed: {err}"); yield break; }
            if (glb == null || glb.Length == 0) { Fail(onError, "GLB download returned no data."); yield break; }

            // 2. Import + instantiate via the optional glTF importer (async Task bridged to a coroutine).
            var parent = spawnParent != null ? spawnParent : transform;
            var loader = ModelLoaderBridge.Create();
            var task = loader.LoadFromBytesAsync(glb, parent, CancellationToken.None);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted)
            {
                Fail(onError, $"glTF import threw: {task.Exception?.GetBaseException().Message}");
                yield break;
            }
            var model = task.Result;
            if (model == null) { Fail(onError, "glTF import failed (see console)."); yield break; }

            // 3. Orientation + scale.
            ApplyOrientation(model.transform, provider);
            if (normalizeHeight > 0f) NormalizeHeight(model, normalizeHeight);

            if (replaceExisting && CurrentModel != null && CurrentModel != model)
                Destroy(CurrentModel);
            CurrentModel = model;

            onSuccess?.Invoke(model);
            OnModelLoaded?.Invoke(model);
        }

        /// <summary>Verifies config + that a glTF importer is installed; reports via onError if not.</summary>
        private bool EnsurePrereqs(Action<string> onError)
        {
            if (config == null) { Fail(onError, "EstuaryConfig is not assigned on EstuaryModelLoader."); return false; }
            if (!ModelLoaderBridge.IsAvailable)
            {
                Fail(onError,
                    "No runtime glTF importer is installed. Install glTFast via " +
                    "'Estuary > Install glTF Importer (glTFast)' or Package Manager " +
                    "(com.unity.cloud.gltfast), then reload.");
                return false;
            }
            return true;
        }

        private void ApplyOrientation(Transform model, string provider)
        {
            Vector3 offset =
                string.Equals(provider, "tripo", StringComparison.OrdinalIgnoreCase) ? tripoRotationOffset :
                string.Equals(provider, "meshy", StringComparison.OrdinalIgnoreCase) ? meshyRotationOffset :
                Vector3.zero;

            if (offset != Vector3.zero)
                model.localRotation *= Quaternion.Euler(offset);
        }

        /// <summary>Uniformly scales the model so its renderer-bounds height matches targetHeight (meters).</summary>
        private static void NormalizeHeight(GameObject model, float targetHeight)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float height = bounds.size.y;
            if (height <= Mathf.Epsilon) return;

            float factor = targetHeight / height;
            model.transform.localScale *= factor;
        }

        private void Fail(Action<string> onError, string message)
        {
            Debug.LogError($"[Estuary] EstuaryModelLoader: {message}");
            onError?.Invoke(message);
            OnModelLoadFailed?.Invoke(message);
        }

        #endregion
    }
}
