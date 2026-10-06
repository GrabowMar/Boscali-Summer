using System;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Lifecycle;
// Unity calls Awake, Update, FixedUpdate and OnDestroy by reflection.
#pragma warning disable IDE0051

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>A subsystem driven by <see cref="WingManager"/>. Services activate when a mission becomes
    /// playable and deactivate on leaving it, so no mission state survives into menus.</summary>
    internal interface IWingService
    {
        string Name { get; }
        void Activate();
        void Deactivate();
        void Tick(float dt);
        void FixedTick(float dt);
    }

    /// <summary>Persistent host that owns mission lifecycle and ticks services in registration order. A
    /// service that throws is disabled for the rest of the mission instead of breaking the others.</summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class WingManager : MonoBehaviour, ISceneService
    {
        private readonly List<IWingService> services = new List<IWingService>();
        private readonly HashSet<IWingService> faulted = new HashSet<IWingService>();
        private WingConfig settings;
        private bool active;
        private bool patchCheckDone;

        internal static WingManager Instance { get; private set; }

        internal static bool InPlayableState
        {
            get
            {
                GameState s = GameManager.gameState;
                return s == GameState.SinglePlayer || s == GameState.Multiplayer;
            }
        }

        internal void Configure(WingConfig wingSettings)
        {
            settings = wingSettings;
            if (settings != null)
                settings.VerboseLogging.SettingChanged += OnLoggingChanged;
        }

        internal void Register(IWingService service)
        {
            services.Add(service);
            if (active) Run(service, Phase.Activate, 0f);
        }

        /// <summary>Scene reset is the mission teardown half only: services deactivate (bounded by the
        /// registration list) and the next frame re-activates them when a mission is playable. Calling
        /// it twice, or before the first mission, is a no-op.</summary>
        public void ResetForScene()
        {
            faulted.Clear();
            if (active) EndMission();
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (settings != null)
                settings.VerboseLogging.SettingChanged -= OnLoggingChanged;
            settings = null;
            if (WingLog.Logger != null)
                WingLogExport.Stop();
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        private void OnLoggingChanged(object sender, EventArgs e)
        {
            WingLog.Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.VerboseLoggingChanged,
                settings != null && settings.VerboseLogging.Value ? 1 : 0));
        }

        private void Update()
        {
            // BoscaliMod applies the Wing patches after Install returns, so the manifest
            // gap-check runs on the first frame instead, once, and only warns.
            if (!patchCheckDone)
            {
                patchCheckDone = true;
                PatchManifest.Verify();
            }
            // The network is up before a mission is (the lobby): its hooks and hellos run regardless.
            try
            {
                WingNet.Tick(Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                WingNet.Fail(e);   // review M6c I2: a transport fault must not stop the AI's tick
            }
            bool playable = InPlayableState;
            if (playable != active)
            {
                if (playable) BeginMission();
                else EndMission();
            }
            if (!active) return;

            WingFrameGate.NoteFrame(Time.unscaledDeltaTime);
            float dt = Time.deltaTime;
            for (int i = 0; i < services.Count; i++) Run(services[i], Phase.Tick, dt);
        }

        private void FixedUpdate()
        {
            if (!active) return;
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < services.Count; i++) Run(services[i], Phase.FixedTick, dt);
        }

        private void BeginMission()
        {
            active = true;
            faulted.Clear();
            // Snapshot fidelity on mission entry; setting changes apply to the next mission.
            WingFidelity.Begin(WingSettings.Instance.Mode.Value);
            WingLog.Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.MissionStarted,
                WingFidelity.Mode == WingMode.Performance ? 1 : 0));
            for (int i = 0; i < services.Count; i++) Run(services[i], Phase.Activate, 0f);
        }

        private void EndMission()
        {
            for (int i = services.Count - 1; i >= 0; i--) Run(services[i], Phase.Deactivate, 0f);
            active = false;
            WingLog.Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.WingReset, 0));
        }

        private enum Phase { Activate, Deactivate, Tick, FixedTick }

        // Dispatch by phase rather than delegate so the per-frame path allocates nothing.
        private void Run(IWingService service, Phase phase, float dt)
        {
            if (faulted.Contains(service)) return;
            try
            {
                switch (phase)
                {
                    case Phase.Activate: service.Activate(); break;
                    case Phase.Deactivate: service.Deactivate(); break;
                    case Phase.Tick: service.Tick(dt); break;
                    case Phase.FixedTick: service.FixedTick(dt); break;
                }
            }
            catch (Exception e)
            {
                faulted.Add(service);
                WingLog.Logger.LogError($"[Runtime] service '{service.Name}' failed in {phase} and is disabled " +
                    $"until the next mission: {e}");
            }
        }
    }
}
