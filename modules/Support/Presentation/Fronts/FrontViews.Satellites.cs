using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    // The ORBIT room's SATELLITES tab mapping. Inputs, the rail and the ORBIT room are in FrontViews.cs.
    internal static partial class FrontViews
    {
        // ---- SATELLITES tab -------------------------------------------------------------------------------------------

        /// <summary>
        /// The SATELLITES tab from what <see cref="Orbit"/> just built (own and hostile birds, airbases), the OPS mirror's sites and the SPACE queue: pads, the three
        /// fleet cards with their LAUNCH offer per kind, hostile birds and ASAT, map ghosts of queued pads, the coverage gap and the header counts.
        /// </summary>
        internal static void Satellites(FrontRoomView v, in FrontInputs c, IReadOnlyList<OpsSiteRow> sites)
        {
            v.Pads.Clear(); v.Fleet.Clear(); v.Hostile.Clear(); v.Ghosts.Clear();
            int alive = 0;
            foreach (OrbitTrackView b in v.Birds) if (b.Own && b.Alive) alive |= 1 << (int)b.Kind;
            Pads(v, c, sites, alive);
            QueuedCounts queued = FrontLadder.Queued(v.Row.Queue);
            v.BirdsUp = 0;
            for (int k = 0; k < Kinds.Length; k++)
            {
                bool found = TryOwn(v, k, out OrbitTrackView b), up = found && b.Alive;
                string state = FleetState(v, k, up, found && !b.Alive, b, out AvState tone);
                if (up) v.BirdsUp++;
                v.Fleet.Add(new FleetCardView
                {
                    Kind = k, Bus = b.Bus, Callsign = Names[k], State = state, Tone = tone, Up = up, Fuel = up ? b.Fuel : 0f, Covers = up ? Covers(v, b) : "", Perk = PerkLine(v, k),
                    CanRelocate = up && !b.Relocating && b.Geo.DeltaV >= GeoOrbit.NodeDeltaV(b.Kind, GeoOrbit.MinimumNodeChange),
                    Launch = Offer(Front.Space, ProgrammeId.LaunchSatellite, (byte)k, v.Row, queued, c),
                });
            }
            for (int i = 0; i < v.Row.Queue.Count; i++)
            {
                FrontQueueRow q = v.Row.Queue[i];
                if (q.Id != ProgrammeId.LaunchSatellite || !q.HasPoint || !GeoSpace.TryUnpackLaunch(q.Kind, out BirdKind kind, out SatelliteBus bus)) continue;
                v.Ghosts.Add(new MapGhostView
                {
                    U = c.Frame.U(q.X), V = c.Frame.V(q.Z), Radius = GeoSpace.Footprint(kind, bus),
                    Label = "FIRST CROSSING · " + Names[(int)kind]
                });
            }
            v.BirdCap = Math.Min(FrontRules.BirdCap, FrontRules.BirdsPerPad * v.PadsUp);
            v.Asat = Offer(Front.Space, ProgrammeId.Asat, 0, v.Row, queued, c);
            foreach (OrbitTrackView b in v.Birds)
            {
                if (b.Own) continue;
                v.Hostile.Add(new HostileBirdView { Kind = (int)b.Kind, Id = b.TrackId, Word = GeoSpace.Names[(int)b.Kind] });
            }
            v.CoverageGap = "";
            // A gap needs a bird up and a known world: before the mirrors arrive, or with no satellite at all, every base would read as a false gap.
            for (int i = 0; v.BirdsUp > 0 && (c.SpaceLinked || c.WorldKnown) && i < v.Airbases.Count; i++)
            {
                MapAirbaseView a = v.Airbases[i];
                if (a.Side != AirbaseSide.Own || UnderOwn(v, a)) continue;
                v.CoverageGap = "GAP · " + a.Name + " NOT COVERED";
                break;
            }
        }

        /// <summary>
        /// Pads by slot: standing and lost ones from the OPS mirror, and each queued LAUNCH PAD (queue order) in the slot the host will build it into
        /// (<see cref="FrontLadder.PadSlot"/>; it takes over a lost card in that slot). A queued pad with a point is also a map ghost. Up pads link the birds on
        /// station (<paramref name="alive"/>, a kind mask) two each, in slot and kind order: the wire carries no per-pad link, so this words the capacity rule.
        /// </summary>
        private static void Pads(FrontRoomView v, in FrontInputs c, IReadOnlyList<OpsSiteRow> sites, int alive)
        {
            int present = 0, down = 0, taken = 0;
            for (int i = 0; sites != null && i < sites.Count; i++)
            {
                if (sites[i].Slot >= FrontRules.PadCap) continue;
                present |= 1 << sites[i].Slot;
                if (sites[i].State != SiteState.Up) down |= 1 << sites[i].Slot;
            }
            for (int i = 0; i < v.Queue.Count; i++)
            {
                FrontQueueRow q = v.Row.Queue[i];
                int slot = q.Id == ProgrammeId.UplinkSite ? FrontLadder.PadSlot(present, down, ref taken) : -1;
                if (slot < 0) continue;
                FrontQueueView qv = v.Queue[i];
                var p = new PadView
                {
                    Slot = slot, Name = SiteName(slot), Building = true, Row = qv.Row, Progress = qv.Progress, BuildLeft = qv.BuildLeft, Links = "",
                    U = q.HasPoint ? c.Frame.U(q.X) : -1f, V = q.HasPoint ? c.Frame.V(q.Z) : -1f, Place = q.HasPoint ? PlaceOf(q.X, q.Z, c) : "AUTO PLACE",
                };
                v.Pads.Add(p);
                if (q.HasPoint) v.Ghosts.Add(new MapGhostView { U = p.U, V = p.V, Label = p.Name + " · " + p.Place + " · " + (qv.Building ? FrontPanels.Clocked("", qv.BuildLeft) : Pct(qv.Progress)) });
            }
            for (int i = 0; sites != null && i < sites.Count; i++)
            {
                OpsSiteRow s = sites[i];
                if (s.Slot >= FrontRules.PadCap || (taken & (1 << s.Slot)) != 0) continue;
                bool up = s.State == SiteState.Up;
                v.Pads.Add(new PadView { Slot = s.Slot, Name = SiteName(s.Slot), Up = up, Lost = !up, U = c.Frame.U(s.X), V = c.Frame.V(s.Z), Place = PlaceOf(s.X, s.Z, c), Links = "" });
            }
            v.Pads.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            v.PadsUp = 0;
            int kind = 0;
            for (int i = 0; i < v.Pads.Count; i++)
            {
                PadView p = v.Pads[i];
                if (!p.Up) continue;
                v.PadsUp++;
                string links = "";
                for (int n = 0; n < FrontRules.BirdsPerPad; n++)
                {
                    while (kind < Kinds.Length && (alive & (1 << kind)) == 0) kind++;
                    if (kind >= Kinds.Length) break;
                    links += (links.Length > 0 ? ", " : "") + Names[kind++];
                }
                p.Links = "LINKS " + (links.Length > 0 ? links : "NONE");
                v.Pads[i] = p;
            }
        }

        private static string Pct(float share) => (int)(Mathf.Clamp01(share) * 100f) + " %";

        private static bool TryOwn(FrontRoomView v, int k, out OrbitTrackView b)
        {
            foreach (OrbitTrackView t in v.Birds) if (t.Own && (int)t.Kind == k) { b = t; return true; }
            b = default;
            return false;
        }

        /// <summary>A fleet card's state: on station or burning; else its launch row (building, funding, or waiting for a pad); else lost or never launched.</summary>
        private static string FleetState(FrontRoomView v, int k, bool up, bool lost, in OrbitTrackView b, out AvState tone)
        {
            if (up)
            {
                tone = b.Relocating ? AvState.Caution : AvState.Ready;
                return b.Relocating ? "TRANSFERRING " + FrontRules.Clock(b.EtaSeconds) : "IN ORBIT";
            }
            for (int i = 0; i < v.Queue.Count; i++)
            {
                FrontQueueRow q = v.Row.Queue[i];
                if (q.Id != ProgrammeId.LaunchSatellite || !GeoSpace.TryUnpackLaunch(q.Kind, out BirdKind queuedKind, out _) || (int)queuedKind != k) continue;
                FrontQueueView qv = v.Queue[i];
                tone = !qv.Building && q.Waiting ? AvState.Caution : AvState.Info;
                return qv.Building ? FrontPanels.Clocked("LAUNCHING ", qv.BuildLeft) : q.Waiting ? "WAITS FOR A PAD" : "QUEUED · " + Pct(qv.Progress);
            }
            tone = lost ? AvState.Danger : AvState.Inert;
            return lost ? "LOST" : "NOT LAUNCHED";
        }

        /// <summary>"COVERS FELDSPAR, OPAL" (+n past three): the airbases under a bird's footprint by the map's rule (<see cref="GeoBird.Distance"/> within its reach), or "COVERS NOTHING".</summary>
        private static string Covers(FrontRoomView v, in OrbitTrackView b)
        {
            string names = "";
            int n = 0;
            foreach (MapAirbaseView a in v.Airbases)
            {
                if (GeoBird.Distance(b.U, b.V, a.U, a.V) > b.Radius) continue;
                if (n++ < 3) names += (names.Length > 0 ? ", " : "") + a.Name;
            }
            return n == 0 ? "COVERS NOTHING" : "COVERS " + names + (n > 3 ? " +" + (n - 3) : "");
        }

        private static bool UnderOwn(FrontRoomView v, in MapAirbaseView a)
        {
            foreach (OrbitTrackView b in v.Birds) if (b.Own && b.Alive && GeoBird.Distance(b.U, b.V, a.U, a.V) <= b.Radius) return true;
            return false;
        }

        /// <summary>The perk a bird kind enables, worded READY or with its lock reason: the first ready one of that kind's perks, else its lowest.</summary>
        private static string PerkLine(FrontRoomView v, int k)
        {
            int pick = -1;
            for (int i = 0; i < v.Perks.Count; i++)
            {
                if (PerkBird(v.Perks[i].Id) != k) continue;
                if (v.Perks[i].Reason.Length == 0) { pick = i; break; }
                if (pick < 0) pick = i;
            }
            if (pick < 0) return "";
            FrontPerkView p = v.Perks[pick];
            return p.Name + (p.Reason.Length == 0 ? " READY" : p.Reason.StartsWith("NEEDS", StringComparison.Ordinal) ? " " + p.Reason : " · " + p.Reason);
        }

        /// <summary>Where a pad stands, worded: the nearest airbase's short name within twice the pick radius, else its grid.</summary>
        private static string PlaceOf(float x, float z, in FrontInputs c)
        {
            string best = null;
            float bestD = 2f * FrontRules.PadPickMetres;
            for (int i = 0; c.Airbases != null && i < c.Airbases.Count; i++)
            {
                float d = Dist(c.Airbases[i].X, c.Airbases[i].Z, x, z);
                if (d <= bestD) { bestD = d; best = c.Airbases[i].Name; }
            }
            return best != null ? ShortName(best) : "GRID " + OpsKit.Grid(x, z);
        }

        internal static string SiteName(int slot) => "PAD-" + (Math.Min(Math.Max(slot, 0), FrontRules.PadCap - 1) + 1);

        /// <summary>The uplink markers: the faction's own sites (OPS mirror) with their standing and what losing each costs, then the enemy sites the CYBER fog reveals. No site, no marker: nothing is invented.</summary>
        private static void Sites(FrontRoomView v, in FrontInputs c)
        {
            SupportManager m = SupportManager.Active;
            IReadOnlyList<BoscaliSummer.Modules.Support.Domain.Ops.OpsSiteRow> own = m?.OpsMirror.Sites;
            IReadOnlyList<CyberNodeRow> foe = m != null && m.CyberMirror.Known ? m.CyberMirror.State.Nodes : null;
            int up = 0, n = own != null ? own.Count : 0;
            for (int i = 0; i < n; i++) if (own[i].State == BoscaliSummer.Modules.Support.Domain.Ops.SiteState.Up) up++;
            string space = c.Family == SpaceFamilyState.Dark ? "DARK" : "DEGRADED";
            for (int i = 0; i < n; i++)
            {
                BoscaliSummer.Modules.Support.Domain.Ops.OpsSiteRow s = own[i];
                bool live = s.State == BoscaliSummer.Modules.Support.Domain.Ops.SiteState.Up, rebuilding = s.State == BoscaliSummer.Modules.Support.Domain.Ops.SiteState.Rebuilding;
                v.Sites.Add(new OrbitSiteView
                {
                    X = c.Frame.U(s.X), Y = c.Frame.V(s.Z), Own = true, Live = live, Rebuilding = rebuilding, Name = SiteName(s.Slot),
                    State = live ? "LIVE" : rebuilding ? "REBUILDING" : "DESTROYED", Grid = "GRID " + OpsKit.Grid(s.X, s.Z),
                    Cost = live ? (up <= 1 ? "LAST SITE · LOSS DARKENS SPACE" : "LOSS DEGRADES SPACE")
                        : rebuilding ? "SPACE " + space + " UNTIL REBUILT" : "SPACE " + space + " · QUEUE A LAUNCH PAD",
                });
            }
            for (int i = 0; foe != null && i < foe.Count && v.Sites.Count < 6; i++)
            {
                if (foe[i].Kind != NodeKind.Uplink) continue;
                v.Sites.Add(new OrbitSiteView
                {
                    X = c.Frame.U(foe[i].X), Y = c.Frame.V(foe[i].Z), Own = false, Live = true, Name = "HOSTILE " + NodeTag(NodeKind.Uplink, foe[i].Id), State = "REVEALED",
                    Grid = "GRID " + OpsKit.Grid(foe[i].X, foe[i].Z), Cost = "STRIKE IT TO DEGRADE THEIR SPACE",
                });
            }
        }
    }
}
