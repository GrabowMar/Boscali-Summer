using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime.Actions;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Host-owned constellation grounded in actual, killable native sites.</summary>
    internal sealed class SpaceService : MonoBehaviour, ISceneService
    {
        private const int MaximumFactions = 8, MaximumBases = 16, MaximumScanUnits = 4096;
        private sealed class FactionSpace
        {
            public SpaceState State;
            /// <summary>One entry per pad slot: the raised site (or the carrier hosting the pad), null while the slot was never built.</summary>
            public Unit[] Links;
            public float[] Baseline;
            /// <summary>Where each site was raised, in world metres: a destroyed site keeps its place on the ORBIT room.</summary>
            public float[] SiteX, SiteZ;
            public IReadOnlyList<Unit> View;
            public SpaceObservations Observations;
            public TaskedDesk Tasked;
            /// <summary>Callsigns of the humans whose posts this faction shows, so a poll does not look every one up again.</summary>
            public readonly Dictionary<ulong, string> Labels = new Dictionary<ulong, string>();
        }
        private struct IntentEntry { public string Words; public float NextAt; }
        private readonly struct Candidate
        {
            public readonly GlobalPosition Anchor;
            public readonly Airbase Parent;
            public readonly float RearScore;
            public Candidate(GlobalPosition anchor, Airbase parent, float score) { Anchor = anchor; Parent = parent; RearScore = score; }
        }

        private readonly Dictionary<FactionHQ, FactionSpace> factions = new Dictionary<FactionHQ, FactionSpace>();
        /// <summary>Scratch for pad placement: the held land airbases and carriers, and the Airbase or Ship each one stands for.</summary>
        private readonly List<FrontPlacement.Base> heldBases = new List<FrontPlacement.Base>(MaximumBases);
        private readonly List<Component> heldSites = new List<Component>(MaximumBases);
        private readonly UplinkSpawner spawner = new UplinkSpawner();
        private readonly SpaceWatchOfficer watch = new SpaceWatchOfficer();
        private readonly Dictionary<FactionHQ, IntentEntry> intents = new Dictionary<FactionHQ, IntentEntry>();
        private SupportManager manager;
        private PhasedGate tickGate;
        private float nextCleanup;
        private float nextTaskedWarning;
        private bool coarse; // mirror gate reads use the 1 Hz world state instead of re-sampling natives per viewer
        public int Generation { get; private set; } = 1;
        internal UplinkSpawner Spawner => spawner;

        public void Configure(SupportManager support)
        {
            manager = support;
            watch.Configure(this, support);
        }

        public void ResetForScene()
        {
            foreach (var pair in factions)
            {
                pair.Value.Tasked?.Retire(); // returns in-flight escrow and makes every old launch callback inert
                pair.Value.State.Clear();
                pair.Value.Observations?.Dispose();
            }
            Generation = Generation == int.MaxValue ? 1 : Generation + 1;
            manager?.SpaceNet?.ResetForScene(); // old receipts and every faction subscription end with the scene
            watch.ResetForScene();
            intents.Clear();
            factions.Clear();
            tickGate.Reset();
            spawner.ResetForScene();
        }
        private void OnDestroy() => ResetForScene();

        private readonly List<SpaceMark> snapMarks = new List<SpaceMark>(SpaceContacts.MaxMarks);

        /// <summary>Host: the nearest fresh own-faction MARK within <see cref="SpaceFireControl.MarkSnapRadius"/> of a perk's aim point.</summary>
        internal bool TryNearestMark(FactionHQ owner, float x, float z, float now, out SpaceMark mark)
        {
            mark = default;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction) || faction.Observations == null) return false;
            snapMarks.Clear();
            faction.Observations.Contacts.CopyMarks(now, snapMarks);
            return SpaceFireControl.TryNearestMark(snapMarks, x, z, now, out mark);
        }

        /// <summary>Authoritative read for host decisions: re-samples the natives first, so a gate sees native loss immediately.</summary>
        public bool TryGetState(FactionHQ owner, out SpaceState state)
        {
            state = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return false;
            if (!coarse) Refresh(owner, faction, SupportManager.MissionNow());
            state = faction.State;
            return true;
        }

        /// <summary>
        /// Display read (quotes, panels, the sky): the 1 Hz world state the host tick keeps current, never a re-sample. Safe to call
        /// every frame; a native loss shows within about a second.
        /// </summary>
        public bool TryGetStateCoarse(FactionHQ owner, out SpaceState state)
        {
            state = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return false;
            state = faction.State;
            return true;
        }

        internal IReadOnlyList<Unit> UplinksFor(FactionHQ owner) =>
            owner != null && factions.TryGetValue(owner, out FactionSpace faction) ? faction.View : Array.Empty<Unit>();

        private static void Locate(FactionSpace faction, int slot)
        {
            Unit site = faction.Links[slot];
            if (site == null) return;
            GlobalPosition at = site.transform.position.ToGlobalPosition();
            faction.SiteX[slot] = (float)at.x; faction.SiteZ[slot] = (float)at.z;
        }

        /// <summary>
        /// Host: the faction's own uplink sites for the OPERATIONS mirror, read from the 1 Hz state the tick keeps (never a re-sample): the place each was raised, and whether it stands, is
        /// destroyed, or is destroyed with the LAUNCH PAD programme queued or building. A slot never built is not listed. Enemy sites are never listed here (a revealed one is a CYBER node).
        /// </summary>
        internal void CopySites(FactionHQ owner, List<OpsSiteRow> into)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return;
            // A queued pad goes to an empty slot first: only with every slot built does it replace the first destroyed one.
            bool rebuilding = faction.State.UplinkCount == faction.Links.Length &&
                manager != null && manager.Fronts != null && manager.Fronts.HasProgramme(owner, ProgrammeId.UplinkSite);
            for (int i = 0; i < faction.Links.Length && into.Count < OpsWire.MaxSites; i++)
            {
                if (!faction.State.PadPresent(i)) continue;
                bool down = faction.State.UplinkDown(i);
                into.Add(new OpsSiteRow { Slot = (byte)i, State = !down ? SiteState.Up : rebuilding ? SiteState.Rebuilding : SiteState.Down, X = faction.SiteX[i], Z = faction.SiteZ[i] });
                if (down) rebuilding = false; // one programme raises one pad: the first destroyed one (TryBuildPad); a second destroyed pad is only destroyed
            }
        }

        // ---- OPS FRONTS programme effects (host) ----------------------------------------------------

        /// <summary>
        /// Host: can this kind go up now? The ladder's LAUNCH rule on the live pads and birds (not what is queued): the kind is not up, a pad stands and links
        /// one more bird. Unavailable for a faction without a SPACE or a kind out of range.
        /// </summary>
        internal FrontOutcome CheckLaunch(FactionHQ owner, BirdKind kind)
        {
            if ((byte)kind >= SpaceRules.BirdCount || !TryGetState(owner, out SpaceState s)) return FrontOutcome.Unavailable;
            var live = new FrontWorld { PadsLive = s.LiveUplinkCount, BirdsUp = (byte)(~s.DownMask & ((1 << SpaceRules.BirdCount) - 1)) };
            return FrontLadder.Check(Front.Space, ProgrammeId.LaunchSatellite, (byte)kind, live, default);
        }

        /// <summary>LAUNCH SATELLITE finished: that kind is on station at its home point with a full tank. False (nothing changed) when <see cref="CheckLaunch"/> refuses.</summary>
        internal bool TryLaunchBird(FactionHQ owner, BirdKind kind)
        {
            if (CheckLaunch(owner, kind) != FrontOutcome.None || !factions[owner].State.Launch(kind)) return false;
            Plugin.Logger?.LogInfo("[Support.Space] " + owner.name + " " + kind + " bird launched and on station.");
            return true;
        }

        /// <summary>
        /// Host: can a pad be built (at the picked point when <paramref name="hasPoint"/>)? A slot must be free (never built or destroyed); a picked point must lie
        /// within <see cref="FrontRules.PadPickMetres"/> of a held airbase or own carrier. Without a point anything the faction holds or fields will do: <see cref="RearSite"/>
        /// tries held airbases, then a carrier, then ground near an own unit's start (spec 4.5 fallback), and a completion that finds no spot fails NO SPOT and backs off.
        /// Spawns nothing; reads only the pads' health (no observation tick).
        /// </summary>
        internal FrontOutcome CheckPad(FactionHQ owner, bool hasPoint, float x, float z)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return FrontOutcome.Unavailable;
            RefreshPads(owner, faction, SupportManager.MissionNow());
            if (FreeSlot(faction) < 0) return FrontOutcome.UplinksMax;
            if (!hasPoint) return AnyFoothold(owner) ? FrontOutcome.None : FrontOutcome.NoSite;
            CollectHeld(owner, faction);
            return FrontPlacement.NearestHeld(heldBases, x, z, FrontRules.PadPickMetres, allowCarrier: true) >= 0 ? FrontOutcome.None : FrontOutcome.NotHeld;
        }

        /// <summary>A held land airbase or any live own unit (a carrier, or a start position <see cref="RearSite"/> can build near). Stops at the first.</summary>
        private static bool AnyFoothold(FactionHQ owner)
        {
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
                    if (a != null && !a.disabled && !a.AttachedAirbase && a.CurrentHQ == owner && a.center != null) return true;
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; units != null && i < units.Count && i < MaximumScanUnits; i++)
                if (units[i] != null && !units[i].disabled && units[i].NetworkHQ == owner) return true;
            return false;
        }

        /// <summary>
        /// LAUNCH PAD finished: a real site is raised at the picked airbase (any legal ground around it, rear or not), or the picked carrier itself hosts the pad.
        /// Without a point: the deepest rear spot around the held airbases, else a carrier, else ground near a unit's start. The pad takes the first slot never
        /// built, else the first destroyed one (whose wreck the spawner removes). False with the refusal in <paramref name="why"/>.
        /// </summary>
        internal bool TryBuildPad(FactionHQ owner, bool hasPoint, float x, float z, out FrontOutcome why)
        {
            why = CheckPad(owner, hasPoint, x, z);
            if (why != FrontOutcome.None) return false;
            FactionSpace faction = factions[owner];
            int slot = FreeSlot(faction);
            spawner.DiscardGroup(faction.Links[slot]); // only a group the spawner raised; a native carrier link is never destroyed
            float diagonal = TheaterFrame.Resolve().magnitude;
            Unit made = null;
            if (hasPoint)
            {
                Component site = heldSites[FrontPlacement.NearestHeld(heldBases, x, z, FrontRules.PadPickMetres, allowCarrier: true)];
                if (site is Ship carrier) made = carrier;
                else if (site is Airbase airbase && TryAround(owner, airbase.center.position, airbase, diagonal, out Candidate spot, requireRear: false))
                    spawner.TryCreate(owner, slot, spot.Anchor, spot.Parent, out made);
            }
            else
            {
                CollectHeld(owner, faction); // CheckPad fills the scratch only for a picked point
                made = RearSite(owner, slot, diagonal);
            }
            if (made == null) { why = FrontOutcome.NoSpot; return false; }
            faction.Links[slot] = made;
            faction.Baseline[slot] = UplinkSpawner.Health(made);
            Locate(faction, slot);
            faction.State.PlacePad(slot);
            Plugin.Logger?.LogInfo("[Support.Space] " + owner.name + " PAD-" + (slot + 1) + (made is Ship ? " built aboard " + made.name
                : " built at " + Mathf.RoundToInt(faction.SiteX[slot]) + ", " + Mathf.RoundToInt(faction.SiteZ[slot])) + ".");
            return true;
        }

        /// <summary>The slot a new pad takes: the first never built, else the first destroyed; -1 when both stand.</summary>
        private static int FreeSlot(FactionSpace faction)
        {
            for (int i = 0; i < faction.Links.Length; i++) if (!faction.State.PadPresent(i)) return i;
            for (int i = 0; i < faction.Links.Length; i++) if (faction.State.UplinkDown(i)) return i;
            return -1;
        }

        /// <summary>Fills the held-base scratch: held land airbases, then own carriers afloat (ruling R6: a carrier is a pad host) that are not already a standing pad.</summary>
        private void CollectHeld(FactionHQ owner, FactionSpace faction)
        {
            heldBases.Clear(); heldSites.Clear();
            if (FactionRegistry.airbaseLookup != null)
            {
                int inspected = 0;
                foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
                {
                    if (++inspected > 128 || heldSites.Count >= MaximumBases) break;
                    if (a == null || a.disabled || a.AttachedAirbase || a.CurrentHQ != owner || a.center == null) continue;
                    GlobalPosition p = a.center.position.ToGlobalPosition();
                    heldBases.Add(new FrontPlacement.Base(heldSites.Count, (float)p.x, (float)p.z, true, false, true));
                    heldSites.Add(a);
                }
            }
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; units != null && i < units.Count && i < MaximumScanUnits && heldSites.Count < MaximumBases; i++)
            {
                if (!(units[i] is Ship ship) || ship.disabled || ship.NetworkHQ != owner || ship.GetAirbase() == null ||
                    UplinkSpawner.Down(ship, owner) || Array.IndexOf(faction.Links, ship) >= 0) continue;
                GlobalPosition p = ship.transform.position.ToGlobalPosition();
                heldBases.Add(new FrontPlacement.Base(heldSites.Count, (float)p.x, (float)p.z, true, true, false));
                heldSites.Add(ship);
            }
        }

        /// <summary>
        /// No picked point: one site on the deepest rear spot found around the held airbases (one legal spot per base), else the first own carrier, else ground near
        /// one of the faction's unit start positions. Null when none takes. Reads the scratch <see cref="CollectHeld"/> just filled.
        /// </summary>
        // ponytail: one synchronous search (up to 16 bases x 36 ring points) at programme completion; step it across frames if a hitch shows.
        private Unit RearSite(FactionHQ owner, int slot, float diagonal)
        {
            var legal = new List<Candidate>(MaximumBases);
            for (int i = 0; i < heldSites.Count; i++)
                if (heldSites[i] is Airbase airbase && TryAround(owner, airbase.center.position, airbase, diagonal, out Candidate spot)) legal.Add(spot);
            while (legal.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < legal.Count; i++) if (legal[i].RearScore > legal[best].RearScore) best = i;
                if (spawner.TryCreate(owner, slot, legal[best].Anchor, legal[best].Parent, out Unit made)) return made;
                legal.RemoveAt(best);
            }
            for (int i = 0; i < heldSites.Count; i++) if (heldSites[i] is Ship carrier) return carrier;
            List<Unit> units = UnitRegistry.allUnits;
            // Native stored spawn origins are only candidates; terrain/mission/ownership rules still apply.
            for (int i = 0, origins = 0; units != null && i < units.Count && i < MaximumScanUnits && origins < 16; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled || unit.NetworkHQ != owner) continue;
                origins++;
                Vector3 origin = unit.startPosition.ToLocalPosition();
                if (unit is Aircraft) origin.y = Datum.LocalSeaY + 100f;
                if (GroundPlacement.DryGround(origin, out _) && TryAround(owner, origin, null, diagonal, out Candidate field) &&
                    spawner.TryCreate(owner, slot, field.Anchor, field.Parent, out Unit made)) return made;
            }
            return null;
        }

        /// <summary>The parts WATCH OFFICER OVERLORD works one faction through. False until the faction has a SPACE.</summary>
        internal bool TryWatchParts(FactionHQ owner, out SpaceState state, out SpaceObservations observations, out TaskedDesk desk)
        {
            state = null; observations = null; desk = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return false;
            state = faction.State; observations = faction.Observations; desk = faction.Tasked;
            return state != null && observations != null && desk != null;
        }

        /// <summary>Fills the (cleared) list with every faction that has a SPACE. Bounded by <see cref="MaximumFactions"/>.</summary>
        internal void CopyFactions(List<FactionHQ> into)
        {
            into.Clear();
            foreach (var pair in factions) if (into.Count < MaximumFactions) into.Add(pair.Key);
        }

        /// <summary>
        /// The faction's ENEMY INTENT words: the name of the main effort the enemy's director has chosen, read from the host through a
        /// Core contract, or UNKNOWN. Never a coordinate or a unit. Refreshed every five wall seconds, not per viewer poll.
        /// </summary>
        internal string IntentFor(FactionHQ owner)
        {
            if (owner == null || !GameAccess.IsServer()) return IntentWords.Unknown;
            float t = Time.unscaledTime;
            if (intents.TryGetValue(owner, out IntentEntry known) && t < known.NextAt) return known.Words;
            string words = ReadIntent(owner);
            if (intents.Count < MaximumFactions || intents.ContainsKey(owner)) intents[owner] = new IntentEntry { Words = words, NextAt = t + 5f };
            return words;
        }

        private string ReadIntent(FactionHQ owner)
        {
            if (!ModuleServices.TryGet(out IEnemyIntentSource source) || (source is UnityEngine.Object unity && unity == null)) return IntentWords.Unknown;
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return IntentWords.Unknown;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null || hq == owner || hq.faction == null) continue;
                // A human-led enemy has no director intent to show (its main effort is whatever its humans do): UNKNOWN.
                if (manager == null || manager.HumanCount(hq) > 0) continue;
                if (source.TryGetMainEffort(hq.faction.factionName, out string label)) return IntentWords.ForEnemy(0, label);
            }
            return IntentWords.Unknown;
        }

        private string LabelOf(FactionSpace faction, FactionHQ owner, ulong player)
        {
            if (faction.Labels.TryGetValue(player, out string known)) return known;
            string label = manager != null ? manager.PlayerLabel(owner, player) : "";
            // A name is cached once found; an absent player is looked up again next time (bounded by the posts on the board).
            if (label.Length > 0 && faction.Labels.Count < SpaceContacts.MaxPlayers)
                faction.Labels[player] = SpaceWire.Clean(label, SpaceWire.MaxMaker);
            return SpaceWire.Clean(label, SpaceWire.MaxMaker);
        }

        private int RadarDeadline(SpaceState state, float now)
        {
            // A scan the host has switched off, or with no live uplink, is "unavailable", never "ready".
            if (manager == null || !manager.ActionEnabled(SupportActionId.Recon)) return SpaceWire.RadarUnavailable;
            float wait = state.ReadyIn(BirdTask.Scan, now);
            if (float.IsInfinity(wait)) return SpaceWire.RadarUnavailable;
            return wait <= 0.05f ? 0 : SpaceMirror.GateDeadline(now, wait);
        }

        /// <summary>The faction's TASKED desk (the CYBER service posts its BURN packages on it). False until the faction has a SPACE.</summary>
        internal bool TryGetDesk(FactionHQ owner, out TaskedDesk desk)
        {
            desk = owner != null && factions.TryGetValue(owner, out FactionSpace faction) ? faction.Tasked : null;
            return desk != null;
        }

        internal SpaceObservations ObservationsFor(FactionHQ owner) =>
            owner != null && factions.TryGetValue(owner, out FactionSpace faction) ? faction.Observations : null;

        internal int OpenWindow(FactionHQ owner, GlobalPosition point, float radius, BirdKind source,
            float minimumSpeed, float maximumSpeed) =>
            ObservationsFor(owner)?.Open(point, radius, source, minimumSpeed, maximumSpeed) ?? -1;

        /// <summary>OVERWATCH: the faction's RADAR bird follows the moving enemy ground units nearest the aim. NoMovers when none moves within the radius.</summary>
        internal SupportResult Overwatch(FactionHQ owner, GlobalPosition at, float radius, int units, float seconds, out int followed)
        {
            followed = 0;
            SpaceObservations observations = ObservationsFor(owner);
            if (observations == null) return SupportResult.CapabilityUnavailable;
            followed = observations.Follow(at, radius, units, seconds);
            return followed > 0 ? SupportResult.Accepted : SupportResult.NoMovers;
        }

        /// <summary>
        /// The host side of the optical camera task: read the sky at the aim, refuse with words at night or when the sky is unknown,
        /// otherwise open an OPTICAL reveal window whose radius the weather policy sized. Contacts it admits stay revealed for the
        /// window's 20 mission seconds, so <see cref="SpaceObservations.Tick"/> no longer forgets optical-sourced reveals.
        /// </summary>
        internal int OpenOptical(FactionHQ owner, GlobalPosition point, float baseRadius, out SupportResult refusal)
        {
            if (!OpticalRadius(owner, point, baseRadius, out float radius, out refusal)) return -1;
            int admitted = OpenWindow(owner, point, radius, BirdKind.Optical, 0f, float.MaxValue);
            if (admitted < 0) refusal = SupportResult.SpawnFailed;
            return admitted;
        }

        private static bool OpticalRadius(FactionHQ owner, GlobalPosition point, float baseRadius, out float radius, out SupportResult refusal)
        {
            refusal = SupportResult.Accepted; radius = 0f;
            bool known = SpaceSky.TrySample(point, out WeatherViewSample sky);
            OpticalVerdict verdict = SpaceFeedRules.Optical(known, sky);
            if (verdict != OpticalVerdict.Ok)
            {
                refusal = verdict == OpticalVerdict.NightUnavailable ? SupportResult.OpticalNight : SupportResult.SkyUnknown;
                return false;
            }
            // CYBER BIRD JAM halves the footprint of a jammed faction's camera.
            float jam = CyberService.Active != null ? CyberService.Active.BirdOpticalFactor(owner) : 1f;
            radius = SpaceFeedRules.OpticalRadius(baseRadius, sky) * jam;
            return true;
        }

        /// <summary>OVERLORD's resumable twin of <see cref="OpenWindow"/> / <see cref="OpenOptical"/>: null where those return -1, else the unit walk to step.</summary>
        internal SpaceObservations.OpenWalk BeginScan(FactionHQ owner, GlobalPosition point, float radius, bool radar)
        {
            SpaceObservations observations = ObservationsFor(owner);
            if (observations == null) return null;
            if (radar) return observations.BeginOpen(point, radius, BirdKind.Radar, 0f, ReconAction.StationaryThreshold);
            return OpticalRadius(owner, point, radius, out float optical, out _)
                ? observations.BeginOpen(point, optical, BirdKind.Optical, 0f, float.MaxValue) : null;
        }

        // ---- TASKED calls (host authority; Task 6 network handlers call these) ---------------------

        private bool TryDesk(Player player, out TaskedDesk desk)
        {
            desk = null;
            if (!GameAccess.IsServer() || player == null || player.HQ == null || PlayerIdentity.Of(player) == PlayerIdentity.None ||
                manager?.Settings == null || !manager.Settings.Enabled.Value || !factions.TryGetValue(player.HQ, out FactionSpace faction)) return false;
            desk = faction.Tasked;
            return desk != null;
        }

        /// <summary>Posts the player's confirmed MARKs as one TASKED call. Replays by (player, request id).</summary>
        internal TaskedResult SendTasked(Player player, int[] markIds, int requestId) =>
            TryDesk(player, out TaskedDesk desk) ? desk.Send(PlayerIdentity.Of(player), requestId, markIds)
                : new TaskedResult(TaskedOutcome.Unavailable, 0, requestId);

        /// <summary>
        /// Queues a claim. The host arbitrates inside a 200 ms window (favorite first, then receipt order); the winner's
        /// escrow, launch and settlement follow on the host tick and the verdict arrives through <see cref="TryTaskedResult"/>.
        /// </summary>
        internal TaskedResult ClaimTasked(Player player, int postId, int requestId, bool favorite = false) =>
            TryDesk(player, out TaskedDesk desk) ? desk.Claim(PlayerIdentity.Of(player), requestId, postId, favorite)
                : new TaskedResult(TaskedOutcome.Unavailable, postId, requestId);

        internal bool TryTaskedResult(Player player, int requestId, out TaskedResult result)
        {
            result = default;
            return TryDesk(player, out TaskedDesk desk) && desk.TryResult(PlayerIdentity.Of(player), requestId, out result);
        }

        /// <summary>Wires the push for queued claims that resolve later (Task 6 answers the client from it).</summary>
        internal void SubscribeTasked(FactionHQ owner, Action<ulong, int, TaskedResult> handler)
        {
            if (owner != null && handler != null && factions.TryGetValue(owner, out FactionSpace faction) && faction.Tasked != null)
                faction.Tasked.Resolved += handler;
        }

        /// <summary>
        /// Fills one viewer's faction view: the headline always, the rows only for an open feed. Everything a client may learn is
        /// derived here from the host's own state; nothing is read from the client. False when the faction has no SPACE.
        /// </summary>
        internal bool FillFeed(Player viewer, bool rows, SpaceFeedState into, List<SpaceContact> reveals, List<SpaceMark> marks,
            List<TaskedPostInfo> posts)
        {
            coarse = true; // display reads use the 1 Hz world state; only host gates re-sample the natives
            try { return FillFeedCore(viewer, rows, into, reveals, marks, posts); }
            finally { coarse = false; }
        }

        private bool FillFeedCore(Player viewer, bool rows, SpaceFeedState into, List<SpaceContact> reveals, List<SpaceMark> marks,
            List<TaskedPostInfo> posts)
        {
            into.Active = false; into.Feed = false; into.Family = SpaceFamilyState.Dark;
            into.UplinksLive = 0; into.UplinksTotal = 0; into.LiveMarks = 0; into.Gate = TaskedOutcome.None; into.GateDetail = 0;
            into.NewestPost = 0; into.NewestAction = default; into.NewestTargets = 0; into.NewestWatchOfficer = false; into.NewestOwn = false;
            into.NewestMaker = ""; into.RadarReadyAt = 0; into.Intent = "";
            into.ClearRows();
            if (viewer == null || viewer.HQ == null || manager == null || !factions.TryGetValue(viewer.HQ, out FactionSpace faction) ||
                faction.Observations == null) return false;
            float now = SupportManager.MissionNow();
            if (!SpaceRules.MissionTime(now)) return false;
            SpaceContacts contacts = faction.Observations.Contacts;
            contacts.Prune(now);
            into.Active = true;
            into.Family = faction.State.Family(now);
            into.UplinksLive = (byte)faction.State.LiveUplinkCount;
            into.UplinksTotal = (byte)faction.State.UplinkCount;
            into.LiveMarks = (byte)Math.Min(SpaceContacts.MaxMarks, contacts.MarkCount);
            into.Gate = manager.TaskedGate(viewer, out int detail);
            detail = Math.Max(0, detail);
            // A cooldown or freeze counts down on the client: send its deadline (mission time), not seconds left, so a cooling
            // member is not sent a new value every second.
            // The deadline comes from the unrounded remaining time (the integer detail would flip it by a second every poll).
            into.GateDetail = into.Gate == TaskedOutcome.Cooldown ? SpaceMirror.GateDeadline(now, manager.ServerCooldownRemaining(viewer, SupportActionId.Artillery))
                : SpaceMirror.GateIsDeadline(into.Gate) ? SpaceMirror.GateDeadline(now, detail) : detail;
            ulong salt = manager.SpaceNet != null ? manager.SpaceNet.Salt : 0UL;
            ulong viewerId = PlayerIdentity.Of(viewer);
            into.RadarReadyAt = RadarDeadline(faction.State, now);
            string intent = IntentFor(viewer.HQ);
            into.Intent = IntentWords.IsKnown(intent) ? intent : ""; // UNKNOWN is the footer's default, not a payload
            if (faction.Tasked != null)
            {
                // The headline always carries the newest post, so a pilot with the feed closed still hears about a new call.
                faction.Tasked.Board.Snapshot(now, posts);
                int newest = -1;
                for (int i = 0; i < posts.Count; i++) if (newest < 0 || posts[i].Call.Id > posts[newest].Call.Id) newest = i;
                if (newest >= 0)
                {
                    TaskedCall call = posts[newest].Call;
                    into.NewestPost = call.Id; into.NewestAction = call.Action;
                    into.NewestTargets = (byte)Math.Clamp(call.MarkCount, 1, TaskedBoard.MaxMarks);
                    into.NewestWatchOfficer = call.WatchOfficer;
                    into.NewestOwn = !call.WatchOfficer && call.Maker == viewerId;
                    into.NewestMaker = call.WatchOfficer || into.NewestOwn ? "" : LabelOf(faction, viewer.HQ, call.Maker);
                }
            }
            if (!rows) return true;
            into.Feed = true;
            contacts.CopyReveals(now, reveals);
            for (int i = 0; i < reveals.Count && into.Contacts.Count < SpaceWire.MaxContacts; i++)
            {
                SpaceContact c = reveals[i];
                // One row the wire cannot carry is dropped here, not allowed to make the whole message unreadable.
                if (!SpaceWire.Codable(c.X) || !SpaceWire.Codable(c.Z)) continue;
                // The verdict (truth) stays on the host: the client is told a deterministic, noisy probable class.
                ProbableClass probable = SpaceProbable.Of(c.Classification, c.Id, c.ObservationGeneration, salt, out byte percent);
                into.Contacts.Add(new FeedContact { Id = c.Id, UnitId = faction.Observations.UnitIdOf(c.Id), X = c.X, Z = c.Z,
                    Class = probable, Percent = percent, Moving = c.Moving, Source = c.Source, Expires = c.ExpiresAt });
            }
            contacts.CopyMarks(now, marks);
            for (int i = 0; i < marks.Count && into.Marks.Count < SpaceWire.MaxMarks; i++)
                if (SpaceWire.Codable(marks[i].X) && SpaceWire.Codable(marks[i].Z))
                    into.Marks.Add(new FeedMark { Id = marks[i].Id, X = marks[i].X, Z = marks[i].Z, Moving = marks[i].Moving,
                    Source = marks[i].Source, Expires = marks[i].ExpiresAt });
            if (faction.Tasked == null) return true;
            for (int i = 0; i < posts.Count && into.Posts.Count < SpaceWire.MaxPosts; i++)
            {
                TaskedCall call = posts[i].Call;
                bool own = !call.WatchOfficer && call.Maker == viewerId;
                const int price = 0, payoff = 0; // a TASKED claim is free; the wire still carries the two fields
                // The post is a host snapshot of fixed ground points taken at SEND; the client shows them for the post's whole life.
                var points = new FeedPoint[call.MarkCount];
                bool codable = points.Length > 0;
                for (int m = 0; m < points.Length && codable; m++)
                {
                    SpaceMark mark = call.MarkAt(m);
                    codable = SpaceWire.Codable(mark.X) && SpaceWire.Codable(mark.Z);
                    points[m] = new FeedPoint { X = mark.X, Z = mark.Z, Source = mark.Source };
                }
                if (!codable) continue; // a post the wire cannot describe is not offered rather than poisoning the message
                into.Posts.Add(new FeedPost { CallId = call.Id, Action = call.Action, WatchOfficer = call.WatchOfficer, Own = own,
                    Launching = posts[i].Launching, Points = points, Price = price, Payoff = payoff, Expires = call.ExpiresAt,
                    Claimant = posts[i].Held ? manager.PlayerLabel(viewer.HQ, posts[i].Holder) : "",
                    Maker = call.WatchOfficer || own ? "" : LabelOf(faction, viewer.HQ, call.Maker) });
            }
            return true;
        }

        /// <summary>The pilot currently holding a post of this faction (for the CLAIMED BY words).</summary>
        internal bool TryHolder(FactionHQ owner, int callId, out ulong pilot)
        {
            pilot = 0;
            return owner != null && factions.TryGetValue(owner, out FactionSpace faction) && faction.Tasked != null &&
                faction.Tasked.TryHolder(callId, out pilot);
        }

        private void Update()
        {
            if (manager?.Settings == null || !manager.Settings.Enabled.Value)
            {
                if (factions.Count > 0) ResetForScene();
                if (Time.unscaledTime >= nextCleanup)
                {
                    nextCleanup = Time.unscaledTime + 1f;
                    spawner.RetryCleanup();
                }
                return;
            }
            if (!GameAccess.IsServer() || (GameManager.gameState != GameState.SinglePlayer &&
                GameManager.gameState != GameState.Multiplayer)) return;
            float now = SupportManager.MissionNow();
            // Claim arbitration is 200 ms: the desks run every frame, the 1 s world refresh below does not.
            foreach (var pair in factions)
            {
                // One faction's desk fault must not stop the other desks or the world refresh.
                try { pair.Value.Tasked?.Tick(); }
                catch (Exception e)
                {
                    if (Time.unscaledTime >= nextTaskedWarning)
                    {
                        nextTaskedWarning = Time.unscaledTime + 10f;
                        Plugin.Logger?.LogWarning("[Support.Tasked] Desk tick failed: " + e.Message);
                    }
                }
            }
            if (manager.Settings.WatchOfficerEnabled.Value)
            {
                // OVERLORD (host only) staffs a SPACE no human is working. A fault in it never stops the desks or the mirror.
                try { watch.Tick(now); }
                catch (Exception e)
                {
                    if (Time.unscaledTime >= nextTaskedWarning)
                    {
                        nextTaskedWarning = Time.unscaledTime + 10f;
                        Plugin.Logger?.LogWarning("[Support.Overlord] Watch officer tick failed: " + e.Message);
                    }
                }
            }
            try { manager.SpaceNet?.Tick(); }
            catch (Exception e)
            {
                if (Time.unscaledTime >= nextTaskedWarning)
                {
                    nextTaskedWarning = Time.unscaledTime + 10f;
                    Plugin.Logger?.LogWarning("[Support.Space] Mirror poll failed: " + e.Message);
                }
            }
            if (!tickGate.Due(now, 1f, 0f)) return;   // 1 Hz slot 0 (table in HighCommandManager)
            spawner.RetryCleanup();
            // CYBER BIRD JAM: an enemy intrusion that holds our UPLINK node (or a fired BIRD JAM package) slows every bird task.
            // The enemy CYBER front's counter pressure (x1.5 when it leads by 40 or more) rides the same multiplier.
            foreach (var pair in factions)
                pair.Value.State.JamFactor = (CyberService.Active != null ? CyberService.Active.BirdCooldownFactor(pair.Key) : 1f) * (manager?.Fronts != null ? manager.Fronts.SpaceCooldownFactor(pair.Key) : 1f);
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null) continue;
                if (!factions.TryGetValue(hq, out FactionSpace faction))
                {
                    if (factions.Count >= MaximumFactions) continue;
                    faction = Open(hq);
                }
                Refresh(hq, faction, now);
            }
        }

        /// <summary>
        /// Every faction has a SPACE from its first tick (CYBER BURN packages, SOF posts and OVERLORD use its TASKED desk): no pad, no satellite, nothing on
        /// station until a LAUNCH PAD and a LAUNCH SATELLITE build put them there.
        /// </summary>
        private FactionSpace Open(FactionHQ owner)
        {
            int pads = FrontRules.PadCap;
            var links = new Unit[pads];
            var faction = new FactionSpace { State = new SpaceState(pads), Links = links, Baseline = new float[pads], SiteX = new float[pads], SiteZ = new float[pads],
                View = Array.AsReadOnly(links), Observations = new SpaceObservations(owner) };
            faction.Tasked = manager?.CreateTaskedDesk(this, owner, faction.Observations);
            SeedGeo(owner, faction.State);
            factions.Add(owner, faction);
            SubscribeTasked(owner, (player, request, result) => manager?.SpaceNet?.OnTaskedResolved(owner, player, request, result));
            Plugin.Logger?.LogInfo("[Support.Space] " + owner.name + ": SPACE open, no pad and no satellite until built.");
            return faction;
        }

        private static void Refresh(FactionHQ owner, FactionSpace faction, float now)
        {
            faction.Observations?.Tick(now);
            RefreshPads(owner, faction, now);
        }

        private static void RefreshPads(FactionHQ owner, FactionSpace faction, float now)
        {
            for (int i = 0; i < faction.Links.Length; i++)
            {
                if (!faction.State.PadPresent(i)) continue; // a slot never built has no site to read
                Unit link = faction.Links[i];
                float health = faction.Baseline[i] > 0f ? Mathf.Clamp01(UplinkSpawner.Health(link) / faction.Baseline[i]) : 0f;
                faction.State.SetUplink(i, health, UplinkSpawner.Down(link, owner), now);
            }
        }

        /// <summary>Parks the faction's three birds over its side of the front: own airbase centroid shifted toward the enemy's.</summary>
        private static void SeedGeo(FactionHQ owner, SpaceState state)
        {
            float ownU = 0f, ownV = 0f, foeU = 0f, foeV = 0f; int own = 0, foe = 0;
            Vector2 span = TheaterFrame.Resolve();
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
                {
                    if (a == null || a.AttachedAirbase || a.UnitDestroyed() || a.CurrentHQ == null) continue;
                    GlobalPosition p = (a.center != null ? a.center : a.transform).GlobalPosition();
                    float u = GeoSpace.U(p.x, span.x), v = GeoSpace.V(p.z, span.y);
                    if (a.CurrentHQ == owner) { ownU += u; ownV += v; own++; } else { foeU += u; foeV += v; foe++; }
                }
            if (own == 0) { ownU = 0.35f; ownV = 0.5f; } else { ownU /= own; ownV /= own; }
            if (foe == 0) { foeU = 1f - ownU; foeV = 1f - ownV; } else { foeU /= foe; foeV /= foe; }
            var hu = new float[3]; var hv = new float[3];
            GeoSpace.Defaults(ownU, ownV, foeU, foeV, hu, hv);
            state.SetHomes(hu, hv);
        }

        /// <summary>Host: one faction's bird burns to a map point (the SPACE director and the RELOCATE command both come here).</summary>
        internal GeoSpace.Refusal TryRelocate(FactionHQ owner, int bird, float u, float v, float now, out GeoBird after)
        {
            after = default;
            if (owner == null || bird < 0 || bird >= SpaceRules.BirdCount || !factions.TryGetValue(owner, out FactionSpace faction)) return GeoSpace.Refusal.Dead;
            GeoSpace.Refusal why = faction.State.Relocate((BirdKind)bird, now, u, v);
            after = faction.State.Geo((BirdKind)bird);
            if (why == GeoSpace.Refusal.None) Plugin.Logger?.LogInfo("[Support.Space] " + owner.name + " " + GeoSpace.Names[bird] + " bird burns to " + after.ToU.ToString("0.00") + "," + after.ToV.ToString("0.00") + " (fuel " + Mathf.RoundToInt(after.Fuel) + " %).");
            return why;
        }

        /// <param name="requireRear">False for a player-picked base: any legal ground around it, scored 0 (no deep-rear refusal).</param>
        private bool TryAround(FactionHQ owner, Vector3 origin, Airbase parent, float diagonal, out Candidate candidate, bool requireRear = true)
        {
            var found = new List<Candidate>(1);
            CollectAround(owner, origin, parent, diagonal, found, 1, requireRear);
            candidate = found.Count > 0 ? found[0] : default;
            return found.Count > 0;
        }

        private void CollectAround(FactionHQ owner, Vector3 origin, Airbase parent, float diagonal,
            List<Candidate> candidates, int limit, bool requireRear)
        {
            int start = candidates.Count;
            float exclusion = Mathf.Clamp(500f * Mathf.Clamp(diagonal / 150000f, 0.4f, 2.5f), 250f, 1000f);
            float radius = (parent != null ? parent.GetRadius() : 500f) + exclusion + 120f;
            for (int ring = 0; ring < 3; ring++)
                for (int step = 0; step < 12; step++)
                {
                    if (candidates.Count - start >= limit || candidates.Count >= UplinkPlacement.MaxCandidates) return;
                    float angle = (step * 5 % 12) * Mathf.PI / 6f;
                    Vector3 desired = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (radius + ring * 240f);
                    float rear = 0f;
                    if ((requireRear && !RearScore(owner, desired, diagonal, out rear)) ||
                        !spawner.TryPlan(desired.ToGlobalPosition(), parent, out _, out _)) continue;
                    candidates.Add(new Candidate(desired.ToGlobalPosition(), parent, rear));
                }
        }

        private static bool RearScore(FactionHQ owner, Vector3 desired, float diagonal, out float score)
        {
            score = 0f;
            if (ModuleServices.TryGet(out ITerritoryIngress territory))
            {
                GlobalPosition point = desired.ToGlobalPosition();
                if (territory.TryNearestEdge(owner.GetInstanceID(), point.x, point.z, out float x, out float z))
                {
                    float dx = point.x - x, dz = point.z - z;
                    score = Mathf.Sqrt(dx * dx + dz * dz);
                    return territory.TryGetHoldStrength(owner.GetInstanceID(), point.x, point.z, out float hold) &&
                        hold >= 0f && score <= Mathf.Max(40000f, diagonal * 0.25f);
                }
            }
            float nearest = float.PositiveInfinity;
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (airbase == null || airbase.disabled || airbase.center == null || airbase.CurrentHQ == null || airbase.CurrentHQ == owner) continue;
                    Vector3 delta = desired - airbase.center.position; delta.y = 0f;
                    nearest = Mathf.Min(nearest, delta.magnitude);
                }
            if (float.IsInfinity(nearest)) return true; // No known objective front yet: ordinary legality remains.
            score = nearest;
            // Startup/no-territory fallback: bounded objective-distance proxy, not measured front distance.
            return nearest * 0.5f <= Mathf.Max(40000f, diagonal * 0.25f);
        }
    }
}
