using NOAvionics;

using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL (spec 2026-10-04 §4.2, mockups board/orders.html, formation.html, route.html), the page the player spends most of the
    /// WMC on, as a kit v2 flow: the sub-tabs, the dense wing table that is also the scope (who orders go to), then ORDERS (stances and their
    /// fine-tune, the command card with REACT, the queue, then THREATS, FLIGHT POOL and the whole-wing settings), FORMATION (the Station
    /// Board) and ROUTE · AP (the Flight Plan). The wing table is full on ORDERS and one summary line on the others (a tap expands it). The
    /// console body scrolls, so nothing pages or clips.</summary>
    internal sealed partial class WmcTactical : IWmcPage
    {
        public const int SubOrders = 0, SubFormation = 1, SubRoute = 2;
        private static readonly string[] SubLabels = { "ORDERS", "FORMATION", "ROUTE · AP" };
        private static readonly string[] SubKeys = { "orders", "formation", "route" };
        private static readonly AvIcon[] SubIcons = { AvIcon.Flag, AvIcon.LayersSubtract, AvIcon.MapPin };
        private static readonly string[] SubHelp =
        {
            "Stances, every order, the threats and what the scope carries.",
            "The Station Board: the shape each element flies and how well it holds it, spacing and stack.",
            "The Flight Plan: the scope's route as legs, and your own autopilot with NAV.",
        };

        private readonly WmcControls ids;
        private readonly WmcForm form;
        private readonly WmcRoute route;
        private WmcWingTable table;
        private AvFlow flow;
        private AvTicker ticker;
        private int pageIndex;
        private WmcSubPages subPages;
        private AvControl[] subTabs;
        private AvPopup popup;
        private int sub = -1;
        private WmcContext last;
        private bool relayout, tableOpen;

        public WmcTactical(WmcControls controls)
        {
            ids = controls;
            form = new WmcForm(controls);
            route = new WmcRoute(controls);
        }

        public WmcForm Form => form;

        public WmcRoute Route => route;

        public int Sub => sub;

        public string Hint => last != null && last.Count == 0 && !last.Client
            ? "No wingmen yet: CALL one on the WING row, requisition on SUPPLY, or use the radial menu."
            : sub == SubFormation ? form.Hint : sub == SubRoute ? route.Hint
            : "Click the table to choose who orders go to; a stance sets how the scope fights.";

        public string Alert => alertText;

        public void Build(AvFlow pageFlow, AvTicker pageTicker, int index)
        {
            flow = pageFlow;
            ticker = pageTicker;
            pageIndex = index;

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

            BuildTable(flow);

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
            table.SetMode(k != SubOrders, !tableOpen);
            AvPopup.CloseAny();
            WmcNameField.BlurAny();
            if (last != null) Refresh(last);
            flow.RequestRelayout();
        }

        /// <summary>A control of a sub-page shows that sub-page first (automation).</summary>
        public void ShowSubFor(string id)
        {
            if (id == null) return;
            route.RevealFor(id);
            if (id.StartsWith("form.", System.StringComparison.Ordinal)) ShowSub(SubFormation);
            else if (id.StartsWith("plan.", System.StringComparison.Ordinal)) ShowSub(SubRoute);
            else if (id.StartsWith("tac.orders.", System.StringComparison.Ordinal) || id.StartsWith("tac.react.", System.StringComparison.Ordinal)
                     || id.StartsWith("tac.posture.", System.StringComparison.Ordinal) || id.StartsWith("tac.threat", System.StringComparison.Ordinal)
                     || id.StartsWith("tac.stance.", System.StringComparison.Ordinal) || id.StartsWith("tac.queue.", System.StringComparison.Ordinal)
                     || id.StartsWith("tac.alert", System.StringComparison.Ordinal))
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

        public void Shown(WmcContext c)
        {
            profileShown = null;
            if (sub == SubFormation) form.Shown(c);
            else if (sub == SubRoute) route.Shown(c);
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            // The alerts first: the table's summary line, the strip and the footer read this refresh's.
            RefreshAlertList(c);
            table.FlashUntil = flashUntil;
            table.Refresh(c, AlertWord(c, out AvState alertAs), alertAs);
            RefreshTableIds(c);
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
