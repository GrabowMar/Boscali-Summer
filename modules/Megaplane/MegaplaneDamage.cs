using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Component damage for the G-240. Each donor damage island (MegaplaneTemplate.Mapping) is a component: wing panels,
    /// engine pods, tailplane, booms, fins, rudders, and the core (flight deck, fuselage). Hits reach an island through its art
    /// hitboxes; when its hit points run out the host breaks it off with the native networked DetachPart (every peer drops the
    /// piece on its own rigidbody, sub-islands go with it, guns on it fall silent through Turret.criticalParts). What is left
    /// sets the handling read by <see cref="MegaplaneFlight"/>: wing lift per side (a weak side drops and drags the carrier
    /// into a turn), pods per side, tailplane pitch authority, fin/rudder yaw authority. Losing most of one wing or the core
    /// disables the carrier; it then glides down in a long burning dive, shedding pieces, instead of dropping like a stone.</summary>
    internal sealed class MegaplaneDamage : MonoBehaviour
    {
        // Share of a side's lift carried by each island (donor chain engine > gearbay > {nacelle, wing1b > {flap, wing1a,
        // wing2 > {wingtip, aileron}}}), mapped to the five wing sections (assets/.../breaks.py): engine = root (to x 35,
        // with the inboard pod), gearbay = inner, wing1b = mid, wing2 = outer, wingtip = tip; wing1a = outer flap,
        // flap = third pod. Losing the inner section takes everything outboard and kills the carrier.
        private static readonly Dictionary<string, float> Lift = new Dictionary<string, float>
        { { "engine", .30f }, { "gearbay", .25f }, { "wing1b", .17f }, { "wing2", .12f }, { "wingtip", .04f },
          { "wing1a", .06f }, { "aileron", .04f }, { "flap", .02f } };
        private static readonly HashSet<string> Core = new HashSet<string> { "cockpit", "fuselage_F", "fuselage_R", "nose", "intake_L", "intake_R" };
        internal static readonly HashSet<string> Critical = new HashSet<string> { "cockpit", "fuselage_F", "fuselage_R", "body_F" };
        private const float DeadSide = .3f, BreakupMin = 3f, BreakupMax = 7f, EffectScale = 6f;

        private Aircraft aircraft;
        private AeroPart[] parts;
        private (string stem, string side)[] keys;   // parsed once: Update walked EndsWith/SubString per part per frame
        private readonly HashSet<Transform> scaled = new HashSet<Transform>();
        private readonly HashSet<AeroPart> dirty = new HashSet<AeroPart>();
        private float nextBreak, nextCheck;

        internal float LiftL { get; private set; } = 1f;
        internal float LiftR { get; private set; } = 1f;
        internal float PitchAuthority { get; private set; } = 1f;
        internal float YawAuthority { get; private set; } = 1f;
        internal IEnumerable<string> Lost => parts.Where(p => p != null && p.IsDetached()).Select(p => p.name);

        internal static bool Detachable(AeroPart p) => p != null && !Core.Contains(p.name) && !Critical.Contains(p.name) && p.parentUnit != null && p.gameObject != p.parentUnit.gameObject;

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            parts = GetComponentsInChildren<AeroPart>(true).Where(p => p.gameObject != gameObject).ToArray();
            keys = new (string, string)[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                AeroPart part = parts[i];
                string n = part.name;
                keys[i] = (n.EndsWith("_L") || n.EndsWith("_R") ? n.Substring(0, n.Length - 2) : n,
                           n.EndsWith("_L") ? "L" : n.EndsWith("_R") ? "R" : null);
                part.onPartDetached += Separate;
                part.onApplyDamage += e => dirty.Add(part);
            }
        }

        /// <summary>Host: hit points of an island after a hit (hitbox path). Breaks it off once they are gone.</summary>
        internal void AfterHit(AeroPart part, float hitPoints)
        {
            if (hitPoints > 0f || !aircraft.IsServer || part == null || part.IsDetached()) return;
            // a body section shot through kills the carrier (native criticalPart) and then breaks away, as a wing panel does
            if (Detachable(part)) Break(part); else if (Critical.Contains(part.name)) Break(part, true);
            // the flight-deck section hangs off the root island in the donor chain: without the forward body under it, it goes too
            if (part.name == "body_F") Break(parts.FirstOrDefault(p => p != null && p.name == "fuselage_F"), true);

        }

        /// <summary>Host: break an island off through the native networked path.</summary>
        internal void Break(AeroPart part, bool dying = false)
        {
            // dying: any section may go, body sections included: the carrier comes apart in the air
            if (part == null || part.IsDetached() || !(Detachable(part) || dying && part.parentUnit != null && part.gameObject != part.parentUnit.gameObject)) return;
            Vector3 pos = part.transform.position, out_ = pos - aircraft.rb.worldCenterOfMass;
            out_.y = 0f;
            Vector3 v = aircraft.rb.GetPointVelocity(pos) + out_.normalized * 4f + Vector3.down * 3f;
            aircraft.DetachPart(part.id, v, pos - aircraft.transform.position);
        }

        // Every peer: a piece leaving overlaps the hull it came from; without this the two convex sets shove each other apart.
        private void Separate(UnitPart part)
        {
            Collider[] piece = part.GetComponentsInChildren<Collider>(true);
            var set = new HashSet<Collider>(piece);
            foreach (Collider hull in aircraft.GetComponentsInChildren<Collider>(true))
                if (!set.Contains(hull))
                    foreach (Collider c in piece) Physics.IgnoreCollision(c, hull);
            ScaleEffects(part.transform);
            // The airframe feels it: a lurch toward the side that lost the piece (more the further out), a nose bob.
            if (aircraft.LocalSim && aircraft.rb != null && !aircraft.rb.isKinematic)
            {
                Vector3 at = transform.InverseTransformPoint(part.transform.position);
                float side = Mathf.Clamp(at.x / 120f, -1f, 1f);
                aircraft.rb.AddRelativeTorque(new Vector3(.04f * Mathf.Sign(-at.z), .03f * side, -.16f * side), ForceMode.VelocityChange);
            }
        }

        private void Update()
        {
            float l = 0f, r = 0f, pitch = 1f, yawL = 1f, yawR = 1f;
            for (int i = 0; i < parts.Length; i++)
            {
                AeroPart p = parts[i];
                if (p == null) continue;
                bool up = !p.IsDetached();
                string side = keys[i].side, stem = keys[i].stem;
                // As vanilla wingEffectiveness: a holed panel still lifts, from half at 0 hp to all of it undamaged.
                if (side != null && Lift.TryGetValue(stem, out float w) && up) { w *= Mathf.Lerp(.5f, 1f, p.hitPoints * .01f); if (side == "L") l += w; else r += w; }
                string n = p.name;
                if (n == "hstab" && !up) pitch = 0f;
                if ((stem == "vstab1" || stem == "nozzle") && !up) { if (side == "L") yawL = 0f; else yawR = 0f; }
                else if (stem == "rudder" && !up) { if (side == "L") yawL = Mathf.Min(yawL, .4f); else yawR = Mathf.Min(yawR, .4f); }
                // Hit points lower authority a little before anything comes off.
                if (up && n == "hstab") pitch = Mathf.Lerp(.6f, 1f, p.hitPoints * .01f);
            }
            LiftL = l; LiftR = r; PitchAuthority = pitch; YawAuthority = (yawL + yawR) * .5f;

            if (!aircraft.IsServer || Time.time < nextCheck) return;
            nextCheck = Time.time + .5f;
            if (!aircraft.disabled && Mathf.Min(LiftL, LiftR) < DeadSide)
            {
                aircraft.Networkdisabled = true;
                aircraft.ReportKilled();
            }
            if (!aircraft.disabled) Overload();
            if (aircraft.disabled && Time.time > nextBreak)
            {
                // The dying carrier sheds what it still carries, outermost first, a piece every few seconds. The first
                // pass only schedules that timer: no piece falls off in the same frame the carrier disables.
                if (nextBreak > 0f)
                {
                    AeroPart next = parts.Where(p => p != null && p.parentUnit != null && p.gameObject != p.parentUnit.gameObject && !p.IsDetached())
                        .OrderByDescending(p => (p.transform.position - transform.position).sqrMagnitude).FirstOrDefault();
                    if (next != null) Break(next, true);
                }
                nextBreak = Time.time + Random.Range(BreakupMin, BreakupMax);
            }
        }

        /// <summary>As vanilla joints lose break force with hit points, a shot-up panel fails under load: below half its hit
        /// points it carries less g, down to 1 g at zero. Banking hard (or the turbulence of a fishtail) tears it off.</summary>
        private void Overload()
        {
            float rightUp = transform.right.y;
            float g = 1f / Mathf.Sqrt(Mathf.Max(1f - rightUp * rightUp, .05f));   // level-turn load factor from the bank
            g += Mathf.Abs(aircraft.rb.angularVelocity.x) * .8f;                     // pitching adds to it
            foreach (AeroPart p in parts)
            {
                if (p == null || p.hitPoints >= 50f || p.IsDetached() || !Detachable(p)) continue;
                float holds = 1f + .5f * Mathf.Max(p.hitPoints, 0f) / 50f;
                if (g > holds && Random.value < .5f) Break(p);
            }
        }

        // Donor fires and smoke are sized for a 33 m bomber: grow the ones the islands spawn.
        private void LateUpdate()
        {
            if (dirty.Count == 0) return;
            foreach (AeroPart p in dirty) if (p != null) ScaleEffects(p.transform);
            dirty.Clear();
        }

        private void ScaleEffects(Transform island)
        {
            foreach (Transform child in island)
            {
                if (scaled.Contains(child) || child.name.StartsWith("BS_Fx") || child.GetComponent<ParticleSystem>() == null && child.GetComponentInChildren<DamageParticles>(true) == null) continue;
                scaled.Add(child);
                foreach (ParticleSystem ps in child.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                child.localScale *= EffectScale;
            }
        }
    }
}

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Native bullets do not skip their shooter: a 380 m carrier's own guns traverse past its booms, fins and wings.
    /// Its own rounds never count as hits on itself (the fins came off in flight from the aft guns).</summary>
    [HarmonyLib.HarmonyPatch(typeof(Unit), nameof(Unit.RegisterHit))]
    internal static class MegaplaneSelfHitPatch
    {
        private static bool Prefix(Unit __instance, Unit hitUnit) => !(hitUnit == __instance && MegaplaneModule.Is(__instance));
    }
}
