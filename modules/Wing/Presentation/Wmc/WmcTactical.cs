using NOAvionics;
using UnityEngine;

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
    /// <summary>TACTICAL (spec 2026-09-28 FUI §TACTICAL), the page the player spends most of the WMC on, as a kit v2 flow: who orders
    /// go to (the scope bar), the alerts, the FLIGHT list, then 0.9's sub-tabs — ORDERS (the cue line, the order grid with REACT, the
    /// posture rows, THREATS and FLIGHT POOL), FORMATION (shapes, spacing, stack) and ROUTE·AP (the quick route and your own
    /// autopilot). The scope bar serves every sub-page; the console body scrolls, so nothing pages or clips.</summary>
    internal sealed partial class WmcTactical : IWmcPage
    {
        public const int SubOrders = 0, SubFormation = 1, SubRoute = 2;
        private static readonly string[] SubLabels = { "ORDERS", "FORMATION", "ROUTE · AP" };
        private static readonly string[] SubKeys = { "orders", "formation", "route" };
        private static readonly AvIcon[] SubIcons = { AvIcon.Flag, AvIcon.LayersSubtract, AvIcon.MapPin };
        private static readonly string[] SubHelp =
        {
            "Every order, the scope's posture, the threats and what the scope carries.",
            "The shape each element flies, and the wing's spacing and stack.",
            "The scope's quick route, and your own autopilot with NAV.",
        };

        private readonly WmcControls ids;
        private readonly WmcForm form;
        private readonly WmcRoute route;
        private WmcScopeBar scope;
        private AvFlow flow;
        private AvTicker ticker;
        private int pageIndex;
        private WmcSubPages subPages;
        private AvControl[] subTabs;
        private AvPopup popup;
        private int sub = -1;
        private WmcContext last;
        private bool relayout;

        public WmcTactical(WmcControls controls)
        {
            ids = controls;
            form = new WmcForm(controls);
            route = new WmcRoute(controls);
        }

        public WmcForm Form => form;

        public WmcRoute Route => route;

        public int Sub => sub;

        public string SubName => sub >= 0 && sub < SubLabels.Length ? SubLabels[sub] : "";

        public string Hint => last != null && last.Count == 0 && !last.Client
            ? "No wingmen yet: CALL one on the WING row, requisition on SUPPLY, or use the radial menu."
            : sub == SubFormation ? form.Hint : sub == SubRoute ? route.Hint
            : "Click wingmen to choose who orders go to; POSTURE sets how the scope fights.";

        public string Alert => alertText;

        public void Build(AvFlow pageFlow, AvTicker pageTicker, int index)
        {
            flow = pageFlow;
            ticker = pageTicker;
            pageIndex = index;
            scope = flow.Add(new WmcScopeBar(flow.Content, ids, "tac.scope.", "COMMAND"));
            BuildAlerts(flow);
            BuildFlight(flow);

            var specs = new AvControl.Spec[SubLabels.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                int k = i;
                specs[i] = new AvControl.Spec(SubLabels[i], () => ShowSub(k), AvButtonStyle.Default, SubIcons[i]);
            }
            subTabs = flow.Buttons(specs).Controls;
            for (int i = 0; i < subTabs.Length; i++)
            {
                subTabs[i].Help = SubHelp[i];
                ids.Add("tac.sub." + SubKeys[i], subTabs[i]);
            }
            // The 0.9 id of the merged sub-tab (its label lower-cased) still answers.
            ids.Add("tac.sub." + SubLabels[SubRoute].ToLowerInvariant(), subTabs[SubRoute]);

            subPages = flow.Add(new WmcSubPages(flow, ticker, pageIndex, SubLabels.Length));
            BuildOrders(subPages.Flow(SubOrders));
            form.Build(subPages.Flow(SubFormation), ticker, pageIndex);
            route.Build(subPages.Flow(SubRoute), ticker, pageIndex);
            // Last, so its list draws over the page.
            popup = new AvPopup(flow.Content, flow.Width);
            route.UsePopup(popup, flow.Content);
            ShowSub(SubOrders);
        }

        /// <summary>A sub-page by its number (the sub-tabs, automation).</summary>
        public void ShowSub(int k)
        {
            if (subPages == null || k < 0 || k >= SubLabels.Length) return;
            if (k != sub && last != null)
            {
                if (k == SubFormation) form.Shown(last);
                else if (k == SubRoute) route.Shown(last);
            }
            sub = k;
            subPages.Show(k);
            for (int i = 0; i < subTabs.Length; i++) subTabs[i].Latched = i == k;
            AvPopup.CloseAny();
            WmcNameField.BlurAny();
            if (last != null) Refresh(last);
        }

        /// <summary>A control of a sub-page shows that sub-page first (automation).</summary>
        public void ShowSubFor(string id)
        {
            if (id == null) return;
            if (id.StartsWith("form.", System.StringComparison.Ordinal)) ShowSub(SubFormation);
            else if (id.StartsWith("plan.", System.StringComparison.Ordinal)) ShowSub(SubRoute);
            else if (id.StartsWith("tac.orders.", System.StringComparison.Ordinal) || id.StartsWith("tac.react.", System.StringComparison.Ordinal)
                     || id.StartsWith("tac.posture.", System.StringComparison.Ordinal) || id.StartsWith("tac.threat", System.StringComparison.Ordinal))
                ShowSub(SubOrders);
        }

        /// <summary>A sub-page by its label (automation; ROUTE and FORM are the old names).</summary>
        public bool ShowSubNamed(string name)
        {
            if (string.Equals(name, "ROUTE", System.StringComparison.OrdinalIgnoreCase) || string.Equals(name, "AP", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowSub(SubRoute);
                return true;
            }
            if (string.Equals(name, "FORM", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowSub(SubFormation);
                return true;
            }
            for (int i = 0; i < SubLabels.Length; i++)
                if (string.Equals(SubLabels[i], name, System.StringComparison.OrdinalIgnoreCase))
                {
                    ShowSub(i);
                    return true;
                }
            return false;
        }

        private static bool InScope(WmcContext c, in SnapshotMember m) => c.InScope(m);

        private void PickElement(int e) => scope.PickElement(e);

        public void Shown(WmcContext c)
        {
            profileShown = null;
            if (sub == SubFormation) form.Shown(c);
            else if (sub == SubRoute) route.Shown(c);
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            scope.Refresh(c);
            // The alerts first: the card and the footer read this refresh's.
            RefreshAlertList(c);
            RefreshAlertCard(c);
            RefreshFlightHead(c);
            RefreshList(c);
            if (sub == SubOrders) RefreshOrders(c);
            else if (sub == SubFormation) form.Refresh(c);
            else if (sub == SubRoute) route.Refresh(c);
            if (relayout)
            {
                relayout = false;
                flow.RequestRelayout();
            }
        }
    }
}
