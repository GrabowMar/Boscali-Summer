using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The missile-cam gate and readout (2026-10-10). While the C-menu's MISSILE VIEW rides one of our missiles the camera
    /// follows a <see cref="Missile"/>, the native HUD canvas is off, and nothing used to say how far the target was or whether
    /// the seeker still had it. <see cref="IsOwnMissileView"/> is the pure query (the local player's live aircraft, the orbit
    /// camera, the followed unit is that aircraft's own missile); <see cref="Read"/> fills the readout from the missile's
    /// private <c>target</c>, <c>seekerMode</c> and the two velocities. Read-only.
    /// </summary>
    internal static class MissileCam
    {
        private static readonly AccessTools.FieldRef<Missile, Unit> TargetRef = NativeRefs.Field<Missile, Unit>("target");

        public static bool IsOwnMissileView(out Aircraft own, out Missile missile)
        {
            own = null;
            missile = null;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            Aircraft candidate = hud != null ? hud.aircraft : null;
            if (cam == null || candidate == null || candidate.disabled || candidate.Player == null || !candidate.Player.IsLocalPlayer) return false;
            if (!(cam.followingUnit is Missile followed) || followed.disabled || followed.owner != candidate) return false;
            if (cam.currentState != cam.orbitState) return false;
            own = candidate;
            missile = followed;
            return true;
        }

        /// <summary>Fill <paramref name="data"/> for the followed missile; <paramref name="index"/> / <paramref name="count"/> are its place in the follow list.</summary>
        public static void Read(Missile missile, int index, int count, ref MissileCamData data)
        {
            data = default;
            if (missile == null) return;
            Unit target = NativeRefs.Get(TargetRef, missile);
            bool hasTarget = target != null && !target.disabled;
            WeaponInfo info = missile.GetWeaponInfo();
            string seeker;
            try { seeker = missile.GetSeekerType() ?? ""; } catch { seeker = ""; }
            data = new MissileCamData
            {
                Valid = true,
                Index = index,
                Count = count,
                Weapon = info != null && !string.IsNullOrEmpty(info.shortName) ? info.shortName : info != null ? info.weaponName : "",
                Seeker = seeker,
                State = MissileCamText.StateOf((int)missile.seekerMode),
                HasTarget = hasTarget,
                Target = hasTarget && target.definition != null ? target.definition.unitName : "",
                RangeM = float.NaN,
                ClosureMps = float.NaN,
                TtiS = float.NaN,
            };
            if (!hasTarget) return;
            Vector3 rel = target.transform.position - missile.transform.position;
            float range = rel.magnitude;
            data.RangeM = range;
            if (range > 1f && missile.rb != null)
            {
                Vector3 targetVelocity = target.rb != null ? target.rb.velocity : Vector3.zero;
                data.ClosureMps = -Vector3.Dot(targetVelocity - missile.rb.velocity, rel / range);
                data.TtiS = ThreatRank.Tti(range, data.ClosureMps);
            }
        }
    }
}
