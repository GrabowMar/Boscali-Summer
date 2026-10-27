#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Spooktober.Domain;
using BoscaliSummer.Modules.Spooktober.Runtime;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Spooktober
{
    /// <summary>
    /// nomodkit single-player test hook (tests/ingame/spooktober.json); inert outside a sim run. Actions: status,
    /// reset (fresh progress), spawn / defeat (boss = Kaiju|FallenAce|HellsArmy), dismiss,
    /// hell_status / hell_enter (Hell's Army's gate; tests/ingame/spooktober-hell.json),
    /// ace_status, ace_hurt (how = wound|eject|hull) and fake_join (on = true|false; tests/ingame/spooktober-ace.json), relaunch (a new mission:
    /// progress re-read), rad (the look's readout), look (the free camera on the ace or the player) and
    /// tune ("_Knob": value on every material wearing the look).
    /// It writes the real progress entry, so a sim that must leave the install alone ends with reset.
    /// </summary>
    public static class SpooktoberAutomation
    {
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("SpooktoberAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            string action = Text(args, "action") ?? "status";
            string message = null;
            switch (action)
            {
                case "status":
                    break;
                case "reset":
                    // As if the hidden config entry had never been written.
                    if (SpookBosses.Settings == null) return Failure("SpooktoberAutomation", action, "Spooktober module not installed");
                    SpookBosses.Settings.Progress.Value = "";
                    SpookStore.Init(SpookBosses.Settings);
                    SpookBosses.Dismiss(); // and nothing left on the field: the next scenario starts clean
                    break;
                case "spawn":
                case "defeat":
                    if (!Parse(Text(args, "boss"), out SpookBoss boss)) return Failure("SpooktoberAutomation", action, "boss must be Kaiju, FallenAce or HellsArmy");
                    if (action == "spawn") { message = SpookBosses.Spawn(boss); break; }
                    // Seeds the chain directly, whatever the menu would allow: the sim says the boss fell.
                    message = SpookStore.Current.RecordDefeat(boss) ? "recorded" : "already on record";
                    SpookStore.Save();
                    break;
                case "dismiss":
                    message = SpookBosses.Dismiss();
                    break;
                case "ace_status":
                    return AceStatus(action, null);
                case "ace_hurt":
                    return AceStatus(action, AceHurt(Text(args, "how") ?? "wound"));
                case "fake_join":
                    // A single-player sim cannot connect a client: pretend one joined (on = true) or left again (on = false). The
                    // director evicts the ace and GHOST on its next frame.
                    GhostSide.FakeRemote = Bool(args, "on");
                    return AceStatus(action, GhostSide.FakeRemote ? "a remote player is connected (fake)" : "no remote player");
                case "relaunch":
                    // As if the game were restarted into a new mission: progress read back from the config entry.
                    if (SpookBosses.Settings == null) return Failure("SpooktoberAutomation", action, "Spooktober module not installed");
                    SpookStore.Init(SpookBosses.Settings);
                    break;
                case "rad":
                    return Rad(action);
                case "look":
                    return Look(args);
                case "hell":
                    // The hell look on every unit but the player's within "radius" m of the player (a look check before the faction exists).
                    if (!GameManager.GetLocalAircraft(out Aircraft seat)) return Failure("SpooktoberAutomation", action, "no seated aircraft");
                    float reach = Number(args, "radius", 5000f);
                    var recruits = new List<Unit>();
                    foreach (Unit unit in UnitRegistry.allUnits)
                        if (unit != null && unit != seat && !unit.disabled && !(unit is Building) &&
                            Vector3.Distance(unit.transform.position, seat.transform.position) <= reach) recruits.Add(unit);
                    foreach (Unit unit in recruits) HellArmy.Enlist(unit);
                    return HellMap(action, "enlisted " + recruits.Count + " units");
                case "hell_map":
                    return HellMap(action, null);
                case "hell_status":
                    return HellStatus(action, null);
                case "hell_enter":
                    // As if the host flew through the portal (HellRules.Crossed is pinned by the unit tests).
                    if (!HellGate.Alive) return Failure("SpooktoberAutomation", action, "no gate open");
                    HellGate.Enter();
                    return HellStatus(action, "entered");
                case "portal":
                    // A portal "distance" m ahead of the player at its altitude + "height", facing it; "close": true shuts them all.
                    if (Bool(args, "close")) { HellPortal.CloseAll(); message = "closed"; break; }
                    if (!GameManager.GetLocalAircraft(out Aircraft from)) return Failure("SpooktoberAutomation", action, "no seated aircraft");
                    Vector3 ahead = Vector3.ProjectOnPlane(from.transform.forward, Vector3.up).normalized;
                    Vector3 spot = from.transform.position + ahead * Number(args, "distance", 1500f) + Vector3.up * Number(args, "height", 0f);
                    HellPortal.Spawn(spot, Quaternion.LookRotation(-ahead), Number(args, "radius", 60f));
                    message = "portals open: " + HellPortal.Count;
                    break;
                case "tune":
                    // Every "_Name": number arg set on every material wearing the look (live A/B of the shader's knobs); "shader"
                    // picks Boscali/RadGhost (default), Boscali/Hellfire or Boscali/HellPortal.
                    string tunedShader = Text(args, "shader") ?? "Boscali/RadGhost";
                    var tuned = new HashSet<Material>();
                    foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                        foreach (Material m in r.sharedMaterials)
                            if (m != null && m.shader != null && m.shader.name == tunedShader && tuned.Add(m))
                                foreach (KeyValuePair<string, object> knob in args)
                                    if (knob.Key.StartsWith("_", StringComparison.Ordinal)) m.SetFloat(knob.Key, Number(args, knob.Key, 0f));
                    message = "tuned " + tuned.Count + " materials";
                    break;
                default:
                    return Failure("SpooktoberAutomation", action, "unknown action");
            }
            return Status(action, message);
        }

        // ---- FALLEN ACE (Task 9) -----------------------------------------------------------------------------------------------
        private static readonly AccessTools.FieldRef<Unit, Dictionary<PersistentID, float>> DamageCredit =
            AccessTools.FieldRefAccess<Unit, Dictionary<PersistentID, float>>("damageCredit");
        /// <summary>Hurts the ace through the game's own methods. wound: a lethal Pilot.TakeDamage (impact damage, which armour does
        /// not stop; an aircraft has no vital part, UnitPart.criticalPart is false on the FS-20, run 1: it dies when its pilot does or
        /// bails out of a wreck): absorbed with lives to spare (reborn next frame), the death on the last. eject:
        /// Aircraft.StartEjectionSequence on a flyable aircraft, which the ace refuses (no life lost). hull: a 21 % hit on one part
        /// through UnitPart.TakeDamage, a solid hit that should make it blink away.</summary>
        private static string AceHurt(string how)
        {
            Aircraft ace = FallenAce.Current;
            Pilot pilot = AircraftReadout.PrimaryPilot(ace);
            if (ace == null || ace.disabled || pilot == null) return "no ace to hurt";
            switch (how)
            {
                case "eject":
                    ace.StartEjectionSequence();
                    return "ejection";
                case "hull":
                    foreach (UnitPart part in ace.partLookup)
                        if (part != null && !part.IsDetached() && part.hitPoints > 50f)
                        {
                            part.TakeDamage(0f, 0f, 0f, 0f, 60f, PersistentID.None); // 21 net at DamageScale 0.35
                            return "hull hit on " + part.name;
                        }
                    return "no part to hit";
                default:
                    pilot.TakeDamage(0f, 0f, 0f, 0f, 5000f, PersistentID.None); // 17x its 100 hit points at DamageScale 0.35
                    return "lethal wound";
            }
        }

        /// <summary>The evidence: lives, mood and its changes, blinks, rebirths, phantom veers, kills, gun refills, defeat and chain,
        /// the GHOST registry counts, and the damage the ace's lives dealt to the host's side and to the others (Unit.damageCredit,
        /// the game's own kill attribution, kept per attacker on the victim).</summary>
        private static Dictionary<string, object> AceStatus(string action, string message)
        {
            Aircraft ace = FallenAce.Current;
            GameManager.GetLocalHQ(out FactionHQ hostHq);
            int hostHits = 0, otherHits = 0;
            float hostDamage = 0f, otherDamage = 0f;
            var hit = new List<string>();
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.NetworkHQ == null || GhostSide.IsGhost(unit.NetworkHQ)) continue;
                Dictionary<PersistentID, float> credit = DamageCredit(unit);
                if (credit == null) continue;
                float dealt = 0f;
                foreach (PersistentID id in FallenAce.Ids)
                    if (credit.TryGetValue(id, out float damage)) dealt += damage;
                if (!(dealt > 0f)) continue;
                if (unit.NetworkHQ == hostHq) { hostHits++; hostDamage += dealt; }
                else { otherHits++; otherDamage += dealt; }
                hit.Add(UnitTag(unit) + (unit.disabled ? " (down)" : ""));
            }
            int ghosts = 0;
            foreach (Faction each in FactionRegistry.factions)
                if (each != null && each.factionName == GhostSide.Key) ghosts++;
            Pilot pilot = AircraftReadout.PrimaryPilot(ace);
            int ammo = 0;
            if (ace != null)
                foreach (WeaponStation station in ace.weaponStations)
                    if (station?.WeaponInfo != null && station.WeaponInfo.gun) ammo += station.Ammo;
            var result = new Dictionary<string, object>
            {
                ["ok"] = true, ["step"] = action, ["message"] = message,
                ["alive"] = FallenAce.Alive, ["lives"] = FallenAce.Rules?.Lives ?? 0, ["lives_line"] = FallenAce.Rules?.LivesLine(),
                ["mood"] = FallenAce.Rules?.Mood.ToString(), ["mood_changes"] = FallenAce.MoodChanges,
                ["blinks"] = FallenAce.Blinks, ["rebirths"] = FallenAce.Rebirths, ["veers"] = FallenAce.Veers, ["kills"] = FallenAce.Kills,
                ["refills"] = FallenAce.Refills, ["rounds"] = FallenAce.Rounds, ["defeated"] = FallenAce.WasDefeated, ["chain"] = SpookStore.Current.ChainLevel,
                ["active"] = SpookBosses.Active?.ToString(),
                ["name"] = ace != null ? ace.unitName : null, ["unique"] = ace != null ? ace.UniqueName : null,
                ["ace_on_ghost"] = ace != null && GhostSide.IsGhost(ace.NetworkHQ), ["disabled"] = ace != null && ace.disabled,
                ["ghost_factions"] = ghosts, ["ghost_hq"] = GhostSide.Made, ["listed_hqs"] = FactionRegistry.HQLookup.Count,
                ["state"] = pilot?.currentState?.GetType().Name, ["ammo"] = ammo,
                ["target"] = UnitTag(FallenAce.Target), ["ai_target"] = UnitTag(GameAccess.GetAiCurrentTarget(pilot?.AICombatState)),
                ["host_tracks_ace"] = ace != null && hostHq != null && hostHq.GetTrackingData(ace.persistentID) != null,
                ["host_hits"] = hostHits, ["host_damage"] = hostDamage, ["other_hits"] = otherHits, ["other_damage"] = otherDamage,
                ["hit"] = string.Join("; ", hit),
            };
            Plugin.Logger.LogInfo("[Spooktober] ace " + Describe(result));
            // Registers the ace with the sim, so a record op with "ace" in its ids tracks this life.
            if (ace != null) result["ids"] = new Dictionary<string, object> { ["ace"] = ace };
            return result;
        }

        // ---- rad look (Task 10b) -------------------------------------------------------------------------------------------------

        /// <summary>The look's evidence. The ace: its mesh renderers (root and the parts complex physics unparented) wearing
        /// Boscali/RadGhost, how many of those sit outside the root, and any left bare. The kaiju: renderers wearing it (must be 0, it
        /// looks normal). Every renderer in the scene still wearing it, and the live ember systems.</summary>
        private static Dictionary<string, object> Rad(string action)
        {
            int aceWorn = 0, aceApart = 0, aceBare = 0, kaijuWorn = 0, anywhere = 0, embers = 0, sparks = 0;
            Aircraft ace = FallenAce.Current;
            if (ace != null)
                foreach (Renderer r in RadGhostLook.RenderersOf(ace.gameObject))
                {
                    if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    if (!Ghosted(r)) aceBare++;
                    else
                    {
                        aceWorn++;
                        if (!r.transform.IsChildOf(ace.transform)) aceApart++;
                    }
                }
            Transform model = ModuleServices.TryGet(out IKaijuBoss boss) && boss.Body != null ? boss.Body.transform.Find("Model") : null;
            if (model != null)
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                    if (Ghosted(r)) kaijuWorn++; // the kaiju looks normal: always 0
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                if (Ghosted(r)) anywhere++;
            foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
                if (ps.name == "RadGhostEmbers")
                {
                    embers++;
                    sparks += ps.particleCount;
                }
            Dictionary<string, object> result = Status(action, null);
            result["ace_worn"] = aceWorn;
            result["ace_apart"] = aceApart;
            result["ace_bare"] = aceBare;
            result["kaiju_worn"] = kaijuWorn;
            result["worn_anywhere"] = anywhere;
            result["embers"] = embers;
            result["sparks"] = sparks;
            Plugin.Logger.LogInfo("[Spooktober] rad " + Describe(result));
            return result;
        }

        private static bool Ghosted(Renderer r)
        {
            Material m = r.sharedMaterial;
            return m != null && m.shader != null && m.shader.name == "Boscali/RadGhost";
        }

        /// <summary>The free camera "distance" m from the ace (or, with "at": "player", the seated aircraft) on "bearing" deg off its
        /// nose and "height" m above it, looking at it with a "fov" deg view (the look screenshots); the rad readout plus the range.</summary>
        private static Dictionary<string, object> Look(Dictionary<string, object> args)
        {
            string target = Text(args, "at");
            Transform unit = target == "player" ? (GameManager.GetLocalAircraft(out Aircraft own) ? own.transform : null)
                : target == "portal" ? HellPortal.First?.Root.transform
                : target == "hell" ? FirstHell()
                : FallenAce.Current != null ? FallenAce.Current.transform : null;
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (unit == null || camera == null) return Failure("SpooktoberAutomation", "look", "nothing to look at, or no camera");
            Vector3 at = unit.position;
            Vector3 eye = at + Quaternion.Euler(0f, unit.eulerAngles.y + Number(args, "bearing", 60f), 0f) * Vector3.forward *
                          Number(args, "distance", 600f) + Vector3.up * Number(args, "height", 0f);
            camera.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(at - eye));
            camera.cameraVelocity = Vector3.zero;
            float fov = Number(args, "fov", 60f);
            camera.SetDesiredFoV(fov, fov); // after the state switch, which resets the wanted FoV
            Dictionary<string, object> result = Rad("look");
            result["range"] = Vector3.Distance(eye, at);
            return result;
        }

        /// <summary>Hell's Army on the map: members, their map icons and how many of those draw in the black ink, the open
        /// portals and the HELLGATE signs showing.</summary>
        private static Dictionary<string, object> HellMap(string action, string message)
        {
            int icons = 0, black = 0;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && map.mapIcons != null)
                foreach (MapIcon icon in map.mapIcons)
                    if (icon is UnitMapIcon unitIcon && HellArmy.IsHell(unitIcon.unit))
                    {
                        icons++;
                        Color c = unitIcon.iconImage.color;
                        if (c.r < 0.1f && c.g < 0.1f && c.b < 0.1f) black++;
                    }
            int signs = 0;
            foreach (RectTransform r in UnityEngine.Object.FindObjectsOfType<RectTransform>())
                if (r.name.StartsWith("Hellgate ", StringComparison.Ordinal) && r.gameObject.activeInHierarchy) signs++;
            Dictionary<string, object> result = Status(action, message);
            result["members"] = HellArmy.Count;
            result["icons"] = icons;
            result["black_icons"] = black;
            result["portals"] = HellPortal.Count;
            result["signs"] = signs;
            result["map_open"] = DynamicMap.mapMaximized;
            Plugin.Logger.LogInfo("[Spooktober] hell " + Describe(result));
            return result;
        }

        /// <summary>The gate's evidence: open, waves, spawned, the army alive and on HELL, how many of it the host's side tracks and
        /// how many of it are moving, HELL's tracks, the gate's range, fly-ins, defeat and the chain; "ids" registers the army.</summary>
        private static Dictionary<string, object> HellStatus(string action, string message)
        {
            GameManager.GetLocalHQ(out FactionHQ mine);
            FactionHQ hell = HellSide.Side.HQ;
            int onHell = 0, trackedByHost = 0, moving = 0, hellTracks = 0;
            var ids = new Dictionary<string, object>();
            foreach (Unit unit in HellGate.Units)
            {
                if (unit == null || unit.disabled) continue;
                if (HellSide.Side.Owns(unit.NetworkHQ)) onHell++;
                if (mine != null && mine.GetTrackingData(unit.persistentID) != null) trackedByHost++;
                if (unit.speed > 1f) moving++;
                if (ids.Count < 8) ids["hell" + ids.Count] = unit;
            }
            if (hell != null)
                foreach (Unit unit in UnitRegistry.allUnits)
                    if (unit != null && !unit.disabled && unit.NetworkHQ != hell && hell.GetTrackingData(unit.persistentID) != null) hellTracks++;
            HellPortal portal = HellGate.Portal;
            float range = portal != null && GameManager.GetLocalAircraft(out Aircraft own)
                ? Vector3.Distance(own.transform.position, portal.Root.transform.position) : 0f;
            Dictionary<string, object> result = Status(action, message);
            result["gate_open"] = HellGate.Alive;
            result["waves"] = HellGate.Waves;
            result["theme"] = HellGate.LastTheme.ToString();
            result["falters"] = HellGate.Falters;
            result["air"] = HellGate.Aircraft.Count;
            result["spawned_air"] = HellGate.SpawnedAir;
            result["wardens"] = HellGate.Guards.Count;
            result["gate_scale"] = portal != null ? portal.Scale : 0f;
            result["spawned"] = HellGate.Spawned;
            result["army"] = HellGate.Units.Count;
            result["on_hell"] = onHell;
            result["tracked_by_host"] = trackedByHost;
            result["moving"] = moving;
            result["hell_tracks"] = hellTracks;
            result["hell_hq"] = HellSide.Side.Made;
            result["members"] = HellArmy.Count;
            result["range"] = range;
            result["entered"] = HellGate.Entered;
            result["defeated"] = HellGate.WasDefeated;
            Plugin.Logger.LogInfo("[Spooktober] hell gate " + Describe(result));
            if (ids.Count > 0) result["ids"] = ids;
            return result;
        }

        /// <summary>A live ground unit wearing Boscali/Hellfire, else any live unit wearing it.</summary>
        private static Transform FirstHell()
        {
            Transform any = null;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled) continue;
                Renderer r = unit.GetComponentInChildren<Renderer>();
                Material m = r != null ? r.sharedMaterial : null;
                if (m == null || m.shader == null || m.shader.name != "Boscali/Hellfire") continue;
                if (unit is GroundVehicle) return unit.transform;
                if (any == null) any = unit.transform;
            }
            return any;
        }

        private static string UnitTag(Unit unit) =>
            unit == null ? "" : unit.unitName + "/" + (unit.NetworkHQ != null && unit.NetworkHQ.faction != null ? unit.NetworkHQ.faction.factionName : "none");

        private static bool Parse<T>(string text, out T value) where T : struct =>
            Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value);

        private static Dictionary<string, object> Status(string action, string message)
        {
            SpookProgress p = SpookStore.Current;
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["step"] = action, ["message"] = message,
                ["chain"] = p.ChainLevel,
                ["active"] = SpookBosses.Active?.ToString(), ["sounds"] = SpookAudio.Played,
            };
        }
    }
}
#endif
