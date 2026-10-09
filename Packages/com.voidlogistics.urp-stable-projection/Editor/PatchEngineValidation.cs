using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StableProjection.Editor
{
    public static class PatchEngineValidation
    {
        [MenuItem("Tools/Stable Projection/Run installer preflight tests")]
        public static string Run()
        {
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var relative = "Temp/StableProjectionTests/" + Guid.NewGuid().ToString("N");
            var root = PatchEngine.ContainedPath(project, relative);
            var core = Path.Combine(root, "core");
            var urp = Path.Combine(root, "urp");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(urp);
            var file = Path.Combine(core, "sample.hlsl");
            var profile = new PatchProfile { files = new[] { new PatchFile
            {
                path = PatchEngine.Core + "/sample.hlsl", beforeSha256 = PatchEngine.Hash("before\n"),
                edits = new[] { new PatchEdit { before = "before\n", after = "after\n" } }
            } } };
            int checks = 0;
            try
            {
                File.WriteAllText(file, "before\r\n");
                var result = PatchEngine.Prepare(profile, core, urp);
                Require(result.Single().content == "after\n", "CRLF conversion failed.");
                Require(File.ReadAllText(file) == "before\r\n", "Preflight modified source files.");
                checks += 2;
                File.WriteAllText(file, "before\nbefore\n");
                Refuses(() => PatchEngine.Prepare(profile, core, urp));
                checks++;
                File.WriteAllText(file, "upstream changed\n");
                Refuses(() => PatchEngine.Prepare(profile, core, urp));
                checks++;
                File.WriteAllText(file, "before\n// STABLE_PROJECTION_USE\n");
                Refuses(() => PatchEngine.Prepare(profile, core, urp));
                checks++;
                Refuses(() => PatchEngine.ContainedPath(core, "../escape.hlsl"));
                checks++;
                Refuses(() => PatchEngine.LoadProfile(PrecisionInstaller.packageRoot, "18.0.0"));
                Refuses(() => PatchEngine.LoadProfile(PrecisionInstaller.packageRoot, "17.4.0"));
                Refuses(() => PatchEngine.LoadProfile(PrecisionInstaller.packageRoot, "17.3.0-preview.1"));
                checks += 3;
                foreach (var version in new[] { "14.0.12", "17.0.4", "17.1.0", "17.2.0", "17.3.0" })
                {
                    Require(PatchEngine.LoadProfile(PrecisionInstaller.packageRoot, version).files.Length >= 14, "Incomplete adapter: " + version);
                    checks++;
                }

                var message = "Passed " + checks + " installer preflight checks.";
                Directory.CreateDirectory("ValidationResults");
                File.WriteAllText("ValidationResults/preflight.txt", message);
                Debug.Log(message);
                return message;
            }
            finally
            {
                // The only recursive cleanup is the unique test directory verified under this project's Temp.
                if (root == PatchEngine.ContainedPath(project, relative) && Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        static void Refuses(Action action)
        {
            bool refused = false;
            try { action(); }
            catch (InvalidOperationException) { refused = true; }
            catch (InvalidDataException) { refused = true; }
            Require(refused, "An incompatible input was accepted.");
        }
    }
}
