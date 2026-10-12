using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// The nets that talk about the player's own flight (docs/CHATTER_PACKS.md): the AWACS (new bandits, a missile launched at you, your kills), the tower of the base you
    /// are at (clearances, off the ground, home again, the field under attack), intercepted enemy pilots (your missile coming at them, their shot at you, the one you just
    /// shot down, the mate who saw it) and the command net's base captures. It only reads the game: the missile warning receiver, the side's track file, kill messages, the
    /// airbase lists. The enemy net is rationed, so it colours a fight without burying the calls that matter.
    /// </summary>
    internal sealed class FlightNets
    {
        public const string Awacs = "SKYWATCH";
        private const float BanditRange = 60000f, MateRange = 30000f, EnemyGap = 9f, MissileGap = 6f;

        private static readonly string[] EnemyFlights = { "VOLK", "TALON", "GRIM", "IRON", "PIKE", "WRAITH", "HOLLOW", "SABRE" };
        private static readonly string[] EnemyPersonas = { "zealot", "merc", "conscript" };
        private static readonly string[] Clock = { "twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven" };
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        private readonly IChatter chatter;
        private readonly Dictionary<uint, ChatterSpeaker> enemies = new Dictionary<uint, ChatterSpeaker>();
        private readonly HashSet<uint> called = new HashSet<uint>();
        private readonly List<FactionHQ> headquarters = new List<FactionHQ>();
        private readonly System.Random rng = new System.Random();

        private Aircraft me;
        private MissileWarning warning;
        private ChatterSpeaker tower;
        private bool checkedIn, wasAirborne, grounded = true, alarm, missiles;
        private float lastEnemy = float.NegativeInfinity, lastMissile = float.NegativeInfinity, levelStart;

        public FlightNets(IChatter radio)
        {
            chatter = radio;
        }

        public void Reset()
        {
            Watch(null);
            foreach (FactionHQ hq in headquarters) if (hq != null) hq.onAirbaseAdded -= OnAirbase;
            headquarters.Clear();
            enemies.Clear();
            called.Clear();
            tower = null;
            checkedIn = wasAirborne = alarm = missiles = false;
            grounded = true;
            lastEnemy = lastMissile = float.NegativeInfinity;
            levelStart = Time.time;
        }

        /// <summary>Every two seconds while the player sits in an aircraft.</summary>
        public void Tick(Aircraft player)
        {
            if (!ReferenceEquals(me, player)) Watch(player);
            if (me == null || me.NetworkHQ == null) return;
            if (headquarters.Count == 0)
                foreach (FactionHQ hq in UnityEngine.Object.FindObjectsOfType<FactionHQ>())
                {
                    hq.onAirbaseAdded += OnAirbase;
                    headquarters.Add(hq);
                }
            TickGround();
            TickBandits();
            if (missiles && (warning == null || warning.knownMissiles == null || warning.knownMissiles.Count == 0))
            {
                missiles = false;
                chatter.Raise("awacs.missileclear", chatter.Station(ChatterNet.Awacs, Awacs));
            }
            bool underAttack = ModuleServices.TryGet<IBaseDefenseAlarmService>(out var defence) && defence.IsBaseUnderAttack;
            if (underAttack && !alarm && NearTower(me.transform.position) != null) chatter.Raise("tower.alarm", tower);
            alarm = underAttack;
        }

        private void Watch(Aircraft player)
        {
            if (me != null) me.onRegisterMissile -= OnOwnMissile;
            if (warning != null) warning.onMissileWarning -= OnInbound;
            me = player;
            warning = player != null ? player.GetComponent<MissileWarning>() : null;
            missiles = false;
            if (player == null) return;
            player.onRegisterMissile += OnOwnMissile;
            if (warning != null) warning.onMissileWarning += OnInbound;
            // A new aircraft starts on the ground or in the air as it is: no "airborne" call for an air start.
            grounded = player.radarAlt < 3f;
            wasAirborne = !grounded;
        }

        // ------------------------------------------------------------------ tower

        /// <summary>The tower of the player's side nearest to <paramref name="at"/>, or null away from every field.</summary>
        public ChatterSpeaker NearTower(Vector3 at)
        {
            if (me == null || me.NetworkHQ == null || !me.NetworkHQ.AnyNearAirbase(at, out Airbase airbase) || airbase == null) return null;
            string name = airbase.SavedAirbase != null && !string.IsNullOrEmpty(airbase.SavedAirbase.DisplayName) ? airbase.SavedAirbase.DisplayName : "FIELD";
            return tower = chatter.Station(ChatterNet.Tower, name.ToUpperInvariant() + " TOWER");
        }

        private void TickGround()
        {
            float alt = me.radarAlt;
            if (grounded && alt > 30f)
            {
                grounded = false;
                wasAirborne = true;
                if (NearTower(me.transform.position) != null) chatter.Raise("tower.airborne", tower);
                if (!checkedIn)
                {
                    checkedIn = true;
                    chatter.Raise("awacs.checkin", chatter.Station(ChatterNet.Awacs, Awacs));
                }
            }
            else if (!grounded && alt < 1f && me.speed < 25f)
            {
                grounded = true;
                if (wasAirborne && NearTower(me.transform.position) != null) chatter.Raise("tower.welcome", tower);
                wasAirborne = false;
            }
        }

        /// <summary>
        /// The game's centre notice (<c>AircraftActionsReport.ReportText</c>). A clearance becomes the tower's line; anything else becomes a notice on the TAC BOARD's ticker.
        /// True when it was taken (the native box then stays empty); false leaves the game's own box to show it: no board on screen, or a clearance with the radio off.
        /// </summary>
        public bool TakeNotice(string report)
        {
            if (string.IsNullOrEmpty(report) || me == null) return false;
            string evt = null, runway = null;
            if (report.StartsWith("Cleared to taxi to ", StringComparison.Ordinal)) { evt = "tower.taxi"; runway = report.Substring(19); }
            else if (report.StartsWith("Cleared for landing on ", StringComparison.Ordinal)) { evt = "tower.landing"; runway = report.Substring(23); }
            else if (report == "Aborted Landing") evt = "tower.goaround";
            if (evt != null)
            {
                ChatterSpeaker station = NearTower(me.transform.position) ?? tower ?? chatter.Station(ChatterNet.Tower, "TOWER");
                bool said = runway != null
                    ? chatter.Raise(evt, station, evt + ":" + runway, ("runway", runway))
                    : chatter.Raise(evt, station);
                if (said) return true;
            }
            if (!ModuleServices.TryGet<IHudBoard>(out var board) || !board.TryGetScreenBox(out _, out _, out _, out _)) return false;
            string plain = Tags.Replace(report, "").Trim();
            if (plain.Length == 0) return false;
            // The game colours its failure messages; everything else is routine.
            board.Notice("sys", report.IndexOf("<color", StringComparison.OrdinalIgnoreCase) >= 0 ? HudTone.Caution : HudTone.Info, plain.ToUpperInvariant());
            return true;
        }

        // ------------------------------------------------------------------ AWACS

        private void TickBandits()
        {
            FactionHQ hq = me.NetworkHQ;
            if (hq.trackingDatabase == null || me.radarAlt < 30f) return;
            Vec3 from = me.GlobalPosition().ToVec3();
            Aircraft nearest = null;
            Vec3 nearestAt = default;
            float best = BanditRange * BanditRange;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in hq.trackingDatabase)
            {
                TrackingInfo t = pair.Value;
                if (called.Contains(pair.Key.Id) || t == null || !t.TryGetUnit(out Unit u) || !(u is Aircraft enemy) || u.disabled || u.NetworkHQ == null || u.NetworkHQ == hq) continue;
                if (enemy.radarAlt < 20f) continue;
                Vec3 at = t.GetPosition().ToVec3();
                float d = (at - from).SqrLength;
                if (d >= best) continue;
                best = d;
                nearest = enemy;
                nearestAt = at;
            }
            if (nearest == null) return;
            // Called once, whoever says it: a wingman who saw it first uses the same key, and the radio's repeat rule refuses the second call.
            called.Add(nearest.persistentID.Id);
            Vec3 vel = nearest.rb != null ? nearest.rb.velocity.ToVec3() : Vec3.Zero;
            string bra = Bra.Format(from, nearestAt, vel, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial);
            chatter.Raise("awacs.bandit", chatter.Station(ChatterNet.Awacs, Awacs), "wing.bandit:" + nearest.persistentID.Id.ToString(CultureInfo.InvariantCulture),
                ("bra", bra), ("type", TypeOf(nearest)));
        }

        private void OnInbound(MissileWarning.OnMissileWarning e)
        {
            try
            {
                Missile missile = e.missile;
                if (missile == null || me == null) return;
                missiles = true;
                if (Time.time - lastMissile >= MissileGap)
                {
                    lastMissile = Time.time;
                    Vector3 local = me.transform.InverseTransformPoint(missile.transform.position);
                    int hour = Mathf.RoundToInt(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg / 30f);
                    hour = (hour % 12 + 12) % 12;
                    chatter.Raise("awacs.missile", chatter.Station(ChatterNet.Awacs, Awacs), "awacs.missile:" + missile.GetInstanceID().ToString(CultureInfo.InvariantCulture),
                        ("clock", Clock[hour] + " o'clock"));
                }
                if (missile.owner is Aircraft shooter && shooter.NetworkHQ != me.NetworkHQ) Enemy("enemy.fox", shooter, 0.6f);
            }
            catch (Exception error) { BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Chatter.Inbound", error); }
        }

        // ------------------------------------------------------------------ enemy

        private void OnOwnMissile(Missile missile)
        {
            // Raised inside the game's own multicast: nothing here may throw into Missile.StartMissile.
            try
            {
                if (missile == null || me == null) return;
                if (UnitRegistry.TryGetUnit(missile.targetID, out Unit target) && target is Aircraft enemy && enemy.NetworkHQ != me.NetworkHQ) Enemy("enemy.spiked", enemy, 0.7f);
            }
            catch (Exception error) { BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Chatter.OwnMissile", error); }
        }

        /// <summary>A kill message: the player's own kill is confirmed by the AWACS, and an aircraft's pilot or a mate nearby is heard.</summary>
        public void OnKill(PersistentID killerID, PersistentID killedID)
        {
            if (me == null || !UnitRegistry.TryGetPersistentUnit(killerID, out PersistentUnit killer) || !ReferenceEquals(killer.unit, me)) return;
            if (!UnitRegistry.TryGetPersistentUnit(killedID, out PersistentUnit victim) || victim.unit is Missile || victim.GetHQ() == me.NetworkHQ) return;
            string type = victim.definition != null ? victim.definition.unitName : victim.unitName;
            chatter.Raise("awacs.splash", chatter.Station(ChatterNet.Awacs, Awacs), "awacs.splash:" + killedID.Id.ToString(CultureInfo.InvariantCulture),
                ("target", type));
            if (!(victim.unit is Aircraft down)) return;
            ChatterSpeaker lost = EnemySpeaker(down);
            lost.CanTalk = null;                // the last transmission goes out whatever became of the aircraft
            if (rng.NextDouble() < 0.5 && Enemy("enemy.lastwords", down, 1f)) return;
            Aircraft mate = NearestEnemy(down);
            if (mate != null) Enemy("enemy.matedown", mate, 1f, ("who", Title(lost.Callsign)));
        }

        private bool Enemy(string evt, Aircraft speaker, float chance, params (string, string)[] facts)
        {
            if (Time.time - lastEnemy < EnemyGap || rng.NextDouble() > chance) return false;
            if (!chatter.Raise(evt, EnemySpeaker(speaker), null, facts)) return false;
            lastEnemy = Time.time;
            return true;
        }

        private ChatterSpeaker EnemySpeaker(Aircraft aircraft)
        {
            uint id = aircraft.persistentID.Id;
            if (enemies.TryGetValue(id, out ChatterSpeaker speaker)) return speaker;
            string callsign = EnemyFlights[(id / 4) % EnemyFlights.Length] + " " + (id % 4 + 1).ToString(CultureInfo.InvariantCulture);
            speaker = new ChatterSpeaker("enemy:" + id.ToString(CultureInfo.InvariantCulture), ChatterNet.Enemy, callsign,
                EnemyPersonas[(id * 2654435761u >> 16) % EnemyPersonas.Length]) { CanTalk = () => aircraft != null && !aircraft.disabled };
            enemies[id] = speaker;
            return speaker;
        }

        private Aircraft NearestEnemy(Aircraft lost)
        {
            FactionHQ hq = me.NetworkHQ;
            if (hq.trackingDatabase == null) return null;
            Aircraft nearest = null;
            float best = MateRange * MateRange;
            Vector3 at = lost.transform.position;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in hq.trackingDatabase)
            {
                TrackingInfo t = pair.Value;
                if (t == null || !t.TryGetUnit(out Unit u) || !(u is Aircraft enemy) || u.disabled || ReferenceEquals(u, lost) || u.NetworkHQ != lost.NetworkHQ) continue;
                float d = (enemy.transform.position - at).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = enemy;
            }
            return nearest;
        }

        // ------------------------------------------------------------------ command

        private void OnAirbase(Airbase airbase)
        {
            try
            {
                // The game fills the lists while the mission loads: only a change during play is news.
                if (me == null || airbase == null || airbase.AttachedAirbase || Time.time - levelStart < 15f || airbase.SavedAirbase == null) return;
                bool ours = airbase.CurrentHQ == me.NetworkHQ;
                string name = airbase.SavedAirbase.DisplayName;
                if (string.IsNullOrEmpty(name)) return;
                chatter.Raise(ours ? "command.basetaken" : "command.baselost", chatter.Station(ChatterNet.Command, "COMMAND"), "base:" + name + ":" + ours, ("base", name));
            }
            catch (Exception error) { BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Chatter.Airbase", error); }
        }

        private static string TypeOf(Unit unit)
        {
            string type = unit.definition != null ? unit.definition.unitName : unit.unitName;
            return string.IsNullOrEmpty(type) ? "bandit" : type;
        }

        /// <summary>"VOLK 3" as a line says it: "Volk 3".</summary>
        private static string Title(string callsign) =>
            string.IsNullOrEmpty(callsign) ? "" : callsign.Substring(0, 1) + callsign.Substring(1).ToLowerInvariant();
    }
}
