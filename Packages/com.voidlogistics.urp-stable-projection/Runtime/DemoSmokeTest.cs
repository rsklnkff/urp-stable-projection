using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace StableProjection
{
    /// <summary>Optional automated Player smoke check used only with -stableProjectionSmoke on the command line.</summary>
    public sealed class DemoSmokeTest : MonoBehaviour
    {
        RenderTexture _target;
        Camera _camera;
        string _reportDirectory;
        int _frames, _errors;
        bool _running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnableBackgroundTest()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-stableProjectionSmoke") >= 0)
            {
                Application.runInBackground = true;
                Debug.Log("Stable Projection Player smoke test starting.");
            }
        }

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-stableProjectionSmoke");
            if (index < 0)
            {
                enabled = false;
                return;
            }

            Debug.Log("Stable Projection demo scene started.");
            _reportDirectory = index + 1 < args.Length ? args[index + 1] : Application.persistentDataPath;
            Directory.CreateDirectory(_reportDirectory);
            _camera = GetComponent<PrecisionDemo>().demoCamera;
            _target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            _target.Create();
            _camera.targetTexture = _target;
            Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            RenderPipelineManager.endCameraRendering += OnRendered;
            _running = true;
        }

        void LateUpdate()
        {
            if (_running)
            {
                RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _target });
            }
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
            }
        }

        [Serializable] sealed class Report
        {
            public bool passed;
            public string unity, graphicsApi, gpu;
            public int renderedFrames, errors, orangePixels, cyanPixels;
        }

        void OnRendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || !_running || ++_frames < 120)
            {
                return;
            }

            _running = false;
            var previous = RenderTexture.active;
            var readback = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture.active = _target;
            readback.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            int orange = 0, cyan = 0;
            foreach (var pixel in readback.GetPixels32())
            {
                if (pixel.r > 150 && pixel.r > pixel.b * 1.3f)
                {
                    orange++;
                }

                if (pixel.b > 150 && pixel.b > pixel.r * 1.3f)
                {
                    cyan++;
                }
            }

            var report = new Report { passed = _errors == 0 && orange > 1000 && cyan > 1000, unity = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName,
                renderedFrames = _frames, errors = _errors, orangePixels = orange, cyanPixels = cyan };
            File.WriteAllText(Path.Combine(_reportDirectory, "player-smoke.json"), JsonUtility.ToJson(report, true));
            File.WriteAllBytes(Path.Combine(_reportDirectory, "player-demo.png"), readback.EncodeToPNG());
            Destroy(readback);
            Application.Quit(report.passed ? 0 : 1);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            RenderPipelineManager.endCameraRendering -= OnRendered;
            if (_target != null)
            {
                if (_camera != null)
                {
                    _camera.targetTexture = null;
                }

                _target.Release();
                Destroy(_target);
            }
        }
    }
}
