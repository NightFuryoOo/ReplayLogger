using System;
using On;
using UnityEngine.SceneManagement;
using HKHealthManager = global::HealthManager;

namespace ReplayLogger
{
    internal static class HoGRoomConditions
    {
        private static bool hooksInitialized;
        private static string pendingSceneName;

        internal static event Action<string, int> BossHpDetected;

        internal static void Initialize()
        {
            if (hooksInitialized)
            {
                return;
            }

            On.HealthManager.Start += HealthManager_Start;
            hooksInitialized = true;
        }

        internal static void MarkPendingScene(string sceneName)
        {
            if (!string.IsNullOrEmpty(sceneName) && HoGStoragePlanner.RequiresHp(sceneName))
            {
                pendingSceneName = sceneName;
            }
            else
            {
                pendingSceneName = null;
            }
        }

        private static void HealthManager_Start(On.HealthManager.orig_Start orig, HKHealthManager self)
        {
            orig(self);

            if (pendingSceneName == null || self == null || self.gameObject == null)
            {
                return;
            }

            Scene scene = self.gameObject.scene;
            if (!scene.IsValid())
            {
                return;
            }

            string sceneName = scene.name;
            if (!string.Equals(sceneName, pendingSceneName, StringComparison.Ordinal))
            {
                return;
            }

            if (!HoGStoragePlanner.RequiresHp(sceneName))
            {
                pendingSceneName = null;
                return;
            }

            int hp = self.hp;

            if (hp > 0)
            {
                if (string.Equals(scene.name, "GG_Flukemarm", StringComparison.Ordinal) && hp < 500)
                {
                    return;
                }

                if (string.Equals(scene.name, "GG_Mantis_Lords_V", StringComparison.Ordinal) && hp < 750)
                {
                    return;
                }

                pendingSceneName = null;
                BossHpDetected?.Invoke(sceneName, hp);
            }
        }
    }
}
