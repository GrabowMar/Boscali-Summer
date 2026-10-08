using System;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal enum HaloPhase { Aboard, Exit, Glide, Deploy, Canopy, Landed, Gone }

    /// <summary>One jumper at one instant: where the pelvis is and how the body and canopy look.</summary>
    internal struct HaloSample
    {
        public HaloPhase Phase;
        public Vector3 Position;
        /// <summary>Horizontal unit heading.</summary>
        public Vector3 Heading;
        /// <summary>Body pitch in degrees: 0 upright under canopy, 80 belly-to-earth in the glide.</summary>
        public float Pitch;
        /// <summary>0 packed, 0.5 snivel (slider holding it narrow), 1 fully open.</summary>
        public float Canopy;
        /// <summary>Toggle input, -1 hard left to 1 hard right.</summary>
        public float Steer;
        public float Flare;
        public float Land;
        public bool Smoke;
    }

    /// <summary>
    /// The whole HALO stick, decided at release from replicated inputs only (exit point and
    /// velocity, the designated target, the ground), so every peer and the server's outcome agree
    /// without any network traffic. Pure: no physics, sampled analytically at any time.
    /// Fireteams of four exit in pairs, form a wingsuit V, glide on the bearing to their own slot
    /// around the target, pull canopies staggered by team so they stack, S-turn down and flare.
    /// </summary>
    internal sealed class HaloGlidePlan
    {
        public const int TeamSize = 4;
        public const float ExitInterval = 0.6f;
        public const float ExitSeconds = 3f;
        public const float ExitDrop = 40f;
        public const float GlideSink = 18f;
        public const float GlideRatio = 2.5f;
        public const float GlideMaxSpeed = GlideSink * GlideRatio;
        public const float PullHeight = 350f;
        public const float TeamStack = 60f;
        /// <summary>Below this height above the ground there is no glide: canopies open off the ramp.</summary>
        public const float LowDropHeight = 400f;
        public const float DeploySeconds = 3f;
        public const float DeployLoss = 30f;
        public const float CanopySink = 7f;
        public const float CanopyRun = 150f;
        public const float FlareSeconds = 2.5f;
        public const float FormUpSeconds = 10f;
        public const float SlotRing = 28f;
        public const float MemberRing = 6f;
        public const float LandingHold = 4f;
        /// <summary>Standing pelvis height of the jumper model above its soles.</summary>
        public const float PelvisHeight = 0.95f;
        public const int MaxJumpers = 16;

        private struct Team
        {
            public Vector3 LeadForm, Pull, Slot, Approach, Right, GlideVelocity;
            public float LeadFormAt, PullAt;
        }

        private struct Jumper
        {
            public int Team, Rank;
            public float ExitAt, PullAt, CanopyAt, LandAt;
            public Vector3 Exit, ExitVelocity, Form, Offset, Pull, CanopyStart, Land;
            public float Weave;
        }

        private readonly Team[] teams;
        private readonly Jumper[] jumpers;

        public int Count => jumpers.Length;
        public bool LowDrop { get; }
        /// <summary>Ground point the first team lands on: where the server resolves the stick.</summary>
        public Vector3 StickLanding { get; }
        public float TotalSeconds { get; }

        private HaloGlidePlan(Team[] teams, Jumper[] jumpers, bool low)
        {
            this.teams = teams;
            this.jumpers = jumpers;
            LowDrop = low;
            StickLanding = teams[0].Slot;
            float last = 0f;
            foreach (Jumper j in jumpers) last = Math.Max(last, j.LandAt);
            TotalSeconds = last + LandingHold;
        }

        /// <param name="exit">Ramp exit point at release.</param>
        /// <param name="exitVelocity">Aircraft velocity at release.</param>
        /// <param name="aim">Designated target point, or null to glide down the aircraft's heading.</param>
        /// <param name="groundAt">Height of the ground (or roof) under a point.</param>
        public static HaloGlidePlan Build(Vector3 exit, Vector3 exitVelocity, Vector3? aim, Func<Vector3, float> groundAt, int count)
        {
            count = Mathf.Clamp(count, 1, MaxJumpers);
            Vector3 flat = Flat(exitVelocity);
            Vector3 heading = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;
            bool low = exit.y - groundAt(exit) < LowDropHeight;

            var jumpers = new Jumper[count];
            for (int i = 0; i < count; i++)
            {
                float at = (i / 2) * ExitInterval;
                Vector3 door = exit + exitVelocity * at + right * (i % 2 == 0 ? -0.6f : 0.6f);
                // Slipstream carries the jumper on and down while they stabilise.
                Vector3 form = door + flat * (low ? 0.8f : 1.3f) + Vector3.down * (low ? 25f : ExitDrop);
                jumpers[i] = new Jumper
                {
                    Team = i / TeamSize, Rank = i % TeamSize, ExitAt = at, Exit = door, ExitVelocity = exitVelocity, Form = form,
                    Weave = (i % 2 == 0 ? 1f : -1f) * (1f + 0.17f * (i % 5)),
                };
            }

            int teamCount = (count + TeamSize - 1) / TeamSize;
            var teams = new Team[teamCount];
            Vector3 leadForm0 = jumpers[0].Form;
            // Without a target the stick flies its full reach down the heading.
            Vector3 centre = aim ?? Flat(leadForm0) + heading * 1e6f;
            Vector3 centreBearing = Flat(centre - leadForm0);
            float baseAngle = Mathf.Atan2(centreBearing.x, centreBearing.z);

            for (int k = 0; k < teamCount; k++)
            {
                Jumper lead = jumpers[k * TeamSize];
                Vector3 slot = Flat(centre);
                if (k > 0)
                {
                    // The first team takes the target itself; the rest ring it, starting abeam.
                    float a = baseAngle + Mathf.PI * 0.5f + 2f * Mathf.PI * (k - 1) / Math.Max(1, teamCount - 1);
                    slot += new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * SlotRing;
                }
                float pullHeight = PullHeight + TeamStack * k;
                Vector3 approach = Flat(slot - lead.Form);
                approach = approach.sqrMagnitude > 1f ? approach.normalized : heading;
                Vector3 pull;
                float pullAt;
                if (low)
                {
                    // No glide: pull off the ramp and let the canopy carry what it can.
                    pull = lead.Form;
                    pullAt = lead.ExitAt + ExitSeconds;
                    float canopyReach = CanopyRun * 2f;
                    Vector3 run = Flat(slot - pull);
                    if (run.magnitude > canopyReach) slot = Flat(pull) + run.normalized * canopyReach;
                }
                else
                {
                    Vector3 pullFlat = slot - approach * CanopyRun;
                    float pullY = groundAt(slot) + pullHeight;
                    float glideTime = Math.Max(2f, (lead.Form.y - pullY) / GlideSink);
                    Vector3 run = pullFlat - Flat(lead.Form);
                    float reach = GlideMaxSpeed * glideTime;
                    if (run.magnitude > reach)
                    {
                        pullFlat = Flat(lead.Form) + run.normalized * reach;
                        slot = pullFlat + approach * CanopyRun;
                    }
                    pull = new Vector3(pullFlat.x, Math.Min(pullY, lead.Form.y - 2f * GlideSink), pullFlat.z);
                    pullAt = lead.ExitAt + ExitSeconds + (lead.Form.y - pull.y) / GlideSink;
                }
                float slotY = groundAt(slot);
                teams[k] = new Team
                {
                    LeadForm = lead.Form, LeadFormAt = lead.ExitAt + ExitSeconds, Pull = pull, PullAt = pullAt,
                    Slot = new Vector3(slot.x, slotY, slot.z), Approach = approach,
                    Right = Vector3.Cross(Vector3.up, approach).normalized,
                };
                float glideSeconds = Math.Max(0.01f, pullAt - teams[k].LeadFormAt);
                teams[k].GlideVelocity = low ? Vector3.zero : (pull - lead.Form) / glideSeconds;
                // Later teams ring wherever the first one can actually reach.
                if (k == 0) centre = Flat(teams[0].Slot);
            }

            for (int i = 0; i < count; i++)
            {
                ref Jumper j = ref jumpers[i];
                Team t = teams[j.Team];
                int rank = (j.Rank + 1) / 2;
                float side = j.Rank % 2 == 1 ? 1f : -1f;
                // Wingsuit V: lead at the point, wingmen stepped back and slightly high.
                j.Offset = j.Rank == 0 ? Vector3.zero : t.Right * (side * 7f * rank) - t.Approach * (9f * rank) + Vector3.up * (2f * rank);
                j.PullAt = Math.Max(t.PullAt + 0.7f * j.Rank, j.ExitAt + ExitSeconds);
                j.Pull = low ? j.Form : LeadPath(t, j.PullAt) + j.Offset;
                float canopyHeight = Math.Max(20f, j.Pull.y - DeployLoss - t.Slot.y);
                j.CanopyStart = j.Pull + Flat(t.GlideVelocity) * (DeploySeconds * 0.4f) + Vector3.down * DeployLoss;
                j.CanopyAt = j.PullAt + DeploySeconds;
                j.LandAt = j.CanopyAt + canopyHeight / CanopySink;
                float a = Mathf.Atan2(t.Approach.x, t.Approach.z) + j.Rank * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                Vector3 spot = t.Slot + (j.Rank == 0 ? Vector3.zero : new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * MemberRing);
                j.Land = new Vector3(spot.x, groundAt(spot) + PelvisHeight, spot.z);
            }
            return new HaloGlidePlan(teams, jumpers, low);
        }

        private static Vector3 LeadPath(Team t, float time) => t.LeadForm + t.GlideVelocity * (time - t.LeadFormAt);

        public HaloSample Sample(int index, float time)
        {
            Jumper j = jumpers[index];
            Team team = teams[j.Team];
            var s = new HaloSample { Heading = team.Approach };
            float formAt = j.ExitAt + ExitSeconds;
            if (time < j.ExitAt)
            {
                s.Phase = HaloPhase.Aboard;
                s.Position = j.Exit;
                return s;
            }
            if (time < formAt)
            {
                float u = (time - j.ExitAt) / ExitSeconds;
                Vector3 endVelocity = LowDrop ? Flat(j.ExitVelocity) * 0.2f + Vector3.down * 15f : team.GlideVelocity;
                s.Phase = HaloPhase.Exit;
                s.Position = Hermite(j.Exit, j.ExitVelocity * ExitSeconds, j.Form, endVelocity * ExitSeconds, u);
                s.Heading = Heading(j.ExitVelocity, team.Approach);
                // Tumbling off the ramp into a stable belly-down arch.
                s.Pitch = Mathf.Lerp(10f, LowDrop ? 30f : 80f, Smooth(u)) + Mathf.Sin(u * 9f + j.Weave) * 25f * (1f - u);
                return s;
            }
            if (time < j.PullAt)
            {
                Vector3 slotted = LeadPath(team, time) + j.Offset;
                Vector3 error = j.Form - (LeadPath(team, formAt) + j.Offset);
                s.Phase = HaloPhase.Glide;
                s.Position = slotted + error * (1f - Smooth((time - formAt) / FormUpSeconds));
                s.Pitch = 80f;
                s.Steer = Mathf.Sin(time * 0.7f + j.Weave) * 0.25f;
                s.Smoke = true;
                return s;
            }
            if (time < j.CanopyAt)
            {
                float u = (time - j.PullAt) / DeploySeconds;
                Vector3 startVelocity = LowDrop ? Vector3.down * 15f : team.GlideVelocity;
                s.Phase = HaloPhase.Deploy;
                s.Position = Hermite(j.Pull, startVelocity * DeploySeconds, j.CanopyStart, CanopyVelocity(j, team, 0f) * DeploySeconds, u);
                s.Pitch = Mathf.Lerp(80f, 0f, Smooth(u * 1.4f));
                s.Canopy = Mathf.Clamp01(u * 1.15f);
                return s;
            }
            if (time < j.LandAt)
            {
                float duration = j.LandAt - j.CanopyAt;
                float u = (time - j.CanopyAt) / duration;
                s.Phase = HaloPhase.Canopy;
                s.Position = CanopyPath(j, team, u);
                s.Heading = Heading(CanopyVelocity(j, team, u), team.Approach);
                s.Canopy = 1f;
                s.Steer = Mathf.Clamp(Mathf.Cos(WeaveArg(j, u)) * j.Weave, -1f, 1f) * WeaveWeight(u);
                s.Flare = Smooth(1f - (j.LandAt - time) / FlareSeconds);
                return s;
            }
            if (time < j.LandAt + LandingHold)
            {
                float u = (time - j.LandAt) / LandingHold;
                s.Phase = HaloPhase.Landed;
                s.Position = j.Land;
                s.Heading = Heading(CanopyVelocity(j, team, 1f), team.Approach);
                s.Land = Smooth(u * 6f);
                s.Flare = 1f - s.Land;
                // The canopy deflates and folds down behind the jumper.
                s.Canopy = 1f - Smooth(u * 2.5f);
                return s;
            }
            s.Phase = HaloPhase.Gone;
            s.Position = j.Land;
            return s;
        }

        // S-turns that burn off height and die out for a straight final into the slot.
        private const float WeaveAmplitude = 35f;
        private static float WeaveArg(Jumper j, float u) => 2f * Mathf.PI * 1.5f * u + j.Weave;
        private static float WeaveWeight(float u) => 1f - Smooth((u - 0.6f) / 0.3f);

        private static Vector3 CanopyPath(Jumper j, Team t, float u)
        {
            Vector3 straight = Vector3.Lerp(j.CanopyStart, j.Land, u);
            float sway = Mathf.Sin(WeaveArg(j, u)) - Mathf.Sin(j.Weave);
            return straight + t.Right * (WeaveAmplitude * sway * WeaveWeight(u));
        }

        private static Vector3 CanopyVelocity(Jumper j, Team t, float u)
        {
            const float h = 0.01f;
            float duration = Math.Max(0.1f, j.LandAt - j.CanopyAt);
            float a = Mathf.Clamp01(u - h), b = Mathf.Clamp01(u + h);
            return (CanopyPath(j, t, b) - CanopyPath(j, t, a)) / ((b - a) * duration);
        }

        private static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float u)
        {
            u = Mathf.Clamp01(u);
            float u2 = u * u, u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * p0 + (u3 - 2f * u2 + u) * m0 + (-2f * u3 + 3f * u2) * p1 + (u3 - u2) * m1;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Vector3 Heading(Vector3 v, Vector3 fallback)
        {
            Vector3 f = Flat(v);
            return f.sqrMagnitude > 0.04f ? f.normalized : fallback;
        }

        private static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }
    }
}
