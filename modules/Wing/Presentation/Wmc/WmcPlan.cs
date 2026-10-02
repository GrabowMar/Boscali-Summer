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
    /// <summary>BEHAVIOUR (spec FUI §tabs) as a kit v2 flow page: TUNING (every standing setting, granular per element and aircraft),
    /// PLAN (every element's steps on the game's map, with EXECUTE, SKIP, FORM UP and FIT), and RECORD (the plan against what
    /// happened, then the wing's events and radio lines; a line opens it on INSPECT and centres the map on it). FORM and ROUTE·AP moved
    /// to TACTICAL. The sub-pages share one flow line (<see cref="WmcSubPages"/>); the console body scrolls, so nothing pages or clips.
    /// The plan bar's and the profile picker's lists open the toolkit popup (<see cref="AvPopup"/>).</summary>
    internal sealed partial class WmcPlan : IWmcPage
    {
        public const int SubOptions = 0, SubPlan = 1, SubRecord = 2;
        private static readonly string[] SubLabels = { "TUNING", "PLAN", "RECORD" };
        private static readonly AvIcon[] SubIcons = { AvIcon.AdjustmentsHorizontal, AvIcon.ListDetails, AvIcon.Activity };
        private static readonly string[] SubHelp =
        {
            "How the wing fights and flies, per element and per aircraft: targets, weapons, radar, falling back, bingo and winchester, the radio.",
            "Every element's steps on the map, with EXECUTE, SKIP, FORM UP and FIT.",
            "The plan against what happened, lane by lane; the wing's events and radio lines, and the sortie's DEBRIEF.",
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

        public string Hint => sub == SubOptions ? options.Hint
            : sub == SubRecord ? "PLAN is frozen at EXECUTE; REAL is when each step went out and was done. A line with an aircraft opens it on INSPECT; DEBRIEF sums up the sortie."
            : "Pick a tool and right-click the map to add steps to the selected lane; EXECUTE runs the plan.";

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
            options.Build(subPages.Flow(SubOptions), ticker, pageIndex);
            BuildElements(subPages.Flow(SubPlan), ticker, pageIndex);
            BuildRecord(subPages.Flow(SubRecord), ticker, pageIndex);
            // Last, so its list draws over the page (TUNING's profile picker has its own, inside its sub-page).
            popup = new AvPopup(flow.Content, flow.Width);
            ShowSub(SubOptions);
        }

        /// <summary>A sub-page by its number (automation, the tabs).</summary>
        public void ShowSub(int k)
        {
            if (subPages == null || k < 0 || k >= SubLabels.Length) return;
            if (k != sub && last != null && k == SubOptions) options.Shown(last);
            sub = k;
            subPages.Show(k);
            for (int i = 0; i < subTabs.Length; i++) subTabs[i].Latched = i == k;
            AvPopup.CloseAny();
            if (last != null) Refresh(last);
        }

        /// <summary>A control of a sub-page shows that sub-page first (automation).</summary>
        public void ShowSubFor(string id)
        {
            if (id == null) return;
            if (id.StartsWith("opt.", System.StringComparison.Ordinal)) ShowSub(SubOptions);
            else if (id.StartsWith("plan.el", System.StringComparison.Ordinal) || id.StartsWith("plan.step", System.StringComparison.Ordinal)
                     || id.StartsWith("plan.edit.", System.StringComparison.Ordinal) || id.StartsWith("plan.tool.", System.StringComparison.Ordinal)
                     || id.StartsWith("plan.bar.", System.StringComparison.Ordinal) || id.StartsWith("plan.cue.", System.StringComparison.Ordinal)
                     || id.StartsWith("plan.add.", System.StringComparison.Ordinal)) ShowSub(SubPlan);
            else if (id.StartsWith("plan.log.", System.StringComparison.Ordinal)) ShowSub(SubRecord);
        }

        /// <summary>A sub-page by its label (automation).</summary>
        public bool ShowSubNamed(string name)
        {
            // Scenarios written before BEHAVIOUR call PLAN's first sub-page ELEMENTS; OPTIONS is TUNING now, and TIMELINE/LOG
            // merged into RECORD.
            if (string.Equals(name, "ELEMENTS", System.StringComparison.OrdinalIgnoreCase)) name = "PLAN";
            if (string.Equals(name, "OPTIONS", System.StringComparison.OrdinalIgnoreCase)) name = "TUNING";
            if (string.Equals(name, "TIMELINE", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "LOG", System.StringComparison.OrdinalIgnoreCase)) name = "RECORD";
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
            if (sub == SubOptions) options.Shown(c);
            elementsKey = long.MinValue;
            timelineNext = 0f;
            logFilled = false;
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            if (sub == SubOptions) options.Refresh(c);
            else if (sub == SubPlan) RefreshElements(c);
            else if (sub == SubRecord)
            {
                RefreshTimeline(c);
                RefreshLog(c);
            }
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
