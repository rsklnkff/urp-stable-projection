using System.Linq;
namespace StableProjection.Editor
{
    public static class ShaderValidation
    {
        [UnityEditor.MenuItem("Tools/Stable Projection/Compile representative URP shaders")]
        public static void Run()
        {
            var platform = UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Vulkan
                ? UnityEditor.Rendering.ShaderCompilerPlatform.Vulkan : UnityEditor.Rendering.ShaderCompilerPlatform.D3D;
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new System.InvalidOperationException("Run shader compilation validation in Edit Mode.");
            }

            var reportDirectory = "ValidationResults";
            System.IO.Directory.CreateDirectory(reportDirectory);
            var shaderPaths = new System.Collections.Generic.List<string>();
            shaderPaths.AddRange(new[]
            {
                "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLit.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/BakedLit.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/CameraMotionVectors.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/Particles/ParticlesLit.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/Particles/ParticlesUnlit.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLit.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7Billboard.shader",
                "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree8.shader"
            });

            var requests = new System.Collections.Generic.Queue<System.Action>();
            var failures = new System.Collections.Generic.List<string>();
            var shaderCount = 0;
            var completed = 0;
            var keywordSets = new[]
            {
                System.Array.Empty<string>(), new[] { "INSTANCING_ON" }, new[] { "DOTS_INSTANCING_ON" },
                new[] { "_ALPHATEST_ON", "_CASTING_PUNCTUAL_LIGHT_SHADOW" }
            };
            foreach (var path in shaderPaths.Distinct())
            {
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(path);
                if (shader == null)
                {
                    failures.Add("Missing shader: " + path);
                    continue;
                }

                shaderCount++;
                var data = UnityEditor.ShaderUtil.GetShaderData(shader);
                var availableKeywords = shader.keywordSpace.keywordNames;
                for (var subshaderIndex = 0; subshaderIndex < data.SubshaderCount; subshaderIndex++)
                {
                    var subshader = data.GetSubshader(subshaderIndex);
                    for (var passIndex = 0; passIndex < subshader.PassCount; passIndex++)
                    {
                        var pass = subshader.GetPass(passIndex);
                        foreach (var keywords in keywordSets)
                        {
                            if (keywords.Any(keyword => !availableKeywords.Contains(keyword)))
                            {
                                continue;
                            }

                            foreach (var stage in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
                            {
                                if (!pass.HasShaderStage(stage))
                                {
                                    continue;
                                }

                                var label = path + " | " + subshaderIndex + " | " + pass.Name + " | " + stage + " | " + string.Join(",", keywords);
                                requests.Enqueue(() =>
                                {
                                    var result = pass.CompileVariant(stage, keywords, platform, UnityEditor.BuildTarget.StandaloneWindows64);
                                    if (!result.Success)
                                    {
                                        failures.Add(label + ": " + string.Join("; ", result.Messages.Select(message => message.message)));
                                    }
                                });
                            }
                        }
                    }
                }
            }

            var total = requests.Count;
            UnityEditor.SessionState.SetString("StableProjection.CompileStatus", "Running 0/" + total);
            UnityEditor.EditorApplication.CallbackFunction compileNext = null;
            compileNext = () =>
            {
                try
                {
                    if (requests.Count > 0)
                    {
                        requests.Dequeue()();
                        completed++;
                    }
                }
                catch (System.Exception exception)
                {
                    failures.Add(exception.ToString());
                    completed++;
                }

                var status = (requests.Count == 0 ? "Completed " : "Running ") + completed + "/" + total + ", failures: " + failures.Count;
                UnityEditor.SessionState.SetString("StableProjection.CompileStatus", status);
                if (requests.Count > 0)
                {
                    return;
                }

                UnityEditor.EditorApplication.update -= compileNext;
                var lines = new System.Collections.Generic.List<string> { status, "Shaders: " + shaderCount, "Backend: " + platform + " / StandaloneWindows64" };
                lines.AddRange(failures);
                System.IO.File.WriteAllLines(reportDirectory + "/compile-report.txt", lines);
                UnityEngine.Debug.Log(string.Join("\n", lines));
                if (UnityEngine.Application.isBatchMode)
                {
                    UnityEditor.EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
                }
            };
            UnityEditor.EditorApplication.update += compileNext;
        }
    }
}
