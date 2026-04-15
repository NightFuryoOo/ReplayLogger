using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace ReplayLogger
{
    internal static class PlayMakerFsmSceneCache
    {
        private static string cachedSceneName;
        private static PlayMakerFSM[] cachedFsms;

        internal static PlayMakerFSM[] Get(bool forceRefresh = false)
        {
            string activeSceneName = GameManager.instance?.sceneName;
            bool sceneChanged = !string.Equals(cachedSceneName, activeSceneName, StringComparison.Ordinal);
            if (!forceRefresh && !sceneChanged && cachedFsms != null)
            {
                return cachedFsms;
            }

            cachedSceneName = activeSceneName;
            cachedFsms = UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>();
            return cachedFsms ?? Array.Empty<PlayMakerFSM>();
        }

        internal static void Invalidate()
        {
            cachedSceneName = null;
            cachedFsms = null;
        }
    }
}
