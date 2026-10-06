using System;
using System.IO;

namespace ReplayLogger
{
    internal static class PathSanitizer
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        internal static string SanitizeSegment(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            char[] chars = value.ToCharArray();
            bool changed = false;
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(InvalidFileNameChars, chars[i]) >= 0)
                {
                    chars[i] = '_';
                    changed = true;
                }
            }

            string result = changed ? new string(chars) : value;

            result = result.TrimEnd(' ', '.');
            return string.IsNullOrEmpty(result) ? fallback : result;
        }
    }
}
