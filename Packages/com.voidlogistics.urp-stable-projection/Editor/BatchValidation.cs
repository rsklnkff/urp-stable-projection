using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StableProjection.Editor
{
    [InitializeOnLoad]
    public static class BatchValidation
    {
        const string Key = "StableProjection.BatchAction";
        static double _quietUntil;
        static BatchValidation()
        {
            if (Application.isBatchMode && !string.IsNullOrEmpty(SessionState.GetString(Key, "")))
            {
                _quietUntil = EditorApplication.timeSinceStartup + 3;
                EditorApplication.update += Poll;
            }
        }

        public static void Install()
        {
            try
            {
                SessionState.SetString(Key, "install");
                PrecisionInstaller.Install();
                _quietUntil = EditorApplication.timeSinceStartup + 3;
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        public static void Restore()
        {
            try
            {
                SessionState.SetString(Key, "restore");
                PrecisionInstaller.Restore();
                _quietUntil = EditorApplication.timeSinceStartup + 3;
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || PrecisionInstaller.busy)
            {
                _quietUntil = EditorApplication.timeSinceStartup + 3;
                return;
            }

            if (EditorApplication.timeSinceStartup < _quietUntil)
            {
                return;
            }

            var record = PrecisionInstaller.ReadRecord();
            if (record == null)
            {
                Finish(false, "Missing installation state.");
                return;
            }

            if (record.status == "installed" || record.status == "restored")
            {
                if (record.status == "installed")
                {
                    try { PrecisionInstaller.VerifyInstalled(); }
                    catch (Exception e) { Finish(false, e.ToString()); return; }
                }

                Finish(true, record.status);
            }
            else if (record.status.StartsWith("failed", StringComparison.Ordinal) || record.status == "needs-restore")
            {
                Finish(false, record.message);
            }
        }

        public static void Validate()
        {
            try
            {
                PatchEngineValidation.Run();
                PrecisionInstaller.VerifyInstalled();
                var report = PrecisionValidation.Run();
                Finish(report.passed, JsonUtility.ToJson(report, true));
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        [Serializable] sealed class Result { public bool passed; public string action, unity, message; }
        static void Finish(bool passed, string message)
        {
            var action = SessionState.GetString(Key, "validate");
            SessionState.EraseString(Key);
            EditorApplication.update -= Poll;
            Directory.CreateDirectory("ValidationResults");
            var json = JsonUtility.ToJson(new Result { passed = passed, action = action, unity = Application.unityVersion, message = message }, true);
            File.WriteAllText("ValidationResults/" + action + ".json", json);
            if (passed) { Debug.Log(json); } else { Debug.LogError(json); }
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
