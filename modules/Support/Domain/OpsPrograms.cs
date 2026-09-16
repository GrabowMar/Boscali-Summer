using System;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>Wire-stable program bytes. Order is the snapshot's tier-array order.</summary>
    internal enum OpsProgramId : byte
    {
        Pathfinders = 0,
        SabotageCells = 1,
        CsarReadiness = 2,
        HumintNetwork = 3,
        DecryptionArray = 4,
        AssetRecruitment = 5
    }

    /// <summary>The two token pools the programs fill. Wire-stable.</summary>
    internal enum OpsReserve : byte
    {
        SpecOps = 0,
        Intel = 1
    }

    internal readonly struct OpsProgramInfo
    {
        public readonly OpsProgramId Id;
        public readonly OpsReserve Reserve;
        public readonly string Code;
        public readonly string Name;
        public readonly string Summary;

        /// <summary>What buying tier n+1 adds; index 0 describes tier 1.</summary>
        public readonly string[] Tiers;

        /// <summary>Base allocation to reach tier n; index 0 is unused and zero.</summary>
        public readonly float[] Costs;

        /// <summary>Reserve tokens per minute while holding tier n; index 0 is zero.</summary>
        public readonly float[] YieldPerMinute;

        public OpsProgramInfo(OpsProgramId id, OpsReserve reserve, string code, string name, string summary,
                              string[] tiers, float[] costs, float[] yieldPerMinute)
        {
            Id = id;
            Reserve = reserve;
            Code = code;
            Name = name;
            Summary = summary;
            Tiers = tiers;
            Costs = costs;
            YieldPerMinute = yieldPerMinute;
        }
    }

    /// <summary>
    /// One faction's special-operations and intelligence programs: funded tiers and the
    /// readiness tokens they accrue. Groundwork for theater events — nothing spends tokens
    /// yet except <see cref="TryConsume"/>, the seam a future event consumer will call on
    /// the host. The host ticks and mutates; a client only mirrors snapshot bytes. Every
    /// value is bounded: three tiers, six programs, a fixed cap per reserve.
    /// </summary>
    internal sealed class OpsProgramLedger
    {
        public const int MaxTier = 3;
        public const int ReserveCap = 8;

        /// <summary>Longest step one tick may accrue; a stall or a pause never pays out a burst.</summary>
        public const float MaximumTickSeconds = 5f;

        public static readonly OpsProgramInfo[] Programs =
        {
            new OpsProgramInfo(OpsProgramId.Pathfinders, OpsReserve.SpecOps, "PTH", "PATHFINDER TEAMS",
                "Forward teams mark landing zones and guide terminal strikes.",
                new[] { "Two teams inserted forward.", "Terminal guidance qualified.", "Theater-wide LZ network." },
                new[] { 0f, 400f, 750f, 1200f }, new[] { 0f, 0.15f, 0.25f, 0.35f }),
            new OpsProgramInfo(OpsProgramId.SabotageCells, OpsReserve.SpecOps, "SAB", "SABOTAGE CELLS",
                "Stay-behind cells staged against enemy logistics.",
                new[] { "One cell in place.", "Rail and fuel targets surveyed.", "Coordinated cell network." },
                new[] { 0f, 550f, 950f, 1500f }, new[] { 0f, 0.20f, 0.30f, 0.45f }),
            new OpsProgramInfo(OpsProgramId.CsarReadiness, OpsReserve.SpecOps, "CSR", "CSAR READINESS",
                "Rescue crews on alert for downed aircrew.",
                new[] { "Alert crew on standby.", "Escort package assigned.", "Round-the-clock alert." },
                new[] { 0f, 450f, 800f, 1250f }, new[] { 0f, 0.12f, 0.22f, 0.32f }),
            new OpsProgramInfo(OpsProgramId.HumintNetwork, OpsReserve.Intel, "HUM", "HUMINT NETWORK",
                "Handled agents in the theater report enemy intent.",
                new[] { "First sources handled.", "Networks in two sectors.", "Theater-wide reporting." },
                new[] { 0f, 450f, 800f, 1300f }, new[] { 0f, 0.15f, 0.25f, 0.35f }),
            new OpsProgramInfo(OpsProgramId.DecryptionArray, OpsReserve.Intel, "DCR", "DECRYPTION ARRAY",
                "Traffic analysis and codebreaking on intercepted nets.",
                new[] { "Traffic analysis online.", "Tactical codes broken.", "Command cipher exploited." },
                new[] { 0f, 600f, 1000f, 1550f }, new[] { 0f, 0.20f, 0.30f, 0.45f }),
            new OpsProgramInfo(OpsProgramId.AssetRecruitment, OpsReserve.Intel, "REC", "ASSET RECRUITMENT",
                "Recruit sources inside enemy command.",
                new[] { "Approach initiated.", "Staff-level source.", "Source at enemy HQ." },
                new[] { 0f, 700f, 1150f, 1700f }, new[] { 0f, 0.25f, 0.35f, 0.50f })
        };

        public static int ProgramCount => Programs.Length;
        public static int ReserveCount => 2;

        private readonly int[] tiers = new int[Programs.Length];
        private readonly int[] tokens = new int[2];
        private readonly float[] progress = new float[2];

        public static OpsProgramInfo Info(OpsProgramId id) => Programs[(int)id];

        public static string ReserveName(OpsReserve reserve) =>
            reserve == OpsReserve.SpecOps ? "SOF READINESS" : "INTEL TOKENS";

        public static string ReserveTag(OpsReserve reserve) =>
            reserve == OpsReserve.SpecOps ? "SOF" : "INT";

        public int Tier(OpsProgramId id) => tiers[(int)id];

        public int Tokens(OpsReserve reserve) => tokens[(int)reserve];

        /// <summary>Fraction toward the next token, 0..1. Zero while the reserve is full.</summary>
        public float Progress(OpsReserve reserve) => progress[(int)reserve];

        public bool Full(OpsReserve reserve) => tokens[(int)reserve] >= ReserveCap;

        /// <summary>Base allocation for the next tier, before the host's price multipliers; 0 at max.</summary>
        public float NextCost(OpsProgramId id)
        {
            int tier = Tier(id);
            return tier >= MaxTier ? 0f : Info(id).Costs[tier + 1];
        }

        public bool CanInvest(OpsProgramId id) => (int)id < Programs.Length && Tier(id) < MaxTier;

        /// <summary>Authority-free tier change; the host prices and charges separately.</summary>
        public bool TryInvest(OpsProgramId id)
        {
            if (!CanInvest(id)) return false;
            tiers[(int)id]++;
            return true;
        }

        /// <summary>Total funded tiers feeding one reserve.</summary>
        public int FundedTiers(OpsReserve reserve)
        {
            int total = 0;
            for (int i = 0; i < Programs.Length; i++)
                if (Programs[i].Reserve == reserve) total += tiers[i];
            return total;
        }

        public float YieldPerMinute(OpsReserve reserve)
        {
            float total = 0f;
            for (int i = 0; i < Programs.Length; i++)
                if (Programs[i].Reserve == reserve) total += Programs[i].YieldPerMinute[tiers[i]];
            return total;
        }

        /// <summary>Seconds until the next token, or a negative value when none is coming.</summary>
        public float SecondsToNextToken(OpsReserve reserve)
        {
            float rate = YieldPerMinute(reserve) / 60f;
            if (rate <= 0f || Full(reserve)) return -1f;
            return (1f - Progress(reserve)) / rate;
        }

        /// <summary>Host-only accrual.</summary>
        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || deltaTime <= 0f) return;
            float step = Math.Min(deltaTime, MaximumTickSeconds);
            for (int r = 0; r < tokens.Length; r++)
            {
                if (tokens[r] >= ReserveCap)
                {
                    progress[r] = 0f;
                    continue;
                }
                progress[r] += YieldPerMinute((OpsReserve)r) / 60f * step;
                while (progress[r] >= 1f && tokens[r] < ReserveCap)
                {
                    tokens[r]++;
                    progress[r] -= 1f;
                }
                if (tokens[r] >= ReserveCap) progress[r] = 0f;
            }
        }

        /// <summary>
        /// Spend reserve tokens. The seam a theater-event consumer will call on the host;
        /// all-or-nothing so a partial spend never leaves an event half-funded.
        /// </summary>
        public bool TryConsume(OpsReserve reserve, int amount)
        {
            if (amount <= 0 || (int)reserve >= tokens.Length || tokens[(int)reserve] < amount) return false;
            tokens[(int)reserve] -= amount;
            return true;
        }

        /// <summary>Snapshot mirror. Out-of-range bytes clamp; a client never exceeds the caps.</summary>
        public void Mirror(byte[] tierBytes, byte specOpsTokens, byte intelTokens,
                           byte specOpsProgress, byte intelProgress)
        {
            for (int i = 0; i < tiers.Length; i++)
                tiers[i] = tierBytes != null && i < tierBytes.Length ? Math.Min(MaxTier, (int)tierBytes[i]) : 0;
            tokens[0] = Math.Min(ReserveCap, (int)specOpsTokens);
            tokens[1] = Math.Min(ReserveCap, (int)intelTokens);
            progress[0] = tokens[0] >= ReserveCap ? 0f : specOpsProgress / 255f;
            progress[1] = tokens[1] >= ReserveCap ? 0f : intelProgress / 255f;
        }

        /// <summary>Progress quantised for the snapshot byte.</summary>
        public byte ProgressByte(OpsReserve reserve) =>
            (byte)Math.Max(0, Math.Min(255, (int)Math.Round(Progress(reserve) * 255f)));

        public void Clear()
        {
            Array.Clear(tiers, 0, tiers.Length);
            Array.Clear(tokens, 0, tokens.Length);
            Array.Clear(progress, 0, progress.Length);
        }
    }
}
