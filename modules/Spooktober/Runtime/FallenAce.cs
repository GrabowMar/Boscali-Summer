using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Spooktober.Configuration;
using BoscaliSummer.Modules.Spooktober.Domain;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// The FALLEN ACE (spec §4): a gun-only FS-20 on the GHOST side with three lives, owned by the host. This class holds its state,
    /// its spawn, blink and rebirth, and the hooks the patches call (Patches/FallenAcePatches.cs); <see cref="FallenAceDirector"/>
    /// flies its moods. Single player / host without clients only (ruling R15). <see cref="Defeated"/> fires on the third real kill
    /// only: a rebirth, <see cref="Dismiss"/> or a scene end never fires it.
    /// </summary>
    internal static class FallenAce
    {
        internal const string Name = "FALLEN ACE";

        /// <summary>Host: the third real kill.</summary>
        public static event Action Defeated;

        /// <summary>This life's aircraft; null between a lethal hit and the rebirth, and while no ace is out.</summary>
        public static Aircraft Current { get; private set; }
        /// <summary>The boss is on the field: spawned and neither dismissed nor defeated (a rebirth in progress counts).</summary>
        public static bool Alive => Rules != null;
        internal static AceRules Rules { get; private set; }
        /// <summary>Whom the director hunts; the target-choice patch forces it.</summary>
        internal static Unit Target;

        // Evidence since the last spawn, for the sim and the log.
        internal static int MoodChanges, Blinks, Rebirths, Veers, Kills, Refills, Rounds;
        internal static bool WasDefeated;
        /// <summary>Every life's persistent id: damage credit is kept per attacker id, and each rebirth is a new aircraft.</summary>
        internal static readonly List<PersistentID> Ids = new List<PersistentID>();
        internal static float LastHitAt = float.NegativeInfinity, MissileAt = float.NegativeInfinity;

        private static readonly List<Vector2> hits = new List<Vector2>(); // x: when, y: hull fraction lost
        private static readonly Dictionary<FactionHQ, float> homing = new Dictionary<FactionHQ, float>(); // side -> last missile aim at the ace
        private static ConditionalWeakTable<Missile, object> veers = new ConditionalWeakTable<Missile, object>();
        private static readonly List<Rigidbody> bodies = new List<Rigidbody>();
        private static readonly List<Unit> leftovers = new List<Unit>();
        private static readonly FieldInfo ImpactPrev = AccessTools.Field(typeof(ImpactDetector), "velocityPrev");
        private static readonly FieldInfo FuelPrev = AccessTools.Field(typeof(FuelTank), "velocityPrev");
        private static readonly MethodInfo AssessMethod = AccessTools.Method(typeof(AIPilotCombatModes), "AssessHQTargets");
        private static bool rebirthDue;
        private static float gunScale = 1f;
        private static int serial;

        /// <summary>Why the ace cannot come out now, or null.</summary>
        public static string CannotSpawn()
        {
            if (!GameAccess.IsServer()) return "HOST ONLY";
            if (GhostSide.RemoteClients()) return Name + ": SINGLE PLAYER ONLY FOR NOW";
            if (!GameManager.GetLocalAircraft(out Aircraft host) || host == null || host.disabled) return Name + ": GET AIRBORNE FIRST";
            return Airframe() == null ? Name + ": NO AIRFRAME" : null;
        }

        /// <summary>Host: the ace 8 km ahead of the host's aircraft at its altitude (at least 1200 m above the sea), nose on to it.
        /// The answer is the line for the menu.</summary>
        public static string Spawn(SpooktoberSettings settings)
        {
            string why = CannotSpawn();
            if (why != null) return why;
            if (Alive) Dismiss();
            FactionHQ ghost = GhostSide.Ensure();
            if (ghost == null) return Name + ": NO GHOST SIDE";
            GameManager.GetLocalAircraft(out Aircraft host);
            Vector3 ahead = Flat(host.transform.forward);
            Vector3 at = host.transform.position + ahead * 8000f;
            at.y = Mathf.Max(host.transform.position.y, Datum.LocalSeaY + 1200f);
            Rules = new AceRules(settings.Tuning());
            gunScale = settings.AceGunDamageScale.Value;
            MoodChanges = Blinks = Rebirths = Veers = Kills = Refills = Rounds = 0;
            WasDefeated = false;
            Ids.Clear();
            if (Make(ghost, SafeHeight(at), Quaternion.LookRotation(-ahead, Vector3.up), -ahead * 220f) == null)
            {
                Forget();
                Retire();
                return Name + ": SPAWN FAILED";
            }
            Announce(Name + " INBOUND");
            Announce(Rules.LivesLine());
            Taunt("arrive", "I REMEMBER THIS SKY. DO YOU REMEMBER ME?");
            return Name + " INBOUND";
        }

        /// <summary>Host: takes the ace off the field without a kill (out of combat first), then the GHOST side. Never a defeat.</summary>
        public static string Dismiss()
        {
            if (!Alive) return "NO BOSS";
            Aircraft ace = Current;
            Forget();
            if (ace != null && !ace.disabled)
            {
                Embers(ace.transform.position);
                Despawn(ace);
            }
            Retire();
            return Name + " DISMISSED";
        }

        /// <summary>Host: GHOST off the field once no ace flies on it: whatever is left on it (a wreck, a bailed-out pilot) is
        /// removed without a kill and the HQ is forgotten, so neither lingers (review fix 1). They go together at the end of the
        /// frame, so nothing on GHOST outlives its HQ.</summary>
        internal static void Retire()
        {
            if (!GhostSide.Made) return;
            leftovers.Clear();
            foreach (Unit unit in UnitRegistry.allUnits)
                if (unit != null && GhostSide.IsGhost(unit.NetworkHQ) && unit.Identity != null && unit.Identity.NetId != 0) leftovers.Add(unit);
            Mirage.ServerObjectManager objects = GameAccess.NetworkManagerOrNull?.ServerObjectManager;
            foreach (Unit unit in leftovers)
            {
                if (unit is Aircraft aircraft && aircraft.pilots != null)
                    foreach (Pilot pilot in aircraft.pilots)
                        if (pilot != null) pilot.SwitchState(null);
                try { objects?.Destroy(unit.Identity, !unit.Identity.IsSceneObject); }
                catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] GHOST leftover " + unit.name + ": " + e.Message); }
            }
            leftovers.Clear();
            GhostSide.Forget();
        }

        /// <summary>Drops every reference (a scene change took the aircraft): no despawn, no defeat.</summary>
        internal static void Forget()
        {
            Rules = null;
            Current = null;
            Target = null;
            rebirthDue = false;
            hits.Clear();
            homing.Clear();
            LastHitAt = MissileAt = float.NegativeInfinity;
            veers = new ConditionalWeakTable<Missile, object>();
        }

        /// <summary>Every frame on the host: a rebirth the patches asked for, and a death they let through (or never saw).</summary>
        internal static void Frame()
        {
            if (!Alive) return;
            Aircraft ace = Current;
            if (rebirthDue)
            {
                Rebirth(ace, wreck: false);
                return;
            }
            if (ace == null)
            {
                Plugin.Logger.LogWarning("[Spooktober] FALLEN ACE aircraft vanished (not by a kill); the boss is gone.");
                Forget();
                Retire();
                return;
            }
            if (!ace.disabled) return;
            // A death that went through. The last life is the defeat; with lives to spare (a death path the patches do not cover)
            // it is reborn all the same and the wreck stays.
            if (Rules.AbsorbLethal()) Rebirth(ace, wreck: true);
            else Defeat(ace);
        }

        private static void Defeat(Aircraft ace)
        {
            Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE defeated: the third kill.");
            if (ace != null) Embers(ace.transform.position);
            Announce(Name + " DOWN");
            Taunt("death", "FINALLY... THE GROUND IS QUIET.");
            Forget();
            Retire(); // the wreck goes up in the embers above
            WasDefeated = true;
            try { Defeated?.Invoke(); }
            catch (Exception e) { Plugin.Logger.LogError("[Spooktober] FALLEN ACE defeat listener: " + e); }
        }

        /// <summary>The old body bursts into embers (removed without a kill, or left as a wreck) and a fresh one forms 2-4 km behind
        /// its target at the target's altitude, at full health, with the same rules (lives, moods, cooldowns).</summary>
        private static void Rebirth(Aircraft old, bool wreck)
        {
            rebirthDue = false;
            Current = null;
            AceTuning t = Rules.Tuning;
            Vector3 from = old != null ? old.transform.position : Vector3.zero;
            Vector3 heading = old != null ? Flat(old.transform.forward) : Vector3.forward;
            float speed = old != null ? Mathf.Max(old.speed, 180f) : 200f;
            if (old != null)
            {
                Embers(from);
                if (!wreck) Despawn(old);
            }
            // Behind its target, else 2-4 km back along its own track (never in the old body's place).
            Unit anchor = Anchor();
            Vector3 at = from - heading * Random.Range(t.BlinkMin, t.BlinkMax);
            if (anchor != null)
            {
                heading = Flat(anchor.transform.forward);
                at = anchor.transform.position - heading * Random.Range(t.BlinkMin, t.BlinkMax);
                at.y = anchor.transform.position.y;
            }
            FactionHQ ghost = GhostSide.Ensure();
            Aircraft fresh = ghost != null ? Make(ghost, SafeHeight(at), Quaternion.LookRotation(heading, Vector3.up), heading * speed) : null;
            if (fresh == null)
            {
                Plugin.Logger.LogError("[Spooktober] FALLEN ACE rebirth failed; the boss is gone (no defeat).");
                Forget();
                Retire();
                return;
            }
            Rebirths++;
            hits.Clear();
            LastHitAt = float.NegativeInfinity;
            Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE reborn (" + (wreck ? "after a kill" : "lethal hit absorbed") + "), " +
                                  Rules.Lives + " lives left, " + (anchor != null ? "behind " + anchor.unitName : "back along its track") + ".");
            Announce(Rules.LivesLine());
            if (Rules.Lives >= 2) Taunt("reborn1", "YOU KILLED ME ONCE BEFORE. IT DID NOT TAKE.");
            else Taunt("reborn2", "TWICE NOW. THE THIRD ONE YOU HAVE TO EARN.");
        }

        /// <summary>Spawner.SpawnAircraft by hand: it names the unit after its definition right before the spawn, and the kill feed
        /// copies the name at spawn (PersistentUnit), so the ace is named first. Max skill and bravery, guns only.</summary>
        private static Aircraft Make(FactionHQ hq, Vector3 at, Quaternion facing, Vector3 velocity)
        {
            AircraftDefinition definition = Airframe();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (definition == null || spawner == null) return null;
            GameObject go = null;
            try
            {
                go = UnityEngine.Object.Instantiate(definition.unitPrefab, at, facing);
                Aircraft ace = go.GetComponent<Aircraft>();
                ace.NetworkHQ = hq;
                ace.NetworkUniqueName = "FALLEN-ACE-" + ++serial;
                ace.NetworkspawningHangar = null;
                ace.NetworkstartPosition = at.ToGlobalPosition();
                ace.NetworkstartRotation = facing;
                ace.NetworkstartingVelocity = velocity;
                ace.Networkloadout = GunOnly(definition);
                ace.NetworkfuelLevel = 1f;
                ace.skill = 1f;   // max, like the bravery; set outright, not scaled by the HQ's airSkillMultiplier as Spawner would
                ace.bravery = 1f;
                ace.SetLiveryKey(default);
                ace.NetworkplayerRef = PlayerRef.Invalid;
                ace.NetworkunitName = Name;
                spawner.ServerObjectManager.Spawn(ace.Identity);
                Current = ace;
                Ids.Add(ace.persistentID);
                WakeCombat(ace);
                Reassess(ace); // engage at once, not on the AI's next 5 s look
                Embers(at);
                Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE " + ace.UniqueName + " (" + definition.unitName + ") on " + hq.faction.factionName +
                                      ", " + Rules.Lives + " lives.");
                return ace;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[Spooktober] FALLEN ACE spawn failed: " + e);
                if (go != null) UnityEngine.Object.Destroy(go);
                return null;
            }
        }

        /// <summary>The FS-20 Vortex; else the first plane with a gun.</summary>
        private static AircraftDefinition Airframe()
        {
            List<AircraftDefinition> all = GameAccess.EncyclopediaOrNull?.aircraft;
            if (all == null) return null;
            AircraftDefinition fallback = null;
            foreach (AircraftDefinition each in all)
            {
                if (each?.unitPrefab == null || each.unitPrefab.GetComponentInChildren<AutopilotPlane>(true) == null || GunOnly(each) == null) continue;
                if (each.unitName != null && each.unitName.Contains("Vortex")) return each;
                if (fallback == null) fallback = each;
            }
            return fallback;
        }

        /// <summary>The start preset with every store but the guns taken off (a full-length list: an empty one gets the native fit);
        /// null when it carries no gun.</summary>
        private static Loadout GunOnly(AircraftDefinition definition)
        {
            List<Loadout> presets = definition.aircraftParameters?.loadouts;
            if (presets == null || presets.Count == 0 || presets[presets.Count > 1 ? 1 : 0]?.weapons == null) return null;
            var weapons = new List<WeaponMount>(presets[presets.Count > 1 ? 1 : 0].weapons);
            bool gun = false;
            for (int i = 0; i < weapons.Count; i++)
                if (weapons[i]?.info == null || !weapons[i].info.gun) weapons[i] = null;
                else gun = true;
            return gun ? new Loadout { weapons = weapons } : null;
        }

        /// <summary>Out of combat first (a live AI aircraft destroyed mid-attack throws in its combat state), then gone, no kill.</summary>
        private static void Despawn(Aircraft ace)
        {
            try
            {
                if (ace.pilots != null)
                    foreach (Pilot pilot in ace.pilots)
                        if (pilot != null) pilot.SwitchState(null);
                GameAccess.NetworkManagerOrNull?.ServerObjectManager.Destroy(ace.Identity, !ace.Identity.IsSceneObject);
            }
            catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] FALLEN ACE despawn: " + e.Message); }
        }

        /// <summary>Keeps the ace in the game's combat state (it would land after 15 s without a target).</summary>
        internal static void WakeCombat(Aircraft ace)
        {
            Pilot pilot = AircraftReadout.PrimaryPilot(ace);
            if (pilot == null || pilot.dead || pilot.ejected || pilot.AICombatState == null) return;
            if (pilot.currentState != pilot.AICombatState) pilot.SwitchState(pilot.AICombatState);
        }

        /// <summary>The combat AI picks its target every 5 s; a mood or target change asks it at once.</summary>
        internal static void Reassess(Aircraft ace)
        {
            Pilot pilot = AircraftReadout.PrimaryPilot(ace);
            if (AssessMethod == null || pilot?.AICombatState == null || pilot.currentState != pilot.AICombatState) return;
            try { AssessMethod.Invoke(pilot.AICombatState, null); }
            catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] FALLEN ACE reassess: " + (e.InnerException ?? e).Message); }
        }

        // ---- blinks ------------------------------------------------------------------------------------------------------------

        /// <summary>To <paramref name="distance"/> behind <paramref name="anchor"/> at its altitude, on its heading: on its six.</summary>
        internal static void BlinkBehind(Aircraft ace, Unit anchor, float distance)
        {
            Vector3 heading = Flat(anchor.transform.forward);
            Vector3 to = anchor.transform.position - heading * distance;
            to.y = anchor.transform.position.y;
            Quaternion turn = Quaternion.FromToRotation(Flat(ace.transform.forward), heading);
            Blink(ace, SafeHeight(to), turn);
        }

        /// <summary><paramref name="distance"/> to a random side of its own heading, same heading and speed.</summary>
        internal static void BlinkSideways(Aircraft ace, float distance)
        {
            Vector3 right = Vector3.Cross(Vector3.up, Flat(ace.transform.forward));
            Blink(ace, SafeHeight(ace.transform.position + right * (Random.value < 0.5f ? -distance : distance)), Quaternion.identity);
        }

        /// <summary>Moves the whole aircraft (its root, every part body, the parts complex physics unparented) to
        /// <paramref name="to"/>, turned by <paramref name="turn"/> with its velocity, as Wing's SafeRelocate moves one; the
        /// finite-difference velocities restart (zero = no previous value), so the jump is no impact. Embers at both ends.</summary>
        private static void Blink(Aircraft ace, Vector3 to, Quaternion turn)
        {
            Transform root = ace.transform;
            Vector3 from = root.position;
            Embers(from);
            bodies.Clear();
            foreach (Rigidbody rb in ace.GetComponentsInChildren<Rigidbody>()) bodies.Add(rb);
            if (ace.partLookup != null)
                foreach (UnitPart part in ace.partLookup)
                    if (part != null && !part.IsDetached() && part.rb != null && !bodies.Contains(part.rb)) bodies.Add(part.rb);
            foreach (Rigidbody rb in bodies)
            {
                Vector3 p = to + turn * (rb.position - from);
                Quaternion r = turn * rb.rotation;
                rb.position = p;
                rb.rotation = r;
                rb.velocity = turn * rb.velocity;
                rb.angularVelocity = turn * rb.angularVelocity;
                if (!rb.transform.IsChildOf(root)) rb.transform.SetPositionAndRotation(p, r);
            }
            root.SetPositionAndRotation(to, turn * root.rotation);
            ace.velocityPrev = Vector3.zero;
            if (ace.pilots != null)
                foreach (Pilot pilot in ace.pilots)
                    if (pilot != null) pilot.velocityPrev = Vector3.zero;
            ResetPrev(ace.gameObject);
            if (ace.partLookup != null)
                foreach (UnitPart part in ace.partLookup)
                    if (part != null && !part.transform.IsChildOf(root)) ResetPrev(part.gameObject);
            bodies.Clear();
            Embers(to);
            Blinks++;
        }

        private static void ResetPrev(GameObject o)
        {
            if (ImpactPrev != null)
                foreach (ImpactDetector d in o.GetComponentsInChildren<ImpactDetector>(true)) ImpactPrev.SetValue(d, Vector3.zero);
            if (FuelPrev != null)
                foreach (FuelTank f in o.GetComponentsInChildren<FuelTank>(true)) FuelPrev.SetValue(f, Vector3.zero);
        }

        // ---- hooks for the patches (host) ----------------------------------------------------------------------------------------

        /// <summary>UnitPart.TakeDamage on the ace: the hit scaled by DamageScale and its hull loss kept for the blink window. A hit
        /// that would break a vital part is a rebirth while lives remain (false: skip it) and the real death on the last life.</summary>
        internal static bool TakePartHit(UnitPart part, bool critical, ref float pierce, ref float blast, float amount, ref float fire, ref float impact)
        {
            if (Rules == null) return true;
            if (rebirthDue) return false; // between the lethal hit and the rebirth it is only embers
            ArmorProperties armor = part.GetArmorProperties();
            Scale(armor, Rules.Tuning.DamageScale, ref pierce, ref blast, ref fire, ref impact);
            float net = Net(armor, pierce, blast, amount, fire, impact);
            if (!(net > 0f)) return true;
            if (critical && part.hitPoints - net <= 0f && !part.parentUnit.disabled) return !Lethal("a vital hit on " + part.name);
            LastHitAt = Time.time;
            hits.Add(new Vector2(LastHitAt, net * 0.01f)); // part hit points run 0-100
            return true;
        }

        /// <summary>Pilot.ApplyDamage on the ace's pilot: scaled, and a killing wound is a rebirth while lives remain.</summary>
        internal static bool TakePilotHit(float hitPoints, ref float pierce, ref float blast, ref float fire, ref float impact)
        {
            if (Rules == null) return true;
            if (rebirthDue) return false;
            float k = Rules.Tuning.DamageScale;
            pierce *= k;
            blast *= k;
            fire *= k;
            impact *= k;
            return !(hitPoints - (pierce + blast + fire + impact) < 0f) || !Lethal("a killing wound");
        }

        /// <summary>Aircraft.StartEjectionSequence on the ace (true skips it). A bail-out from a wreck (parts gone, in the sea, stopped
        /// on the ground: the AI's own checks) is a lethal moment: a rebirth while lives remain, the death on the last. One from a
        /// flyable aircraft is refused: GHOST has no airbase, so the AI ejects whenever it would land or take off (run 4: two lives
        /// lost that way after its only target died); the director puts it back into combat.</summary>
        internal static bool SkipEjection(Aircraft ace)
        {
            if (Rules == null) return false;
            if (rebirthDue) return true;
            bool wreck = ace.partDamageTracker.GetDetachedRatio() > 0.12f || ace.cockpit.xform.position.y < Datum.LocalSeaY ||
                         (ace.speed < 1f && ace.radarAlt < 1f);
            if (wreck) return Lethal("a bail-out from a wreck");
            Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE refused a bail-out from a flyable aircraft.");
            return true;
        }

        /// <summary>True when the lethal moment is absorbed: the director reborn it next frame. False on the last life.</summary>
        private static bool Lethal(string what)
        {
            if (!Rules.AbsorbLethal()) return false;
            rebirthDue = true;
            Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE absorbed " + what + "; " + Rules.Lives + " lives left.");
            return true;
        }

        /// <summary>UnitPart.TakeDamage and Pilot.TakeDamage on anyone the ace shot: the gunnery perk (AceGunDamageScale).</summary>
        internal static void ScaleGunHit(ArmorProperties armor, ref float pierce, ref float blast, ref float fire, ref float impact)
        {
            if (gunScale != 1f) Scale(armor, gunScale, ref pierce, ref blast, ref fire, ref impact);
        }

        /// <summary>Missile.SetAimpoint for a missile after the ace: a missile launched beyond PhantomRange is a phantom and aims
        /// 350 m to one side (decided on its first call, kept per missile). Only a real one marks a missile inbound for a Feint
        /// (run 3: two AI fighters' phantoms from 6-24 km made it feint every 10 s and stop shooting).</summary>
        internal static void Aim(Missile missile, ref GlobalPosition aimPoint)
        {
            Aircraft ace = Current;
            if (ace == null || Rules == null) return;
            if (missile.NetworkHQ != null) homing[missile.NetworkHQ] = Time.time;
            if (!veers.TryGetValue(missile, out object offset))
            {
                offset = FirstSight(missile, ace);
                veers.Add(missile, offset);
            }
            Vector3 veer = (Vector3)offset;
            if (veer == Vector3.zero) MissileAt = Time.time;
            else aimPoint += veer;
        }

        private static object FirstSight(Missile missile, Aircraft ace)
        {
            Vector3 line = ace.transform.position - missile.transform.position;
            if (!AceRules.PhantomVeers(line.magnitude, Rules.Tuning.PhantomRange)) return Vector3.zero;
            Veers++;
            Vector3 side = Vector3.Cross(line.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE phantom: a missile from " + (line.magnitude / 1000f).ToString("0.0") + " km veers off.");
            return side.normalized * (Random.value < 0.5f ? -350f : 350f);
        }

        /// <summary>CombatAI.ChooseHQTarget for the ace: no target in a Stalk (it breaks off), else the director's target with the
        /// station the game's own analysis rates best. The game's choice stands when that fails.</summary>
        internal static void ChooseTarget(Aircraft ace, List<WeaponStation> stations, ref CombatAI.TargetSearchResults result)
        {
            if (Rules == null) return;
            if (Rules.Mood == AceMood.Stalk)
            {
                result = new CombatAI.TargetSearchResults(null, null, 0f, result.outOfAmmo);
                return;
            }
            Unit target = Target;
            TrackingInfo track = target != null && !target.disabled && ace.NetworkHQ != null ? ace.NetworkHQ.GetTrackingData(target.persistentID) : null;
            if (track == null || stations == null) return;
            WeaponStation best = null;
            float opportunity = 0f;
            foreach (WeaponStation station in stations)
            {
                if (station == null || station.Cargo || station.Ammo <= 0 || station.WeaponInfo == null) continue;
                float value = CombatAI.AnalyzeTarget(station, ace, track, 0f, -1f, 100f).opportunity;
                if (value <= opportunity) continue;
                opportunity = value;
                best = station;
            }
            if (best != null) result = new CombatAI.TargetSearchResults(target, best, opportunity, result.outOfAmmo);
        }

        // ---- helpers ------------------------------------------------------------------------------------------------------------

        /// <summary>Hull fraction lost in the last <paramref name="window"/> seconds (older hits are dropped).</summary>
        internal static float HullLost(float now, float window)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (now - hits[i].x > window) hits.RemoveAt(i);
            float lost = 0f;
            foreach (Vector2 h in hits) lost += h.y;
            return lost;
        }

        internal static void ClearHits() => hits.Clear();

        /// <summary>A missile of <paramref name="hq"/> aimed at the ace in the last 2 s (seekers re-aim every frame).</summary>
        internal static bool Homing(FactionHQ hq, float now) => homing.TryGetValue(hq, out float at) && now - at < 2f;

        /// <summary>What a rebirth or a blink lands behind: the target, else the host's aircraft.</summary>
        internal static Unit Anchor()
        {
            if (Target != null && !Target.disabled) return Target;
            return GameManager.GetLocalAircraft(out Aircraft host) && host != null && !host.disabled ? host : null;
        }

        /// <summary>A share of the hit past the armour: the net damage the part takes is scaled by <paramref name="k"/>.</summary>
        private static void Scale(ArmorProperties armor, float k, ref float pierce, ref float blast, ref float fire, ref float impact)
        {
            if (armor == null) return;
            if (pierce > armor.pierceArmor) pierce = armor.pierceArmor + (pierce - armor.pierceArmor) * k;
            blast *= k;
            if (fire > armor.fireArmor) fire = armor.fireArmor + (fire - armor.fireArmor) * k;
            impact *= k;
        }

        /// <summary>UnitPart.TakeDamage's own sum of the net damage.</summary>
        private static float Net(ArmorProperties armor, float pierce, float blast, float amount, float fire, float impact) =>
            armor == null ? pierce + blast * amount + fire + impact
                : Mathf.Max(pierce - armor.pierceArmor, 0f) / Mathf.Max(armor.pierceTolerance, 0.01f)
                  + blast * amount / Mathf.Max(armor.blastTolerance, 0.01f)
                  + Mathf.Max(fire - armor.fireArmor, 0f) / Mathf.Max(armor.fireTolerance, 0.01f) + impact;

        internal static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }

        /// <summary>At least 250 m above the ground and the sea.</summary>
        private static Vector3 SafeHeight(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 6000f, Vector3.down, out RaycastHit hit, 14000f, PhysicsLayers.StaticsMask))
                at.y = Mathf.Max(at.y, hit.point.y + 250f);
            at.y = Mathf.Max(at.y, Datum.LocalSeaY + 250f);
            return at;
        }

        /// <summary>The rad look's ember burst (blink, rebirth, arrival); a look that fails never stops the mechanics.</summary>
        private static void Embers(Vector3 at)
        {
            try { RadGhostLook.Burst(at); }
            catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] FALLEN ACE ember burst: " + e.Message); }
        }

        /// <summary>The HQ message every peer sees (the kaiju's announcement path).</summary>
        internal static void Announce(string text) => NetworkSceneSingleton<MessageManager>.i?.RpcAllHQMessage(text);

        /// <summary>A COMMS line from FALLEN ACE on the enemy net, lightly glitched.</summary>
        internal static void Taunt(string key, string text)
        {
            if (!ModuleServices.TryGet(out IChatter chatter)) return;
            chatter.Say(chatter.Station(ChatterNet.Enemy, Name), "spook:ace:" + key, SpookText.Glitch(text, serial * 7 + key.Length, 0.08f),
                ChatterPriority.Tactical);
        }
    }
}
