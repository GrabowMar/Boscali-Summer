using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Support's provider of the STRIKE / OPS page of the TAC BOARD (<see cref="StrikeScope"/>), and of the board's PERK and MARK gauges. It answers from what the
    /// module already holds, once per frame at most, allocating nothing steady-state: the local pilot's own strikes (<see cref="SupportManager.ActiveStrikes"/>,
    /// filled on every peer from the host's accepted reply), the OPS perk fan (<see cref="OpsPerkFan"/>), the pilot's own RECON PASS or SAT CAMERA window (the
    /// reply's serials), his own CYBER intrusion and his SOF team (the faction mirrors the host pushes to every member), his allocation and the kinetic bird.
    /// Hosts and clients both serve it: nothing here reads host-only state.
    /// </summary>
    internal sealed class StrikeScopeFeed
    {
        private const int Candidates = 16, MarkLifetime = 120;

        private readonly SupportManager manager;
        private readonly OpsPerkFan perks;
        private readonly StrikeTrack[] tracks = new StrikeTrack[StrikeScopeRules.MaxTracks];
        private readonly PerkTile[] tiles = new PerkTile[StrikeScopeRules.MaxPerks];
        private readonly ChainNodeKind[] chain = new ChainNodeKind[CyberRules.MaxHeldPerIntrusion + 1];
        private readonly float[] costs = new float[StrikeScopeRules.PricedStrikes.Length];
        private readonly int[] ids = new int[Candidates], source = new int[Candidates], picks = new int[StrikeScopeRules.MaxTracks];
        private readonly bool[] flying = new bool[Candidates];
        private readonly PerkPhase[] states = new PerkPhase[IOpsPerks.MaxPerks];
        private readonly float[] cooldowns = new float[IOpsPerks.MaxPerks], totals = new float[IOpsPerks.MaxPerks];
        private readonly string[] names = new string[IOpsPerks.MaxPerks], fronts = new string[IOpsPerks.MaxPerks];
        private readonly int[] perkPicks = new int[StrikeScopeRules.MaxPerks];
        private int perkCount, perkTiles;
        private float nextPerks;
        private readonly Dictionary<int, string> labels = new Dictionary<int, string>(Candidates);
        private readonly string[] callsigns = new string[SofRules.MaxTeams];
        private readonly float[] sofPeak = new float[SofRules.MaxTeams];
        private readonly TeamState[] sofPhase = new TeamState[SofRules.MaxTeams];
        private readonly string[] seconds = new string[1000];

        private StrikeScope cache;
        private bool live;
        private int frame = -1, radarSerial = -1, cameraSerial = -1, satContacts, rodCost;
        private float satOpenedAt = float.NegativeInfinity, nextCosts, lastNow;
        // The own intrusion's trace rate, measured from the mirror: the snapshot the window began at, and the host's pace against the rules' (the trace factor).
        private int traceFor = -1, traceSig;
        private float traceBase, traceBaseAt, traceFactor = 1f;
        private bool satCamera;
        private string armedSource, armedSub, markText;
        private int markAge = -1;

        public StrikeScopeFeed(SupportManager manager, OpsPerkFan perks)
        {
            this.manager = manager;
            this.perks = perks;
            for (int i = 0; i < callsigns.Length; i++) callsigns[i] = SofRules.Callsign(i);
        }

        /// <summary>Make this the provider of the STRIKE page and of the PERK and MARK gauges.</summary>
        public void Register()
        {
            ScopeFeeds.Register<StrikeScope>(Read);
            ScopeGauges.Register("perk", ReadPerk);
            ScopeGauges.Register("mark", ReadMark);
        }

        public void Unregister()
        {
            ScopeFeeds.Unregister<StrikeScope>(Read);
            ScopeGauges.Unregister("perk", ReadPerk);
            ScopeGauges.Unregister("mark", ReadMark);
        }

        // ------------------------------------------------------------------------------------------------ the page feed

        public bool Read(out StrikeScope data)
        {
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                live = Build();
            }
            data = cache;
            return live;
        }

        private bool Build()
        {
            SupportManager m = SupportManager.Active;
            if (m == null || !GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled) return false;
            GlobalPosition at = me.transform.position.ToGlobalPosition();
            float now = SupportManager.MissionNow();
            if (now < lastNow - 1f)      // the mission clock restarted (a new mission, a scene reset): nothing of the last one is open any more
            {
                satOpenedAt = float.NegativeInfinity;
                satContacts = 0;
                nextCosts = 0f;
                traceFor = -1;
                Array.Clear(sofPeak, 0, sofPeak.Length);
            }
            lastNow = now;
            var scope = new StrikeScope { Tracks = tracks, Perks = tiles, Chain = chain, AllocCosts = costs };

            scope.TrackCount = Strikes(m, at, now);
            scope.PerkCount = Perks();
            Window(m, now, ref scope);
            Intrusion(m, now, ref scope);
            Economy(m, ref scope, now);
            Team(m, now, ref scope);
            cache = scope;
            return true;
        }

        /// <summary>The pilot's strikes that are in the air, just landed or in effect: the page's tracks, in the order they were called.</summary>
        private int Strikes(SupportManager m, GlobalPosition at, float now)
        {
            IReadOnlyList<ActiveStrikeInfo> list = m.ActiveStrikes;
            int c = 0;
            for (int i = 0; i < list.Count && c < Candidates; i++)
            {
                ActiveStrikeInfo s = list[i];
                if (!StrikeScopeRules.TryKind(s.ActionId, out StrikeKind kind)) continue;
                float eta = s.ImpactTime - now;
                if (!StrikeScopeRules.Keep(eta, StrikeScopeRules.Linger(kind, s.ImpactTime, s.ExpiryTime))) continue;
                ids[c] = s.RequestId;
                flying[c] = eta > 0f;
                source[c] = i;
                c++;
            }
            int n = StrikeScopeRules.Pick(c, ids, flying, picks);
            if (labels.Count > Candidates * 2) labels.Clear();
            for (int k = 0; k < n; k++)
            {
                ActiveStrikeInfo s = list[source[picks[k]]];
                StrikeScopeRules.TryKind(s.ActionId, out StrikeKind kind);
                float dx = s.Target.x - at.x, dz = s.Target.z - at.z;
                if (!labels.TryGetValue(s.RequestId, out string label)) labels[s.RequestId] = label = "GRID " + TheaterGrid.Km(s.Target.x) + "/" + TheaterGrid.Km(s.Target.z);
                tracks[k] = new StrikeTrack
                {
                    Id = s.RequestId, Kind = kind, Eta = s.ImpactTime - now, Lead = Math.Max(0.1f, s.ImpactTime - s.StartTime),
                    Linger = StrikeScopeRules.Linger(kind, s.ImpactTime, s.ExpiryTime), RangeM = Mathf.Sqrt(dx * dx + dz * dz),
                    BearingDeg = (Mathf.Atan2(dx, dz) * Mathf.Rad2Deg + 360f) % 360f, RadiusM = s.Radius,
                    Legs = kind == StrikeKind.Cruise ? m.CruiseLegCount(s.RequestId) : 0, Target = label,
                };
            }
            return n;
        }

        /// <summary>
        /// The strip: what is armed, what is cooling, what is ready. The perk fan rebuilds its tiles (and their words) on every ask, so the feed asks four times a second at most and
        /// answers from its own arrays in between; a perk armed, fired or ready shows within a quarter of a second.
        /// </summary>
        private int Perks()
        {
            RefreshPerks();
            return perkTiles;
        }

        private void RefreshPerks()
        {
            float now = Time.unscaledTime;
            if (now < nextPerks && now >= nextPerks - 1f) return;
            nextPerks = now + 0.25f;
            int count = Math.Min(perks.PerkCount, IOpsPerks.MaxPerks);
            for (int i = 0; i < count; i++)
            {
                int cooldown = perks.PerkCooldown(i);
                cooldowns[i] = cooldown;
                states[i] = perks.PerkArmed(i) ? PerkPhase.Armed : cooldown > 0 ? PerkPhase.Cooling : PerkPhase.Ready;
                names[i] = StrikeScopeRules.PerkName(perks.PerkLabel(i));
                fronts[i] = perks.PerkFront(i);
                totals[i] = perks.PerkTotal(i);
            }
            perkCount = count;
            perkTiles = StrikeScopeRules.PickPerks(count, states, cooldowns, perkPicks);
            for (int k = 0; k < perkTiles; k++)
            {
                int i = perkPicks[k];
                tiles[k] = new PerkTile { Name = names[i], Front = StrikeScopeRules.Front(fronts[i]), State = states[i], Cooldown = cooldowns[i], Total = totals[i] };
            }
        }

        /// <summary>The window the pilot's own RECON PASS or SAT CAMERA opened: seen as a new serial on the accepted reply, open for the host's 20 s observation.</summary>
        private void Window(SupportManager m, float now, ref StrikeScope scope)
        {
            if (radarSerial < 0) { radarSerial = m.RadarScanSerial; cameraSerial = m.CameraSerial; }      // the first look is a baseline, not an opening
            else
            {
                if (m.RadarScanSerial != radarSerial) { radarSerial = m.RadarScanSerial; satOpenedAt = now; satCamera = false; satContacts = m.RadarScanContacts; }
                if (m.CameraSerial != cameraSerial) { cameraSerial = m.CameraSerial; satOpenedAt = now; satCamera = true; satContacts = m.CameraContacts; }
            }
            float left = satOpenedAt + StrikeScopeRules.SatSeconds - now;
            scope.SatOpen = left > 0f && now >= satOpenedAt;
            scope.SatCamera = satCamera;
            scope.SatLeft = Math.Max(0f, left);
            scope.SatTotal = StrikeScopeRules.SatSeconds;
            scope.SatContacts = satContacts;
        }

        /// <summary>The pilot's own intrusion from the faction's CYBER mirror: held nodes in the order they fell, the hop target after them, the hop's clock and the trace.</summary>
        private void Intrusion(SupportManager m, float now, ref StrikeScope scope)
        {
            CyberMirror mirror = m.CyberMirror;
            if (!mirror.Known || !mirror.State.Active) return;
            List<CyberIntrusionRow> rows = mirror.State.Intrusions;
            for (int r = 0; r < rows.Count; r++)
            {
                CyberIntrusionRow row = rows[r];
                if (!row.Own || (row.Phase != IntrusionPhase.Hopping && row.Phase != IntrusionPhase.Holding)) continue;
                int held = 0, n = 0;
                for (int i = 0; row.Held != null && i < row.Held.Length && n < chain.Length; i++)
                    if (Kind(mirror.State.Nodes, row.Held[i], out NodeKind k)) { chain[n++] = StrikeScopeRules.Chain(k); held++; }
                bool hopping = row.Phase == IntrusionPhase.Hopping && row.HopTarget != 0;
                if (hopping && Kind(mirror.State.Nodes, row.HopTarget, out NodeKind target) && n < chain.Length) chain[n++] = StrikeScopeRules.Chain(target);
                else hopping = false;
                float left = row.HopEndsAt - now, rate = StrikeScopeRules.TraceRate(hopping, held) * MeasureTrace(row, hopping, held, mirror.State.Now);
                scope.CyberLive = true;
                scope.CyberHopping = hopping;
                scope.ChainCount = n;
                scope.ChainHeld = held;
                scope.HopLeft = hopping ? Math.Max(0f, left) : 0f;
                scope.HopProgress = hopping && row.HopSeconds > 0 ? Mathf.Clamp01(1f - left / row.HopSeconds) : 0f;
                scope.Trace = StrikeScopeRules.TraceNow(row.Trace, rate, now - mirror.State.Now);
                scope.TraceRate = rate;
                return;
            }
        }

        /// <summary>
        /// How much faster (or slower) than the rules' own rates the host's trace really burns (the data center, enemy and effect boosts scale it, 0.25 to 3): the trace of the snapshots
        /// the mirror has delivered over a window of a few seconds against what the rules say it should have gained. 1 until the first window is complete.
        /// </summary>
        private float MeasureTrace(in CyberIntrusionRow row, bool hopping, int held, float snapshotAt)
        {
            int sig = (hopping ? 16 : 0) + held;
            if (row.Id != traceFor) { traceFor = row.Id; traceFactor = 1f; traceSig = sig; traceBase = row.Trace; traceBaseAt = snapshotAt; }
            else if (sig != traceSig) { traceSig = sig; traceBase = row.Trace; traceBaseAt = snapshotAt; }      // the rules' rate changed: start a new window
            else if (snapshotAt - traceBaseAt >= StrikeScopeRules.TraceWindow)
            {
                traceFactor = StrikeScopeRules.TraceFactor(row.Trace - traceBase, snapshotAt - traceBaseAt, StrikeScopeRules.TraceRate(hopping, held), traceFactor);
                traceBase = row.Trace;
                traceBaseAt = snapshotAt;
            }
            return traceFactor;
        }

        private static bool Kind(List<CyberNodeRow> nodes, int id, out NodeKind kind)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].Id == id) { kind = nodes[i].Kind; return true; }
            kind = default;
            return false;
        }

        /// <summary>Allocation against the prices of the strikes (refreshed once a second), and the kinetic bird's fuel.</summary>
        private void Economy(SupportManager m, ref StrikeScope scope, float now)
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return;
            if (now >= nextCosts || now < nextCosts - 5f)
            {
                nextCosts = now + 1f;
                for (int i = 0; i < costs.Length; i++) costs[i] = m.Quote(StrikeScopeRules.PricedStrikes[i]).Cost;
                rodCost = (int)costs[costs.Length - 1];
            }
            scope.HasAlloc = true;
            scope.Alloc = m.LocalAllocation;
            scope.RodCost = rodCost;
            scope.AllocCostCount = costs.Length;
            if (player.HQ != null && m.TryOwnGeo(player.HQ, (int)BirdKind.Kinetic, out GeoBird geo))
            {
                scope.HasBird = true;
                scope.BirdFuel = Mathf.Clamp01(geo.Fuel / 100f);
            }
        }

        /// <summary>The pilot's own SOF team on a mission: its callsign and mission and what is left of its phase out of the longest it has been seen to have.</summary>
        private void Team(SupportManager m, float now, ref StrikeScope scope)
        {
            SofMirror mirror = m.SofMirror;
            if (!mirror.Known || !mirror.State.Active) return;
            List<SofTeamRow> teams = mirror.State.Teams;
            for (int i = 0; i < teams.Count; i++)
            {
                SofTeamRow t = teams[i];
                if (!StrikeScopeRules.OnMission(t.State, t.Mission) || t.EndsAt <= 0f) continue;
                int slot = Math.Min(t.Slot, sofPeak.Length - 1);
                float left = Math.Max(0f, t.EndsAt - now);
                if (sofPhase[slot] != t.State || left > sofPeak[slot]) { sofPhase[slot] = t.State; sofPeak[slot] = left; }
                scope.HasSof = true;
                scope.SofLeft = left;
                scope.SofTotal = Math.Max(left, sofPeak[slot]);
                scope.SofName = callsigns[slot];
                scope.SofMission = StrikeScopeRules.MissionWord(t.Mission);
                return;
            }
        }

        // ------------------------------------------------------------------------------------------------ the gauges

        private string Seconds(float s)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(s), 0, seconds.Length - 1);
            return seconds[n] ?? (seconds[n] = n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>PERK: the perk that is armed, else the one that will be ready first, as a cooldown ring. Nothing cooling and nothing armed: no gauge.</summary>
        public bool ReadPerk(out GaugeSpec gauge)
        {
            gauge = default;
            RefreshPerks();
            int best = -1;
            float soonest = float.MaxValue;
            for (int i = 0; i < perkCount; i++)
            {
                if (states[i] == PerkPhase.Armed) { best = i; break; }
                if (states[i] == PerkPhase.Cooling && cooldowns[i] < soonest) { soonest = cooldowns[i]; best = i; }
            }
            if (best < 0) return false;
            bool armed = states[best] == PerkPhase.Armed;
            gauge = new GaugeSpec
            {
                Id = "perk", Label = "PERK", Kind = GaugeKind.Ring, Value = armed ? 1f : Mathf.Clamp01(1f - cooldowns[best] / Mathf.Max(1f, totals[best])), Inner = armed ? "GO" : Seconds(cooldowns[best]),
                Text = names[best], Sub = fronts[best], Tone = armed ? ScopeTone.Ink : ScopeTone.Dim, Ready = armed, Rank = 55,
            };
            return true;
        }

        /// <summary>MARK: the camera mark's age against its two minutes of life, and the call armed for it. No mark, no gauge.</summary>
        public bool ReadMark(out GaugeSpec gauge)
        {
            gauge = default;
            ICameraTargetService camera = manager;
            if (!camera.HasMark) return false;
            float age = camera.AgeSeconds;
            int whole = (int)age;
            if (whole != markAge) { markAge = whole; markText = whole + "S OLD"; }
            string armed = camera.ArmedActionName;
            if (!ReferenceEquals(armed, armedSource)) { armedSource = armed; armedSub = armed == null ? null : armed.Length <= 7 ? armed.ToUpperInvariant() : armed.Substring(0, 7).ToUpperInvariant(); }
            bool stale = age >= MarkLifetime * 0.75f;
            gauge = new GaugeSpec
            {
                Id = "mark", Label = "MARK", Kind = GaugeKind.Ring, Value = Mathf.Clamp01(1f - age / MarkLifetime), Inner = Seconds(Mathf.Max(0f, MarkLifetime - age)), Text = markText,
                Sub = camera.CanCallAtMark ? armedSub ?? "CALL" : "NO CALL", Tone = stale ? ScopeTone.Amber : ScopeTone.Ink, Ready = camera.CanCallAtMark, Rank = 70,
            };
            return true;
        }
    }
}
