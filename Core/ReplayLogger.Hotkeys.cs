using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using On;
using UnityEngine;

namespace ReplayLogger
{
    public partial class ReplayLogger
    {
        private static bool hotkeysInitialized;
        private static readonly HashSet<string> F7BlockedScenes = new(StringComparer.Ordinal)
        {
            "GG_Gruz_Mother",
            "GG_Gruz_Mother_V",
            "GG_Vengefly",
            "GG_Vengefly_V",
            "GG_Brooding_Mawlek",
            "GG_Brooding_Mawlek_V",
            "GG_False_Knight",
            "GG_Failed_Champion",
            "GG_Hornet_1",
            "GG_Hornet_2",
            "GG_Mega_Moss_Charger",
            "GG_Flukemarm",
            "GG_Mantis_Lords",
            "GG_Mantis_Lords_V",
            "GG_Oblobbles",
            "GG_Hive_Knight",
            "GG_Broken_Vessel",
            "GG_Lost_Kin",
            "GG_Nosk",
            "GG_Nosk_V",
            "GG_Nosk_Hornet",
            "GG_Collector",
            "GG_Collector_V",
            "GG_God_Tamer",
            "GG_Crystal_Guardian",
            "GG_Crystal_Guardian_2",
            "GG_Uumuu",
            "GG_Uumuu_V",
            "GG_Traitor_Lord",
            "GG_Grey_Prince_Zote",
            "GG_Mage_Knight",
            "GG_Mage_Knight_V",
            "GG_Soul_Master",
            "GG_Soul_Tyrant",
            "GG_Dung_Defender",
            "GG_White_Defender",
            "GG_Watcher_Knights",
            "GG_Ghost_No_Eyes",
            "GG_Ghost_No_Eyes_V",
            "GG_Ghost_Marmu",
            "GG_Ghost_Marmu_V",
            "GG_Ghost_Xero",
            "GG_Ghost_Xero_V",
            "GG_Ghost_Markoth",
            "GG_Ghost_Markoth_V",
            "GG_Ghost_Galien",
            "GG_Ghost_Gorb",
            "GG_Ghost_Gorb_V",
            "GG_Ghost_Hu",
            "GG_Nailmasters",
            "GG_Painter",
            "GG_Sly",
            "GG_Hollow_Knight",
            "GG_Grimm",
            "GG_Grimm_Nightmare",
            "GG_Radiance"
        };

        private static void InitializeHotkeys()
        {
            if (hotkeysInitialized)
            {
                return;
            }

            On.HeroController.Update += HeroController_Update_OpenFolder;
            hotkeysInitialized = true;
        }

        private static void HeroController_Update_OpenFolder(On.HeroController.orig_Update orig, HeroController self)
        {
            orig(self);
            if (!IsRebindInProgress)
            {
                KeyCode showKey = GetShowLastSavedLogKey();
                if (showKey != KeyCode.None && Input.GetKeyDown(showKey))
                {
                    SavedLogToast.Show(GetHudToastSeconds());
                }

                KeyCode copyKey = GetCopyLastSavedLogKey();
                if (copyKey != KeyCode.None && Input.GetKeyDown(copyKey))
                {
                    CopyLastSavedLogToDesktop();
                }

                KeyCode openKey = GetOpenReplayLoggerFolderKey();
                if (openKey != KeyCode.None && Input.GetKeyDown(openKey) && CanUseHotkey())
                {
                    Instance?.OpenModsFolder();
                }
            }
        }

        private static bool CanUseHotkey()
        {
            if (GameManager.instance == null) return true;
            string scene = GameManager.instance.sceneName;
            if (string.IsNullOrEmpty(scene)) return true;
            return !F7BlockedScenes.Contains(scene);
        }

        private void OpenModsFolder()
        {
            string replayLoggerDir = string.IsNullOrEmpty(modsDir)
                ? string.Empty
                : Path.Combine(modsDir, "ReplayLogger");

            if (string.IsNullOrEmpty(replayLoggerDir) || !Directory.Exists(replayLoggerDir))
            {
                Modding.Logger.LogWarn("ReplayLogger: cannot open ReplayLogger folder (path missing).");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = replayLoggerDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception e)
            {
                Modding.Logger.LogError($"ReplayLogger: failed to open mods folder: {e.Message}");
            }
        }

        private static void CopyLastSavedLogToDesktop()
        {
            if (!SavedLogTracker.TryGet(out SavedLogInfo info))
            {
                return;
            }

            string sourcePath = info.SourcePath;
            if (!File.Exists(sourcePath))
            {
                return;
            }

            string copyRoot = GetCopyLogsRootPath();
            if (string.IsNullOrWhiteSpace(copyRoot))
            {
                return;
            }

            string rootFolder = string.IsNullOrWhiteSpace(info.RootFolder) ? "Other" : info.RootFolder;
            string bossFolder = string.IsNullOrWhiteSpace(info.BossFolder) ? "Unknown" : info.BossFolder;
            string difficultyFolder = string.IsNullOrWhiteSpace(info.DifficultyFolder) ? "None" : info.DifficultyFolder;
            string targetDir = Path.Combine(copyRoot, rootFolder, bossFolder, difficultyFolder);
            string targetPath = Path.Combine(targetDir, Path.GetFileName(sourcePath));

            try
            {
                Directory.CreateDirectory(targetDir);
                File.Copy(sourcePath, targetPath, true);
            }
            catch
            {
                return;
            }
        }
    }
}
