using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Math;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    // ITheaterWarPolicy: politics' hold on a faction's war (Nation sets it every second, Vanilla hands it back). The posture band holds the
    // staff and pilots alike, the AI cap and skill move the vanilla FactionHQ fields the game reads at each deployment and spawn, and a
    // defence factor raises what an enemy assault on this faction needs.
    internal sealed partial class LivingFrontService : ITheaterWarPolicy
    {
        private sealed class Held
        {
            public WarPolicy Policy = WarPolicy.Vanilla;
            /// <summary>AI jets really added to the cap (a cap never goes below zero), so Vanilla takes off exactly that.</summary>
            public int Jets;
            /// <summary>The mission's skill multipliers, read when the hold began.</summary>
            public float Air = -1f, Surface = -1f;
            /// <summary>A forced posture is on, and what the staff had before it.</summary>
            public bool Forced;
            public TheaterWarPosture Before = TheaterWarPosture.Steady;
        }

        private readonly Dictionary<FactionHQ, Held> held = new Dictionary<FactionHQ, Held>(MaximumFactions);

        private static bool IsVanilla(in WarPolicy p) =>
            p.Floor == TheaterWarPosture.Cautious && p.Ceiling == TheaterWarPosture.Bold && p.Jets == 0 && p.Defence <= 1f && p.Skill == 1f;

        public void Apply(FactionHQ hq, in WarPolicy policy)
        {
            if (hq == null || !GameAccess.IsServer()) return;
            if (!held.TryGetValue(hq, out Held h))
            {
                if (IsVanilla(policy)) return;
                held[hq] = h = new Held();
            }
            int baseline = hq.AIAircraftLimit - h.Jets;
            int limit = Mathf.Max(0, baseline + policy.Jets);
            if (limit != hq.AIAircraftLimit) hq.AIAircraftLimit = limit;
            h.Jets = limit - baseline;
            if (h.Air < 0f) { h.Air = hq.airSkillMultiplier; h.Surface = hq.surfaceSkillMultiplier; }
            float skill = policy.Skill > 0f && float.IsFinite(policy.Skill) ? policy.Skill : 1f;
            hq.airSkillMultiplier = h.Air * skill;
            hq.surfaceSkillMultiplier = h.Surface * skill;
            h.Policy = policy;
            if (hq.faction != null && wars.TryGetValue(hq.faction.factionName, out FactionWar war) && war.HQ != null) Hold(war, h);
            if (IsVanilla(policy)) held.Remove(hq);
        }

        /// <summary>Keeps the staff's posture inside the band; a lifted force hands back the posture it found.</summary>
        private void Hold(FactionWar war, Held h)
        {
            TheaterWarPosture floor = h.Policy.Floor, ceiling = h.Policy.Ceiling;
            bool forced = floor == ceiling;
            if (forced && !h.Forced) h.Before = war.Posture;
            TheaterWarPosture want = forced ? floor : h.Forced ? h.Before : war.Posture;
            if (want < floor) want = floor;
            if (want > ceiling) want = ceiling;
            h.Forced = forced;
            if (want == war.Posture) return;
            war.Posture = want;
            war.Log.Add("POSTURE " + want.ToString().ToUpperInvariant() + " · CABINET");
            if (war.SensedAt >= 0f && !war.InReview)
                try { Publish(war, Time.timeSinceLevelLoad); }
                catch (Exception e) { LogFault("[TheaterOps] posture not published: " + e.Message); }
        }

        /// <summary>A pilot's posture outside the band is refused with the reason.</summary>
        private bool OutsideBand(FactionWar war, byte posture, out string why)
        {
            why = "";
            if (war.HQ == null || !held.TryGetValue(war.HQ, out Held h) || posture >= (byte)h.Policy.Floor && posture <= (byte)h.Policy.Ceiling) return false;
            why = h.Policy.Floor == h.Policy.Ceiling ? "THE CABINET HOLDS " + h.Policy.Floor.ToString().ToUpperInvariant() : "BOLD NEEDS WAR SUPPORT 25";
            return true;
        }

        /// <summary>What an assault by this faction needs on top of its ratio: the strongest defence any other faction holds (SHIELD).</summary>
        private float DefenceAgainst(FactionWar war)
        {
            float d = 1f;
            foreach (KeyValuePair<FactionHQ, Held> pair in held)
                if (pair.Key != war.HQ && pair.Value.Policy.Defence > d) d = pair.Value.Policy.Defence;
            return d;
        }

        public bool LaunchOffensive(FactionHQ hq, float x, float z, string by)
        {
            if (!Authoritative || hq?.faction == null || !wars.TryGetValue(hq.faction.factionName, out FactionWar war) || war.HQ == null) return false;
            // The live front nearest the objective that a drawn arrow may start from (1.5 to 30 km off).
            float best = float.MaxValue, sx = 0f, sz = 0f;
            float min = TheaterDrawLimits.MinimumMetres * TheaterDrawLimits.MinimumMetres, max = TheaterDrawLimits.MaximumMetres * TheaterDrawLimits.MaximumMetres;
            for (int i = 0; i < war.Reads.Count; i++)
            {
                if (!war.Reads[i].Live) continue;
                float d = Scalar.Distance2DSquared(war.Reads[i].X, war.Reads[i].Z, x, z);
                if (d < min || d > max || d >= best) continue;
                best = d; sx = war.Reads[i].X; sz = war.Reads[i].Z;
            }
            if (best == float.MaxValue) return false;
            return ApplyIntent(hq, 3, 0, 0, 0, new IntentActor(0UL, by), sx, sz, x, z).Code == TheaterCommandCode.Accepted;
        }

        public bool TryMainEffort(FactionHQ hq, out string name)
        {
            name = hq?.faction != null && wars.TryGetValue(hq.faction.factionName, out FactionWar war) && war.Active != null ? war.Active.Name : null;
            return !string.IsNullOrEmpty(name);
        }
    }
}
