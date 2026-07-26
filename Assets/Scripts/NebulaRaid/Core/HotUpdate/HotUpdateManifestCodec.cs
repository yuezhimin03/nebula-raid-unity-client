using System;
using System.Collections.Generic;

namespace NebulaRaid.HotUpdate
{
    public static class HotUpdateManifestCodec
    {
        private static readonly string[] RootFields =
        {
            "schemaVersion",
            "bundleVersion",
            "minimumAppVersion",
            "entrypoint",
            "files",
        };

        private static readonly string[] FileFields = { "path", "sha256", "size" };

        public static HotUpdateManifest Parse(string json)
        {
            object? parsed = StrictJsonParser.Parse(json);
            Dictionary<string, object?> root = RequireObject(parsed, "manifest");
            EnsureExactFields(root, RootFields, "manifest");
            int schemaVersion = RequireInt32(root, "schemaVersion");
            string bundleVersion = RequireString(root, "bundleVersion");
            string minimumAppVersion = RequireString(root, "minimumAppVersion");
            string entrypoint = RequireString(root, "entrypoint");
            List<object?> fileValues = RequireArray(root, "files");
            HotUpdateFile[] files = new HotUpdateFile[fileValues.Count];

            for (int i = 0; i < fileValues.Count; i++)
            {
                Dictionary<string, object?> file = RequireObject(fileValues[i], "files[" + i + "]");
                EnsureExactFields(file, FileFields, "files[" + i + "]");
                files[i] = new HotUpdateFile(
                    RequireString(file, "path"),
                    RequireString(file, "sha256"),
                    RequireInt64(file, "size"));
            }

            return new HotUpdateManifest(
                schemaVersion,
                bundleVersion,
                minimumAppVersion,
                entrypoint,
                files);
        }

        private static Dictionary<string, object?> RequireObject(object? value, string label)
        {
            if (!(value is Dictionary<string, object?> result))
            {
                throw new FormatException(label + " must be a JSON object.");
            }

            return result;
        }

        private static List<object?> RequireArray(
            Dictionary<string, object?> owner,
            string field)
        {
            if (!owner.TryGetValue(field, out object? value) || !(value is List<object?> result))
            {
                throw new FormatException(field + " must be an array.");
            }

            return result;
        }

        private static string RequireString(
            Dictionary<string, object?> owner,
            string field)
        {
            if (!owner.TryGetValue(field, out object? value) || !(value is string result))
            {
                throw new FormatException(field + " must be a string.");
            }

            return result;
        }

        private static int RequireInt32(Dictionary<string, object?> owner, string field)
        {
            long value = RequireInt64(owner, field);
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new FormatException(field + " is outside Int32 range.");
            }

            return (int)value;
        }

        private static long RequireInt64(Dictionary<string, object?> owner, string field)
        {
            if (!owner.TryGetValue(field, out object? value) || !(value is long result))
            {
                throw new FormatException(field + " must be an integer.");
            }

            return result;
        }

        private static void EnsureExactFields(
            Dictionary<string, object?> value,
            string[] expected,
            string label)
        {
            if (value.Count != expected.Length)
            {
                throw new FormatException(label + " has missing or unsupported fields.");
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (!value.ContainsKey(expected[i]))
                {
                    throw new FormatException(label + " is missing " + expected[i] + ".");
                }
            }
        }
    }
}

