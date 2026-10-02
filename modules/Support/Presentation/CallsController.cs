using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;
using BepInEx.Configuration;
using NOAvionics;
using NuclearOption.MissionEditorScripts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// Client CALL flow (core §5.3): press arms, a second press fires at the own designation (POD) or the last map pick;
    /// right-click on the map while armed also fires. Every outcome ends in words. Never spends: the host does.
    /// </summary>
    internal sealed class CallsController : MonoBehaviour, ISceneService
    {
        private static readonly SupportActionId[] Defaults =
            { SupportActionId.Recon, SupportActionId.Prsm, SupportActionId.JtacMark, SupportActionId.Cruise };

        private readonly ArmState arm = new ArmState();
        private readonly CallRequestTracker request = new CallRequestTracker();
        private SupportManager manager;
        private SupportSettings settings;
        private IObservationSource observations;
        private GlobalPosition? mapAim;
        public float LastWordsAt { get; private set; }

        public SupportActionId?[] Favourites { get; } = new SupportActionId?[4];
        public SupportActionId? Armed => arm.Armed;
        public AimSource AimNow { get; private set; }
        public string LastWords { get; private set; } = "";
        public bool Pending => request.Pending;

        public void Configure(SupportManager manager, SupportSettings settings, IObservationSource observations)
        {
            this.manager = manager;
            this.settings = settings;
            this.observations = observations;
            for (int i = 0; i < Favourites.Length; i++) Favourites[i] = Defaults[i];
        }

        public void ResetForScene()
        {
            arm.Clear();
            request.Clear();
            mapAim = null;
            LastWords = "";
            LastWordsAt = -100f;
        }

        /// <summary>The shared pre-flight: busy, unlocked, thawed, off cooldown, affordable. Says why and returns false.</summary>
        private bool Check(SupportActionId id)
        {
            if (request.Pending || manager.RequestPending) { Say(CallWords.Refusal(CallRefusal.Busy), AvUiCue.Caution); return false; }
            if (!manager.Unlocked(id, out string unlock)) { Say(CallWords.Refusal(CallRefusal.Locked, unlock: unlock), AvUiCue.Caution); return false; }
            if (manager.LocalFrozenSeconds > 0) { Say(CallWords.Refusal(CallRefusal.Frozen, seconds: manager.LocalFrozenSeconds), AvUiCue.Caution); return false; }
            if (manager.LocalCooldownRemaining > 0.5f)
            {
                Say(CallWords.Refusal(CallRefusal.Cooldown, seconds: Mathf.CeilToInt(manager.LocalCooldownRemaining)), AvUiCue.Caution);
                return false;
            }
            int cost = manager.Quote(id).Cost;
            if (manager.LocalCredit + 0.001f < cost) { Say(CallWords.Refusal(CallRefusal.LowCredit, need: cost), AvUiCue.Caution); return false; }
            return true;
        }

        public void Press(SupportActionId id)
        {
            if (manager == null) return;
            float now = Time.unscaledTime;
            if (!Check(id)) return;

            if (arm.Press(id, now) == ArmStep.Armed)
            {
                mapAim = null;
                bool mapOk = manager.ArmLocalPick(Label(id), point =>
                {
                    mapAim = point;
                    if (arm.Armed == id && Check(id)) Fire(id, point, Time.unscaledTime); // right-click while armed fires at once
                });
                Say("ARMED · " + Label(id) + (mapOk ? " · PRESS AGAIN OR RIGHT-CLICK MAP" : " · PRESS AGAIN TO FIRE (MAP BUSY)"), AvUiCue.Engage);
                return;
            }
            if (TryPod(out GlobalPosition pod)) { Fire(id, pod, now); return; }
            if (mapAim.HasValue) { Fire(id, mapAim.Value, now); return; }
            Say(CallWords.Refusal(CallRefusal.NoAim), AvUiCue.Caution);
        }

        /// <summary>Host answer: called by SupportManager.ReceiveResult with the request id and the result words.</summary>
        internal void Answer(int requestId, bool accepted, string words)
        {
            if (!request.Resolve(requestId)) return; // late answer after the local timeout: the credit message is the truth
            Say(accepted ? "SHOT · " + words : words, accepted ? AvUiCue.Confirm : AvUiCue.Caution);
        }

        private void Fire(SupportActionId id, GlobalPosition target, float now)
        {
            int requestId = manager.RequestAt(id, target);
            if (requestId <= 0) { Say(CallWords.Refusal(CallRefusal.Unavailable), AvUiCue.Caution); return; } // arm stays
            arm.Clear();
            manager.Disarm();
            if (!manager.RequestPending) { Say(CallWords.Refusal(CallRefusal.Offline), AvUiCue.Caution); return; }
            request.Begin(requestId, manager.Quote(id).Cost, now);
            AimNow = AimSource.None;
            Say("PENDING · " + Label(id), AvUiCue.Press);
        }

        private void Update()
        {
            if (manager == null) return;
            float now = Time.unscaledTime;
            if (arm.Tick(now)) { manager.Disarm(); Say("DISARMED", AvUiCue.Release); }
            if (request.Tick(now, out _)) { manager.AbandonPending(); Say(CallWords.Refusal(CallRefusal.Timeout), AvUiCue.Caution); }
            AimNow = arm.Armed == null ? AimSource.None : Aim.Pick(TryPod(out _), mapAim.HasValue);

            if (GameplayUI.GameIsPaused || InputFieldChecker.InsideInputField || !Application.isFocused) return;
            Poll(settings.CallKey1, 0);
            Poll(settings.CallKey2, 1);
            Poll(settings.CallKey3, 2);
            Poll(settings.CallKey4, 3);
        }

        private void Poll(ConfigEntry<KeyboardShortcut> key, int slot)
        {
            if (key == null || key.Value.MainKey == KeyCode.None || !key.Value.IsDown()) return;
            if (Favourites[slot].HasValue) Press(Favourites[slot].Value);
        }

        private bool TryPod(out GlobalPosition point)
        {
            point = default;
            if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled
                && aircraft.weaponManager != null)
            {
                List<Unit> targets = aircraft.weaponManager.GetTargetList();
                if (targets != null && targets.Count > 0 && targets[0] != null && !targets[0].disabled)
                {
                    point = targets[0].GlobalPosition();
                    return true;
                }
            }
            if (observations != null && observations.TryGet(out ObservationPoint mark))
            {
                point = new GlobalPosition(mark.X, mark.Y, mark.Z);
                return true;
            }
            return false;
        }

        private void Say(string words, AvUiCue cue)
        {
            LastWords = words ?? "";
            LastWordsAt = Time.unscaledTime;
            AvUiSound.Play(cue);
        }

        private static string Label(SupportActionId id) => CallSheet.TryGet(id, out CallRow row) ? row.Label : id.ToString();
    }
}
