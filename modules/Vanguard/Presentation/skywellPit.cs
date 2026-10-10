using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// SKYWELL PIT STOP on every peer: job-change bookkeeping, the tether, lamps. Everything is derived from the published
    /// <see cref="SkywellView"/>; no per-frame traffic.
    /// </summary>
    internal sealed partial class SkywellVisuals
    {
        private static readonly Color Teal = new Color(0.10f, 0.85f, 0.80f);
        private static readonly Color Amber = new Color(1f, 0.55f, 0.05f);
        private static readonly Color Green = new Color(0.10f, 1f, 0.25f);
        private static readonly Color Red = new Color(1f, 0.08f, 0.05f);
        private static readonly Color Blue = new Color(0.35f, 0.80f, 1f);

        private static readonly MaterialPropertyBlock TetherBlock = new MaterialPropertyBlock();
        private Material tetherMaterial;

        /// <summary>
        /// A job (or receiver, or state) just changed: restart the job clock, and pick the magazine slot a hand-off takes its round from.
        /// A round handed over (the magazine count dropped) flashes the gripper's lamp.
        /// </summary>
        private static void PitSync(Rig rig, SkywellView v)
        {
            bool receiverChanged = v.Receiver != rig.LastReceiver;
            if (v.Job != rig.LastJob || v.JobStation != rig.LastStation || v.JobIndex != rig.LastIndex || v.State != rig.LastState || receiverChanged)
            {
                if (!receiverChanged && v.JobIndex > rig.LastIndex) rig.CommitFlash = Time.time;
                rig.JobStart = Time.time;
                rig.ArmClock = 0f; // the arm's clock starts when the tender is in place for the new job
                bool handoff = v.Job == PitJobKind.Handoff || v.Job == PitJobKind.Cassette;
                int slot = SkywellMagazine.NextSlot(v.Rounds);
                rig.CarrySlot = handoff && SkywellTender.Serving(v.State) && slot < SkywellMagazine.Rounds ? slot : -1;
                rig.LastJob = v.Job;
                rig.LastStation = v.JobStation;
                rig.LastIndex = v.JobIndex;
                rig.LastState = v.State;
                rig.LastReceiver = v.Receiver;
            }
            if (v.Rounds < rig.LastRounds) rig.CommitFlash = Time.time;
            rig.LastRounds = v.Rounds;
        }

        /// <summary>Deck chase, tender and fan lamps, boom tip and gripper; red blink on a hot break.</summary>
        private static void PoseGlow(Rig rig, SkywellView v, bool hot)
        {
            float t = Time.time;
            bool blink4 = Mathf.Repeat(t * 4f, 1f) < 0.5f, blink2 = Mathf.Repeat(t * 2f, 1f) < 0.5f;
            TenderState s = v.State;
            bool moving = SkywellTender.Moving(s);

            if (hot) rig.GlowDeck.Set(Red, blink4 ? 1.8f : 0.2f);
            else if (moving) rig.GlowDeck.Set(Amber, blink2 ? 1.6f : 0.2f);
            else rig.GlowDeck.Clear();

            if (hot) rig.GlowTender.Set(Red, blink4 ? 1.5f : 0.2f);
            else if (s == TenderState.Approach || s == TenderState.Station) rig.GlowTender.Set(Amber, 1.4f);
            else if (s == TenderState.Service) rig.GlowTender.Set(Amber, 1f + 0.4f * Mathf.Sin(t * Mathf.PI * 6f));
            else if (moving) rig.GlowTender.Set(Amber, blink2 ? 1.2f : 0.2f);
            else if (s == TenderState.Stowed) rig.GlowTender.Clear();
            else rig.GlowTender.Set(Teal, 0.5f);

            float power = SkywellTender.FanPower(s, SkywellTender.Progress(s, v.StateAge));
            bool strobe = Mathf.Repeat(t, 1f) < 0.1f; // 1 Hz strobe, 0.1 s on
            for (int i = 0; i < 4; i++)
            {
                if (power < 0.05f) rig.GlowFans[i].Clear();
                else if (hot) rig.GlowFans[i].Set(Red, blink4 ? 1.5f : 0.2f);
                else if (strobe) rig.GlowFans[i].Set((i & 1) == 0 ? Red : Green, 1.5f);
                else rig.GlowFans[i].Set(Teal, 0.2f + 0.6f * power);
            }

            if (rig.Swing > 0.5f) rig.GlowNozzle.Set(Blue, 1.2f + 0.6f * Mathf.Sin(t * Mathf.PI * 8f));
            else rig.GlowNozzle.Set(Teal, 0.6f);

            if (t - rig.CommitFlash < 0.25f) rig.GlowGripper.Set(Green, 1.8f);
            else if (rig.Reach >= 0.1f) rig.GlowGripper.Set(Amber, 1.4f);
            else rig.GlowGripper.Set(Teal, 0.15f);
        }

        /// <summary>
        /// Fuel + power tether from the deck winch to the tender's TetherPoint, with a catenary-ish sag (a taut tow barely sags, a slack
        /// tether on station sags a few percent of its length). While a receiver is served the line sags until it comes level into the
        /// tender's nose from ahead: under the receiver's nose, never across the airframe.
        /// </summary>
        /// <returns>The line's length, m (0 when it is not shown).</returns>
        private float PoseTether(Rig rig, Transform tt, TenderState state, float p, Aircraft receiver, float attach, float dt)
        {
            bool show = rig.Winch.Ok && state != TenderState.Stowed && attach < 0.99f;
            if (rig.Tether == null)
            {
                if (!show) return 0f;
                if (tetherMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    tetherMaterial = new Material(shader) { color = new Color(0.08f, 0.085f, 0.09f) };
                }
                rig.Tether = new GameObject("SkywellTether").AddComponent<LineRenderer>();
                rig.Tether.sharedMaterial = tetherMaterial;
                rig.Tether.positionCount = TetherPoints;
                rig.Tether.widthMultiplier = 0.07f;
                rig.Tether.numCapVertices = 2;
                rig.Tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            rig.Tether.enabled = show;
            if (!show) return 0f;
            TintTether(rig, rig.Swing);
            rig.TetherAvoid = Mathf.MoveTowards(rig.TetherAvoid, receiver != null ? 1f : 0f, dt / 0.5f);
            Vector3 a = rig.Winch.T.position + tt.up * 0.15f;
            Vector3 b = rig.TetherPoint != null ? rig.TetherPoint.position : rig.Tender.T.position;
            float length = Vector3.Distance(a, b);
            float tension = state == TenderState.Tow || state == TenderState.Deploying || state == TenderState.Stowing ? 1f
                : state == TenderState.Return ? 0.8f : state == TenderState.Approach ? 0.55f : 0.25f;
            Vector3 control = SkywellTender.TetherControl(a, b, tension, rig.TetherAvoid, Vector3.up);
            for (int i = 0; i < TetherPoints; i++)
                rig.Tether.SetPosition(i, SkywellTender.Bezier(a, control, b, i / (float)(TetherPoints - 1)));
            return length;
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
