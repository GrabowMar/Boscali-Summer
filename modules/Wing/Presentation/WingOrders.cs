using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Where every command goes (spec WMC program §3.2): checked and executed on the host (a client's transport
    /// arrives with P12), answered with a toast — the executor's words, nothing when the ack is empty.</summary>
    internal static class WingOrders
    {
        public static OrderResult Run(WingOrder o)
        {
            OrderResult r = OrderExecutor.Execute(o);
            WingAcks.Push(o, r);
            string text = r.Accepted ? r.Ack : r.Reason;
            if (!string.IsNullOrEmpty(text)) WingToast.Show(text);
            if (r.Accepted && o.Source == OrderSource.Player) WingSortieOrders.OnPlayerOrder(o);
            return r;
        }
    }
}
