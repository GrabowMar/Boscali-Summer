using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The nation layer as the STR desk sees it: set by the panel each tick (resolved late, the Nation module installs after Command) and read by
    /// the parts below, which hide themselves while <see cref="View"/> is null.
    /// </summary>
    internal static class StrNation
    {
        internal static INationView Service;
        internal static NationView View;
        internal static bool Active => View != null;

        internal static readonly StrLineRow[] Lines = new StrLineRow[StrNationView.MaxLines];
        internal static int LineCount;

        /// <summary>Refills the pooled line rows; the panel calls it once before each bind.</summary>
        internal static void Fill() => LineCount = View == null ? 0 : StrNationView.Lines(View, Lines);

        /// <summary>Sends a request; the answer is a sent verb or the host's own refusal words.</summary>
        internal static StrAct Ask(NationVerb verb, int a = 0, int b = 0)
        {
            if (Service == null) return new StrAct(StrWhy.NoBoard, "NATION OFF");
            NationOutcome o = Service.Request(verb, a, b);
            return o == NationOutcome.Ok ? new StrAct(StrWhy.None, "ORDER SENT", true) : new StrAct(StrWhy.Gone, NationWords.Outcome(o));
        }

        internal static string Money(float v) => ((int)Math.Round(v)).ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>The words of the host's last answer while they are fresh ("" none); the panel keys its rebind on this too.</summary>
        internal static string ReplyWords()
        {
            INationView s = Service;
            if (s == null || View == null || s.LastOutcome == NationOutcome.Ok || Time.unscaledTime - s.LastOutcomeAt >= 4f) return "";
            return NationWords.Outcome(s.LastOutcome);
        }
    }

    /// <summary>The one-line answer of the host to the last nation request, for four seconds.</summary>
    internal sealed class StrNationReply : StrLaidPart
    {
        private readonly TMP_Text text;

        internal StrNationReply(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Nation reply", controls, hub)
        {
            text = Text(Rect, "Text", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            string w = StrNation.ReplyWords();
            SetShown(w.Length > 0);
            AvText.Set(text, w);
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            if (place) AvLay.Place(text, 8f, 0f, width - 16f, 18f);
            return 20f;
        }

        public override void Restyle() => text.color = AvInk.State(AvState.Caution);
    }

    /// <summary>One production line: the stock row's name, plants on it, progress to the next unit, stock against target, time, and AUTO / PINNED.</summary>
    internal sealed class StrLineRowPart : StrLaidPart
    {
        private const float Pad = 8f, ButtonW = 96f, ButtonH = 26f, StockW = 64f, PlantsW = 40f, TimeW = 76f, H = 60f;

        private readonly int index;
        private readonly AvFrame frame;
        private readonly TMP_Text icon, name, stock, plants, time;
        private readonly AvStepProgress bar;
        private readonly StrButton button;
        private StrLineRow row;

        internal StrLineRowPart(RectTransform parent, StrControls controls, IStrHub hub, int ordinal) : base(parent, "Line " + (ordinal + 1), controls, hub)
        {
            index = ordinal;
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            icon = AvIcons.Make(Rect, AvIcon.Plane, 18f, Color.white);
            name = Text(Rect, "Name", AvTextRole.Head, TextAlignmentOptions.MidlineLeft);
            stock = Text(Rect, "val.stock", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
            plants = Text(Rect, "Plants", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            time = Text(Rect, "Time", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            bar = new AvStepProgress(Rect, "Progress");
            button = new StrButton(Rect, "AUTO", AvButtonStyle.Default, Press, AvIcon.Settings);
            Restyle();
        }

        // FAVOUR sets the PRIORITY policy to this arm; pressing the favoured arm goes back to BALANCED. The host judges the policy lock.
        private StrAct Press()
        {
            StrLineRow r = row;
            if (StrNation.View == null || r == null) return new StrAct(StrWhy.NoBoard, "NATION OFF");
            return StrNation.Ask(NationVerb.Priority, r.Pinned ? (int)PriorityPolicy.Balanced : r.Row);
        }

        protected override void Apply(StrViewModel vm)
        {
            row = StrNation.Active && index < StrNation.LineCount ? StrNation.Lines[index] : null;
            SetShown(row != null);
            if (row == null) return;
            AvIcons.Set(icon, row.Aircraft ? AvIcon.Plane : AvIcon.Truck, 18f);
            AvText.Set(name, row.Name);
            AvText.Set(stock, row.Count.ToString(CultureInfo.InvariantCulture) + "/" + row.Target.ToString(CultureInfo.InvariantCulture));
            AvText.Set(plants, row.Plants.ToString(CultureInfo.InvariantCulture) + " PL");
            AvText.Set(time, row.Time);
            bar.SetProgress((int)(row.Progress * 1000f), 1000);
            button.Control.SetIcon(row.Pinned ? AvIcon.Lock : AvIcon.Settings);
            button.Bind(Controls, "nation.line." + (index + 1).ToString(CultureInfo.InvariantCulture), row.Pinned ? "FAVOURED" : "FAVOUR",
                () => new StrAct(StrWhy.None, ""),
                row.Pinned ? "The staff gives this arm two plants in three. Press to go back to an even split." : "Press to favour this arm: the staff gives it two plants in three.",
                row.Pinned);
            Paint();
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            if (!place) return H;
            float right = width - Pad - ButtonW, textW = Mathf.Max(60f, right - 8f - Pad);
            AvLay.Place(icon, Pad, Pad + 1f, 22f, 22f);
            AvLay.Place(name, Pad + 26f, Pad, Mathf.Max(30f, textW - 26f - StockW), 22f);
            AvLay.Place(stock, Pad + textW - StockW, Pad, StockW, 22f);
            AvLay.Place(plants, Pad, Pad + 28f, PlantsW, 16f);
            AvLay.Place(bar.Rect, Pad + PlantsW + 2f, Pad + 33f, Mathf.Max(20f, textW - PlantsW - TimeW - 8f), 6f);
            AvLay.Place(time, Pad + textW - TimeW, Pad + 28f, TimeW, 16f);
            button.Place(right, (H - ButtonH) * 0.5f, ButtonW, ButtonH);
            return H;
        }

        private void Paint()
        {
            frame.Paint(AvInk.Inert, row != null && row.Pinned ? AvInk.Effort.WithAlpha(0.45f) : AvInk.Hairline);
            name.color = AvInk.Ink;
            icon.color = AvInk.Dim;
            plants.color = AvInk.Dim;
            time.color = row != null && row.Low ? AvInk.State(AvState.Danger) : AvInk.Dim;
            stock.color = row == null ? AvInk.Ink : row.Count >= row.Target ? AvInk.State(AvState.Ready) : row.Low ? AvInk.State(AvState.Danger) : AvInk.Ink;
            bar.Paint(row != null && row.Low ? AvInk.State(AvState.Danger) : AvInk.Key, AvInk.Hairline);
        }

        public override void Restyle()
        {
            Paint();
            button.Restyle();
        }
    }

    /// <summary>The plant being built: name, progress, time left.</summary>
    internal sealed class StrConstructionRow : StrLaidPart
    {
        private const float Pad = 8f, TimeW = 60f, H = 44f;
        private readonly AvFrame frame;
        private readonly TMP_Text icon, name, time;
        private readonly AvStepProgress bar;

        internal StrConstructionRow(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Construction", controls, hub)
        {
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            icon = AvIcons.Make(Rect, AvIcon.BuildingFactory, 18f, Color.white);
            name = Text(Rect, "Name", AvTextRole.Head, TextAlignmentOptions.MidlineLeft);
            time = Text(Rect, "Time", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
            bar = new AvStepProgress(Rect, "Progress");
            name.text = "NEW FACTORY";
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            NationView v = StrNation.View;
            SetShown(v != null && v.Building);
            if (!Shown) return;
            AvText.Set(time, StrNationView.Clock(v.BuildLeft));
            bar.SetProgress(v.BuildTotal > 0f ? (int)(Mathf.Clamp01(1f - v.BuildLeft / v.BuildTotal) * 1000f) : 0, 1000);
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            if (!place) return H;
            AvLay.Place(icon, Pad, Pad - 2f, 22f, 22f);
            AvLay.Place(name, Pad + 26f, Pad - 2f, width - 2f * Pad - 26f - TimeW, 22f);
            AvLay.Place(time, width - Pad - TimeW, Pad - 2f, TimeW, 22f);
            AvLay.Place(bar.Rect, Pad, H - Pad - 4f, width - 2f * Pad, 6f);
            return H;
        }

        public override void Restyle()
        {
            frame.Paint(AvInk.Inert, AvInk.Hairline);
            icon.color = AvInk.Dim;
            name.color = AvInk.Ink;
            time.color = AvInk.Ink;
            bar.Paint(AvInk.Key, AvInk.Hairline);
        }
    }

    /// <summary>BUILD FACTORY with its price, and how many plants of the slots stand.</summary>
    internal sealed class StrBuildRow : StrLaidPart
    {
        private const float Pad = 2f, ButtonH = 28f, ButtonW = 200f;
        private readonly TMP_Text label, count;
        private readonly StrButton build;

        internal StrBuildRow(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Build", controls, hub)
        {
            label = Text(Rect, "Label", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            label.text = "FACTORIES";
            count = Text(Rect, "val.count", AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft);
            build = new StrButton(Rect, "BUILD FACTORY", AvButtonStyle.Primary, () => StrNation.Ask(NationVerb.Build), AvIcon.BuildingFactory);
            Restyle();
        }

        private static StrAct Gate()
        {
            NationView v = StrNation.View;
            if (v == null) return new StrAct(StrWhy.NoBoard, "NATION OFF");
            if (v.Building) return new StrAct(StrWhy.Pending, "A PLANT IS ALREADY BEING BUILT");
            if (StrNationView.PlantCount(v) >= v.Slots) return new StrAct(StrWhy.Gone, NationWords.Outcome(NationOutcome.NoSlot));
            if (v.Funds < v.FactoryCost) return new StrAct(StrWhy.NoFunds, NationWords.Outcome(NationOutcome.NoFunds));
            return new StrAct(StrWhy.None, "");
        }

        protected override void Apply(StrViewModel vm)
        {
            NationView v = StrNation.View;
            SetShown(v != null);
            if (v == null) return;
            AvText.Set(count, StrNationView.PlantCount(v).ToString(CultureInfo.InvariantCulture) + "/" + v.Slots.ToString(CultureInfo.InvariantCulture));
            build.Bind(Controls, "nation.build", "BUILD FACTORY " + StrNation.Money(v.FactoryCost), Gate, "Build a plant with the nation's funds. It takes a while and needs a free slot.");
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            if (!place) return ButtonH + 4f;
            AvLay.Place(label, Pad, 0f, 70f, ButtonH);
            AvLay.Place(count, Pad + 72f, 0f, 60f, ButtonH);
            build.Place(width - Pad - ButtonW, 0f, ButtonW, ButtonH);
            return ButtonH + 4f;
        }

        public override void Restyle()
        {
            label.color = AvInk.Dim;
            count.color = AvInk.Ink;
            build.Restyle();
        }
    }

    /// <summary>A three-way policy row: ECONOMY and PRIORITY go to the nation, WAR PLAN is the staff posture through the desk's own verb.</summary>
    internal sealed class StrPolicyRow : StrLaidPart
    {
        internal enum Kind : byte { Economy, Priority, Plan }

        private static readonly string[][] Words =
        {
            new[] { "CIVILIAN", "BALANCED", "WAR ECONOMY" },
            new[] { "AIR", "BALANCED", "GROUND" },
            new[] { "CAUTIOUS", "STEADY", "BOLD" },
        };
        private static readonly string[] Titles = { "ECONOMY", "PRIORITY", "WAR PLAN" };
        private static readonly string[] Helps =
        {
            "How hard the nation works its plants. War economy turns out more and strains stability.",
            "Which stock the plants favour when the staff picks a line.",
            "How boldly the staff presses the front.",
        };
        private static readonly TheaterWarPosture[] Postures = { TheaterWarPosture.Cautious, TheaterWarPosture.Steady, TheaterWarPosture.Bold };
        private const float ButtonH = 26f, HeadH = 16f;

        private readonly Kind kind;
        private readonly TMP_Text label, caption;
        private readonly StrButton[] buttons = new StrButton[3];

        internal StrPolicyRow(RectTransform parent, StrControls controls, IStrHub hub, Kind which) : base(parent, "Policy " + Titles[(int)which], controls, hub)
        {
            kind = which;
            label = Text(Rect, "Label", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            label.text = Titles[(int)which];
            caption = Text(Rect, "Caption", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < 3; i++)
            {
                int option = i;
                buttons[i] = new StrButton(Rect, Words[(int)which][i], AvButtonStyle.Default, () => Press(option));
            }
            Restyle();
        }

        private StrAct Press(int option)
        {
            switch (kind)
            {
                case Kind.Economy: return StrNation.Ask(NationVerb.Economy, option);
                case Kind.Priority: return StrNation.Ask(NationVerb.Priority, option);
                default: return Hub.Run(StrActionKind.Posture, StrItemId.None, (int)Postures[option]);
            }
        }

        private StrAct Gate(int option)
        {
            if (kind == Kind.Plan) return StrGate.Check(Vm, StrActionKind.Posture, StrItemId.None, (int)Postures[option]);
            NationView v = StrNation.View;
            if (v == null) return new StrAct(StrWhy.NoBoard, "NATION OFF");
            bool current = kind == Kind.Economy ? (int)v.Economy == option : (int)v.Priority == option;
            if (current) return new StrAct(StrWhy.SamePosture, "ALREADY SET");
            float locked = kind == Kind.Economy ? v.EconomyLock : v.PriorityLock;
            return locked > 0f ? new StrAct(StrWhy.Locked, "LOCKED " + StrNationView.Clock(locked)) : new StrAct(StrWhy.None, "");
        }

        protected override void Apply(StrViewModel vm)
        {
            NationView v = StrNation.View;
            SetShown(v != null);
            if (v == null) return;
            int selected = kind == Kind.Economy ? (int)v.Economy : kind == Kind.Priority ? (int)v.Priority : Array.IndexOf(Postures, vm.Posture);
            float locked = kind == Kind.Economy ? v.EconomyLock : kind == Kind.Priority ? v.PriorityLock : 0f;
            for (int i = 0; i < 3; i++)
            {
                int option = i;
                string id = kind == Kind.Plan ? StrControlIds.Posture(Postures[i]) : "nation." + Titles[(int)kind].ToLowerInvariant() + "." + i.ToString(CultureInfo.InvariantCulture);
                buttons[i].Bind(Controls, id, Words[(int)kind][i], () => Gate(option), kind == Kind.Plan ? TheaterWords.Posture(Postures[i]) : Helps[(int)kind], selected == i);
            }
            AvText.Set(caption, locked > 0f ? "LOCKED " + StrNationView.Clock(locked) : "");
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            if (!place) return HeadH + ButtonH + 6f;
            AvLay.Place(label, 2f, 0f, width * 0.5f, HeadH);
            AvLay.Place(caption, width * 0.5f, 0f, width * 0.5f - 2f, HeadH);
            float w = AvFlowMath.ColumnWidth(width, 3, 2f);
            for (int i = 0; i < 3; i++) buttons[i].Place(i * (w + 2f), HeadH + 2f, w, ButtonH);
            return HeadH + ButtonH + 6f;
        }

        public override void Restyle()
        {
            label.color = AvInk.Dim;
            caption.color = AvInk.State(AvState.Caution);
            foreach (StrButton b in buttons) b.Restyle();
        }
    }

    /// <summary>BOARD: the two-line ticker under the tabs, next off the line and the stock warning.</summary>
    internal sealed class StrNationTicker : StrLaidPart
    {
        private readonly TMP_Text next, low;

        internal StrNationTicker(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Nation ticker", controls, hub)
        {
            next = Text(Rect, "Next", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            low = Text(Rect, "Low", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            NationView v = StrNation.View;
            SetShown(v != null);
            if (v == null) return;
            AvText.Set(next, StrNationView.NextLine(v));
            AvText.Set(low, StrNationView.LowLine(v));
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            bool second = low.text.Length > 0;
            if (place)
            {
                AvLay.Place(next, 4f, 0f, width - 8f, 16f);
                low.gameObject.SetActive(second);
                AvLay.Place(low, 4f, 16f, width - 8f, 16f);
            }
            return second ? 36f : 20f;
        }

        public override void Restyle()
        {
            next.color = AvInk.Dim;
            low.color = AvInk.State(AvState.Danger);
        }
    }

    /// <summary>BOARD: one WAR PLAN chip in place of the posture row; pressing it opens the PRODUCTION tab where the plan is set.</summary>
    internal sealed class StrWarPlanChip : StrPart
    {
        private const float ButtonH = 26f;
        private readonly StrButton chip;

        internal StrWarPlanChip(RectTransform parent, StrControls controls, IStrHub hub, Func<StrAct> open) : base(parent, "War plan chip", controls, hub)
        {
            chip = new StrButton(Rect, "WAR PLAN", AvButtonStyle.Default, open, AvIcon.Flag);
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            SetShown(StrNation.Active);
            if (!Shown) return;
            chip.Bind(Controls, "nation.warplan", "WAR PLAN" + StrWords.Dot + vm.Posture.ToString().ToUpperInvariant(), () => new StrAct(StrWhy.None, ""),
                "The staff's war plan. Press to open PRODUCTION, where it is set.");
            Changed();
        }

        public override float Measure(float width) => Shown ? ButtonH + 4f : 0f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Shown) chip.Place(2f, 0f, s.W - 4f, ButtonH);
        }

        public override void Restyle() => chip.Restyle();
    }
}
