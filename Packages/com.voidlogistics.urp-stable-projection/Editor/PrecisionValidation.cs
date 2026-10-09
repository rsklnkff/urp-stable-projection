using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace StableProjection.Editor
{
    public static class PrecisionValidation
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const int samples = 128;
        const int modes = 6;

        [Serializable] public sealed class Report
        {
            public bool passed;
            public string unity, graphicsApi, gpu;
            public double rigError, correctionError, cameraMotionPixels;
            public int lifecycleChecks, historyChecks, gpuSamples;
            public double[] maxErrorPixels, legacyMatrixPixels;
        }

        [MenuItem("Tools/Stable Projection/Run GPU precision validation")]
        public static void RunMenu()
        {
            Debug.Log(JsonUtility.ToJson(Run(), true));
        }

        public static Report Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException("Run in Edit Mode with a graphics device; do not use -nographics.");
            }

            PrecisionInstaller.VerifyInstalled();
            var root = new GameObject("Camera precision validation") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var cameraObject = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            Shader shader = null;
            Material material = null;
            Mesh mesh = null;
            RenderTexture target = null;
            Texture2D readback = null;
            var oldTarget = RenderTexture.active;
            using var cmd = new CommandBuffer { name = "Standard matrix precision validation" };
            try
            {
                root.transform.position = new Vector3(-237874, 22.8f, -243998);
                root.transform.rotation = Quaternion.Euler(0, 47.65f, 0);
                camera.transform.localPosition = new Vector3(0, 76.36754f, -76.36754f);
                var preciseAnchor = (double3)(float3)root.transform.position + new double3(0.007, 0.000003, -0.005);
                var expectedCamera = preciseAnchor + math.mul(new double3x3(new float3x3((quaternion)root.transform.rotation)), (double3)(float3)camera.transform.localPosition);
                var rigError = math.distance(GetRigPosition(camera.transform, preciseAnchor - (double3)(float3)root.transform.position), expectedCamera);
                Require(rigError < 1e-8, "Local camera offsets lost precision.");
                var lifecycleChecks = CheckLifecycle(camera);
                var historyChecks = CheckHistory(camera);
                shader = ShaderUtil.CreateShaderAsset(File.ReadAllText(Path.Combine(PrecisionInstaller.packageRoot, "Editor/Shaders/PrecisionProbe.shader.txt")));
                material = new Material(shader);
                ShaderUtil.CompilePass(material, 0, true);
                Require(!ShaderUtil.ShaderHasError(shader), "GPU probe failed: " + string.Join("; ", Array.ConvertAll(ShaderUtil.GetShaderMessages(shader), m => m.message)));
                mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero }, triangles = new[] { 0, 1, 2 } };
                const int width = samples * modes * 2;
                target = new RenderTexture(width, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.Create();
                readback = new Texture2D(width, 1, TextureFormat.RGBAFloat, false, true);
                var expected = new double4[width];
                var legacy = new double[2];
                var correctionError = 0.0;
                var vertex = new Vector4(1.23567f, -0.73531f, 0.01619f, 1);
                var previousView = Matrix4x4.identity;
                var previousProjection = Matrix4x4.identity;
                var previousModel = Matrix4x4.identity;
                var previousReference = new double4();
                cmd.SetRenderTarget(target);
                cmd.ClearRenderTarget(false, true, Color.clear);
                for (var perspective = 0; perspective < 2; perspective++)
                {
                    for (var i = 0; i < samples; i++)
                    {
                        var t = i / (float)(samples - 1);
                        camera.transform.localPosition = new Vector3(0, 76 + t, -76 - t);
                        var position = GetRigPosition(camera.transform, preciseAnchor - (double3)(float3)root.transform.position);
                        camera.transform.rotation = Quaternion.LookRotation((float3)(preciseAnchor - position), root.transform.forward);
                        var view = camera.worldToCameraMatrix;
                        var baseProjection = perspective == 0 ? Matrix4x4.Ortho(-20, 20, -12, 12, 0.1f, 1000)
                            : Matrix4x4.Perspective(55 + t, 1.6f, 0.1f, 1000);
                        var projection = perspective == 0 ? GetProjection(view, baseProjection, position, out _) : baseProjection;
                        var gpuProjection = GL.GetGPUProjectionMatrix(projection, true);
                        var model = Matrix4x4.TRS(root.transform.position, Quaternion.Euler(17 + t, 23, -8), new Vector3(-0.5f, 1.5f, 2));
                        var reference = Project(view, gpuProjection, model, vertex);
                        if (perspective == 0)
                        {
                            var relative = math.mul(D(model), (double4)(float4)vertex).xyz - position;
                            var preciseView = D(view);
                            var rotation = new double3x3(preciseView.c0.xyz, preciseView.c1.xyz, preciseView.c2.xyz);
                            var preciseClip = math.mul(D(GL.GetGPUProjectionMatrix(baseProjection, true)), new double4(math.mul(rotation, relative), 1));
                            correctionError = Math.Max(correctionError, PixelError(reference, preciseClip));
                        }

                        legacy[perspective] = Math.Max(legacy[perspective], PixelError((float4)((GL.GetGPUProjectionMatrix(baseProjection, true) * (view * model)) * vertex), reference));
                        if (i == 0)
                        {
                            previousView = view;
                            previousProjection = gpuProjection;
                            previousModel = model;
                            previousReference = reference;
                        }

                        cmd.SetGlobalMatrix("_StableProjectionMotionView", view);
                        cmd.SetGlobalMatrix("_StableProjectionMotionProjection", gpuProjection);
                        cmd.SetGlobalMatrix("_StableProjectionPreviousMotionView", previousView);
                        cmd.SetGlobalMatrix("_StableProjectionPreviousMotionProjection", previousProjection);
                        cmd.SetGlobalMatrix("_ProbePreviousModel", previousModel);
                        for (var mode = 0; mode < modes; mode++)
                        {
                            var index = (perspective * samples + i) * modes + mode;
                            var drawView = view;
                            var drawProjection = projection;
                            var drawModel = model;
                            var drawVertex = vertex;
                            expected[index] = mode == 2 ? previousReference : reference;
                            if (mode == 3)
                            {
                                drawVertex = model.GetColumn(3);
                                expected[index] = Project(view, gpuProjection, Matrix4x4.identity, drawVertex);
                            }
                            else if (mode == 4)
                            {
                                drawView = Matrix4x4.Translate(new Vector3(-320, -180, 0));
                                drawProjection = Matrix4x4.Ortho(-320, 320, -180, 180, -100, 100);
                                drawModel = Matrix4x4.TRS(new Vector3(240, 100, 0), Quaternion.Euler(0, 0, t * 10), Vector3.one);
                                expected[index] = Project(drawView, GL.GetGPUProjectionMatrix(drawProjection, true), drawModel, vertex);
                            }
                            else if (mode == 5)
                            {
                                drawView = Matrix4x4.TRS(new Vector3(-237900, 55, -244000), Quaternion.Euler(50, 80 + t, 5), Vector3.one).inverse;
                                drawProjection = Matrix4x4.Ortho(-100, 100, -100, 100, -1000, 1000);
                                expected[index] = Project(drawView, GL.GetGPUProjectionMatrix(drawProjection, true), model, vertex);
                            }

                            cmd.SetViewProjectionMatrices(drawView, drawProjection);
                            cmd.SetGlobalVector("_ProbeVertex", drawVertex);
                            cmd.SetGlobalInteger("_ProbeMode", mode);
                            cmd.SetViewport(new Rect(index, 0, 1, 1));
                            cmd.DrawMesh(mesh, drawModel, material);
                        }

                        previousView = view;
                        previousProjection = gpuProjection;
                        previousModel = model;
                        previousReference = reference;
                    }
                }

                Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, 1), 0, 0);
                readback.Apply();
                var colors = readback.GetPixels();
                var errors = new double[modes];
                for (var i = 0; i < colors.Length; i++)
                {
                    var c = colors[i];
                    var error = PixelError(new double4(c.r, c.g, c.b, c.a), expected[i]);
                    errors[i % modes] = Math.Max(errors[i % modes], error);
                    Require(!double.IsNaN(error) && !double.IsInfinity(error) && error < 0.002, "GPU error at sample " + i + " (mode " + i % modes + "): " + error + " pixels.");
                }

                Require(correctionError < 0.0002, "Projection correction lost precision: " + correctionError);
                var cameraMotionPixels = CheckCameraMotion();
                var result = new Report { passed = true, unity = Application.unityVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName,
                    rigError = rigError, lifecycleChecks = lifecycleChecks, historyChecks = historyChecks, gpuSamples = width, correctionError = correctionError,
                    maxErrorPixels = errors, cameraMotionPixels = cameraMotionPixels, legacyMatrixPixels = legacy };
                Directory.CreateDirectory("ValidationResults");
                File.WriteAllText("ValidationResults/stable-projection-gpu.json", JsonUtility.ToJson(result, true));
                return result;
            }
            finally
            {
                RenderTexture.active = oldTarget;
                foreach (var item in new Object[] { readback, target, mesh, material, shader, root })
                {
                    if (item != null)
                    {
                        Object.DestroyImmediate(item);
                    }
                }
            }
        }

        static int CheckLifecycle(Camera camera)
        {
            var controller = camera.gameObject.AddComponent<PreciseOrthographicCamera>();
            camera.orthographic = true;
            camera.orthographicSize = 12;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000;
            controller.ApplyProjection();
            var first = camera.projectionMatrix;
            for (var i = 0; i < 10; i++)
            {
                controller.ApplyProjection();
                Require(first == camera.projectionMatrix, "Repeated camera setup accumulated the correction.");
            }

            camera.orthographicSize = 7;
            camera.nearClipPlane = 0.2f;
            controller.ApplyProjection();
            Require(Mathf.Abs(camera.projectionMatrix.m11 - 1f / 7) < 1e-7f, "Zoom retained a stale projection.");
            controller.RestoreProjection();
            Require(camera.nearClipPlane == 0.2f && camera.farClipPlane == 1000, "Restoration lost changed clip distances.");
            Require(Mathf.Abs(camera.projectionMatrix.m11 - 1f / 7) < 1e-7f, "Automatic projection was not restored.");
            camera.orthographic = false;
            first = camera.projectionMatrix;
            controller.ApplyProjection();
            Require(first == camera.projectionMatrix, "Orthographic correction affected perspective.");
            Object.DestroyImmediate(controller);
            return 5;
        }

        static Matrix4x4 GetProjection(Matrix4x4 view, Matrix4x4 projection, double3 position, out float depthOffset)
            => ProjectionMath.CorrectOrthographic(view, projection, new DoublePosition(position.x, position.y, position.z), out depthOffset);

        static double3 GetRigPosition(Transform transform, double3 remainder)
        {
            var p = ProjectionMath.AccumulatePosition(transform);
            return new double3(p.x, p.y, p.z) + remainder;
        }

        static object NewHistory() => Activator.CreateInstance(typeof(UniversalAdditionalCameraData).Assembly.GetType("UnityEngine.Rendering.Universal.MotionVectorsPersistentData"), true);
        static Matrix4x4 GetHistory(object history, string name)
        {
            var field = history.GetType().GetField(name, flags);
            return field != null ? ((Matrix4x4[])field.GetValue(history))[0]
                : (Matrix4x4)history.GetType().GetField(name.Replace("m_", "m_StableProjection"), flags).GetValue(history);
        }

        static void SetHistory(object history, string name, Matrix4x4 value)
        {
            var field = history.GetType().GetField(name, flags);
            if (field != null)
            {
                ((Matrix4x4[])field.GetValue(history))[0] = value;
            }
            else
            {
                history.GetType().GetField(name.Replace("m_", "m_StableProjection"), flags).SetValue(history, value);
            }
        }

        static int CheckHistory(Camera camera)
        {
            var assembly = typeof(UniversalAdditionalCameraData).Assembly;
            var dataType = assembly.GetType("UnityEngine.Rendering.Universal.UniversalCameraData") ?? typeof(CameraData);
            var data = Activator.CreateInstance(dataType);
            dataType.GetField("camera", flags).SetValue(data, camera);
            var xrProperty = dataType.GetProperty("xr", flags);
            xrProperty.SetValue(data, Activator.CreateInstance(xrProperty.PropertyType, true));
            var aspect = dataType.GetField("aspectRatio", flags);
            aspect.SetValue(data, 1.6f);
            var set = dataType.GetMethod("SetViewAndProjectionMatrix", flags);
            var projection = Matrix4x4.Ortho(-20, 20, -12, 12, 0.1f, 1000);
            set.Invoke(data, new object[] { camera.worldToCameraMatrix, projection });
            var history = NewHistory();
            var type = history.GetType();
            var update = type.GetMethod("Update", flags);
            var indices = (int[])type.GetField("m_LastFrameIndex", flags).GetValue(history);
            update.Invoke(history, new object[] { data });
            var first = GetHistory(history, "m_Projection");
            Require(GetHistory(history, "m_PreviousProjection") == first, "First frame did not seed projection history.");
            projection.m03 += 0.00012f;
            set.Invoke(data, new object[] { camera.worldToCameraMatrix, projection });
            indices[0] = Time.frameCount - 2;
            if (indices[0] == -1)
            {
                indices[0] = -2;
            }

            update.Invoke(history, new object[] { data });
            Require(GetHistory(history, "m_PreviousProjection") == first, "Previous projection was not preserved.");
            var second = GetHistory(history, "m_Projection");
            Require(second != first, "Small projection correction did not enter history.");
            update.Invoke(history, new object[] { data });
            Require(GetHistory(history, "m_PreviousProjection") == first, "Same-frame render advanced history twice.");
            aspect.SetValue(data, 2f);
            update.Invoke(history, new object[] { data });
            Require(GetHistory(history, "m_PreviousProjection") == second, "Aspect change did not reset history.");
            type.GetMethod("Reset", flags).Invoke(history, null);
            Require(GetHistory(history, "m_Projection") == Matrix4x4.identity, "Explicit reset retained projection history.");
            return 6;
        }

        static double CheckCameraMotion()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/CameraMotionVectors.shader");
            var material = new Material(shader);
            ShaderUtil.CompilePass(material, 0, true);
            Require(!ShaderUtil.ShaderHasError(shader), "Camera motion shader failed to compile.");
            const int count = 128;
            var target = new RenderTexture(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            var depth = new Texture2D(1, 1, TextureFormat.RFloat, false, true);
            depth.SetPixel(0, 0, new Color(0.5f, 0, 0, 0));
            depth.Apply();
            var readback = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true);
            var oldTarget = RenderTexture.active;
            var oldDepth = Shader.GetGlobalTexture("_CameraDepthTexture");
            var oldScreen = Shader.GetGlobalVector("_ScaledScreenParams");
            var history = NewHistory();
            var set = history.GetType().GetMethod("SetCameraReprojectionMatrices", flags);
            using var cmd = new CommandBuffer { name = "Standard camera motion validation" };
            try
            {
                var expected = new double2[count];
                cmd.SetRenderTarget(target);
                cmd.ClearRenderTarget(false, true, Color.magenta);
                cmd.SetGlobalTexture("_CameraDepthTexture", depth);
                cmd.SetGlobalVector("_CameraDepthTexture_TexelSize", Vector4.one);
                for (var i = 0; i < count; i++)
                {
                    var stationary = i % 4 == 0;
                    var position = new double3(-237930 + i * 0.001, 99, -244049 - i * 0.0017);
                    var previousPosition = stationary ? position : position + new double3(-0.0009, 0.00003, 0.0007);
                    var rotation = Quaternion.Euler(35, 48 + i * 0.002f, 0);
                    var previousRotation = stationary ? rotation : Quaternion.Euler(35, 48 + (i - 1) * 0.002f, 0);
                    var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS((float3)position, rotation, Vector3.one).inverse;
                    var previousView = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS((float3)previousPosition, previousRotation, Vector3.one).inverse;
                    var size = 12 + i * 0.01f;
                    var previousSize = stationary ? size : size - 0.01f;
                    var projection = Matrix4x4.Ortho(-size, size, -size, size, 0.1f, 1000);
                    var previousProjection = Matrix4x4.Ortho(-previousSize, previousSize, -previousSize, previousSize, 0.1f, 1000);
                    projection = GL.GetGPUProjectionMatrix(GetProjection(view, projection, position, out _), true);
                    previousProjection = GL.GetGPUProjectionMatrix(GetProjection(previousView, previousProjection, previousPosition, out _), true);
                    var jitter = Matrix4x4.identity;
                    jitter.m03 = 0.0003f;
                    jitter.m13 = -0.0002f;
                    var jittered = jitter * projection;
                    var point = math.mul(math.inverse(math.mul(D(jittered), D(view))), new double4(0, 0, 0.5, 1));
                    var currentClip = math.mul(math.mul(D(projection), D(view)), point);
                    var previousClip = math.mul(math.mul(D(previousProjection), D(previousView)), point);
                    expected[i] = (currentClip.xy / currentClip.w - previousClip.xy / previousClip.w) * new double2(0.5, SystemInfo.graphicsUVStartsAtTop ? -0.5 : 0.5);
                    SetHistory(history, "m_View", view);
                    SetHistory(history, "m_Projection", projection);
                    SetHistory(history, "m_PreviousView", previousView);
                    SetHistory(history, "m_PreviousProjection", previousProjection);
                    #if UNITY_6000_0_OR_NEWER
                    set.Invoke(history, new object[] { CommandBufferHelpers.GetRasterCommandBuffer(cmd), view, jittered });
    #else
                    set.Invoke(history, new object[] { cmd, view, jittered });
    #endif
                    // URP 14 derives UV from absolute raster position; make this one-pixel viewport sample the center too.
                    cmd.SetGlobalVector("_ScaledScreenParams", new Vector4(2 * i + 1, 1, 1 + 1f / (2 * i + 1), 2));
                    cmd.SetViewport(new Rect(i, 0, 1, 1));
                    cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                }

                Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, count, 1), 0, 0);
                readback.Apply();
                var colors = readback.GetPixels();
                var maxError = 0.0;
                for (var i = 0; i < count; i++)
                {
                    var error = math.length(new double2(colors[i].r, colors[i].g) - expected[i]) * 1248;
                    Require(!double.IsNaN(error) && !double.IsInfinity(error) && error < 0.003, "Camera-only motion error at sample " + i + ": " + error + " pixels.");
                    maxError = Math.Max(maxError, error);
                }

                return maxError;
            }
            finally
            {
                RenderTexture.active = oldTarget;
                Shader.SetGlobalTexture("_CameraDepthTexture", oldDepth);
                Shader.SetGlobalVector("_ScaledScreenParams", oldScreen);
                foreach (var item in new Object[] { material, readback, depth, target })
                {
                    Object.DestroyImmediate(item);
                }
            }
        }

        static double4x4 D(Matrix4x4 value) => new double4x4((float4x4)value);
        static double4 Project(Matrix4x4 view, Matrix4x4 projection, Matrix4x4 model, Vector4 vertex) => math.mul(D(projection), math.mul(D(view), math.mul(D(model), (double4)(float4)vertex)));
        static double PixelError(double4 actual, double4 expected) => math.length(actual.xy / actual.w - expected.xy / expected.w) * 624;

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

}
