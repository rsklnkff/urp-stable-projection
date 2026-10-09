using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StableProjection.Editor
{
    public static class DemoValidation
    {
        public static void CreateCaptureAndBuild()
        {
            try
            {
                PatchEngineValidation.Run();
                DemoBuilder.Create();
                var demo = UnityEngine.Object.FindObjectOfType<PrecisionDemo>();
                if (demo == null)
                {
                    throw new InvalidOperationException("Demo creation was cancelled.");
                }

                var camera = demo.demoCamera;
                camera.aspect = 16f / 9f;
                var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                target.Create();
                var previous = RenderTexture.active;
                var readback = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    Directory.CreateDirectory("ValidationResults/DemoFrames");
                    for (int frame = 0; frame < 48; frame++)
                    {
                        float t = frame / 12f;
                        var offset = new Vector3(Mathf.Sin(t * 0.31f) * 0.04f, 0.5f + Mathf.Sin(t * 0.4f) * 0.03f, -12);
                        camera.transform.localPosition = offset;
                        camera.transform.localRotation = Quaternion.LookRotation(-offset, Vector3.up);
                        demo.preciseCamera.ApplyProjection();
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                        readback.Apply();
                        File.WriteAllBytes("ValidationResults/DemoFrames/frame-" + frame.ToString("D3") + ".png", readback.EncodeToPNG());
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                    UnityEngine.Object.DestroyImmediate(readback);
                    UnityEngine.Object.DestroyImmediate(target);
                    demo.preciseCamera.RestoreProjection();
                }

                var scenePath = EditorSceneManager.GetActiveScene().path;
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                Directory.CreateDirectory("ValidationResults/Player");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath }, locationPathName = "ValidationResults/Player/StableProjectionDemo.exe",
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
                });
                var result = report.summary.result;
                File.WriteAllText("ValidationResults/demo-build.txt", result + " | errors=" + report.summary.totalErrors + " | bytes=" + report.summary.totalSize);
                EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Directory.CreateDirectory("ValidationResults");
                File.WriteAllText("ValidationResults/demo-failure.txt", e.ToString());
                EditorApplication.Exit(1);
            }
        }
    }
}
