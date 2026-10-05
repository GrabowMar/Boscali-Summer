using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Where every command goes (spec WMC program §3.2): checked and executed on the host (a client's transport
    /// arrives with P12), answered with a toast — the executor's words, nothing when the ack is empty.</summary>
    internal static class WingOrders
    {
        /// <summary>BEHAVIOUR › STANCES' DEFAULT FOR: an accepted player order whose kind has a default stance also applies that
        /// stance to the same scope (the SetDoctrine it sends has no default, so this never recurses).</summary>
        private static void ApplyDefaultStance(WingOrder o)
        {
            WingTask t = o.Task;
            string key = Domain.Pure.StanceDefaults.Key(o.Kind.ToString(), t != null ? (int)t.Kind : 0, t != null && t.GuardRadius > 0f, t != null && t.Scout);
            if (key == null) return;
            Domain.Pure.Stance s = WmcStanceFiles.Book.Find(WmcStanceFiles.Book.DefaultFor(key));
            if (s != null) WmcStanceActions.Run(o.Scope, s);
        }

        public static OrderResult Run(WingOrder o)
        {
            OrderResult r = OrderExecutor.Execute(o);
            WingAcks.Push(o, r);
            string text = r.Accepted ? r.Ack : r.Reason;
            if (!string.IsNullOrEmpty(text)) WingToast.Show(text);
            if (r.Accepted && o.Source == OrderSource.Player) ApplyDefaultStance(o);
            return r;
        }
    }
}
