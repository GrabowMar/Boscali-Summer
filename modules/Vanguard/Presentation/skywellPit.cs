using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// SKYWELL PIT STOP on every peer: job-change bookkeeping, the hot-swap round, latches, lamps, wingtip trails and the
    /// re-dock lockout. Everything is derived from the published <see cref="SkywellView"/>; no per-frame traffic.
    /// </summary>
    internal sealed partial class SkywellVisuals
    {
        private const float PitKiteSpeed = 22f;   // m/s while a job is in flight (tanker frame)
        private const float HotKiteSpeed = 28f;   // m/s snapping back after a hot break

        private static readonly Color Teal = new Color(0.10f, 0.85f, 0.80f);
        private static readonly Color Amber = new Color(1f, 0.55f, 0.05f);
        private static readonly Color Green = new Color(0.10f, 1f, 0.25f);
        private static readonly Color Red = new Color(1f, 0.08f, 0.05f);
        private static readonly Color Blue = new Color(0.35f, 0.80f, 1f);
        private static readonly Color NavRed = new Color(1f, 0.05f, 0.05f);
        private static readonly Color NavGreen = new Color(0.05f, 1f, 0.20f);

        private static bool needsBrakeReset;      // local aircraft: the server let go, the pilot must release the brake before the next dock
        // Sim diagnostics (status: kiteReach / kiteNozzle / ...Max / kiteRoundMin): the joints really were driven.
        internal static float ProbeReach, ProbeNozzle, ProbeReachMax, ProbeNozzleMax, ProbeRoundMin = 1f;

        private static Material trailMaterial;
        private static readonly MaterialPropertyBlock TetherBlock = new MaterialPropertyBlock();

        /// <summary>
        /// A job (or receiver, or phase) just changed: restart the job clock and decide where the loader comes from.
        /// The previous handoff having committed means its real missile is on the rail now, so the cradle round vanishes.
        /// </summary>
        private static void PitSync(Rig rig, SkywellView v)
        {
            bool receiverChanged = v.Receiver != rig.LastReceiver;
            if (v.Job != rig.LastJob || v.JobStation != rig.LastStation || v.JobIndex != rig.LastIndex || v.Phase != rig.LastPhase || receiverChanged)
            {
                bool wasStation = rig.LastJob == PitJobKind.Handoff || rig.LastJob == PitJobKind.Cassette;
                bool isStation = v.Job == PitJobKind.Handoff || v.Job == PitJobKind.Cassette;
                bool committed = !receiverChanged && v.JobIndex > rig.LastIndex;
                rig.JobStart = Time.time;
                rig.From = wasStation && !receiverChanged ? (v.JobStation == rig.LastStation ? PitFrom.SameStation : PitFrom.OtherStation) : PitFrom.Home;
                rig.RoundHidden = wasStation && committed;
                if (committed)
                {
                    rig.CommitFlash = Time.time;
                    if (wasStation && !isStation) rig.RoundVis = 0f; // last handoff: the arm reloads its cradle on the way home
                }
                if (isStation)
                {
                    Aircraft r = Resolve(v.Receiver);
                    WeaponInfo info = r != null && r.weaponStations != null && v.JobStation < r.weaponStations.Count ? r.weaponStations[v.JobStation]?.WeaponInfo : null;
                    rig.RoundFactor = v.Job == PitJobKind.Cassette ? 0.55f
                        : info != null ? Mathf.Clamp(Mathf.Pow(info.massPerRound / 120f, 1f / 3f), 0.55f, 1.5f) : 1f;
                }
                rig.LastJob = v.Job;
                rig.LastStation = v.JobStation;
                rig.LastIndex = v.JobIndex;
                rig.LastPhase = v.Phase;
                rig.LastReceiver = v.Receiver;
            }
        }

        /// <summary>The round in the cradle: shown while reaching, gone at the commit, regrown from the belly magazine for the next.</summary>
        private static void PoseRound(Rig rig, bool stationStage, float jobTime, float dt)
        {
            rig.RoundVis = stationStage ? PitStaging.RoundScale(jobTime, rig.RoundHidden) : Mathf.MoveTowards(rig.RoundVis, 1f, dt / 0.7f);
            if (rig.RoundJ.T == null) return;
            rig.RoundJ.T.localScale = rig.RoundJ.Scale * Mathf.Max(0.001f, rig.RoundVis * rig.RoundFactor);
        }

        /// <summary>Hold-down latches flop open as the kite lifts off and close when it is home.</summary>
        private static void PoseLatches(Rig rig)
        {
            float w = Mathf.SmoothStep(0f, 1f, rig.Deploy >= 1f ? Mathf.Clamp01(rig.Launch / 0.06f) : 0f);
            if (rig.LatchR.T != null) rig.LatchR.T.localRotation = rig.LatchR.Rot * Quaternion.AngleAxis(70f * w, Vector3.up);
            if (rig.LatchL.T != null) rig.LatchL.T.localRotation = rig.LatchL.Rot * Quaternion.AngleAxis(-70f * w, Vector3.up);
        }

        /// <summary>Deck chase, kite pips, cradle lamps, boom ring and nav lights; red blink on a hot break.</summary>
        private static void PoseGlow(Rig rig, SkywellView v, bool hot, float connected)
        {
            float t = Time.time;
            bool blink4 = Mathf.Repeat(t * 4f, 1f) < 0.5f, blink2 = Mathf.Repeat(t * 2f, 1f) < 0.5f;

            if (hot) rig.GlowDeck.Set(Red, blink4 ? 1.8f : 0.2f);
            else if (rig.Deploy > 0f && rig.Deploy < 1f) rig.GlowDeck.Set(Amber, blink2 ? 1.6f : 0.2f);
            else rig.GlowDeck.Clear();

            if (hot) rig.GlowKite.Set(Red, blink4 ? 1.5f : 0.2f);
            else if (v.Phase == SkywellPhase.Engage) rig.GlowKite.Set(Amber, 1.4f);
            else if (v.Phase == SkywellPhase.Transfer) rig.GlowKite.Set(Amber, 1f + 0.4f * Mathf.Sin(t * Mathf.PI * 6f));
            else if (v.Phase == SkywellPhase.Served) rig.GlowKite.Set(Green, 1.5f);
            else rig.GlowKite.Set(Teal, 0.5f);

            if (t - rig.CommitFlash < 0.25f) rig.GlowCradle.Set(Green, 1.8f);
            else if (rig.Reach >= 0.1f && rig.RoundVis > 0.5f) rig.GlowCradle.Set(Amber, 1.4f);
            else rig.GlowCradle.Set(Teal, 0.15f);

            if (connected > 0.5f) rig.GlowNozzle.Set(Blue, 1.2f + 0.6f * Mathf.Sin(t * Mathf.PI * 8f));
            else rig.GlowNozzle.Set(Teal, 0.6f);

            if (rig.Launch > 0.5f)
            {
                bool flash = Mathf.Repeat(t, 1f) < 0.1f; // 1 Hz strobe, 0.1 s on
                rig.GlowTipL.Set(NavRed, flash ? 1.5f : 0.25f);
                rig.GlowTipR.Set(NavGreen, flash ? 1.5f : 0.25f);
            }
            else
            {
                rig.GlowTipL.Clear();
                rig.GlowTipR.Clear();
            }
        }

        /// <summary>Vapour trails off the winglet fins once the glider is flying.</summary>
        private void PoseTrails(Rig rig, bool on, Vector3 left, Vector3 right)
        {
            if (rig.TrailL == null)
            {
                if (!on) return;
                rig.TrailL = NewTrail();
                rig.TrailR = NewTrail();
                if (rig.TrailL == null) return;
            }
            rig.TrailL.emitting = rig.TrailR.emitting = on;
            rig.TrailL.transform.position = left;
            rig.TrailR.transform.position = right;
        }

        private static TrailRenderer NewTrail()
        {
            if (trailMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;
                trailMaterial = new Material(shader);
            }
            TrailRenderer trail = new GameObject("SkywellTrail").AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            trail.time = 0.6f;
            trail.widthMultiplier = 0.09f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.3f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(0.92f, 0.94f, 0.96f), 0f), new GradientColorKey(new Color(0.92f, 0.94f, 0.96f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.30f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            return trail;
        }

        /// <summary>The tether turns blue-steel while fuel flows.</summary>
        private static void TintTether(Rig rig, float connected)
        {
            if (Mathf.Abs(connected - rig.TetherBlue) <= 0.02f) return;
            rig.TetherBlue = connected;
            Color c = Color.Lerp(new Color(0.08f, 0.085f, 0.09f), new Color(0.12f, 0.35f, 0.55f), connected);
            TetherBlock.SetColor("_BaseColor", c);
            TetherBlock.SetColor("_Color", c);
            rig.Tether.SetPropertyBlock(TetherBlock);
        }
    }
}
