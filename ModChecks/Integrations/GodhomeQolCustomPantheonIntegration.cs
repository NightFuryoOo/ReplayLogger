using System;
using System.Collections.Generic;
using System.Reflection;

namespace ReplayLogger
{
    internal static class GodhomeQolCustomPantheonIntegration
    {
        internal const string DoorKey = "GodhomeQoLCustomPantheon";
        internal const string DefaultName = "Custom Pantheon";
        private const string RunnerTypeName = "GodhomeQoL.Modules.BossChallenge.CustomPantheonRunner";
        private const string ChainNameProperty = "ActiveChainName";
        private const string DescribeMethod = "DescribeActiveRun";

        private static Type runnerType;
        private static PropertyInfo chainNameProperty;
        private static MethodInfo describeMethod;

        internal static bool IsCustomPantheonDoor(string playerData)
        {
            return string.Equals(playerData, DoorKey, StringComparison.Ordinal);
        }

        internal static string GetChainName()
        {
            EnsureCache();
            try
            {
                if (chainNameProperty?.GetValue(null, null) is string name && !string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }
            catch
            {
            }

            return DefaultName;
        }

        internal static List<string> DescribeRun(string chainName, IReadOnlyList<string> route)
        {
            List<string> lines = new()
            {
                $"Custom Pantheon: {chainName}",
                $"Custom Pantheon Fights: {route?.Count ?? 0}"
            };

            string[] described = null;
            EnsureCache();
            try
            {
                described = describeMethod?.Invoke(null, null) as string[];
            }
            catch
            {
            }

            if (described != null && described.Length > 0)
            {
                foreach (string line in described)
                {
                    lines.Add($"Custom Pantheon Fight {line}");
                }
            }
            else if (route != null)
            {
                for (int i = 0; i < route.Count; i++)
                {
                    lines.Add($"Custom Pantheon Fight {i + 1}. {route[i]}");
                }
            }

            return lines;
        }

        private static void EnsureCache()
        {
            if (runnerType != null)
            {
                return;
            }

            try
            {
                runnerType = TypeLookup.FindType(RunnerTypeName);
                if (runnerType == null)
                {
                    return;
                }

                const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                chainNameProperty = runnerType.GetProperty(ChainNameProperty, staticFlags);
                describeMethod = runnerType.GetMethod(DescribeMethod, staticFlags, null, Type.EmptyTypes, null);
            }
            catch
            {
            }
        }
    }
}
