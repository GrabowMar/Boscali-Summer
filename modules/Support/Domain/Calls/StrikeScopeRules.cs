using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>
    /// What the STRIKE / OPS page of the TAC BOARD asks of Support (HUD v8), pure: which actions are strikes and which kind each is, which strikes the page keeps,
    /// the perk strip's pick and short names, the trace a mirror's snapshot has reached since it was sent, the CYBER intrusion's trace rate and the SOF words.
    /// The feed (<c>StrikeScopeFeed</c>, Runtime) fills the page's struct with these.
    /// </summary>
    internal static class StrikeScopeRules
    {
        /// <summary>Strikes on the page and perks on the strip.</summary>
        public const int MaxTracks = 3, MaxPerks = 3;
        /// <summary>A landed strike stays on the page this long (the page holds its IMPACT for 3.2 s, the rest is margin); a strike with an effect, while the effect lasts and a little more.</summary>
        public const float LandedSeconds = 6f, EffectMargin = 2f;
        /// <summary>The SAT window the pilot's own RECON PASS or SAT CAMERA opens: the host's observation window.</summary>
        public const float SatSeconds = SpaceRevealWindow.ObservationSeconds;
        /// <summary>A trace snapshot is trusted forward this far (the host sends at most every two seconds): then the trace holds until the next one.</summary>
        public const float TraceHorizon = 2.5f;
        /// <summary>The allocation bar's perk prices (PRSM, CRUISE, ROD), in this order.</summary>
        public static readonly SupportActionId[] PricedStrikes = { SupportActionId.Prsm, SupportActionId.Cruise, SupportActionId.Artillery };

        /// <summary>Is the action a strike the page draws (the rod, PRSM, cruise, EMP, the decoy barrage), and which.</summary>
        public static bool TryKind(SupportActionId id, out StrikeKind kind)
        {
            switch (id)
            {
                case SupportActionId.Artillery: kind = StrikeKind.Rod; return true;
                case SupportActionId.Prsm: kind = StrikeKind.Prsm; return true;
                case SupportActionId.Cruise: kind = StrikeKind.Cruise; return true;
                case SupportActionId.Emp: kind = StrikeKind.Emp; return true;
                case SupportActionId.FlareMissile: kind = StrikeKind.Decoy; return true;
                default: kind = default; return false;
            }
        }

        /// <summary>The effect after the impact the page shows: the EMP and the decoy barrage run on, the rest are done at the impact.</summary>
        public static float Linger(StrikeKind kind, float impactTime, float expiryTime) =>
            kind == StrikeKind.Emp || kind == StrikeKind.Decoy ? Math.Max(0f, expiryTime - impactTime) : 0f;

        /// <summary>Does the page keep this strike: in the air, just landed, or its effect still running.</summary>
        public static bool Keep(float eta, float linger)
        {
            if (eta > 0f) return true;
            float since = -eta;
            return since <= (linger > 0f ? linger + EffectMargin : LandedSeconds);
        }

        /// <summary>
        /// The strikes to draw out of <paramref name="count"/> candidates (each flagged in <paramref name="inFlight"/> or not, with its request id):
        /// those in the air first, then the latest landed, at most <see cref="MaxTracks"/>; written to <paramref name="into"/> in the order they were called (id ascending).
        /// Returns how many.
        /// </summary>
        public static int Pick(int count, int[] ids, bool[] inFlight, int[] into)
        {
            int n = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                // the newest first within a pass, so a full page drops the oldest
                while (n < MaxTracks)
                {
                    int best = -1;
                    for (int i = 0; i < count; i++)
                    {
                        if (inFlight[i] != (pass == 0) || Contains(into, n, i)) continue;
                        if (best < 0 || ids[i] > ids[best]) best = i;
                    }
                    if (best < 0) break;
                    into[n++] = best;
                }
            }
            for (int a = 1; a < n; a++)
                for (int b = a; b > 0 && ids[into[b]] < ids[into[b - 1]]; b--) { int t = into[b]; into[b] = into[b - 1]; into[b - 1] = t; }
            return n;
        }

        private static bool Contains(int[] list, int n, int value)
        {
            for (int i = 0; i < n; i++) if (list[i] == value) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------------ perks

        /// <summary>The perk strip: armed perks first, then cooling ones (the soonest to be ready first), then ready ones in catalogue order; at most <see cref="MaxPerks"/> indices into <paramref name="into"/>. Returns how many.</summary>
        public static int PickPerks(int count, PerkPhase[] state, float[] cooldown, int[] into)
        {
            int n = 0;
            for (int i = 0; i < count && n < MaxPerks; i++) if (state[i] == PerkPhase.Armed) into[n++] = i;
            while (n < MaxPerks)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                    if (state[i] == PerkPhase.Cooling && !Contains(into, n, i) && (best < 0 || cooldown[i] < cooldown[best])) best = i;
                if (best < 0) break;
                into[n++] = best;
            }
            for (int i = 0; i < count && n < MaxPerks; i++) if (state[i] == PerkPhase.Ready) into[n++] = i;
            return n;
        }

        /// <summary>The perk's name as the strip has room for it (nine characters): the catalogue's label when it fits, else a short one.</summary>
        public static string PerkName(string label)
        {
            switch (label)
            {
                case "ORBITAL ROD": return "ROD";
                case "RECON PASS": return "RECON";
                case "SAT CAMERA": return "CAMERA";
                case "CRUISE SALVO": return "CRUISE";
                case "ELINT SWEEP": return "ELINT";
                case "DECOY BARRAGE": return "DECOY";
                case "RADAR BLIND": return "BLIND";
                case "SAM NET DOWN": return "SAM NET";
                case "RECON TEAM": return "TEAM";
                case "SABOTAGE STRIKE": return "SABOTAGE";
                case "DIRECT ACTION": return "DIRECT";
                default: return string.IsNullOrEmpty(label) ? "" : label.Length <= 9 ? label : label.Substring(0, 9);
            }
        }

        public static PerkFront Front(string frontWord) => frontWord == "CYBER" ? PerkFront.Cyber : frontWord == "SOF" ? PerkFront.Sof : PerkFront.Space;

        // ------------------------------------------------------------------------------------------------ CYBER and SOF

        /// <summary>The trace burn in percent per second: a hop adds its own rate, every held node adds the held rate (<see cref="CyberRules"/>).</summary>
        public static float TraceRate(bool hopping, int held) => (hopping ? CyberRules.HopTraceRate(NodeKind.Radar) : 0f) + CyberRules.HeldTraceRate * Math.Max(0, held);

        /// <summary>A window of at least this many seconds between two snapshots is long enough to tell the real trace rate from the rules' (the trace is a whole percent on the wire).</summary>
        public const float TraceWindow = 3f;

        /// <summary>The host's pace against the rules': what the trace gained over <paramref name="seconds"/> against what <paramref name="ruleRate"/> predicts, within the host's 0.25 to 3; <paramref name="previous"/> when nothing can be said (no time, no burn expected).</summary>
        public static float TraceFactor(float gained, float seconds, float ruleRate, float previous)
        {
            if (seconds <= 0f || ruleRate <= 0f) return previous;
            float f = Math.Max(0f, gained) / (ruleRate * seconds);
            return f < 0.25f ? 0.25f : f > 3f ? 3f : f;
        }

        /// <summary>The trace a snapshot of <paramref name="trace"/> percent has reached <paramref name="age"/> seconds later at <paramref name="rate"/> percent per second.</summary>
        public static float TraceNow(float trace, float rate, float age) => Math.Min(CyberRules.TraceMax, trace + rate * Math.Max(0f, Math.Min(age, TraceHorizon)));

        public static ChainNodeKind Chain(NodeKind kind) => (ChainNodeKind)(byte)kind;

        /// <summary>A SOF team is on a mission: it has one and is on its way, on site, pinned or coming home.</summary>
        public static bool OnMission(TeamState state, MissionKind mission) =>
            mission != MissionKind.None && (state == TeamState.Moving || state == TeamState.OnSite || state == TeamState.Pinned || state == TeamState.Returning);

        /// <summary>The mission in one word of at most seven letters.</summary>
        public static string MissionWord(MissionKind mission) =>
            mission == MissionKind.Recon ? "RECON" : mission == MissionKind.Lase ? "LASE" : mission == MissionKind.Sabotage ? "SABOT" : mission == MissionKind.Seize ? "SEIZE" : mission == MissionKind.Tap ? "TAP" : "";
    }
}
