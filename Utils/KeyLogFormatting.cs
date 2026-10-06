using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReplayLogger
{
    internal static class KeyLogFormatting
    {
        internal const int KeyColorHexCacheMaxSize = 512;

        internal static string GetCachedColorHex(Color32 color, Dictionary<Color32, string> cache)
        {
            if (cache.TryGetValue(color, out string colorHex))
            {
                return colorHex;
            }

            if (cache.Count >= KeyColorHexCacheMaxSize)
            {
                cache.Clear();
            }

            colorHex = ColorUtility.ToHtmlStringRGBA(color);
            cache[color] = colorHex;
            return colorHex;
        }

        internal static string FormatHudElapsedTime(long relativeMs)
        {
            long totalSeconds = Math.Max(0L, relativeMs) / 1000L;
            int hours = (int)((totalSeconds / 3600L) % 24L);
            int minutes = (int)((totalSeconds / 60L) % 60L);
            int seconds = (int)(totalSeconds % 60L);
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        internal static long GetCachedFrameUnixTimeOrNow(long cachedFrameUnixTime)
        {
            return cachedFrameUnixTime > 0 ? cachedFrameUnixTime : DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        internal static long CaptureFrameUnixTime(ref long cachedFrameUnixTime)
        {
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            cachedFrameUnixTime = now;
            return now;
        }
    }
}
