#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using Cysharp.Threading.Tasks;
using NOAvionics;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>Sandbox-only observations of real match preservation and native menu/rehost teardown.</summary>
    internal static class MissionDashboardLifecycleProbe
    {
        private static IMissionDashboardView checkpointRuntime;
        private static MissionDashboardStatus checkpoint;
        private static float checkpointTime;
        private static GameObject oldRoot;
        private static int oldOwnerId;
        private static MissionKey? missionKey;
        private static bool menuObserved, rehostComplete;
        private static string rehostError;

        private static void RequireSandbox()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                throw new InvalidOperationException("Dashboard probe requires a nomodkit scenario.");
        }

        private static int Roots(out GameObject root)
        {
            root = null;
            int count = 0;
            foreach (var ticker in Resources.FindObjectsOfTypeAll<AvTicker>())
                if (ticker.gameObject.name == "AvWindow mission-dashboard")
                {
                    root = ticker.gameObject;
                    count++;
                }
            return count;
        }

        public static object Checkpoint(Dictionary<string, object> args)
        {
            RequireSandbox();
            if (!ModuleServices.TryGet(out checkpointRuntime) || !checkpointRuntime.TryRead(out checkpoint))
                throw new InvalidOperationException("No live gamemode report.");
            checkpointTime = MissionTimeSource.Now();
            if (checkpoint.Name != "FUZZBALL" || checkpoint.Score0 < 1)
                throw new InvalidOperationException("Credit a real Fuzzball aircraft kill before the checkpoint.");
            return new Dictionary<string, object>
            {
                ["score0"] = checkpoint.Score0, ["score1"] = checkpoint.Score1,
                ["remaining"] = checkpoint.RemainingSeconds, ["missionTime"] = checkpointTime
            };
        }

        public static object CheckProfile(Dictionary<string, object> args)
        {
            RequireSandbox();
            ModuleServices.TryGet<IMissionDashboardView>(out var runtime);
            MissionDashboardStatus current = default;
            runtime?.TryRead(out current);
            float elapsed = MissionTimeSource.Now() - checkpointTime;
            float timerElapsed = checkpoint.RemainingSeconds - current.RemainingSeconds;
            return new Dictionary<string, object>
            {
                ["sameRuntime"] = runtime != null && runtime == checkpointRuntime,
                ["scorePreserved"] = current.Score0 == checkpoint.Score0 && current.Score1 == checkpoint.Score1,
                ["timerPreserved"] = current.Ready && current.Name == checkpoint.Name && elapsed >= 0f &&
                    timerElapsed >= -0.1f && System.Math.Abs(timerElapsed - elapsed) <= 2f,
                ["weatherAllowed"] = MissionProfile.AllowsModule("weather"),
                ["weatherRequiredOff"] = MissionProfile.RequiredOff("weather"),
                ["score0"] = current.Score0, ["score1"] = current.Score1,
                ["remaining"] = current.RemainingSeconds, ["elapsed"] = elapsed, ["timerElapsed"] = timerElapsed
            };
        }

        public static object RememberRoot(Dictionary<string, object> args)
        {
            RequireSandbox();
            if (Roots(out oldRoot) != 1 || oldRoot == null || !MissionPrompt.DashboardVisible)
                throw new InvalidOperationException("A single visible dashboard is required before native quit.");
            missionKey = MissionManager.CurrentMission?.LoadKey;
            if (!missionKey.HasValue) throw new InvalidOperationException("The live mission has no reloadable key.");
            oldOwnerId = oldRoot.GetInstanceID();
            menuObserved = rehostComplete = false;
            rehostError = null;
            return State(args);
        }

        public static bool MenuReady
        {
            get
            {
                bool ready = oldOwnerId != 0 && oldRoot == null && Roots(out _) == 0 &&
                    !MissionManager.IsRunning && GameManager.gameState == GameState.Menu &&
                    SceneManager.GetActiveScene().path == MapLoader.MainMenu;
                if (ready) menuObserved = true;
                return ready;
            }
        }

        public static object Rehost(Dictionary<string, object> args)
        {
            RequireSandbox();
            if (!MenuReady || !missionKey.HasValue)
                throw new InvalidOperationException("Observe the destroyed dashboard in the native main menu before rehosting.");
            RehostNative().Forget();
            return new Dictionary<string, object> { ["started"] = true };
        }

        private static async UniTask RehostNative()
        {
            try
            {
                // Let native HandleStopAsync finish after activating its main-menu scene.
                await UniTask.Yield();
                if (!MissionSaveLoad.TryLoad(missionKey.Value, out Mission mission, out string error))
                    throw new InvalidOperationException("Reload failed: " + error);
                MissionManager.SetMission(mission, checkIfSame: false);
                await NetworkManagerNuclearOption.i.StartHostAsync(
                    new HostOptions(SocketType.Offline, GameState.SinglePlayer, mission.MapKey));
                rehostComplete = true;
            }
            catch (Exception error) { rehostError = error.ToString(); }
        }

        public static bool RehostReady
        {
            get
            {
                if (rehostError != null) throw new InvalidOperationException(rehostError);
                return rehostComplete && MissionManager.IsRunning && MissionPrompt.DashboardVisible &&
                    Roots(out var root) == 1 && root != null && root.GetInstanceID() != oldOwnerId;
            }
        }

        public static object State(Dictionary<string, object> args)
        {
            int count = Roots(out var root);
            return new Dictionary<string, object>
            {
                ["oldOwnerId"] = oldOwnerId, ["ownerId"] = root != null ? root.GetInstanceID() : 0,
                ["oldDestroyed"] = oldOwnerId != 0 && oldRoot == null,
                ["newOwner"] = root != null && root.GetInstanceID() != oldOwnerId,
                ["rootCount"] = count, ["visible"] = MissionPrompt.DashboardVisible,
                ["menuObserved"] = menuObserved, ["rehostComplete"] = rehostComplete,
                ["gameState"] = GameManager.gameState.ToString(),
                ["scene"] = SceneManager.GetActiveScene().path,
                ["mission"] = MissionManager.CurrentMission?.Name ?? "",
                ["error"] = rehostError ?? ""
            };
        }
    }
}
#endif
