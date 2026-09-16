using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Framework.Features
{
    /// <summary>
    /// Process-wide list of the host-authoritative settings every installed feature
    /// publishes. Command's SET SERVER page reads it late, so a feature that installs after
    /// Command (or not at all) is still shown honestly and never reached into.
    /// </summary>
    internal sealed class HostSettingsBoard
    {
        private readonly List<IHostSettingsView> views = new List<IHostSettingsView>();

        /// <summary>Registration order is display order: the composition root's feature order.</summary>
        public IReadOnlyList<IHostSettingsView> Views => views;

        public void Add(IHostSettingsView view)
        {
            if (view == null || views.Contains(view)) return;
            views.Add(view);
        }

        public void Remove(IHostSettingsView view)
        {
            if (view != null) views.Remove(view);
        }
    }
}
