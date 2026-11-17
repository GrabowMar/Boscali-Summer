using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Services;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// The nets that talk about the player's own flight (docs/CHATTER_PACKS.md): the AWACS (new bandits, a missile launched at you, your kills), the tower of the base you
    /// are at (clearances, off the ground, home again, the field under attack), intercepted enemy pilots (your missile coming at them, their shot at you, the one you just
    /// shot down, the mate who saw it) and the command net's base captures. Since 2026-11-15 also the AWACS picture every three minutes, SAM calls, friendly losses, and
    /// the allied net (other friendly flights' kills, last words and thanks). It only reads the game: the missile warning receiver, the side's track file, kill messages,
    /// the airbase lists. The enemy and allied nets are rationed, so they colour a fight without burying the calls that matter.
    /// </summary>
    internal sealed class FlightNets
    {
        public const string Awacs = ChatterStations.Awacs;
        private const float BanditRange = 60000f, EnemyGap = 9f, MissileGap = 6f;
        private const float PictureRange = 80000f, GroupRadius = 8000f, PictureSeconds = 180f, FriendlyRange = 40000f, AlliedRange = 25000f,
            ThanksRange = 3000f, AlliedGap = 8f, WingmanRange = 2500f;

        private static readonly string[] EnemyFlights = { "VOLK", "TALON", "GRIM", "IRON", "PIKE", "WRAITH", "HOLLOW", "SABRE" };
        private static readonly string[] AlliedFlights = { "VIPER", "HAMMER", "LANCER", "COBRA", "BRONCO", "DAGGER", "HUNTER", "RAPIER" };
        private static readonly string[] EnemyPersonas = { "zealot", "merc", "conscript" };
        private static readonly string[] Clock = { "twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven" };
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        private readonly IChatter chatter;
        private readonly Dictionary<uint, ChatterSpeaker> enemies = new Dictionary<uint, ChatterSpeaker>(), allies = new Dictionary<uint, ChatterSpeaker>();
        private readonly List<Vec3> groups = new List<Vec3>();
        private float nextPicture, lastAllied = float.NegativeInfinity;
        private readonly HashSet<uint> called = new HashSet<uint>();
        private readonly List<FactionHQ> headquarters = new List<FactionHQ>();
        private readonly Dictionary<Airbase, FactionHQ> baseOwners = new Dictionary<Airbase, FactionHQ>();
        private readonly HashSet<uint> reportedShips = new HashSet<uint>();
        private FactionHQ playerHq;
        private readonly System.Random rng = new System.Random();

        private Aircraft me;
        private MissileWarning warning;
        private ChatterSpeaker tower;
        private bool checkedIn, wasAirborne, grounded = true, alarm, missiles;
        private float lastEnemy = float.NegativeInfinity, lastMissile = float.NegativeInfinity;
        private int captureSerial;
        private float gearSince = float.NaN, lastGear = float.NegativeInfinity;
        private int fuelCalled;

        public FlightNets(IChatter radio)
        {
            chatter = radio;
        }

        public void Reset()
        {
            Watch(null);
            foreach (FactionHQ hq in headquarters) if (hq != null) hq.onAirbaseAdded -= OnAirbase;
            headquarters.Clear();
            baseOwners.Clear();
            reportedShips.Clear();
            playerHq = null;
            enemies.Clear();
            allies.Clear();
            called.Clear();
            nextPicture = Time.time + PictureSeconds / 2f;
            lastAllied = float.NegativeInfinity;
            tower = null;
            checkedIn = wasAirborne = alarm = missiles = false;
            grounded = true;
            lastEnemy = lastMissile = lastGear = float.NegativeInfinity;
            gearSince = float.NaN;
            fuelCalled = 0;
            captureSerial = 0;
        }

        /// <summary>Every two seconds while the player sits in an aircraft.</summary>
        public void Tick(Aircraft player)
        {
            if (!ReferenceEquals(me, player)) Watch(player);
            if (player != null) playerHq = player.NetworkHQ;
            else if (GameManager.GetLocalPlayer<Player>(out Player local)) playerHq = local.HQ;
            if (playerHq == null) return;
            if (headquarters.Count == 0)
            {
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq == null) continue;
                    hq.onAirbaseAdded += OnAirbase;
                    headquarters.Add(hq);
                }
                if (FactionRegistry.airbaseLookup != null)
                    foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                        if (airbase != null && !airbase.AttachedAirbase) baseOwners[airbase] = airbase.CurrentHQ;
            }
            if (me == null || me.NetworkHQ == null) return;
            TickGround();
            TickBandits();
            TickPicture();
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
            // A ground start at a field: the tower says hello once the pilot is in the seat.
            if (grounded && player.speed < 5f && NearTower(player.transform.position) != null) chatter.Raise("tower.startup", tower);
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
                fuelCalled = 0;
            }
            if (!grounded) Remind(alt);
        }

        /// <summary>The controller's reminders: the wheels still down at speed (again every two minutes), fuel at a quarter and at a tenth (once each per flight).</summary>
        private void Remind(float alt)
        {
            ChatterSpeaker awacs = chatter.Station(ChatterNet.Awacs, Awacs);
            if (me.gearDeployed && alt > 150f && me.speed > 120f)
            {
                if (float.IsNaN(gearSince)) gearSince = Time.time;
                else if (Time.time - gearSince > 20f && Time.time - lastGear > 120f && chatter.Raise("awacs.gear", awacs)) lastGear = Time.time;
            }
            else gearSince = float.NaN;
            float fuel = me.GetFuelLevel();
            string percent = Mathf.Max(1, Mathf.RoundToInt(fuel * 100f)).ToString(CultureInfo.InvariantCulture);
            if (fuel < 0.10f && fuelCalled < 2) { fuelCalled = 2; chatter.Raise("awacs.fuelcritical", awacs, null, ("n", percent)); }
            else if (fuel < 0.25f && fuelCalled < 1) { fuelCalled = 1; chatter.Raise("awacs.fuel", awacs, null, ("n", percent)); }
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
            // No board on screen, or its notice line switched off by the pilot (NOTICE is part of the left column: it goes with ShowTicker and with Corners): the game's own box shows the notice.
            if (!ModuleServices.TryGet<IHudBoard>(out var board) || !board.TryGetScreenBox(out _, out _, out _, out _) ||
                ModuleServices.TryGet<ITacBoardSettings>(out var options) && (!options.ShowTicker || !options.Corners)) return false;
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
            Vec3 from = Pure(me.GlobalPosition());
            Aircraft nearest = null;
            Vec3 nearestAt = default;
            float best = BanditRange * BanditRange;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in hq.trackingDatabase)
            {
                TrackingInfo t = pair.Value;
                if (called.Contains(pair.Key.Id) || t == null || !t.TryGetUnit(out Unit u) || !(u is Aircraft enemy) || u.disabled || u.NetworkHQ == null || u.NetworkHQ == hq) continue;
                if (enemy.radarAlt < 20f) continue;
                Vec3 at = Pure(t.GetPosition());
                float d = (at - from).SqrLength;
                if (d >= best) continue;
                best = d;
                nearest = enemy;
                nearestAt = at;
            }
            if (nearest == null) return;
            // Called once, whoever says it: a wingman who saw it first uses the same key, and the radio's repeat rule refuses the second call.
            if (called.Count > 512) called.Clear();
            called.Add(nearest.persistentID.Id);
            Vec3 vel = nearest.rb != null ? Pure(nearest.rb.velocity) : Vec3.Zero;
            string bra = Bra.Format(from, nearestAt, vel, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial);
            chatter.Raise("awacs.bandit", chatter.Station(ChatterNet.Awacs, Awacs), "wing.bandit:" + nearest.persistentID.Id.ToString(CultureInfo.InvariantCulture),
                ("bra", bra), ("type", TypeOf(nearest)));
        }

        /// <summary>Every three minutes while airborne, the controller's picture: how many hostile groups (aircraft within 8 km of each other count once) within 80 km, and the nearest.</summary>
        private void TickPicture()
        {
            if (Time.time < nextPicture || me.radarAlt < 30f) return;
            nextPicture = Time.time + PictureSeconds;
            FactionHQ hq = me.NetworkHQ;
            if (hq.trackingDatabase == null) return;
            Vec3 from = Pure(me.GlobalPosition());
            groups.Clear();
            Aircraft nearest = null;
            Vec3 nearestAt = default;
            float best = PictureRange * PictureRange;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in hq.trackingDatabase)
            {
                TrackingInfo t = pair.Value;
                if (t == null || !t.TryGetUnit(out Unit u) || !(u is Aircraft enemy) || u.disabled || u.NetworkHQ == null || u.NetworkHQ == hq || enemy.radarAlt < 20f) continue;
                Vec3 at = Pure(t.GetPosition());
                float d = (at - from).SqrLength;
                if (d > PictureRange * PictureRange) continue;
                // ponytail: greedy grouping, first aircraft seeds a group; fine for the handful a theatre holds.
                bool joined = false;
                foreach (Vec3 g in groups) if ((g - at).SqrLength < GroupRadius * GroupRadius) { joined = true; break; }
                if (!joined) groups.Add(at);
                if (d < best) { best = d; nearest = enemy; nearestAt = at; }
            }
            if (nearest == null) return;
            Vec3 vel = nearest.rb != null ? Pure(nearest.rb.velocity) : Vec3.Zero;
            string bra = Bra.Format(from, nearestAt, vel, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial);
            ChatterSpeaker awacs = chatter.Station(ChatterNet.Awacs, Awacs);
            if (groups.Count == 1) chatter.Raise("awacs.picture1", awacs, null, ("bra", bra), ("type", TypeOf(nearest)));
            else chatter.Raise("awacs.picturemany", awacs, null, ("n", groups.Count.ToString(CultureInfo.InvariantCulture)), ("bra", bra));
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
                    // A shot from the ground is called as a SAM.
                    chatter.Raise(missile.owner is Aircraft ? "awacs.missile" : "awacs.sam", chatter.Station(ChatterNet.Awacs, Awacs),
                        "awacs.missile:" + missile.GetInstanceID().ToString(CultureInfo.InvariantCulture), ("clock", Clock[hour] + " o'clock"));
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
            if (!UnitRegistry.TryGetPersistentUnit(killedID, out PersistentUnit victim) || victim.unit is Missile) return;
            if (victim.unit is Ship ship) { OnShipDisabled(ship); return; }
            if (me == null || me.NetworkHQ == null) return;
            bool known = UnitRegistry.TryGetPersistentUnit(killerID, out PersistentUnit killer);
            if (ReferenceEquals(victim.unit, me) && known && killer.unit is Aircraft enemyKiller && enemyKiller.NetworkHQ != me.NetworkHQ)
            {
                Enemy("enemy.splash", enemyKiller, 0.5f);
                return;
            }
            if (!known || !ReferenceEquals(killer.unit, me))
            {
                OthersKill(known ? killer : null, victim, killedID);
                return;
            }
            if (victim.GetHQ() == me.NetworkHQ) return;
            string type = victim.definition != null ? victim.definition.unitName : victim.unitName;
            if (!(victim.unit is Aircraft down)) return;
            ChatterSpeaker lost = EnemySpeaker(down);
            lost.CanTalk = null;                // the last transmission goes out whatever became of the aircraft
            // One loss, one contextual response. Drawing the AWACS call first would consume the shared cooldown
            // and make the new enemy performances unreachable.
            if (rng.NextDouble() < 0.35 && Enemy("enemy.lastwords", down, 1f)) return;
            if (rng.NextDouble() < 0.15)
            {
                Aircraft saved = NearestFriendly(down.transform.position, ThanksRange);
                if (saved != null && Allied("allied.thanks", saved, 1f)) return;
            }
            chatter.Raise("awacs.splash", chatter.Station(ChatterNet.Awacs, Awacs), "awacs.splash:" + killedID.Id.ToString(CultureInfo.InvariantCulture),
                ("target", type));
        }

        /// <summary>The native disable transition begins flooding: the captain can order evacuation before the hull disappears.</summary>
        public void OnShipDisabled(Ship ship)
        {
            if (ship == null || playerHq == null || ship.NetworkHQ == null) return;
            uint id = ship.persistentID.Id;
            if (!reportedShips.Add(id)) return;
            bool friendly = ship.NetworkHQ == playerHq;
            bool carrier = ship.GetAirbase() != null;
            string evt = friendly ? carrier ? "command.carrierevac" : "command.shiplost" : "command.shipsunk";
            ChatterSpeaker speaker = chatter.Station(ChatterNet.Command, friendly ? carrier ? "CARRIER CAPTAIN" : "SHIP CAPTAIN" : "COMMAND");
            if (friendly) speaker.Character = "captain";
            chatter.Raise(evt, speaker, "ship:" + id.ToString(CultureInfo.InvariantCulture), ("target", TypeOf(ship)));
        }

        // ------------------------------------------------------------------ allied flights (2026-11-15)

        /// <summary>
        /// Somebody else's kill near the player: a friendly aircraft lost within 40 km is called by the AWACS (and, half the time, its pilot's last words go out first);
        /// a friendly flight's kill within 25 km is that pilot's splash call.
        /// </summary>
        private void OthersKill(PersistentUnit killer, PersistentUnit victim, PersistentID killedID)
        {
            Vector3 here = me.transform.position;
            string type = victim.definition != null ? victim.definition.unitName : victim.unitName;
            if (victim.GetHQ() == me.NetworkHQ && victim.unit is Aircraft lost && !ReferenceEquals(lost, me) && (lost.transform.position - here).sqrMagnitude < FriendlyRange * FriendlyRange)
            {
                if (!IsLikelyWingman(lost) && rng.NextDouble() < 0.5)
                {
                    ChatterSpeaker pilot = AlliedSpeaker(lost);
                    pilot.CanTalk = null;               // the last transmission goes out whatever became of the aircraft
                    Allied("allied.lastwords", lost, 1f);
                }
                chatter.Raise("awacs.friendlydown", chatter.Station(ChatterNet.Awacs, Awacs), "friendlydown:" + killedID.Id.ToString(CultureInfo.InvariantCulture),
                    ("type", string.IsNullOrEmpty(type) ? "aircraft" : type));
                return;
            }
            if (killer != null && killer.unit is Aircraft friend && friend.NetworkHQ == me.NetworkHQ && !ReferenceEquals(friend, me) && !friend.disabled &&
                victim.GetHQ() != me.NetworkHQ && victim.unit is Aircraft && (friend.transform.position - here).sqrMagnitude < AlliedRange * AlliedRange && !IsLikelyWingman(friend))
                Allied("allied.splash", friend, 0.7f, ("type", string.IsNullOrEmpty(type) ? "bandit" : type));
        }

        /// <summary>
        /// ponytail: no contract names the player's wingmen to this module, so a friendly within 2.5 km of the player is taken for one (the wing net speaks for those).
        /// A wing roster contract would make this exact.
        /// </summary>
        private bool IsLikelyWingman(Aircraft aircraft) => (aircraft.transform.position - me.transform.position).sqrMagnitude < WingmanRange * WingmanRange;

        private Aircraft NearestFriendly(Vector3 at, float range)
        {
            Aircraft nearest = null;
            float best = range * range;
            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (a == null || ReferenceEquals(a, me) || a.disabled || a.NetworkHQ != me.NetworkHQ || IsLikelyWingman(a)) continue;
                float d = (a.transform.position - at).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = a;
            }
            return nearest;
        }

        private bool Allied(string evt, Aircraft speaker, float chance, params (string, string)[] facts)
        {
            if (Time.time - lastAllied < AlliedGap || rng.NextDouble() > chance) return false;
            if (!chatter.Raise(evt, AlliedSpeaker(speaker), null, facts)) return false;
            lastAllied = Time.time;
            return true;
        }

        private ChatterSpeaker AlliedSpeaker(Aircraft aircraft)
        {
            uint id = aircraft.persistentID.Id;
            if (allies.TryGetValue(id, out ChatterSpeaker speaker)) return speaker;
            if (allies.Count >= 128) allies.Clear();
            string callsign = AlliedFlights[(id / 4) % AlliedFlights.Length] + " " + (id % 4 + 1).ToString(CultureInfo.InvariantCulture);
            speaker = new ChatterSpeaker("allied:" + id.ToString(CultureInfo.InvariantCulture), ChatterNet.Allied, callsign) { CanTalk = () => aircraft != null && !aircraft.disabled };
            allies[id] = speaker;
            return speaker;
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
            if (enemies.Count >= 128) enemies.Clear();
            string callsign = EnemyFlights[(id / 4) % EnemyFlights.Length] + " " + (id % 4 + 1).ToString(CultureInfo.InvariantCulture);
            speaker = new ChatterSpeaker("enemy:" + id.ToString(CultureInfo.InvariantCulture), ChatterNet.Enemy, callsign,
                EnemyPersonas[(id * 2654435761u >> 16) % EnemyPersonas.Length]) { CanTalk = () => aircraft != null && !aircraft.disabled };
            enemies[id] = speaker;
            return speaker;
        }

        // ------------------------------------------------------------------ command

        private void OnAirbase(Airbase airbase)
        {
            try
            {
                // The game fills the lists while the mission loads: only a change during play is news.
                if (playerHq == null || airbase == null || airbase.AttachedAirbase || airbase.SavedAirbase == null) return;
                bool known = baseOwners.TryGetValue(airbase, out FactionHQ previous);
                FactionHQ owner = airbase.CurrentHQ;
                baseOwners[airbase] = owner;
                if (!known || owner == previous) return;
                bool ours = owner == playerHq, wasOurs = previous == playerHq;
                string name = airbase.SavedAirbase.DisplayName;
                if (string.IsNullOrEmpty(name)) return;
                ChatterSpeaker speaker = chatter.Station(ChatterNet.Command, ours || wasOurs ? "GROUND CONTROL" : "COMMAND");
                if (ours || wasOurs) speaker.Character = "ground";
                string evt = ours ? "command.basetaken" : wasOurs ? "command.baselost" : "command.enemybase";
                chatter.Raise(evt, speaker, "base:" + airbase.GetInstanceID() + ":" + ++captureSerial, ("base", name));
            }
            catch (Exception error) { BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Chatter.Airbase", error); }
        }

        private static Vec3 Pure(GlobalPosition g) => new Vec3(g.x, g.y, g.z);

        private static Vec3 Pure(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private static string TypeOf(Unit unit)
        {
            string type = unit.definition != null ? unit.definition.unitName : unit.unitName;
            return string.IsNullOrEmpty(type) ? "bandit" : type;
        }

    }
}
