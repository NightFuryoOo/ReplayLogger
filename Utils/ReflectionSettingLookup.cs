using System;
using System.Globalization;
using System.Reflection;

namespace ReplayLogger
{
    internal static class ReflectionSettingLookup
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool TryGetBool(Type type, string primaryName, string altName, ref FieldInfo field, ref PropertyInfo property, out bool value)
        {
            value = false;
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                field = type.GetField(primaryName, StaticFlags) ?? type.GetField(altName, StaticFlags);
                if (field == null)
                {
                    property = type.GetProperty(primaryName, StaticFlags) ?? type.GetProperty(altName, StaticFlags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetCachedValue(null)
                    : field?.GetCachedValue(null);

                if (raw is bool flag)
                {
                    value = flag;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        internal static bool TryGetBool(Type type, string fieldName, ref FieldInfo field, ref PropertyInfo property, out bool value)
        {
            value = false;
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                field = type.GetField(fieldName, StaticFlags);
                if (field == null)
                {
                    property = type.GetProperty(fieldName, StaticFlags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetCachedValue(null)
                    : field?.GetCachedValue(null);

                if (raw is bool flag)
                {
                    value = flag;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        internal static bool TryGetFloat(Type type, string primaryName, string altName, ref FieldInfo field, ref PropertyInfo property, out float value)
        {
            value = 0f;
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                field = type.GetField(primaryName, StaticFlags) ?? type.GetField(altName, StaticFlags);
                if (field == null)
                {
                    property = type.GetProperty(primaryName, StaticFlags) ?? type.GetProperty(altName, StaticFlags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetCachedValue(null)
                    : field?.GetCachedValue(null);

                if (raw == null)
                {
                    return false;
                }

                value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }

            return false;
        }

        internal static bool TryGetInt(Type type, string fieldName, ref FieldInfo field, ref PropertyInfo property, out int value)
        {
            value = 0;
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                field = type.GetField(fieldName, StaticFlags);
                if (field == null)
                {
                    property = type.GetProperty(fieldName, StaticFlags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetCachedValue(null)
                    : field?.GetCachedValue(null);

                if (raw == null)
                {
                    return false;
                }

                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }

            return false;
        }
    }
}
