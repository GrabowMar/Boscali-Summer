using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>The old staff's front, as the old logic keeps it (string status; no census, no counts). Deleted with the adapter.</summary>
    internal sealed class LegacyFront
    {
        internal readonly string Key, Label, Status;
        internal readonly float X, Z, Pressure, Trend;
        internal readonly bool Observed;

        internal LegacyFront(string key, string label, float x, float z, string status, float pressure, float trend, bool observed)
        { Key = key; Label = label; X = x; Z = z; Status = status; Pressure = pressure; Trend = trend; Observed = observed; }
    }

    /// <summary>The old staff's proposal (string kind and risk). Deleted with the adapter.</summary>
    internal sealed class LegacyProposal
    {
        internal readonly int Id, Revision;
        internal readonly string Kind, Label, TargetKey, Risk;
        internal readonly float X, Z;

        internal LegacyProposal(int id, int revision, string kind, string label, string targetKey, float x, float z, string risk)
        { Id = id; Revision = revision; Kind = kind; Label = label; TargetKey = targetKey; X = x; Z = z; Risk = risk; }
    }

    /// <summary>
    /// Throw-away (S2 freeze): maps the old staff state into the frozen snapshot so the old logic keeps running behind the new
    /// contract. Whatever the old logic does not know stays at the unknown encoding (255, -1, no wave, no census, no result).
    /// The engine slice replaces this with the real publish and deletes this file.
    /// </summary>
    internal static class LegacySnapshot
    {
        private const int DisplayName = 28;
        private const float OperationRadius = 4000f;   // the radius the old TryGetStation gave the air-station view

        internal static TheaterWarSnapshot Build(LivingFrontService.FactionWar war, float now)
        {
            var snapshot = new TheaterWarSnapshot
            {
                Faction = war.HQ.faction.factionName,
                Posture = war.Posture,
                Caps = TheaterCaps.StaffRunning,
                StaffPickSeconds = war.Proposals.Count > 0 ? Math.Min(120f, Math.Max(0f, war.OfferDeadline - now)) : -1f,
                Reason = war.Active != null || war.Proposals.Count > 0 ? TheaterEmptyReason.None
                    : war.Fronts.Count == 0 ? TheaterEmptyReason.NoFronts : TheaterEmptyReason.FrontsQuiet,
            };

            int count = Math.Min(war.Fronts.Count, LivingWarRules.MaximumFronts);
            snapshot.Fronts = new TheaterFrontView[count];
            for (int i = 0; i < count; i++)
            {
                LegacyFront f = war.Fronts[i];
                WarFrontRead read = i < war.Reads.Count ? war.Reads[i] : default;
                snapshot.Fronts[i] = new TheaterFrontView
                {
                    Key = f.Key, Name = Clip(f.Label), X = f.X, Z = f.Z,
                    Pressure = f.Observed ? Math.Min(1f, Math.Max(0f, f.Pressure)) : 0f,
                    Trend = f.Observed ? Math.Min(1f, Math.Max(-1f, f.Trend)) : 0f,
                    ObservedAgeSeconds = f.Observed ? 0f : -1f,
                    Status = f.Status == "IN CONTACT" ? TheaterFrontStatus.InContact : f.Status == "UNDER PRESSURE" ? TheaterFrontStatus.UnderPressure
                        : f.Status == "HOLDING" ? TheaterFrontStatus.Holding : TheaterFrontStatus.Unconfirmed,
                    Observed = f.Observed, Objective = read.Objective, Held = read.Held,
                    NearFriendly = Tally(read.Friendly), NearHostile = f.Observed ? Tally(read.Hostile) : (byte)255,
                };
            }

            count = Math.Min(war.Proposals.Count, LivingWarRules.MaximumOffers);
            snapshot.Proposals = new TheaterProposalView[count];
            for (int i = 0; i < count; i++)
            {
                LegacyProposal p = war.Proposals[i];
                WarFrontRead read = default;
                for (int j = 0; j < war.Reads.Count; j++) if (war.Reads[j].Key == p.TargetKey) { read = war.Reads[j]; break; }
                snapshot.Proposals[i] = new TheaterProposalView
                {
                    Id = p.Id, Revision = p.Revision, Kind = KindOf(p.Kind), Name = Clip(p.Label), TargetKey = p.TargetKey,
                    X = p.X, Z = p.Z, Radius = OperationRadius, Pressure = Math.Min(1f, Math.Max(0f, read.Pressure)),
                    Risk = p.Risk == "HIGH" ? TheaterRisk.High : p.Risk == "MODERATE" ? TheaterRisk.Moderate : TheaterRisk.Unknown,
                    Carrier = TheaterCarrier.Ground, FriendlyGround = Tally(read.Friendly),
                    HostileTracked = read.Observed ? Tally(read.Hostile) : (byte)255, Recommended = i == 0,
                };
            }

            LivingFrontService.Operation op = war.Active;
            if (op != null)
                snapshot.Operation = new TheaterOperationView
                {
                    Id = op.Id, Revision = op.Revision, Kind = KindOf(op.Kind), TargetKey = op.Key, Name = Clip(op.Label),
                    X = op.X, Z = op.Z, OriginX = op.X, OriginZ = op.Z, Radius = OperationRadius,
                    Phase = op.Phase == "FORMING" ? TheaterPhase.Forming : op.Phase == "ADVANCING" ? TheaterPhase.Advancing
                        : op.Phase == "IN CONTACT" ? TheaterPhase.InContact : op.Phase == "HOLDING" ? TheaterPhase.Holding
                        : op.Phase == "SCOUTING" ? TheaterPhase.Scouting : TheaterPhase.Awaiting,
                    ElapsedSeconds = Math.Min(3600f, Math.Max(0f, now - op.Started)), LimitSeconds = LivingWarRules.OperationLimitSeconds,
                    Forces = new TheaterForcesView { Groups = (byte)Math.Min(32, Math.Max(0, op.GroundGroups)) },
                };

            snapshot.Log = new string[war.Log.Count];
            for (int i = 0; i < snapshot.Log.Length; i++) snapshot.Log[i] = war.Log.Entries[i];
            return snapshot;
        }

        private static TheaterOpKind KindOf(string kind) =>
            kind == "DEFEND" ? TheaterOpKind.Defend : kind == "RECON" ? TheaterOpKind.Recon : TheaterOpKind.Assault;

        private static byte Tally(int count) => (byte)Math.Min(254, Math.Max(0, count));

        private static string Clip(string name) =>
            string.IsNullOrEmpty(name) ? "" : name.Length > DisplayName ? name.Substring(0, DisplayName) : name;
    }
}
