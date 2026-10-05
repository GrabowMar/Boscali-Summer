using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Ops;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// What a finished countdown does. ZERO-DAY: a SAM NET FAIL on one revealed SAM cluster through the launch block a held SAM C2 node uses (3 minutes, halved when an enemy EW truck stands
    /// within 18 km of the cluster).
    /// </summary>
    internal sealed partial class OpsService
    {
        /// <summary>The countdown ended and the bar held: run the operation's effect. The desk is already DONE.</summary>
        private void Fire(FactionOps f, OpEvent e)
        {
            switch (e.Op)
            {
                case OpKind.ZeroDay: FireZeroDay(f, e.Target); break;
            }
        }

        private void FireZeroDay(FactionOps f, in OpTarget target)
        {
            float now = SupportManager.MissionNow();
            bool truckNear = cyber != null && cyber.EnemyTruckWithin(f.Owner, target.X, target.Z, OpsRules.EwHalveMetres);
            float seconds = OpsRules.ZeroDayEffectSeconds(truckNear);
            if (cyber == null || !cyber.AddSamNetFail(f.Owner, target, seconds))
            {
                Plugin.Logger?.LogWarning("[Support.Ops] " + f.Owner.name + " SAM NET FAIL could not be applied (CYBER desk missing or the effect book is full).");
                return;
            }
            f.Desk.SetEffectEnd(OpDomain.Cyber, now + seconds);
            Plugin.Logger?.LogInfo("[Support.Ops] " + f.Owner.name + " SAM NET FAIL on node " + target.Id + " for " + (int)seconds + " s" + (truckNear ? " (enemy EW truck within 18 km: halved)." : "."));
        }
    }
}
