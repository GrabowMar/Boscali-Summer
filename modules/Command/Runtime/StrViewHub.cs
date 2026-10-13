using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using Cysharp.Threading.Tasks;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The one client-side producer of the Station Board: while the map is maximised it reads the services at 4 Hz (StrFeedsReader),
    /// builds the model (StrViewBuilder), and owns selection, hover, the layer switches and the single command path for the panel and
    /// the map overlay. It copies and sends; it never computes host state. A press, a select and a hover build at once, so a highlight
    /// is on the same frame, not 250 ms later.
    /// </summary>
    internal sealed class StrViewHub : MonoBehaviour, ISceneService, IStrHub
    {
        private const float TickSeconds = 0.25f;

        private CommandSettings settings;
        private StrUiState ui;
        private StrFeeds feeds;
        private readonly StrVerbs verbs;
        private float nextTick;
        private string lastFaction = "";
        private string lastSortieAnswer = "";

        /// <summary>The scene's hub, for the panel, the overlay and the automation hooks.</summary>
        internal static StrViewHub Active { get; private set; }

        /// <summary>Never null: Mode explains an empty model. A scene reset starts a new one, so a surface reads this each tick and keeps no copy.</summary>
        public StrViewModel Current { get; private set; } = new StrViewModel();
        public int Revision => Current.Revision;
        /// <summary>Interface state, a copy; written only through the methods below.</summary>
        public StrUiState Ui => ui;
        /// <summary>The last feeds the reader copied (the wing line of the tasked sortie, the automation read-back).</summary>
        internal ref readonly StrFeeds Feeds => ref feeds;

        /// <summary>Platform (P1..) and sortie (S1..) numbering, reset on scene reset and on a faction change.</summary>
        internal StrOrdinals Platforms { get; } = new StrOrdinals("P");
        internal StrOrdinals SortieNumbers { get; } = new StrOrdinals("S");

        /// <summary>Test seam: when set, feeds come from here instead of the services.</summary>
        internal Func<StrFeeds> FeedsSource;
        /// <summary>Test seam: when set, verbs go here instead of the services.</summary>
        internal IStrVerbs Verbs;

        // The reader's memory between ticks (it keeps nothing of its own): the stopwatch of a first report that has not come, the
        // 1 Hz refreshes, and the buffers the feeds point at, so a tick allocates nothing.
        internal float WaitingSince = -1f, NextSlowAt;
        internal int KnownAirDefence = -1, WingCount, TaskedSortieId = -1;
        internal readonly StrWingIn[] WingBuffer = new StrWingIn[StrViewModel.MaxSorties];
        internal readonly StrPostIn[] PostBuffer = new StrPostIn[StrViewModel.MaxPosts];
        internal readonly StrOfficerIn[] OfficerBuffer = new StrOfficerIn[2 * StrViewModel.MaxOfficers];
        internal AirDefenceRing[] Rings;
        // SUPPLY: the convoy list the reader copies, and when the host said each group and the next donation are allowed again (unscaled time, NaN
        // until it has answered).
        internal readonly StrConvoyIn[] ConvoyBuffer = new StrConvoyIn[StrViewModel.MaxConvoys];
        internal readonly float[] ConvoyReadyAt = new float[StrViewModel.MaxConvoys];
        internal int ConvoyCount;
        internal float DonateReadyAt = float.NaN;
        private const float DonateCooldownSeconds = 60f;   // Player.CmdDonateFactionFunds: one donation a minute
        private int supplyCursor, supplyEpoch;

        public StrViewHub()
        {
            verbs = new StrVerbs(this);
            ForgetSupply();
        }

        private void ForgetSupply()
        {
            supplyEpoch++;
            supplyCursor = ConvoyCount = 0;
            DonateReadyAt = float.NaN;
            for (int i = 0; i < ConvoyReadyAt.Length; i++) { ConvoyReadyAt[i] = float.NaN; ConvoyBuffer[i] = default; }
        }

        /// <summary>
        /// Asks the host for one cooldown per call (the reader calls at 1 Hz): the groups in turn, then the donation. These are the game's own requests
        /// (its menu sends the whole list every five seconds); one a second stays under their rate limit of two.
        /// </summary>
        // ponytail: asked whenever the map is maximised, not only while the SUPPLY tab shows, and a first sweep takes one second per group.
        internal void AskSupply(Player player, int count)
        {
            int slot = supplyCursor++ % (count + 1);
            AskSupplyAsync(player, slot < count ? slot : -1, supplyEpoch).Forget();
        }

        private async UniTaskVoid AskSupplyAsync(Player player, int slot, int epoch)
        {
            float left;
            try { left = slot < 0 ? await player.CmdGetDelayContribute() : await player.CmdGetDelaySpawnConvoy((byte)slot); }
            catch (Exception) { return; }   // the pilot left or the request was dropped: the cooldown keeps its last answer, or stays unknown
            if (this == null || epoch != supplyEpoch || float.IsNaN(left) || float.IsInfinity(left)) return;
            float at = Time.unscaledTime + Mathf.Max(0f, left);
            if (slot < 0) DonateReadyAt = at;
            else ConvoyReadyAt[slot] = at;
        }

        /// <summary>A purchase was just sent: the group's own cooldown starts now, ahead of the host's next answer.</summary>
        internal void NoteBought(int index, float coolDown)
        {
            if (index >= 0 && index < ConvoyReadyAt.Length) ConvoyReadyAt[index] = Time.unscaledTime + Mathf.Max(0f, coolDown);
        }

        internal void NoteDonated() => DonateReadyAt = Time.unscaledTime + DonateCooldownSeconds;

        internal void Configure(CommandSettings commandSettings)
        {
            settings = commandSettings;
            LayersFromSettings();
        }

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            ResetForScene();
            if (Active == this) Active = null;
        }

        private void OnDestroy() => OnDisable();

        public void ResetForScene()
        {
            Platforms.Reset();
            SortieNumbers.Reset();
            Current = new StrViewModel();
            ui = default;
            LayersFromSettings();
            feeds = default;
            WaitingSince = -1f; NextSlowAt = 0f; KnownAirDefence = -1; WingCount = 0; TaskedSortieId = -1;
            ForgetSupply();
            nextTick = 0f; lastFaction = ""; lastSortieAnswer = "";
        }

        private void LayersFromSettings()
        {
            ui.ShowFronts = settings == null || settings.StrOverlayFronts.Value;
            ui.ShowOperation = settings == null || settings.StrOverlayOperation.Value;
            ui.ShowSorties = settings == null || settings.StrOverlaySorties.Value;
        }

        private void Update()
        {
            if (!MissionProfile.AllowsModule("strategic-command")) return;
            if (!DynamicMap.mapMaximized || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickSeconds;
            TickNow();
        }

        /// <summary>Builds now. Selection, hover and every press call it.</summary>
        internal void TickNow()
        {
            if (settings != null && !MissionProfile.AllowsModule("strategic-command")) return;
            float now = Time.unscaledTime;
            if (FeedsSource != null) feeds = FeedsSource();
            else StrFeedsReader.Read(ref feeds, this, now);
            NoteFaction(feeds.FactionCode);

            StrViewBuilder.Build(Current, in feeds, in ui, Platforms, SortieNumbers, now);
            if (!Current.SelectionVanished) return;
            // A selected or hovered row left the board: drop our copy and build once more so no stale highlight is drawn.
            ui.Selected = Current.SelectedId;
            ui.Hover = Current.HoverId;
            StrViewBuilder.Build(Current, in feeds, in ui, Platforms, SortieNumbers, now);
        }

        // A different faction is a different war: its platform and sortie numbers start again. A flap through "no faction" renumbers nothing.
        private void NoteFaction(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return;
            if (lastFaction.Length > 0 && !string.Equals(lastFaction, faction, StringComparison.Ordinal))
            {
                Platforms.Reset();
                SortieNumbers.Reset();
                ui.Selected = ui.Hover = StrItemId.None;
            }
            lastFaction = faction;
        }

        /// <summary>The sortie view's answer to an accept or cancel, shown for six seconds when it is new and not empty (called by the reader).</summary>
        internal void NoteSortieAnswer(string answer, float now)
        {
            if (string.IsNullOrEmpty(answer) || string.Equals(answer, lastSortieAnswer, StringComparison.Ordinal)) return;
            lastSortieAnswer = answer;
            ui.Notice = answer;
            ui.NoticeUntil = now + StrActions.NoticeSeconds;
        }

        /// <summary>focusMap also flies the map to the item (MapFocus.To(x, z, true)); an item with no known place flies nowhere.</summary>
        public void Select(StrItemId id, bool focusMap)
        {
            ui.Selected = id;
            if (focusMap && StrAnchors.TryGet(Current, id, out float x, out float z)) MapFocus.To(x, z, true);
            TickNow();
        }

        public void Hover(StrItemId id)
        {
            ui.Hover = id;
            TickNow();
        }

        public void Unhover(StrItemId id)
        {
            if (ui.Hover != id) return;
            ui.Hover = StrItemId.None;
            TickNow();
        }

        public void ClearSelection()
        {
            ui.Selected = StrItemId.None;
            TickNow();
        }

        /// <summary>StrActions.Run through the verbs, then TickNow.</summary>
        public StrAct Run(StrActionKind kind, StrItemId id, int arg = 0)
        {
            if (settings != null && !MissionProfile.AllowsModule("strategic-command"))
                return new StrAct(StrWhy.Forbidden, "STR IS SWITCHED OFF FOR THIS MISSION");
            StrAct act = StrActions.Run(kind, id, arg, Current, Verbs ?? verbs, ref ui, Time.unscaledTime);
            TickNow();
            return act;
        }

        /// <summary>Writes the layer into the interface state and the client config.</summary>
        public void SetLayer(StrLayers layer, bool on)
        {
            if ((layer & StrLayers.Fronts) != 0) { ui.ShowFronts = on; if (settings != null) settings.StrOverlayFronts.Value = on; }
            if ((layer & StrLayers.Operation) != 0) { ui.ShowOperation = on; if (settings != null) settings.StrOverlayOperation.Value = on; }
            if ((layer & StrLayers.Sorties) != 0) { ui.ShowSorties = on; if (settings != null) settings.StrOverlaySorties.Value = on; }
            TickNow();
        }
    }
}
