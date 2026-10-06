using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The BURN packages of <see cref="CyberService"/>: what a claimed package does when it fires, and the held-node kill assist.</summary>
    internal sealed partial class CyberService
    {
        private readonly Dictionary<uint, float> recentKills = new Dictionary<uint, float>(16);

        /// <summary>
        /// EXPLOIT on FLARE BARRAGE and EMP (spec §1.4): while the faction holds any node the effect lasts x1.5 (host only; the quote chip says -25 %).
        /// <paramref name="ceiling"/> keeps the value inside what the replicated flare name accepts on peers.
        /// </summary>
        internal static float ExploitDuration(FactionHQ owner, float seconds, float ceiling) =>
            Active != null && owner != null && Active.HoldsAnyNode(owner) ? System.Math.Min(seconds * CyberPackages.ExploitDurationFactor, ceiling) : seconds;

        partial void ResetPackages() => recentKills.Clear();

        /// <summary>A claimed CYBER package fires: its effect starts for the package seconds. False when the effect book is full.</summary>
        internal bool FirePackage(FactionHQ owner, TaskedLaunchJob job)
        {
            if (!factions.TryGetValue(owner, out FactionCyber f) || !CyberPackages.TryOfAction(job.Action, out PackageDef def)) return false;
            bool exploit = CyberRules.Exploit(def.Node);
            CyberEffect e = CyberPackages.Package(def, job.CallId, job.Aim.X, job.Aim.Z, f.Key, exploit, SupportManager.MissionNow());
            if (!f.Desk.Effects.Add(e)) return false;
            Plugin.Logger?.LogInfo("[Support.Cyber] " + def.Label + " fired by " + job.Pilot + " at " + job.Aim.X.ToString("0") + "," + job.Aim.Z.ToString("0") + ".");
            return true;
        }

        /// <summary>An enemy unit died while its node is held by a faction: the operator earns a small assist (spec §1.3). Deduped per target.</summary>
        internal void NoteKill(Unit target)
        {
            if (!GameAccess.IsServer() || factions.Count == 0 || target == null || target.NetworkHQ == null || target.persistentID.Id == 0) return;
            float now = SupportManager.MissionNow();
            if (recentKills.TryGetValue(target.persistentID.Id, out float at) && now - at < 5f) return;
            foreach (var pair in factions)
            {
                FactionCyber f = pair.Value;
                if (f.Owner == target.NetworkHQ) continue;
                foreach (CyberIntrusion x in f.Desk.Network.Active)
                    foreach (HeldNode h in x.Held)
                        if (f.Desk.Ids.TryKey(h.NodeId, out NodeKind kind, out uint key) && key == target.persistentID.Id && kind != NodeKind.Relay)
                        {
                            if (recentKills.Count > 64) recentKills.Clear();
                            recentKills[target.persistentID.Id] = now;
                            manager.CyberAssist(f.Owner, x.Operator, 5f);
                            return;
                        }
            }
        }
    }
}
