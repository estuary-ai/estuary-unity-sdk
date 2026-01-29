using System;
using System.Collections;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;

namespace Estuary
{
    /// <summary>
    /// A simple webcam wrapper for LiveKit video streaming.
    /// 
    /// Note: This class manages webcam capture independently.
    /// For LiveKit video, use the built-in Room.LocalParticipant.SetCameraEnabled() 
    /// or the LiveKitVideoManager which handles both approaches.
    /// </summary>
    public sealed class WebcamVideoSource : IDisposable
    {
        private const int DEFAULT_WIDTH = 1280;
        private const int DEFAULT_HEIGHT = 720;
        private const int DEFAULT_FPS = 10;
        
        private readonly MonoBehaviour _coroutineRunner;
        private readonly string _deviceName;
        private readonly bool _useFrontCamera;
        
        private WebCamTexture _webcamTexture;
        private Texture2D _frameTexture;
        private Color32[] _framePixels;
        private byte[] _frameBuffer;
        private Coroutine _captureCoroutine;
        private bool _started;
        private bool _disposed;
        
        private int _targetWidth;
        private int _targetHeight;
        private int _targetFps;
        private float _lastFrameTime;
        
        /// <summary>
        /// Event fired when a new frame is captured.
        /// Parameters: ARGB32 bytes, width, height
        /// </summary>
        public event Action<byte[], int, int> OnFrameCaptured;
        
        /// <summary>
        /// Whether the webcam is currently capturing.
        /// </summary>
        public bool IsCapturing => _started && _webcamTexture != null && _webcamTexture.isPlaying;
        
        /// <summary>
        /// Current webcam texture (can be used for preview).
        /// </summary>
        public WebCamTexture WebcamTexture => _webcamTexture;
        
        /// <summary>
        /// Current frame width.
        /// </summary>
        public int Width => _webcamTexture?.width ?? _targetWidth;
        
        /// <summary>
        /// Current frame height.
        /// </summary>
        public int Height => _webcamTexture?.height ?? _targetHeight;
        
        /// <summary>
        /// Creates a new webcam video source.
        /// </summary>
        public WebcamVideoSource(
            MonoBehaviour coroutineRunner,
            string deviceName = null,
            bool useFrontCamera = false,
            int width = DEFAULT_WIDTH,
            int height = DEFAULT_HEIGHT,
            int fps = DEFAULT_FPS)
        {
            _coroutineRunner = coroutineRunner;
            _deviceName = deviceName;
            _useFrontCamera = useFrontCamera;
            _targetWidth = width;
            _targetHeight = height;
            _targetFps = fps;
        }
        
        /// <summary>
        /// Set the target resolution.
        /// </summary>
        public void SetResolution(int width, int height)
        {
            _targetWidth = width;
            _targetHeight = height;
            
            if (_started)
            {
                Stop();
                Start();
            }
        }
        
        /// <summary>
        /// Set the target frame rate.
        /// </summary>
        public void SetFrameRate(int fps)
        {
            _targetFps = Mathf.Clamp(fps, 1, 30);
        }
        
        /// <summary>
        /// Get the current frame buffer.
        /// </summary>
        public byte[] GetFrameBuffer()
        {
            return _frameBuffer ?? Array.Empty<byte>();
        }
        
        /// <summary>
        /// Begins capturing video from the webcam.
        /// </summary>
        public void Start()
        {
            if (_started || _disposed) return;
            
            string deviceToUse = FindWebcamDevice();
            
            if (string.IsNullOrEmpty(deviceToUse) && WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("[WebcamVideoSource] No webcam devices found");
                return;
            }
            
            _webcamTexture = deviceToUse != null
                ? new WebCamTexture(deviceToUse, _targetWidth, _targetHeight, _targetFps)
                : new WebCamTexture(_targetWidth, _targetHeight, _targetFps);
            
            _webcamTexture.Play();
            
            if (!_webcamTexture.isPlaying)
            {
                Debug.LogError("[WebcamVideoSource] Failed to start webcam");
                return;
            }
            
            Debug.Log($"[WebcamVideoSource] Started: {_webcamTexture.deviceName} ({_webcamTexture.width}x{_webcamTexture.height})");
            
            _started = true;
            _lastFrameTime = Time.time;
            
            if (_coroutineRunner != null)
            {
                _captureCoroutine = _coroutineRunner.StartCoroutine(CaptureCoroutine());
            }
        }
        
        /// <summary>
        /// Stops capturing video from the webcam.
        /// </summary>
        public void Stop()
        {
            if (!_started) return;
            
            if (_captureCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_captureCoroutine);
                _captureCoroutine = null;
            }
            
            if (_webcamTexture != null)
            {
                _webcamTexture.Stop();
                UnityEngine.Object.Destroy(_webcamTexture);
                _webcamTexture = null;
            }
            
            if (_frameTexture != null)
            {
                UnityEngine.Object.Destroy(_frameTexture);
                _frameTexture = null;
            }
            
            _framePixels = null;
            _frameBuffer = null;
            _started = false;
            
            Debug.Log("[WebcamVideoSource] Stopped");
        }
        
        private string FindWebcamDevice()
        {
            if (!string.IsNullOrEmpty(_deviceName))
            {
                return _deviceName;
            }
            
            if (_useFrontCamera)
            {
                foreach (var device in WebCamTexture.devices)
                {
                    if (device.isFrontFacing)
                    {
                        return device.name;
                    }
                }
            }
            
            return null;
        }
        
        private IEnumerator CaptureCoroutine()
        {
            while (_webcamTexture != null && _webcamTexture.width < 100)
            {
                yield return null;
            }
            
            if (_webcamTexture == null)
            {
                yield break;
            }
            
            int width = _webcamTexture.width;
            int height = _webcamTexture.height;
            
            _frameTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            _framePixels = new Color32[width * height];
            _frameBuffer = new byte[width * height * 4];
            
            float frameInterval = 1f / _targetFps;
            
            while (_started && !_disposed && _webcamTexture != null && _webcamTexture.isPlaying)
            {
                if (Time.time - _lastFrameTime < frameInterval)
                {
                    yield return null;
                    continue;
                }
                
                _lastFrameTime = Time.time;
                CaptureFrame();
                
                yield return null;
            }
        }
        
        private void CaptureFrame()
        {
            if (_webcamTexture == null || !_webcamTexture.isPlaying || _frameTexture == null)
                return;
            
            if (_frameTexture.width != _webcamTexture.width || _frameTexture.height != _webcamTexture.height)
            {
                int width = _webcamTexture.width;
                int height = _webcamTexture.height;
                
                UnityEngine.Object.Destroy(_frameTexture);
                _frameTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
                _framePixels = new Color32[width * height];
                _frameBuffer = new byte[width * height * 4];
            }
            
            _webcamTexture.GetPixels32(_framePixels);
            
            for (int i = 0; i < _framePixels.Length; i++)
            {
                int offset = i * 4;
                _frameBuffer[offset + 0] = _framePixels[i].a;
                _frameBuffer[offset + 1] = _framePixels[i].r;
                _frameBuffer[offset + 2] = _framePixels[i].g;
                _frameBuffer[offset + 3] = _framePixels[i].b;
            }
            
            OnFrameCaptured?.Invoke(_frameBuffer, _frameTexture.width, _frameTexture.height);
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
            }
            _disposed = true;
        }
    }
}
