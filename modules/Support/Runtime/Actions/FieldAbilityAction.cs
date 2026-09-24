using System;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// SPEC OPS › ACTIONS: an ability a held post grants. SPOT (observation post) reveals hostile
    /// ground units around the mark; SUPPRESS (saboteur cell) jams hostile ground radars there for
    /// a short window. Only a post of the right kind whose reach covers the mark can carry it, and
    /// the best-ranked such team sets its size. Allocation, the shared cooldown and the ability's
    /// own recharge are the manager's; this class re-checks coverage and touches the game.
    /// </summary>
    internal sealed class FieldAbilityAction : ISupportAction
    {
        private readonly FieldAbility ability;

        public FieldAbilityAction(FieldAbility ability) => this.ability = ability;

        public float BaseCost(in SupportContext context) =>
            FieldCatalog.AbilityCost(ability) * context.Settings.SpecOpsCostScale.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            SpecOpsDetachment detachment = context.Host.Space.DetachmentFor(context.Owner);
            if (detachment == null) return SupportResult.CapabilityUnavailable;
            if (!detachment.Enabled) return SupportResult.Disabled;
            SupportTargeting.TryMapPoint(context.Target, out Vector3 ground);
            GlobalPosition target = ground.ToGlobalPosition();
            int team = detachment.Covering(FieldCatalog.PostFor(ability), target.x, target.z);
            if (team < 0) return SupportResult.NoFieldPost;
            int rank = detachment.Team(team).Rank;

            try
            {
                if (ability == FieldAbility.Spot || ability == FieldAbility.Skywatch || ability == FieldAbility.Eavesdrop)
                {
                    RevealFilter filter = ability == FieldAbility.Spot ? RevealFilter.Ground :
                        ability == FieldAbility.Skywatch ? RevealFilter.Air : RevealFilter.Emitters;
                    float radius = ability == FieldAbility.Spot ? FieldCatalog.SpotRadius(rank) :
                        ability == FieldAbility.Skywatch ? FieldCatalog.SkywatchRadius(rank) : FieldCatalog.EavesdropRadius(rank);
                    int contacts = ReconAction.Reveal(context.Owner, target, radius, context.Logger, filter);
                    context.Host.ReportContacts(context.RequestId, contacts);
                    return SupportResult.Accepted;
                }
                bool hunt = ability == FieldAbility.Hunt;
                float jamRadius = hunt ? FieldCatalog.HuntRadius(rank) : FieldCatalog.SuppressRadius(rank);
                float jamSeconds = hunt ? FieldCatalog.HuntSeconds(rank) : FieldCatalog.SuppressSeconds(rank);
                if (!context.Host.SpecOps.AddJam(context.Owner, target.x, target.z, jamRadius, jamSeconds,
                    context.Host.OrbitNow)) return SupportResult.Busy;
                if (hunt)
                {
                    try
                    {
                        int contacts = ReconAction.Reveal(context.Owner, target, jamRadius, context.Logger,
                            RevealFilter.Emitters);
                        context.Host.ReportContacts(context.RequestId, contacts);
                    }
                    catch (Exception e)
                    {
                        context.Logger.LogWarning("[Support] HUNT reveal failed after jam activation: " + e.Message);
                    }
                }
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] " + FieldWords.Ability(ability) + " failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
