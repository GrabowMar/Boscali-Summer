using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// MALD-X EXPOSE (server, 1 Hz). Every enemy radar emitter (ground / ship radars, not aircraft) that holds a
    /// live bait inside its range with line of sight for 3 s cumulative is EXPOSED to the bait's faction for 60 s:
    /// the faction's tracking database is refreshed through the game's own FactionHQ.RpcUpdateTrackingInfo (the call
    /// every radar detection makes), so map icons, AI targeting and lock data all see it. Exposures outlive the bait.
    /// <para>MALD-X CHAFF (server, 2 Hz). Each cloud a bait seeds hangs for a minute, bait alive or not. A hostile radar
    /// missile whose line of sight to an aircraft of the bait's side crosses a cloud is blinded and the cloud is spent.</para>
    /// </summary>
    internal sealed class MaldxService : MonoBehaviour, ISceneService
    {
        private sealed class Bait
        {
            public Missile Missile;
            public Unit Launcher;
            public int Exposed, Blinded;
        }

        private struct Cloud
        {
            public GlobalPosition At;
            public float Until;
            public FactionHQ Hq;
            public Bait Source;
        }

        private static readonly List<Bait> Baits = new List<Bait>();
        private static readonly List<Cloud> Clouds = new List<Cloud>();
        private static readonly Dictionary<FactionHQ, MaldxLedger> Ledgers = new Dictionary<FactionHQ, MaldxLedger>();
        private readonly List<Radar> emitters = new List<Radar>();
        private readonly HashSet<Radar> unique = new HashSet<Radar>();
        private readonly List<uint> live = new List<uint>();
        private float nextScan, nextScreen;

        public void ResetForScene()
        {
            Baits.Clear();
            Clouds.Clear();
            Ledgers.Clear();
        }

        /// <summary>Server: a launched bait seeds one chaff cloud where it is.</summary>
        public static void Seed(Missile missile, GlobalPosition at)
        {
            Bait bait = Baits.Find(b => b.Missile == missile);
            if (bait == null || missile.NetworkHQ == null) return;
            Clouds.Add(new Cloud { At = at, Until = Time.timeSinceLevelLoad + MaldxRules.ChaffSeconds, Hq = missile.NetworkHQ, Source = bait });
            VanguardStats.Bump("maldx.clouds");
        }

        private static void Screen(float now)
        {
            Clouds.RemoveAll(c => c.Until <= now || c.Hq == null);
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count && Clouds.Count > 0; i++)
            {
                if (!(units[i] is Missile threat) || threat.disabled || !SeductionRule.IsRadarSeeker(threat.GetSeekerType())) continue;
                if (!UnitRegistry.TryGetUnit(threat.targetID, out Unit target) || !(target is Aircraft) || target.disabled) continue;
                GlobalPosition from = threat.GlobalPosition();
                Vector3 toTarget = target.GlobalPosition() - from;
                int hit = Clouds.FindIndex(c => c.Hq == target.NetworkHQ && c.Hq != threat.NetworkHQ && MaldxRules.Screens(c.At - from, toTarget));
                if (hit < 0) continue;
                Bait bait = Clouds[hit].Source;
                if (!SeekerCapture.Blind(threat, bait.Missile)) continue;
                Clouds.RemoveAt(hit);
                bait.Blinded++;
                VanguardStats.Bump("maldx.chaffed");
                MaldxNet.Send(MaldxNet.Chaffed, bait.Missile, bait.Launcher, target.persistentID.Id, bait.Blinded);
            }
        }

        /// <summary>First plan tick of a bait on the server.</summary>
        public static void Launched(Missile missile, Unit launcher)
        {
            if (missile == null || !missile.IsServer) return;
            var bait = new Bait { Missile = missile, Launcher = launcher };
            Baits.Add(bait);
            missile.onDisableUnit += _ => Down(bait);
            VanguardStats.Bump("maldx.launched");
            if (IsPlayer(launcher)) MaldxNet.Send(MaldxNet.Airborne, missile, launcher, 0u, 0);
        }

        private static void Down(Bait bait)
        {
            if (!Baits.Remove(bait)) return;
            VanguardStats.Bump("maldx.down");
            if (IsPlayer(bait.Launcher)) MaldxNet.Send(MaldxNet.Down, bait.Missile, bait.Launcher, 0u, bait.Exposed);
        }

        private static bool IsPlayer(Unit unit) => unit is Aircraft a && a.Player != null;

        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            if (Clouds.Count > 0 && now >= nextScreen) // clouds exist only where the baits fly: on the server
            {
                nextScreen = now + MaldxRules.ChaffInterval;
                Screen(now);
            }
            if (now < nextScan || !GameAccess.IsServer()) return;
            nextScan = now + MaldxRules.ScanInterval;
            if (Baits.Count > 0) Scan(now);
            Refresh(now);
        }

        private void Scan(float now)
        {
            CollectEmitters();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null) continue;
                MaldxLedger ledger = null;
                for (int i = 0; i < emitters.Count; i++)
                {
                    Unit site = emitters[i].GetAttachedUnit();
                    if (site.NetworkHQ == hq) continue;
                    Bait seen = SeenBy(emitters[i], site, hq);
                    if (seen == null) continue;
                    if (ledger == null && !Ledgers.TryGetValue(hq, out ledger)) Ledgers[hq] = ledger = new MaldxLedger();
                    if (!ledger.See(site.persistentID.Id, now, MaldxRules.ScanInterval)) continue;
                    seen.Exposed++;
                    VanguardStats.Bump("maldx.exposed");
                    Plugin.Logger?.LogInfo("[Vanguard] MALD-X exposed " + site.definition.unitName + " to " + hq.faction?.factionName);
                    MaldxNet.Send(MaldxNet.Exposed, seen.Missile, seen.Launcher, site.persistentID.Id, ledger.Exposed(now));
                }
            }
        }

        // Shared HQ radars (SAM batteries) plus every unit's own radar (ships, AAA); aircraft radars are not emitter sites.
        private void CollectEmitters()
        {
            emitters.Clear();
            unique.Clear();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                List<Radar> shared = GameAccess.GetHqRadars(hq);
                if (shared != null)
                    for (int i = 0; i < shared.Count; i++) AddEmitter(shared[i]);
            }
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit != null && !unit.disabled && !(unit is Aircraft) && !(unit is Missile)) AddEmitter(unit.radar as Radar);
            }
        }

        private void AddEmitter(Radar radar)
        {
            if (radar == null || !radar.activated || !radar.IsOperational() || radar.GetRadarRange() <= 0f) return;
            Unit site = radar.GetAttachedUnit();
            if (site == null || site.disabled || site is Aircraft || site is Missile || site.NetworkHQ == null) return;
            if (unique.Add(radar)) emitters.Add(radar);
        }

        // First live bait of `hq` inside this radar's range, above its horizon and not masked by terrain.
        private static Bait SeenBy(Radar radar, Unit site, FactionHQ hq)
        {
            if (radar.IsJammed()) return null;
            Transform scan = radar.GetScanPoint();
            if (scan == null) scan = site.transform;
            GlobalPosition from = scan.GlobalPosition();
            float range = radar.GetRadarRange();
            for (int i = 0; i < Baits.Count; i++)
            {
                Missile m = Baits[i].Missile;
                if (m == null || m.disabled || m.NetworkHQ != hq) continue;
                GlobalPosition at = m.GlobalPosition();
                Vector3 to = at - from;
                if (to.sqrMagnitude > range * range) continue;
                if (!MaldxRules.AboveHorizon(from.y, at.y, new Vector2(to.x, to.z).magnitude)) continue;
                if (Physics.Linecast(scan.position, m.transform.position, (int)PhysicsLayers.StaticsMask)) continue;
                return Baits[i];
            }
            return null;
        }

        // Keep every live exposure fresh in its faction's tracking database (Observed() needs a spot inside 4 s).
        private void Refresh(float now)
        {
            if (Ledgers.Count == 0) return;
            int total = 0, tracked = 0;
            foreach (KeyValuePair<FactionHQ, MaldxLedger> pair in Ledgers)
            {
                if (pair.Key == null) continue;
                pair.Value.Live(now, live);
                for (int i = 0; i < live.Count; i++)
                {
                    if (!UnitRegistry.TryGetUnit(new PersistentID { Id = live[i] }, out Unit site) || site.disabled)
                    {
                        pair.Value.Forget(live[i]);
                        continue;
                    }
                    pair.Key.RpcUpdateTrackingInfo(site.persistentID);
                    total++;
                    if (pair.Key.IsTargetBeingTracked(site)) tracked++;
                }
            }
            VanguardStats.Counters["maldx.live"] = total;
            VanguardStats.Counters["maldx.tracked"] = tracked;
        }
    }
}
