using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace StableProjection.Editor
{
    [Serializable] public sealed class PatchEdit { public string before, after; }
    [Serializable] public sealed class PatchFile { public string path, beforeSha256, afterSha256; public PatchEdit[] edits; }
    [Serializable] public sealed class PatchProfile { public string id, referenceVersion; public int major, minor; public PatchFile[] files; }
    public sealed class PreparedFile
    {
        public string path, content, sourceHash;
        public bool exactReference;
    }

    public static class PatchEngine
    {
        public const string Core = "com.unity.render-pipelines.core";
        public const string Urp = "com.unity.render-pipelines.universal";
        public const string Package = "com.voidlogistics.urp-stable-projection";

        public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n").TrimStart('\ufeff');

        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(text)))).Replace("-", "").ToLowerInvariant();
            }
        }

        public static PatchProfile LoadProfile(string packageRoot, string version)
        {
            if (!Version.TryParse(version, out var number))
            {
                throw new InvalidOperationException("Prerelease or unknown URP version: " + version);
            }

            var id = number.Major == 14 && number.Minor == 0 ? "urp-14" : "urp-" + number.Major + "." + number.Minor;
            if (!(number.Major == 14 && number.Minor == 0) && !(number.Major == 17 && number.Minor >= 0 && number.Minor <= 3))
            {
                throw new InvalidOperationException("No adapter for URP " + version + ". Supported families: 14.0 and 17.0–17.3.");
            }

            return JsonUtility.FromJson<PatchProfile>(File.ReadAllText(Path.Combine(packageRoot, "Editor/Patches/" + id + ".json")));
        }

        public static List<PreparedFile> Prepare(PatchProfile profile, string coreRoot, string urpRoot)
        {
            var result = new List<PreparedFile>();
            foreach (var file in profile.files)
            {
                var slash = file.path.IndexOf('/');
                if (slash < 0)
                {
                    throw new InvalidDataException("Invalid patch path: " + file.path);
                }

                var package = file.path.Substring(0, slash);
                var root = package == Core ? coreRoot : package == Urp ? urpRoot : throw new InvalidDataException("Unexpected package.");
                var path = ContainedPath(root, file.path.Substring(slash + 1));
                var source = Normalize(File.ReadAllText(path));
                if (source.Contains("STABLE_PROJECTION_USE") || source.Contains("_StableProjectionMotionView") || source.Contains("SPACEWAR_USE_STABLE_OBJECT_PROJECTION"))
                {
                    throw new InvalidOperationException("Already patched or contains an earlier precision fork: " + file.path);
                }

                var content = source;
                foreach (var edit in file.edits)
                {
                    int at = content.IndexOf(edit.before, StringComparison.Ordinal);
                    if (at < 0 || content.IndexOf(edit.before, at + edit.before.Length, StringComparison.Ordinal) >= 0)
                    {
                        throw new InvalidOperationException("Expected exactly one matching source block in " + file.path + ". No packages were changed.");
                    }

                    content = content.Substring(0, at) + edit.after + content.Substring(at + edit.before.Length);
                }

                result.Add(new PreparedFile { path = file.path, content = content, sourceHash = Hash(source), exactReference = Hash(source) == file.beforeSha256 });
            }

            return result;
        }

        public static string ContainedPath(string root, string relative)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(fullRoot, comparison))
            {
                throw new InvalidDataException("Path escapes its package directory: " + relative);
            }

            return path;
        }
    }
}
