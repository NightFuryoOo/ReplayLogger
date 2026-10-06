using System;
using System.Collections.Generic;
using System.Linq;

namespace ReplayLogger
{
    internal static class RandomOrderRoute
    {
        internal static bool TryValidate(IReadOnlyList<string> standardRoute, IReadOnlyList<string> captured, out string reason)
        {
            reason = null;
            if (standardRoute == null || standardRoute.Count == 0)
            {
                reason = "no standard route for this pantheon";
                return false;
            }

            if (captured == null || captured.Count == 0)
            {
                reason = "the pantheon sequence was not captured";
                return false;
            }

            if (captured.Count != standardRoute.Count)
            {
                reason = $"the sequence has {captured.Count} scenes, the pantheon has {standardRoute.Count}";
                return false;
            }

            Dictionary<string, int> remaining = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string scene in standardRoute)
            {
                remaining.TryGetValue(scene, out int count);
                remaining[scene] = count + 1;
            }

            foreach (string scene in captured)
            {
                if (scene == null || !remaining.TryGetValue(scene, out int count) || count == 0)
                {
                    reason = $"unexpected scene '{scene}' in the sequence";
                    return false;
                }

                remaining[scene] = count - 1;
            }

            return true;
        }

        internal static bool IsStandardOrder(IReadOnlyList<string> standardRoute, IReadOnlyList<string> captured)
        {
            return standardRoute != null
                && captured != null
                && standardRoute.SequenceEqual(captured, StringComparer.Ordinal);
        }
    }
}
