using System;
using System.Globalization;

namespace NebulaRaid.HotUpdate
{
    internal readonly struct SemanticVersion : IComparable<SemanticVersion>
    {
        private SemanticVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public int CompareTo(SemanticVersion other)
        {
            int major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            int minor = Minor.CompareTo(other.Minor);
            return minor != 0 ? minor : Patch.CompareTo(other.Patch);
        }

        public static SemanticVersion Parse(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException(fieldName + " is required.");
            }

            string[] parts = value.Split('.');
            if (parts.Length != 3)
            {
                throw new FormatException(fieldName + " must use major.minor.patch.");
            }

            return new SemanticVersion(
                ParsePart(parts[0], fieldName),
                ParsePart(parts[1], fieldName),
                ParsePart(parts[2], fieldName));
        }

        private static int ParsePart(string part, string fieldName)
        {
            if (part.Length == 0
                || (part.Length > 1 && part[0] == '0')
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value < 0)
            {
                throw new FormatException(fieldName + " contains an invalid version component.");
            }

            return value;
        }
    }
}

