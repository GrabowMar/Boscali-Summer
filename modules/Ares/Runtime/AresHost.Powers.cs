using System;
using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Ares.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Ares.Runtime
{
    /// <summary>POWERS: strikes, weather, the clock, bosses, free OPS casts, fire and levelling.</summary>
    internal sealed partial class AresHost
    {
        public static readonly string[] Patterns = { "SINGLE", "BARRAGE", "CRUISE" };
        public static readonly string[] WeatherNames = { "live", "storm", "hurricane", "front", "fog", "wave" };
        public static readonly string[] OpsNames = { "ROD", "EMP", "DECOY", "SALVO" };
        private static readonly byte[] OpsIds = { 6, 7, 9, 31 }; // SupportActionId: Artillery (orbital rod), Emp, FlareMissile, Cruise

        private const int BarrageRounds = 8;
        private const float BarrageRadius = 250f, LevelRadius = 200f;

        private List<MissileDefinition> munitions;
        private int munition, strikes;
        public int Pattern { get; set; }
        public int OpsAction { get; set; }

        public List<MissileDefinition> Munitions()
        {
            if (munitions != null) return munitions;
            munitions = new List<MissileDefinition>();
            if (Encyclopedia.i != null && Encyclopedia.i.missiles != null)
            {
                bool events = MissionManager.AllowEventContent;
                foreach (MissileDefinition definition in Encyclopedia.i.missiles)
                    if (definition != null && definition.unitPrefab != null && definition.IsAllowed(events)) munitions.Add(definition);
                munitions.Sort((a, b) => string.Compare(a.unitName, b.unitName, StringComparison.OrdinalIgnoreCase));
            }
            return munitions;
        }

        public int Munition
        {
            get => munition;
            set
            {
                int n = Munitions().Count;
                munition = n == 0 ? 0 : ((value % n) + n) % n;
            }
        }

        public string MunitionName
        {
            get
            {
                List<MissileDefinition> list = Munitions();
                return list.Count == 0 ? "NONE" : (list[Mathf.Clamp(munition, 0, list.Count - 1)].unitName ?? "?").ToUpperInvariant();
            }
        }

        public string ArmStrike() =>
            Arm("STRIKE", "STRIKE " + MunitionName + " (" + Patterns[Pattern] + "): click the target. Esc cancels.", false, (at, heading) => StrikeAt(at));

        /// <summary>Drops or flies the chosen munition onto a point for the SPAWN side (its warhead, its kill credit to no one).</summary>
        public string StrikeAt(GlobalPosition at)
        {
            if (!Host(out string refusal)) return Say(refusal);
            List<MissileDefinition> list = Munitions();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (list.Count == 0 || spawner == null) return Say("No munitions in this game build.");
            MissileDefinition definition = list[Mathf.Clamp(munition, 0, list.Count - 1)];
            FactionHQ hq = SideHq(Side);
            if (hq == null) GameAccess.TryGetLocalFaction(out hq);
            Vector3 target = Ground(at, out _);
            int seed = ++strikes;
            switch (Pattern)
            {
                case 1:
                    StartCoroutine(Barrage(spawner, definition, hq, target, seed));
                    return Say("BARRAGE: " + BarrageRounds + " x " + MunitionName + " inbound.");
                case 2:
                    float bearing = seed * 137.5f % 360f;
                    AresMath.Rotate(0f, -12000f, bearing, out float dx, out float dz);
                    var start = new Vector3(target.x + dx, 0f, target.z + dz);
                    start.y = Mathf.Max(GroundY(start), target.y) + 300f;
                    Vector3 toward = (target - start).normalized;
                    return Say(Round(spawner, definition, hq, start, toward * 250f, target, seed)
                        ? "CRUISE: " + MunitionName + " inbound from 12 km." : "The game refused the round.");
                default:
                    return Say(Round(spawner, definition, hq, target + Vector3.up * 1500f, Vector3.down * 80f, target, seed)
                        ? "SPLASH: " + MunitionName + " falling from 1.5 km." : "The game refused the round.");
            }
        }

        private IEnumerator Barrage(Spawner spawner, MissileDefinition definition, FactionHQ hq, Vector3 centre, int seed)
        {
            for (int i = 0; i < BarrageRounds; i++)
            {
                AresMath.Barrage(seed, i, BarrageRadius, out float x, out float z);
                var point = new Vector3(centre.x + x, 0f, centre.z + z);
                point.y = GroundY(point);
                Round(spawner, definition, hq, point + Vector3.up * 1500f, Vector3.down * 80f, point, seed * 31 + i);
                yield return new WaitForSeconds(0.3f);
            }
        }

        /// <summary>One owner-less round, placed and aimed by hand as the OPS rod is (the spawner only moves the transform).</summary>
        private static bool Round(Spawner spawner, MissileDefinition definition, FactionHQ hq, Vector3 from, Vector3 velocity, Vector3 aim, int salt)
        {
            if (!GameAccess.IsServer()) return false;
            Missile missile = spawner.SpawnSavedMissile(definition.unitPrefab, from.ToGlobalPosition(), Quaternion.LookRotation(velocity.normalized), hq,
                string.Empty, string.Empty, velocity, "ARES:strike:" + salt);
            if (missile == null) return false;
            missile.transform.position = from;
            if (missile.rb != null) missile.rb.position = from;
            missile.SetAimpoint(aim.ToGlobalPosition(), Vector3.zero);
            missile.Arm();
            return true;
        }

        public string Weather(string name)
        {
            if (!Host(out string refusal)) return Say(refusal);
            if (!ModuleServices.TryGet(out IWeatherDirector weather)) return Say("The Weather module is off.");
            if (name == "live")
            {
                weather.Resume();
                return Say("WEATHER: live again.");
            }
            return Say(weather.Hold(name) ? "WEATHER: " + name.ToUpperInvariant() + " held." : "The weather refused " + name.ToUpperInvariant() + ".");
        }

        public float Hour => NetworkSceneSingleton<LevelInfo>.i != null ? NetworkSceneSingleton<LevelInfo>.i.NetworktimeOfDay : 12f;

        public string SetHour(float hour)
        {
            if (!Host(out string refusal)) return Say(refusal);
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return Say("No level clock.");
            float h = ((hour % 24f) + 24f) % 24f;
            level.SetTimeOfDay(h);
            return Say("TIME: " + AresMath.Hour(h) + ".");
        }

        public static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };
        public int SpeedIndex { get; private set; } = 2;

        public string SetSpeed(int index)
        {
            if (GameManager.gameState != GameState.SinglePlayer) return Say("SPEED is single player only.");
            SpeedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);
            TimeScaleManager.Scale = Speeds[SpeedIndex];
            return Say("SPEED: x" + Speeds[SpeedIndex].ToString("0.##") + ".");
        }

        public string SummonKaiju()
        {
            if (!Host(out string refusal)) return Say(refusal);
            return Say(ModuleServices.TryGet(out IKaijuBoss kaiju) ? "KAIJU: " + kaiju.Summon() : "The Kaiju module is off.");
        }

        public string SummonAce()
        {
            if (!Host(out string refusal)) return Say(refusal);
            return Say(ModuleServices.TryGet(out IFallenAce ace) ? "FALLEN ACE: " + ace.Summon() : "The Spooktober module is off.");
        }

        public string DismissBosses()
        {
            if (!Host(out string refusal)) return Say(refusal);
            var gone = new List<string>();
            if (ModuleServices.TryGet(out IKaijuBoss kaiju) && kaiju.Body != null) gone.Add(kaiju.Dismiss());
            if (ModuleServices.TryGet(out IFallenAce ace) && ace.Alive) gone.Add(ace.Dismiss());
            return Say(gone.Count == 0 ? "No boss is out." : "DISMISSED: " + string.Join(" / ", gone));
        }

        public string ArmCast() =>
            Arm("CAST", "CAST " + OpsNames[OpsAction] + " FREE: click the target. Esc cancels.", false, (at, heading) => CastAt(at));

        /// <summary>The OPS action at a point, free: BYPASS REQS and NO OPS COOLDOWN switch on first (and stay on, shown on CHEATS).</summary>
        public string CastAt(GlobalPosition at)
        {
            if (!Host(out string refusal)) return Say(refusal);
            if (!ModuleServices.TryGet(out ISupportCast ops)) return Say("The Support module is off.");
            settings.Diagnostics.BypassRequirements.Value = true;
            settings.Diagnostics.DisableOpsCooldowns.Value = true;
            string status = ops.CastAt(OpsIds[Mathf.Clamp(OpsAction, 0, OpsIds.Length - 1)], at);
            return Say(OpsNames[OpsAction] + ": " + (string.IsNullOrEmpty(status) ? "requested." : status));
        }

        public string ArmFire() => Arm("FIRE", "FIRE: click where it should burn. Esc cancels.", false, (at, heading) => FireAt(at));

        /// <summary>A fire at the point and four more around it, 40 m out.</summary>
        public string FireAt(GlobalPosition at)
        {
            if (!Host(out string refusal)) return Say(refusal);
            if (!ModuleServices.TryGet(out IFireStarter fire)) return Say("The Fire and Destruction module is off.");
            int lit = fire.Ignite(at) ? 1 : 0;
            for (int i = 0; i < 4; i++)
            {
                AresMath.Rotate(0f, 40f, i * 90f + 45f, out float dx, out float dz);
                if (fire.Ignite(new GlobalPosition(at.x + dx, at.y, at.z + dz))) lit++;
            }
            return Say(lit > 0 ? "FIRE: " + lit + " site(s) burning." : "Nothing caught (water, fires off or the fire limit).");
        }

        public string ArmLevel() => Arm("LEVEL", "LEVEL: click the centre; every building within 200 m falls. Esc cancels.", false, (at, heading) => LevelAt(at));

        /// <summary>Every map building and network building within 200 m destroyed (one scene search per click).</summary>
        public string LevelAt(GlobalPosition at)
        {
            if (!Host(out string refusal)) return Say(refusal);
            Vector3 centre = Ground(at, out _);
            float r2 = LevelRadius * LevelRadius;
            int fallen = 0;
            foreach (MapBuilding building in FindObjectsOfType<MapBuilding>())
            {
                if (building == null || (building.transform.position - centre).sqrMagnitude > r2) continue;
                try
                {
                    building.TakeDamage(0f, 0f, 1f, 0f, 100000f, default);
                    fallen++;
                }
                catch (Exception error) { log?.LogWarning("[Ares] level: " + error.Message); }
            }
            foreach (Unit unit in UnitRegistry.allUnits.ToArray())
                if (unit is Building building && !building.disabled && (building.transform.position - centre).sqrMagnitude <= r2)
                {
                    building.DisableUnit();
                    fallen++;
                }
            return Say(fallen > 0 ? "LEVELLED: " + fallen + " building(s)." : "No buildings within 200 m.");
        }
    }
}
