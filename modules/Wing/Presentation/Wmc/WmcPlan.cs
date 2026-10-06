using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>BEHAVIOUR (spec 2026-10-04 §4.1, §4.4) as a kit v2 flow page: STANCES (the stance list and editor, what each element
    /// flies, the radio: <see cref="WmcOptions"/>) and SORTIE (the plan against what happened on one timeline per element, the plan's
    /// bar, the events and the after-action report). It replaces TUNING, PLAN and RECORD: PLAN's map tools are gone (plans are built on
    /// ORDERS by queueing; SORTIE times, saves and runs them), and the STEPS table became the selected step's line and the LATE / EARLY
    /// events. The sub-pages share one flow line (<see cref="WmcSubPages"/>); the console body scrolls, so nothing pages or clips.
    /// The plan bar's list opens the toolkit popup (<see cref="AvPopup"/>).</summary>
    internal sealed partial class WmcPlan : IWmcPage
    {
        /// <summary>STANCES is the first sub-page (it keeps TUNING's number: the panel's scope bar rule), SORTIE the second (PLAN's:
        /// the map shows the plan only there).</summary>
        public const int SubStances = 0, SubSortie = 1, SubOptions = SubStances, SubPlan = SubSortie;
        private static readonly string[] SubLabels = { "STANCES", "SORTIE" };
        private static readonly AvIcon[] SubIcons = { AvIcon.AdjustmentsHorizontal, AvIcon.Activity };
        private static readonly string[] SubHelp =
        {
            "How the wing fights and flies: stances (targets, weapons, radar, defence, fall back, bingo and winchester), which element flies which, your radio.",
            "The plan against what happened, lane by lane, with the events and the after-action report. Plans are built on ORDERS.",
        };

        private readonly WmcControls ids;
        private readonly WmcOptions options;
        private AvFlow flow;
        private AvTicker ticker;
        private WmcSubPages subPages;
        private AvControl[] subTabs;
        private AvPopup popup;
        private int sub = -1;
        private WmcContext last;
        private bool relayout;

        public WmcPlan(WmcControls controls)
        {
            ids = controls;
            options = new WmcOptions(controls);
        }

        public WmcOptions Options => options;

        public int Sub => sub;

        public string SubName => sub >= 0 && sub < SubLabels.Length ? SubLabels[sub] : "";

        public string Hint => sub == SubStances ? options.Hint
            : "PLAN (dashed) is frozen at EXECUTE; REAL (solid) is when each step went out and was done. Tap a bar for its step.";

        public string Alert => null;

        public void Build(AvFlow pageFlow, AvTicker pageTicker, int pageIndex)
        {
            flow = pageFlow;
            ticker = pageTicker;
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
                ids.Add("plan.sub." + SubLabels[i].ToLowerInvariant(), subTabs[i]);
            }

            subPages = flow.Add(new WmcSubPages(flow, ticker, pageIndex, SubLabels.Length));
            options.Build(subPages.Flow(SubStances), ticker, pageIndex);
            BuildSortie(subPages.Flow(SubSortie), ticker, pageIndex);
            // Last, so its list draws over the page.
            popup = new AvPopup(flow.Content, flow.Width);
            ShowSub(SubStances);
        }

        /// <summary>A sub-page by its number (automation, the tabs).</summary>
        public void ShowSub(int k)
        {
            if (subPages == null || k < 0 || k >= SubLabels.Length) return;
            if (k != sub && last != null && k == SubStances) options.Shown(last);
            sub = k;
            subPages.Show(k);
            for (int i = 0; i < subTabs.Length; i++) subTabs[i].Latched = i == k;
            AvPopup.CloseAny();
            sortieKey = long.MinValue;
            if (last != null) Refresh(last);
        }

        /// <summary>A control of a sub-page shows that sub-page first (automation).</summary>
        public void ShowSubFor(string id)
        {
            if (id == null) return;
            if (id.StartsWith("opt.", System.StringComparison.Ordinal)) ShowSub(SubStances);
            else if (id.StartsWith("plan.", System.StringComparison.Ordinal) && !id.StartsWith("plan.sub.", System.StringComparison.Ordinal)) ShowSub(SubSortie);
        }

        /// <summary>A sub-page by its label (automation). Scenarios written for TUNING / OPTIONS open STANCES; PLAN, ELEMENTS, RECORD,
        /// TIMELINE and LOG open SORTIE.</summary>
        public bool ShowSubNamed(string name)
        {
            if (string.Equals(name, "TUNING", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "OPTIONS", System.StringComparison.OrdinalIgnoreCase)) name = "STANCES";
            if (string.Equals(name, "PLAN", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "ELEMENTS", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "RECORD", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "TIMELINE", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "LOG", System.StringComparison.OrdinalIgnoreCase)) name = "SORTIE";
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
            if (sub == SubStances) options.Shown(c);
            sortieKey = long.MinValue;
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            if (sub == SubStances) options.Refresh(c);
            else if (sub == SubSortie) RefreshSortie(c);
            if (relayout)
            {
                relayout = false;
                flow.RequestRelayout();
            }
        }

        private static void Enable(AvControl b, bool on)
        {
            if (b.Interactable != on) b.Interactable = on;
        }

        /// <summary>"m:ss" (minutes unbounded); a dash when it is not a time.</summary>
        private static string Clock(float seconds) =>
            float.IsNaN(seconds) || float.IsInfinity(seconds) ? WmcText.Unknown : AvNum.Clock(Mathf.Max(0f, seconds));
    }
}
