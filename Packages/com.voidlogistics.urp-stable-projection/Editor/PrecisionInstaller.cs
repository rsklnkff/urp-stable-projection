using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace StableProjection.Editor
{
    [Serializable] public sealed class PackageRestorePoint
    {
        public string name, originalSpecifier, installedSpecifier, version;
        public bool originallyDirect;
    }

    [Serializable] public sealed class InstallRecord
    {
        public int schema = 1;
        public string status, message, operationId, stagingDirectory, expectedEditor, adapter;
        public int packIndex;
        public PackageRestorePoint[] packages;
    }

    [InitializeOnLoad]
    public static class PrecisionInstaller
    {
        const string StatePath = "ProjectSettings/StableProjection.install.json";
        const string ArchiveFolder = "Packages/StableProjectionArchives";
        static PackRequest _pack;
        static AddAndRemoveRequest _resolve;
        static double _started;
        public static bool busy => _pack != null || _resolve != null;
        public static string packageRoot => PackageInfo.FindForAssembly(typeof(PrecisionInstaller).Assembly).resolvedPath;
        public static string projectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string stateFile => Path.Combine(projectRoot, StatePath);
        public static string lastMessage { get; private set; } = "Run Check compatibility to inspect the installed packages.";

        static PrecisionInstaller()
        {
            EditorApplication.delayCall += RecoverAfterReload;
        }

        public static InstallRecord ReadRecord()
        {
            return File.Exists(stateFile) ? JsonUtility.FromJson<InstallRecord>(File.ReadAllText(stateFile)) : null;
        }

        static void Save(InstallRecord record)
        {
            File.WriteAllText(stateFile, JsonUtility.ToJson(record, true), new UTF8Encoding(false));
            lastMessage = record.message;
        }

        static PackageInfo Find(string name)
        {
            return PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == name)
                ?? throw new InvalidOperationException("Install URP in this project first. Missing: " + name);
        }

        static void RequireIdle()
        {
            if (busy || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException("Wait for package imports/compilation and stop Play Mode before changing packages.");
            }
        }

        public static List<PreparedFile> Check(out PatchProfile profile)
        {
            var core = Find(PatchEngine.Core);
            var urp = Find(PatchEngine.Urp);
            if (core.source == PackageSource.Embedded || urp.source == PackageSource.Embedded)
            {
                throw new InvalidOperationException("Embedded Core/URP overrides are present. Preserve your edits and return to registry or archive packages first.");
            }

            if (core.version != urp.version)
            {
                throw new InvalidOperationException("Core and URP versions must match; found " + core.version + " and " + urp.version + ".");
            }

            profile = PatchEngine.LoadProfile(packageRoot, urp.version);
            var result = PatchEngine.Prepare(profile, core.resolvedPath, urp.resolvedPath);
            int different = result.Count(f => !f.exactReference);
            lastMessage = "URP " + urp.version + ": all " + result.Count + " files match the " + profile.id + " adapter. "
                + (different == 0 ? "All files match the reference snapshot." : different + " files differ outside the checked edit blocks; compilation and rendering need validation.");
            return result;
        }

        public static void Install()
        {
            RequireIdle();
            var existing = ReadRecord();
            if (existing != null && existing.status != "restored" && existing.status != "failed-before-install")
            {
                throw new InvalidOperationException("An install/restore record already exists (" + existing.status + "). Restore original packages before installing again.");
            }

            var prepared = Check(out var profile);
            var record = new InstallRecord
            {
                status = "preparing", operationId = Guid.NewGuid().ToString("N"), expectedEditor = Application.unityVersion,
                adapter = profile.id, message = "Preparing copies; original packages remain active.",
                packages = new[] { RestorePoint(Find(PatchEngine.Core)), RestorePoint(Find(PatchEngine.Urp)) }
            };
            record.stagingDirectory = "Temp/StableProjection/" + record.operationId;
            Save(record);
            try
            {
                var stage = Path.Combine(projectRoot, record.stagingDirectory);
                foreach (var item in record.packages)
                {
                    CopyTree(Find(item.name).resolvedPath, Path.Combine(stage, item.name));
                }

                foreach (var file in prepared)
                {
                    File.WriteAllText(PatchEngine.ContainedPath(stage, file.path), file.content, new UTF8Encoding(false));
                }

                record.status = "packing";
                record.message = "Packing checked Core/URP copies.";
                record.packIndex = 0;
                Save(record);
                StartPack(record);
            }
            catch (Exception e)
            {
                record.status = "failed-before-install";
                record.message = e.Message;
                Save(record);
                throw;
            }
        }

        [Serializable] sealed class StringValue { public string value; }

        static PackageRestorePoint RestorePoint(PackageInfo info)
        {
            var manifest = File.ReadAllText(Path.Combine(projectRoot, "Packages/manifest.json"));
            var pattern = "\"" + Regex.Escape(info.name) + "\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")";
            var match = Regex.Match(manifest, pattern);
            var specifier = match.Success ? JsonUtility.FromJson<StringValue>("{\"value\":" + match.Groups[1].Value + "}").value : null;
            return new PackageRestorePoint { name = info.name, version = info.version, originallyDirect = match.Success, originalSpecifier = specifier };
        }

        static void CopyTree(string source, string target)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Package contains a symbolic link: " + source);
            }

            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Package contains a symbolic link: " + file);
                }

                var destination = Path.Combine(target, Path.GetFileName(file));
                File.Copy(file, destination, false);
                File.SetAttributes(destination, FileAttributes.Normal);
            }

            foreach (var directory in Directory.GetDirectories(source))
            {
                if (Path.GetFileName(directory) != ".git")
                {
                    CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
                }
            }
        }

        static void StartPack(InstallRecord record)
        {
            var stage = Path.Combine(projectRoot, record.stagingDirectory);
            var output = Path.Combine(stage, "archives");
            Directory.CreateDirectory(output);
#pragma warning disable CS0618
            _pack = Client.Pack(Path.Combine(stage, record.packages[record.packIndex].name), output);
#pragma warning restore CS0618
            StartPolling();
        }

        static void StartPolling()
        {
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            var record = ReadRecord();
            try
            {
                if (_pack != null)
                {
                    if (!_pack.IsCompleted)
                    {
                        CheckTimeout();
                        return;
                    }

                    var completed = _pack;
                    _pack = null;
                    if (completed.Status != StatusCode.Success)
                    {
                        throw new InvalidOperationException(completed.Error.message);
                    }

                    var item = record.packages[record.packIndex];
                    var archives = Path.Combine(projectRoot, ArchiveFolder);
                    Directory.CreateDirectory(archives);
                    string filename = item.name + "-" + item.version + "-stable-projection-0.1.0-" + record.operationId.Substring(0, 8) + ".tgz";
                    File.Copy(completed.Result.tarballPath, Path.Combine(archives, filename), false);
                    item.installedSpecifier = "file:StableProjectionArchives/" + filename;
                    record.packIndex++;
                    Save(record);
                    if (record.packIndex < record.packages.Length)
                    {
                        StartPack(record);
                        return;
                    }

                    record.status = "installing";
                    record.message = "Resolving patched archives. Unity may show its local-package signature notice.";
                    Save(record);
                    _resolve = Client.AddAndRemove(record.packages.Select(p => p.installedSpecifier).ToArray());
                    _started = EditorApplication.timeSinceStartup;
                }

                if (_resolve != null)
                {
                    if (!_resolve.IsCompleted)
                    {
                        CheckTimeout();
                        return;
                    }

                    var completed = _resolve;
                    _resolve = null;
                    if (completed.Status != StatusCode.Success)
                    {
                        throw new InvalidOperationException(completed.Error.message);
                    }

                    record.message = "Package Manager resolved successfully; verifying registered package sources after import.";
                    Save(record);
                    EditorApplication.update -= Poll;
                    EditorApplication.delayCall += RecoverAfterReload;
                }
            }
            catch (Exception e)
            {
                _pack = null;
                _resolve = null;
                EditorApplication.update -= Poll;
                record.status = record.status == "packing" ? "failed-before-install" : "needs-restore";
                record.message = e.ToString();
                Save(record);
                Debug.LogError("Stable Projection: " + e.Message);
            }
        }

        static void CheckTimeout()
        {
            if (EditorApplication.timeSinceStartup - _started > 600)
            {
                throw new TimeoutException("Package Manager did not finish in 10 minutes. Let its request finish, then reopen the installer to verify or restore.");
            }
        }

        public static void Restore()
        {
            RequireIdle();
            var record = ReadRecord() ?? throw new InvalidOperationException("No restore record exists.");
            if (record.status == "restored")
            {
                lastMessage = "Original package references are already restored.";
                return;
            }

            foreach (var item in record.packages)
            {
                var actual = RestorePoint(Find(item.name));
                if (actual.originalSpecifier != item.installedSpecifier && actual.originalSpecifier != item.originalSpecifier)
                {
                    throw new InvalidOperationException("The " + item.name + " dependency was changed after installation. Restore manually using " + StatePath + "; no references changed.");
                }
            }

            var adds = record.packages.Where(p => p.originallyDirect).Select(p => ToIdentifier(p.name, p.originalSpecifier)).ToArray();
            var removes = record.packages.Where(p => !p.originallyDirect).Select(p => p.name).ToArray();
            record.status = "restoring";
            record.message = "Restoring the exact original direct/transitive dependency arrangement.";
            Save(record);
            _resolve = Client.AddAndRemove(adds, removes);
            StartPolling();
        }

        static string ToIdentifier(string name, string specifier)
        {
            return specifier.StartsWith("file:", StringComparison.Ordinal) || specifier.Contains("://") || specifier.StartsWith("git@", StringComparison.Ordinal)
                ? specifier : name + "@" + specifier;
        }

        static void RecoverAfterReload()
        {
            if (busy)
            {
                return;
            }

            var record = ReadRecord();
            if (record == null)
            {
                return;
            }

            lastMessage = record.message;
            if (record.status == "installing" || record.status == "restoring")
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    EditorApplication.delayCall += RecoverAfterReload;
                    return;
                }

                bool installing = record.status == "installing";
                bool matches = record.packages.All(p =>
                {
                    var resolved = Find(p.name);
                    var actual = RestorePoint(resolved);
                    if (installing)
                    {
                        return actual.originalSpecifier == p.installedSpecifier && resolved.source == PackageSource.LocalTarball
                            && resolved.packageId.EndsWith(Path.GetFileName(p.installedSpecifier), StringComparison.Ordinal);
                    }

                    return actual.originallyDirect == p.originallyDirect && (!p.originallyDirect || actual.originalSpecifier == p.originalSpecifier)
                        && !resolved.packageId.Contains("-stable-projection-");
                });
                if (!matches)
                {
                    record.message = "Package resolution has not reached the expected references. Inspect Package Manager, then use Restore if necessary.";
                    Save(record);
                    return;
                }

                record.status = installing ? "installed" : "restored";
                record.message = installing ? "Installed. Run Validate installed patch and GPU precision, then test your Player."
                    : "Original package references restored. Generated archives were retained; remove unused archives from version control when convenient.";
                Save(record);
            }
            else if (record.status == "preparing" || record.status == "packing")
            {
                record.status = "failed-before-install";
                record.message = "Preparation was interrupted by a domain reload. Original package references were not changed; retry Install.";
                Save(record);
            }
        }

        public static string VerifyInstalled()
        {
            var core = Find(PatchEngine.Core);
            var urp = Find(PatchEngine.Urp);
            var profile = PatchEngine.LoadProfile(packageRoot, urp.version);
            foreach (var file in profile.files)
            {
                var index = file.path.IndexOf('/');
                var root = file.path.StartsWith(PatchEngine.Core + "/", StringComparison.Ordinal) ? core.resolvedPath : urp.resolvedPath;
                var source = PatchEngine.Normalize(File.ReadAllText(PatchEngine.ContainedPath(root, file.path.Substring(index + 1))));
                foreach (var edit in file.edits)
                {
                    if (!source.Contains(edit.after))
                    {
                        throw new InvalidOperationException("Installed patch is incomplete: " + file.path);
                    }
                }
            }

            return "Verified " + profile.files.Length + " installed patched files for " + urp.version + ".";
        }
    }

    public sealed class PrecisionInstallerWindow : EditorWindow
    {
        string _message = "";
        Vector2 _scroll;

        [MenuItem("Tools/Stable Projection/Installer")]
        public static void Open() => GetWindow<PrecisionInstallerWindow>("Stable Projection");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Stable Projection for URP", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Supports checked URP 14.0 and 17.0–17.3 sources. Installation packs local copies of your existing Core/URP; it does not upgrade Unity or change your scenes.", MessageType.Info);
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12)
            {
                EditorGUILayout.HelpBox("DX12 is outside the tested support scope: a default compiler/device combination fails the numerical precision check. See Documentation for evidence. Graphics API and compiler settings are left to the project.", MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(PrecisionInstaller.busy || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Check compatibility"))
                {
                    Run(() => { PrecisionInstaller.Check(out _); return PrecisionInstaller.lastMessage; });
                }

                if (GUILayout.Button("Install checked patch"))
                {
                    Run(() => { PrecisionInstaller.Install(); return PrecisionInstaller.lastMessage; });
                }

                if (GUILayout.Button("Validate installed patch"))
                {
                    Run(PrecisionInstaller.VerifyInstalled);
                }

                if (GUILayout.Button("Restore original packages"))
                {
                    Run(() => { PrecisionInstaller.Restore(); return PrecisionInstaller.lastMessage; });
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(PrecisionInstaller.lastMessage, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("Commit Packages/StableProjectionArchives, manifest.json, packages-lock.json and ProjectSettings/StableProjection.install.json together. For URP upgrades, restore first, upgrade normally, then re-check.", MessageType.None);
        }

        void Run(Func<string> action)
        {
            try { _message = action(); }
            catch (Exception e) { _message = e.Message; Debug.LogException(e); }
        }

        void OnInspectorUpdate() => Repaint();
    }
}
