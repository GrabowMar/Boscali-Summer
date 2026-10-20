using System;
using System.Collections;
using BepInEx.Logging;
using BoscaliSummer.Modules.Support.Domain.Space;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// STARFALL (SPACE R4): the KINETIC bird walks a line of small rods across the aim. 3 to 5 of them by the front's POWER (<see cref="StarfallPlan"/>), 300 m apart along the line from the pilot's aircraft through
    /// the aim, released 0.4 s apart, each about 35 % of an ORBITAL ROD's blast. It is ORBITAL ROD's own pipeline: the same rod shot (<see cref="ArtilleryAction.Shot"/> and <see cref="ArtilleryAction.Release"/>), the same
    /// <see cref="Visuals.KineticRodStrikeVisuals"/> descent and the same <see cref="RodGuard"/> stand-off, which refuses the whole line when any impact point has a friendly player inside it. The first rod is the bird's physical
    /// launch receipt; the rest follow from a short walk that holds one Strike slot until they have landed.
    /// </summary>
    internal sealed class StarfallAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) => 1f; // availability flag; the allocation price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            MissileDefinition definition = context.Host.Vanilla.Artillery(context.Settings.ArtilleryDefinitionKey.Value);
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning("[Support] STARFALL is unavailable: no non-nuclear missile definition resolved.");
                return SupportResult.CapabilityUnavailable;
            }
            if (NetworkSceneSingleton<Spawner>.i == null) return SupportResult.CapabilityUnavailable;
            RodGuard.Track(definition);
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground)) return SupportResult.InvalidTarget;
            bool hasOrigin = SupportTargeting.TryOrigin(context.Player, out Vector3 origin);
            if (hasOrigin && Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value) return SupportResult.OutOfRange;

            int count = StarfallPlan.Count(context.Power);
            var points = new SpaceImpact[count];
            GlobalPosition aim = ground.ToGlobalPosition(), from = origin.ToGlobalPosition();
            StarfallPlan.Points((float)aim.x, (float)aim.z, (float)from.x, (float)from.z, hasOrigin, count, points);
            var impacts = new GlobalPosition[count];
            for (int i = 0; i < count; i++)
            {
                if (!SupportTargeting.TryMapPoint(new GlobalPosition(points[i].X, context.Target.y, points[i].Z), out Vector3 landing)) return SupportResult.InvalidTarget;
                impacts[i] = landing.ToGlobalPosition();
                // Refused before anything is spent: no friendly may be inside the standoff of any impact of the line.
                if (RodGuard.FriendlyNear(context.Owner, impacts[i]))
                {
                    context.Logger.LogInfo("[Support] STARFALL refused: a friendly player is inside the standoff of rod " + (i + 1) + ".");
                    return SupportResult.FriendlyNear;
                }
            }
            if (context.SpaceTask == null || !context.SpaceTask.CanLaunch) return SupportResult.UplinkDown;
            int scene = context.Host.SceneGeneration;
            if (!context.Host.TryReserve(context.Owner, SupportPool.Strike)) return SupportResult.Busy;

            string unique = SupportNaming.Unique("Rod", context);
            string guide = context.Player?.Aircraft != null ? context.Player.Aircraft.UniqueName : string.Empty;
            var shots = new ArtilleryAction.Shot[count];
            for (int i = 0; i < count; i++)
                shots[i] = new ArtilleryAction.Shot
                {
                    Host = context.Host, Logger = context.Logger, Owner = context.Owner, Definition = definition,
                    SpaceTask = i == 0 ? context.SpaceTask : null, Continuation = i > 0, SharedPool = true,
                    Impact = impacts[i], Scene = scene, Guide = guide,
                    Name = SupportEffectPolicy.RodName(unique + ":f" + i, StarfallPlan.BlastScale, StarfallPlan.BlastScale)
                };
            SupportResult first = ArtilleryAction.Release(shots[0]); // the bird's physical launch receipt
            if (first != SupportResult.Accepted)
            {
                context.Host.Release(context.Owner, SupportPool.Strike);
                return first;
            }
            RodGuard.Warn(context.Owner, "ORBITAL BARRAGE INBOUND — " + count + " RODS WALKING · FIRST IMPACT IN " + Mathf.Max(1, Mathf.FloorToInt(RodGuard.Flight.Seconds)) + " S");
            try { context.Host.Run(Walk(context.Host, shots, scene, context.Owner, context.Logger)); }
            catch (Exception e)
            {
                context.Host.Release(context.Owner, SupportPool.Strike);
                context.Logger.LogWarning("[Support] STARFALL walk could not start (only the first rod flies): " + e.Message);
                context.Host.ReportContacts(context.RequestId, 1);
                return SupportResult.Accepted;
            }
            context.Host.ReportContacts(context.RequestId, count);
            context.Logger.LogInfo("[Support] STARFALL: " + count + " rods, power " + context.Power.ToString("0.00") + ".");
            return SupportResult.Accepted;
        }

        /// <summary>Releases rods 2..N 0.4 s apart (any that finds a friendly in its stand-off ends the line), then holds the one Strike slot until every rod has landed or 30 s have passed.</summary>
        private static IEnumerator Walk(ISupportHost host, ArtilleryAction.Shot[] shots, int scene, FactionHQ owner, ManualLogSource logger)
        {
            try
            {
                for (int i = 1; i < shots.Length; i++)
                {
                    yield return new WaitForSeconds(StarfallPlan.ReleaseGap);
                    if (host.SceneGeneration != scene) yield break;
                    SupportResult result = ArtilleryAction.Release(shots[i]);
                    if (result == SupportResult.Accepted) continue;
                    logger.LogInfo("[Support] STARFALL rod " + (i + 1) + " of " + shots.Length + " not released: " + result + ".");
                    if (result == SupportResult.FriendlyNear) break;
                }
                float deadline = Time.time + 30f;
                while (Time.time < deadline && Flying(shots)) yield return null;
            }
            finally
            {
                if (host.SceneGeneration == scene) host.Release(owner, SupportPool.Strike);
            }
        }

        private static bool Flying(ArtilleryAction.Shot[] shots)
        {
            for (int i = 0; i < shots.Length; i++)
                if (shots[i].Launched != null && !shots[i].Launched.disabled) return true;
            return false;
        }
    }
}
