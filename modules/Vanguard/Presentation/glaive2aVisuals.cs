using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    // GLAIVE-2A presentation, every peer: arm unfold / rotor spin from the phase clock, client turret slew, launcher HUD.
    internal sealed partial class SentinelDrone
    {
        private const string HudOwner = "vanguard.glaive2a";
        private const string HudChannel = "vg.glaive2a";
        private static IHudBoard board;
        private IHudLine line;
        private float nextHud;

        private void LateUpdate()
        {
            if (missile == null || missile.disabled)
            {
                ReleaseHud();
                return;
            }
            float now = Time.time, dt = Time.deltaTime;
            if (!missile.IsServer) Pose(dt); // the host poses in its physics tick
            float unfold = Unfold(now), spin = Spin(now);
            for (int i = 0; i < 4; i++)
            {
                if (arms[i] != null) arms[i].localRotation = Quaternion.AngleAxis(armSwing[i] * unfold, armAxis[i]) * armRest[i];
                if (rotors[i] == null) continue;
                // Diagonal pairs counter-rotate (FL+RR one way, FR+RL the other).
                rotorAngle[i] = (rotorAngle[i] + SentinelProfile.RotorRate * spin * dt * (i == 0 || i == 3 ? 1f : -1f)) % 360f;
                rotors[i].localRotation = Quaternion.AngleAxis(rotorAngle[i], rotorAxis[i]) * rotorRest[i];
            }
            Hud(now);
        }

        private float Unfold(float now)
        {
            if (deployAt < 0f) return 0f;
            float unfold = SentinelProfile.Unfold(now - deployAt);
            return dropAt < 0f ? unfold : unfold * (1f - SentinelProfile.Unfold(now - dropAt));
        }

        // Only the launching pilot sees the line; one line per live sentry.
        private void Hud(float now)
        {
            if (now < nextHud) return;
            nextHud = now + .25f;
            if (board == null && !ModuleServices.TryGet(out board)) return;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || missile.owner != me)
            {
                ReleaseHud();
                return;
            }
            board.DeclareChannel(HudChannel, "SENTINEL");
            line ??= board.Acquire(HudOwner, HudChannel, missile.persistentID.Id.ToString());
            if (line == null) return;
            float hold = Mathf.Clamp01(hoverLeft / SentinelProfile.HoverSeconds);
            switch (Phase)
            {
                case SentinelPhase.Launch:
                case SentinelPhase.Ingress:
                    line.Set(HudTone.Info, "SENTINEL ENROUTE", (holdRange / 1000f).ToString("F1") + " KM", 0f);
                    break;
                case SentinelPhase.Brake:
                    line.Set(HudTone.Info, "SENTINEL ROTORS UP", "BRAKING " + (holdRange / 1000f).ToString("F1") + " KM", Spin(now));
                    break;
                case SentinelPhase.Hover:
                    if (engaged != null && !engaged.disabled)
                        line.Set(HudTone.Caution, "SENTINEL ENGAGING " + Label(engaged), Rounds + " RDS", hold);
                    else
                        line.Set(HudTone.Info, "SENTINEL ON STATION", Rounds + " RDS  " + Mathf.CeilToInt(hoverLeft) + " S", hold);
                    break;
                default:
                    if (Rounds <= 0) line.Set(HudTone.Warning, "SENTINEL DRY", "SCUTTLING", 0f);
                    else line.Set(HudTone.Info, "SENTINEL OFF STATION", Rounds + " RDS LEFT", 0f);
                    break;
            }
        }

        private static string Label(Unit unit) =>
            unit is Missile ? "MISSILE" : unit.definition != null ? unit.definition.unitName.ToUpperInvariant() : "AIR";

        private void ReleaseHud()
        {
            line?.Release();
            line = null;
        }
    }
}
