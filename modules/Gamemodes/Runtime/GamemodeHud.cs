using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Gamemodes.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>Match changes use the common HUD's transient notice line; ongoing status lives in the HUD and MIS.</summary>
    internal sealed class GamemodeHud : MonoBehaviour, ISceneService
    {
        private GamemodeRuntime runtime;
        private float nextRefresh;
        private string lastNotice;

        public void Configure(GamemodeRuntime owner) => runtime = owner;

        public void ResetForScene() { lastNotice = null; nextRefresh = 0f; }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            if (runtime == null || Application.isBatchMode || !runtime.TryRead(out MissionDashboardStatus state))
            {
                lastNotice = null;
                return;
            }
            if (!state.Ready || !ModuleServices.TryGet<IHudBoard>(out var board)) return;
            string score = state.HasScores ? "BDF " + state.Score0 + " : " + state.Score1 + " PALA" : state.Detail;
            // The live countdown never refreshes a notice's dwell. A phase, score, cargo or route change speaks once.
            string detail = score + (state.HasScores && !string.IsNullOrEmpty(state.Detail) ? " / " + state.Detail : "") +
                (string.IsNullOrEmpty(state.Route) ? "" : " / " + state.Route);
            string notice = state.Name + " / " + state.Phase + " / " + detail;
            if (notice == lastNotice) return;
            board.Notice("mission", runtime.Current.Phase == GamemodePhase.Intermission ? HudTone.Caution : HudTone.Info,
                state.Name + " / " + state.Phase, detail);
            lastNotice = notice;
        }
    }
}
