using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Features.Campaign.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Campaign.Runtime
{
    /// <summary>
    /// Writes the embedded campaign mission into the game's user mission directory.
    ///
    /// <para>One staged file write, once per shipped revision. Every failure path is a log
    /// line: a read-only profile, a missing payload or an unwritable directory must never
    /// take the rest of the mod down with it. A mission file the mod did not write is left
    /// exactly as it is.</para>
    /// </summary>
    internal static class CampaignMissionInstaller
    {
        public const string MissionName = "Boscali Summer";
        private const string ResourceName = "BoscaliSummer.Campaign.BoscaliSummerMission.json";
        private const string MarkerName = "boscali-summer.installed";

        public static void Install(ManualLogSource logger)
        {
            try
            {
                string profile = Application.persistentDataPath;
                if (string.IsNullOrEmpty(profile))
                {
                    logger.LogWarning("Campaign mission not installed: no persistent data path.");
                    return;
                }

                string folder = Path.Combine(profile, "Missions", MissionName);
                string missionPath = Path.Combine(folder, MissionName + ".json");
                string markerPath = Path.Combine(folder, MarkerName);
                MissionInstallAction action = MissionInstallPlan.Decide(
                    File.Exists(missionPath), File.Exists(markerPath), ReadRevision(markerPath));

                if (action == MissionInstallAction.Skip) return;
                if (action == MissionInstallAction.Foreign)
                {
                    logger.LogWarning("Campaign mission not installed: " + missionPath +
                                      " already exists and was not written by Boscali Summer.");
                    return;
                }

                byte[] payload = ReadPayload();
                if (payload == null || payload.Length == 0)
                {
                    logger.LogWarning("Campaign mission not installed: embedded payload missing.");
                    return;
                }

                Directory.CreateDirectory(folder);
                string staging = missionPath + ".tmp";
                File.WriteAllBytes(staging, payload);
                File.Copy(staging, missionPath, overwrite: true);
                File.Delete(staging);
                File.WriteAllText(markerPath, MissionInstallPlan.Revision.ToString());
                logger.LogInfo("Campaign mission " +
                               (action == MissionInstallAction.Install ? "installed" : "updated") +
                               " at revision " + MissionInstallPlan.Revision + ": " + missionPath);
            }
            catch (Exception e)
            {
                logger.LogWarning("Campaign mission install failed: " + e.GetType().Name + " — " + e.Message);
            }
        }

        private static int ReadRevision(string markerPath)
        {
            try
            {
                if (!File.Exists(markerPath)) return 0;
                return int.TryParse(File.ReadAllText(markerPath).Trim(), out int revision) ? revision : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static byte[] ReadPayload()
        {
            using (Stream stream = typeof(CampaignMissionInstaller).GetTypeInfo().Assembly
                       .GetManifestResourceStream(ResourceName))
            {
                if (stream == null) return null;
                var payload = new byte[stream.Length];
                int offset = 0;
                while (offset < payload.Length)
                {
                    int read = stream.Read(payload, offset, payload.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                return offset == payload.Length ? payload : null;
            }
        }
    }
}
