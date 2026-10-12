using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.DynamicOperations.Domain;
using BoscaliSummer.Modules.DynamicOperations.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    // STRIKE COMMAND POST: a card for an enemy command post this faction has confirmed. The staff board owns which posts are known; this owns the card.
    internal sealed partial class OperationsManager
    {
        private void AddStrikePost(FactionHQ hq, FactionBoard board, float now, float scale, int chainDepth)
        {
            if (!ModuleServices.TryGet(out IHighCommandView staff)) return;
            // In the tied pass only a post inside the operation's radius qualifies (and the card then earns a wave credit); the untied pass takes the nearest to a base.
            float x = tie.IsLive ? tie.X : float.NaN, z = tie.IsLive ? tie.Z : float.NaN;
            float reach = tie.IsLive ? SortieFocusRules.ClampRadius(tie.Radius) : 60000f;
            if (!staff.TryPickStrikePost(hq, x, z, reach, id => board.Rules.WasIssued(OperationKind.StrikePost, id + 1), out CommandPostTarget post) ||
                post.Post == null || board.Rules.WasIssued(OperationKind.Interdict, post.Post.GetInstanceID())) return;       // one kill, one payout
            DistanceToOwnedBase(new Vector3(post.X, 0f, post.Z), hq, out Airbase anchor);
            if (anchor == null) return;
            // Issued key = office + 1: once per office per mission, even after the building is rebuilt.
            Target target = Add(board, now, OperationKind.StrikePost, anchor, post.Post, hq, OperationReward.None, 1200, 100, scale, chainDepth, post.Id + 1);
            if (target == null) return;
            target.Position = new Vector3(post.X, post.Y, post.Z);
            target.Radius = 0f;
            target.SubjectId = post.Id;
            target.Name = OperationsNet.Text(post.Rank + " " + post.Name + " · " + post.Site);
        }

        private static bool IsStrikePostUnit(FactionBoard board, Unit unit)
        {
            for (int i = 0; i < board.Targets.Count; i++)
                if (board.Targets[i].Mission.Kind == OperationKind.StrikePost && board.Targets[i].Mission.IsLive && ReferenceEquals(board.Targets[i].Unit, unit)) return true;
            return false;
        }
    }
}
