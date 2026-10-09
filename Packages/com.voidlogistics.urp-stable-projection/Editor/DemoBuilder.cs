using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StableProjection.Editor
{
    public static class DemoBuilder
    {
        [MenuItem("Tools/Stable Projection/Create comparison demo scene")]
        public static void Create()
        {
            PrecisionInstaller.VerifyInstalled();
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                throw new System.InvalidOperationException("Select a URP pipeline asset in Graphics/Quality settings first.");
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            const string parent = "Assets/StableProjectionDemo";
            if (!AssetDatabase.IsValidFolder(parent))
            {
                AssetDatabase.CreateFolder("Assets", "StableProjectionDemo");
            }

            var folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/Comparison");
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Precision comparison");
            var demo = root.AddComponent<PrecisionDemo>();
            root.AddComponent<DemoSmokeTest>();
            var geometry = new GameObject("Geometry").transform;
            geometry.SetParent(root.transform, false);
            var rig = new GameObject("Camera rig").transform;
            rig.SetParent(root.transform, false);
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(rig, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            camera.orthographic = true;
            camera.orthographicSize = 4;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().antialiasing = AntialiasingMode.None;
            var precise = cameraObject.AddComponent<PreciseOrthographicCamera>();
            var mesh = BuildGrid();
            AssetDatabase.CreateAsset(mesh, folder + "/PrecisionGrid.asset");
            var shader = Shader.Find("Stable Projection/Demo");
            for (int side = 0; side < 2; side++)
            {
                var material = new Material(shader) { name = side == 0 ? "Conventional" : "Stable" };
                material.SetColor("_Color", side == 0 ? new Color(1, 0.55f, 0.3f) : new Color(0.2f, 0.85f, 1));
                if (side == 1)
                {
                    material.EnableKeyword("_STABLE_DEMO");
                }

                AssetDatabase.CreateAsset(material, folder + "/" + material.name + ".mat");
                var panel = new GameObject(material.name);
                panel.transform.SetParent(geometry, false);
                panel.transform.localPosition = new Vector3(side == 0 ? -2.3f : 2.3f, 0, 0);
                panel.transform.localRotation = Quaternion.Euler(12, 19, 4);
                panel.AddComponent<MeshFilter>().sharedMesh = mesh;
                panel.AddComponent<MeshRenderer>().sharedMaterial = material;
            }

            demo.demoCamera = camera;
            demo.cameraRig = rig;
            demo.geometryRoot = geometry;
            demo.preciseCamera = precise;
            rig.position = geometry.position = new Vector3(240000, 0, -240000);
            camera.transform.localPosition = new Vector3(0, 0.5f, -12);
            camera.transform.localRotation = Quaternion.LookRotation(-camera.transform.localPosition, Vector3.up);
            EditorSceneManager.SaveScene(scene, folder + "/PrecisionComparison.unity");
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
        }

        static Mesh BuildGrid()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int axis = 0; axis < 2; axis++)
            {
                for (int line = 0; line <= 32; line++)
                {
                    float position = -1.6f + line * 0.1f;
                    for (int segment = 0; segment < 40; segment++)
                    {
                        float start = -1.6f + segment * 0.08f, end = start + 0.08f;
                        int index = vertices.Count;
                        for (int corner = 0; corner < 4; corner++)
                        {
                            float along = corner < 2 ? start : end;
                            float across = position + ((corner == 0 || corner == 3) ? -0.012f : 0.012f);
                            vertices.Add(axis == 0 ? new Vector3(along, across, Mathf.Sin(along * 3) * 0.1f)
                                : new Vector3(across, along, Mathf.Sin(across * 3) * 0.1f));
                        }

                        triangles.AddRange(new[] { index, index + 1, index + 2, index, index + 2, index + 3 });
                    }
                }
            }

            var mesh = new Mesh { name = "Precision grid" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
