using System;
using System.Globalization;

namespace ReplayLogger
{
    internal static class OptionalFormatting
    {
        internal static string FormatToggle(bool value) => value ? "On" : "Off";

        internal static string FormatOptionalToggle(Optional<bool> value)
        {
            return value.HasValue ? FormatToggle(value.Value) : "N/A";
        }

        internal static string FormatOptionalFloat(Optional<float> value)
        {
            return value.HasValue
                ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "N/A";
        }

        internal static string FormatOptionalInt(Optional<int> value)
        {
            return value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "N/A";
        }

        internal static float NormalizeFloat(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.ToEven);
        }
    }
}
