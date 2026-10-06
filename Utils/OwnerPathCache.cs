using System.Collections.Generic;
using UnityEngine;

namespace ReplayLogger
{
    internal static class OwnerPathCache
    {
        internal const float CleanupTickSeconds = 0.5f;
        internal const int CleanupMinSize = 256;
        internal const int CleanupBatchSize = 128;

        internal static string GetCachedPath(GameObject ownerObject, Dictionary<GameObject, string> ownerPathByGameObject)
        {
            if (ownerObject == null)
            {
                return string.Empty;
            }

            if (ownerPathByGameObject.TryGetValue(ownerObject, out string cachedPath))
            {
                return cachedPath ?? string.Empty;
            }

            string path = ownerObject.GetFullPath();
            ownerPathByGameObject[ownerObject] = path;
            return path;
        }

        internal static void CleanupIfNeeded<TValue>(
            Dictionary<GameObject, TValue> ownerPathByGameObject,
            List<GameObject> cleanupBuffer,
            ref float lastCleanupTime,
            ref int cleanupCursor,
            float now,
            bool force = false)
        {
            if (ownerPathByGameObject.Count < CleanupMinSize)
            {
                cleanupCursor = 0;
                return;
            }

            if (!force && now - lastCleanupTime < CleanupTickSeconds)
            {
                return;
            }

            lastCleanupTime = now;
            cleanupBuffer.Clear();
            int startIndex = cleanupCursor;
            int endIndexExclusive = startIndex + CleanupBatchSize;
            int index = 0;
            foreach (var pair in ownerPathByGameObject)
            {
                if (index < startIndex)
                {
                    index++;
                    continue;
                }

                if (index >= endIndexExclusive)
                {
                    break;
                }

                if (pair.Key == null)
                {
                    cleanupBuffer.Add(pair.Key);
                }

                index++;
            }

            foreach (GameObject key in cleanupBuffer)
            {
                ownerPathByGameObject.Remove(key);
            }

            if (index < endIndexExclusive)
            {
                cleanupCursor = 0;
                return;
            }

            int remainingCount = ownerPathByGameObject.Count;
            cleanupCursor = endIndexExclusive >= remainingCount ? 0 : endIndexExclusive;
        }
    }
}
