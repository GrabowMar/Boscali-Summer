using System.Globalization;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// The drop cue: while the local pilot has a landing zone and carries paratroopers or a
    /// HAMILCAR, the game's centre message box (<see cref="AircraftActionsReport.ReportText"/>:
    /// the box that carries “Weapon safety lock Disabled”) says whether releasing now puts the
    /// load on the zone or falls short, with the range against the reach. The cue never draws a
    /// card of its own: entering the window, leaving it and every 15 s inside it are one box
    /// line each, and the Hud module's CUE stack draws them the way it draws every other line
    /// of that box. Read-only: it never touches the drop itself.
    /// </summary>
    internal sealed class LandingZoneCue : MonoBehaviour, ISceneService
    {
        private const float DisplaySeconds = 5f;
        private const float RemindSeconds = 15f;

        private static readonly FieldInfo WeaponMountField = HarmonyLib.AccessTools.Field(typeof(Weapon), "mount");

        private float nextRead;
        private bool? haloIn, gliderIn;
        private float haloInsideAt, gliderInsideAt;
        private Aircraft seenAircraft;
        private float seenX, seenZ;
        private bool seen;

        public void ResetForScene()
        {
            nextRead = 0f;
            Forget();
        }

        private void Forget()
        {
            haloIn = gliderIn = null;
            seenAircraft = null;
            seen = false;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + 0.25f;

            if (!ModuleServices.TryGet(out ILandingZone zoneSvc) || !zoneSvc.TryGetLocal(out GlobalPosition zone)) { Forget(); return; }
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || me.weaponStations == null) { Forget(); return; }
            // A new aircraft or a moved zone starts the cue again, so the pilot hears the
            // standing state once instead of waiting for the next crossing.
            if (!seen || seenAircraft != me || seenX != zone.x || seenZ != zone.z)
            {
                seen = true;
                seenAircraft = me;
                seenX = zone.x;
                seenZ = zone.z;
                haloIn = gliderIn = null;
            }

            bool halo = false, glider = false;
            foreach (WeaponStation station in me.weaponStations)
            {
                if (station == null) continue;
                foreach (Weapon weapon in station.Weapons)
                {
                    if (weapon == null || !(WeaponMountField?.GetValue(weapon) is WeaponMount mount) || mount.jsonKey == null) continue;
                    if (weapon is MountedTroops && mount.jsonKey == AirAssaultController.HaloMountKey && weapon.ammo > 0) halo = true;
                    else if (mount.jsonKey.StartsWith("VG_Hamilcar", System.StringComparison.Ordinal) && weapon.ammo > 0) glider = true;
                }
            }
            if (!halo) haloIn = null;
            if (!glider) gliderIn = null;
            if (!halo && !glider) return;

            Vector3 self = me.GlobalPosition().AsVector3();
            float dist = new Vector2(zone.x - self.x, zone.z - self.z).magnitude;
            float agl = self.y - zone.y;
            if (halo)
            {
                float reach = LandingZoneReach.Halo(agl);
                bool inside = LandingZoneReach.InWindow(dist, reach);
                Announce(ref haloIn, ref haloInsideAt, inside, inside
                    ? $"HALO ▸ LZ {Km(dist)} — DROP NOW"
                    : $"HALO ▸ LZ {Km(dist)} — OUT OF GLIDE (MAX {Km(reach)})");
            }
            if (glider)
            {
                float reach = LandingZoneReach.Glider(agl);
                bool inside = LandingZoneReach.InWindow(dist, reach);
                Announce(ref gliderIn, ref gliderInsideAt, inside, inside
                    ? $"HAMILCAR ▸ LZ {Km(dist)} — RELEASE NOW"
                    : $"HAMILCAR ▸ LZ {Km(dist)} — OUT OF GLIDE (MAX {Km(reach)})");
            }
        }

        /// <summary>One line on the centre box when the window opens or closes, and a repeat of the release line while it stays open.</summary>
        private static void Announce(ref bool? state, ref float insideAt, bool inside, string text)
        {
            float now = Time.unscaledTime;
            if (state != inside)
            {
                state = inside;
                if (inside) insideAt = now;
                Report(text);
            }
            else if (inside && now - insideAt >= RemindSeconds)
            {
                insideAt = now;
                Report(text);
            }
        }

        private static void Report(string text)
        {
            AircraftActionsReport report = SceneSingleton<AircraftActionsReport>.i;
            if (report != null) report.ReportText(text, DisplaySeconds);
        }

        private static string Km(float metres) => (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " KM";
    }
}
