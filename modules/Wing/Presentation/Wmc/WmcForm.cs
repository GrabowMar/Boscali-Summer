using NOAvionics;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    /// <summary>TACTICAL › FORMATION (spec bezel v2 §5 FORM; the 0.9 deck modernized) on kit v2: the plan view of the scope's shape with
    /// the members' live positions and the shape's numbers beside it, the family and shapes for the scope's element, then what is
    /// wing-wide — spacing and stack — under its own head so scope is never ambiguous (POWER is on POSTURE). The maneuvers are
    /// ORDERS' REACT row. The scope is TACTICAL's scope bar. Control ids are unchanged.</summary>
    internal sealed class WmcForm : IWmcPage
    {
        private const int MaxShapes = 12, MaxFamilies = 4, MaxDots = WcSnapshot.MaxMembers;
        private const float PlanSize = 140f;

        private readonly WmcControls ids;
        private AvFlow flow;
        private WmcContext last;
        private FormView view;
        private AvSection familySection, shapeSection;
        private WmcButtonGrid familyGrid, shapeGrid;
        private AvSegmented spacingRow, stackRow;
        private int spacingValue = -1, stackValue = -1;
        private readonly string[] shapeIds = new string[MaxShapes];
        private readonly List<FormationDefinition> shapes = new List<FormationDefinition>();
        private readonly List<string> families = new List<string>();
        private int formKey = int.MinValue, shapesKey = int.MinValue;

        public WmcForm(WmcControls controls)
        {
            ids = controls;
        }

        public string Hint => "Shapes are the scope element's; spacing and stack are the whole wing's.";

        public string Alert => null;

        public void Build(AvFlow pageFlow, AvTicker ticker, int pageIndex)
        {
            flow = pageFlow;
            flow.Section(AvIcon.LayersSubtract, "FORMATION");
            view = flow.Add(new FormView(flow.Content));

            familySection = flow.Section(AvIcon.LayersSubtract, "FAMILY");
            familyGrid = flow.Add(new WmcButtonGrid(flow.Content, MaxFamilies, MaxFamilies, i => new AvControl.Spec("", () => PickFamily(i))));
            for (int i = 0; i < MaxFamilies; i++) ids.Add("form.family" + i, familyGrid[i]);

            shapeSection = flow.Section(AvIcon.LayersSubtract, "SHAPE");
            shapeGrid = flow.Add(new WmcButtonGrid(flow.Content, 4, MaxShapes, i => new AvControl.Spec("", () => PickShape(i))));
            for (int i = 0; i < MaxShapes; i++) ids.Add("form.shape" + i, shapeGrid[i]);

            flow.Section(AvIcon.Plane, "WHOLE WING", "ALL ELEMENTS");
            spacingRow = flow.Add(new AvSegmented(flow.Content, "SPACING", new[] { "40 M", "80 M", "160 M", "350 M" },
                () => spacingValue, i => WmcPostureActions.Spacing(last, i)));
            string[] spacingTips = { "CLOSE: 40 m between slots.", "STANDARD: 80 m between slots.", "OPEN: 160 m between slots.", "SPREAD: 350 m between slots." };
            for (int i = 0; i < spacingTips.Length; i++)
            {
                spacingRow.Options[i].Help = spacingTips[i];
                ids.Add("form.spacing" + i, spacingRow.Options[i]);
            }
            stackRow = flow.Add(new AvSegmented(flow.Content, "STACK", new[] { "HIGH", "LEVEL", "LOW" }, () => stackValue, PickStack));
            string[] stackTips = { "The wing flies above you.", "Level with you.", "The wing flies below you." };
            string[] stackKeys = { "high", "level", "low" };
            for (int i = 0; i < stackTips.Length; i++)
            {
                stackRow.Options[i].Help = stackTips[i];
                ids.Add("form." + stackKeys[i], stackRow.Options[i]);
            }
        }

        public void Shown(WmcContext c)
        {
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            RefreshFormation(c);
        }

        private void PickFamily(int i)
        {
            if (i >= families.Count) return;
            WmcUi.Order(last, () =>
            {
                FormationDefinition first = shapes.Find(d => d.Family == families[i]);
                if (first != null) WingOrders.Run(new WingOrder { Kind = OrderKind.SetShape, Text = first.Id, Scope = last.Scope });
            });
        }

        private void PickShape(int i)
        {
            WmcPostureActions.Shape(last, shapeIds[i]);
        }

        private void PickStack(int i) => WmcPostureActions.Stack(last, i);

        private void RefreshFormation(WmcContext c)
        {
            WingService w = c.Wing;
            FormationSelection sel = w?.Selection;
            bool host = w != null && !c.Client && sel != null;
            for (int i = 0; i < MaxFamilies; i++) familyGrid[i].Interactable = host && c.CanOrder;
            if (!host)
            {
                if (formKey != -2)
                {
                    formKey = -2;
                    view.SetValues("Formation is the host's", "", "", "", "");
                    familySection.SetCaption("");
                    shapeSection.SetCaption("");
                    flow.RequestRelayout();
                }
                return;
            }
            int e = c.ScopeElement;
            FormationDefinition current = w.ShapeOf(e) ?? sel.Current;
            string elementName = w.Roster.Name(e);
            int members = 0;
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element == e) members++;
            float stack = w.Stack;
            int stackWord = WmcPostureActions.StackIndex(stack);
            int key = (current.Id?.GetHashCode() ?? 0) * 31 + (int)sel.Spacing * 7 + stackWord * 3 + (w.AfterburnerAllowed ? 1 : 0)
                + e * 1009 + members * 101 + (elementName?.GetHashCode() ?? 0);
            if (key != formKey)
            {
                formKey = key;
                string letter = ElementRoster.Letter(e);
                // The leader's own position counts as a slot (spec: "SLOTS n/m IN SLOT" from data RefreshPlanView already has).
                int totalSlots = current.Slots.Length + 1;
                view.SetValues(
                    current.Name.ToUpperInvariant() + " · " + current.Family.ToUpperInvariant(),
                    sel.Spacing.ToString().ToUpperInvariant() + " · " + AvNum.Fixed(sel.SpacingMetres, 0) + " m",
                    (stackWord == 0 ? "HIGH" : stackWord == 2 ? "LOW" : "LEVEL") + " · " + (w.AfterburnerAllowed ? "GATE" : "BUSTER"),
                    letter + (elementName != letter ? " " + elementName : "") + " · " + AvNum.Fixed(members, 0) + " AC",
                    AvNum.Fixed(Mathf.Min(members, totalSlots), 0) + "/" + AvNum.Fixed(totalSlots, 0) + " IN SLOT");
                string elementNote = "ELEMENT " + letter;
                familySection.SetCaption(elementNote);
                shapeSection.SetCaption(elementNote);
            }
            RefreshShapes(sel, current);
            SetRow(spacingRow, ref spacingValue, (int)sel.Spacing, c.CanOrder);
            SetRow(stackRow, ref stackValue, stackWord, c.CanOrder);
            view.RefreshPlanView(c, current, sel.SpacingMetres, e, members);
        }

        private static void SetRow(AvSegmented row, ref int shown, int value, bool canOrder)
        {
            if (shown != value)
            {
                shown = value;
                row.Refresh();
            }
            foreach (AvControl o in row.Options)
                if (o.Interactable != canOrder) o.Interactable = canOrder;
        }

        private void RefreshShapes(FormationSelection sel, FormationDefinition current)
        {
            // The suitable list follows the wing's shape use (jet, rotary, escort): the wing's own shape names it.
            int key = (current.Id?.GetHashCode() ?? 0) * 31 + (sel.Current?.Id?.GetHashCode() ?? 0);
            if (key == shapesKey) return;
            shapesKey = key;
            sel.Suitable(shapes);
            FormationSelection.Families(shapes, families);
            int fam = Mathf.Min(families.Count, MaxFamilies);
            familyGrid.SetShownCount(fam);
            for (int i = 0; i < fam; i++)
            {
                familyGrid[i].Label = families[i].ToUpperInvariant();
                familyGrid[i].Latched = families[i] == current.Family;
            }
            int n = 0;
            foreach (FormationDefinition d in shapes)
            {
                if (d.Family != current.Family || n >= MaxShapes) continue;
                shapeIds[n] = d.Id;
                shapeGrid[n].Label = WmcWords.Shape(d.Id, d.Name);
                shapeGrid[n].Latched = d.Id == current.Id;
                shapeGrid[n].Help = d.Name + ": this element's shape.";
                n++;
            }
            for (int i = n; i < MaxShapes; i++) shapeIds[i] = null;
            shapeGrid.SetShownCount(n);
            flow.RequestRelayout();
        }

        /// <summary>The plan view (a bracketed card, a faint grid, the shape's slots and the element's live positions) with the shape's
        /// numbers beside it. The drawing is domain data, so it stays a local part of kit primitives.</summary>
        private sealed class FormView : AvPart
        {
            private const float CardW = PlanSize + 12f, PreviewH = PlanSize + 10f, KvRow = 18f;
            private static readonly string[] Keys = { "SHAPE", "SPACING", "STACK", "ELEMENT", "SLOTS" };
            private readonly AvFrame card;
            private readonly RectTransform plan;
            private readonly Image planBack, gridV, gridA, gridB, leader, legendSlot, legendLive;
            private readonly Image[] slotDots = new Image[MaxDots], liveDots = new Image[MaxDots];
            private readonly TMP_Text[] keyText = new TMP_Text[5], valueText = new TMP_Text[5];
            private readonly TMP_Text leaderText, slotWord, liveWord;

            public FormView(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "FormView");
                card = AvFrame.Add(Rect, "Card", AvChamfer.Diagonal(8f));
                card.Bracket = 8f;
                plan = AvLay.Child(Rect, "PlanView");
                planBack = Solid(plan, "Back", 0f, 0f, PlanSize, PlanSize);
                gridV = Solid(plan, "GridV", PlanSize * 0.5f, 0f, 1f, PlanSize);
                gridA = Solid(plan, "GridA", 0f, PlanSize / 3f, PlanSize, 1f);
                gridB = Solid(plan, "GridB", 0f, 2f * PlanSize / 3f, PlanSize, 1f);
                // The leader's mark and label are drawn (the MFD font has no ▲ ○ ●: they showed as boxes at stop 1).
                leader = Solid(plan, "Leader", PlanSize * 0.5f - 3f, PlanSize / 3f - 3f, 6f, 6f);
                leaderText = AvText.Make(plan, "LeaderText", AvTextRole.Micro, "LDR");
                AvLay.Place(leaderText.rectTransform, PlanSize * 0.5f + 5f, PlanSize / 3f - 14f, 30f, 12f);
                for (int i = 0; i < MaxDots; i++)
                {
                    slotDots[i] = Solid(plan, "Slot" + i, 0f, 0f, 8f, 8f);
                    liveDots[i] = Solid(plan, "Live" + i, 0f, 0f, 5f, 5f);
                    slotDots[i].gameObject.SetActive(false);
                    liveDots[i].gameObject.SetActive(false);
                }
                for (int i = 0; i < 5; i++)
                {
                    keyText[i] = AvText.Make(Rect, "Key" + i, AvTextRole.Label, Keys[i]);
                    AvText.Fit(keyText[i], false);
                    valueText[i] = AvText.Make(Rect, "Value" + i, AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                    AvText.Fit(valueText[i], false);
                }
                legendSlot = Solid(Rect, "LegendSlot", 0f, 0f, 8f, 8f);
                slotWord = AvText.Make(Rect, "SlotWord", AvTextRole.Micro, "SLOT");
                legendLive = Solid(Rect, "LegendLive", 0f, 0f, 5f, 5f);
                liveWord = AvText.Make(Rect, "LiveWord", AvTextRole.Micro, "LIVE");
                Restyle();
            }

            private static Image Solid(RectTransform parent, string name, float x, float y, float w, float h)
            {
                Image img = AvLay.Solid(parent, name, Color.clear);
                AvLay.Place(img.rectTransform, x, y, w, h);
                return img;
            }

            public void SetValues(string shape, string spacing, string stack, string element, string slots)
            {
                Set(0, shape);
                Set(1, spacing);
                Set(2, stack);
                Set(3, element);
                Set(4, slots);
            }

            private void Set(int i, string text) => WmcKit.Set(valueText[i], text);

            public override float Measure(float width) => PreviewH;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(card.rectTransform, 0f, 0f, CardW, PreviewH);
                AvLay.Place(plan, 6f, (PreviewH - PlanSize) * 0.5f, PlanSize, PlanSize);
                float tx = CardW + 14f, tw = s.W - tx - 4f, ky = 0f;
                for (int i = 0; i < 5; i++)
                {
                    AvLay.Place(keyText[i].rectTransform, tx, ky, tw * 0.34f, KvRow);
                    AvLay.Place(valueText[i].rectTransform, tx + tw * 0.34f, ky, tw * 0.66f, KvRow);
                    ky += KvRow;
                }
                ky += 8f;
                AvLay.Place(legendSlot.rectTransform, tx, ky + 5f, 8f, 8f);
                AvLay.Place(slotWord.rectTransform, tx + 12f, ky, 40f, 18f);
                AvLay.Place(legendLive.rectTransform, tx + 58f, ky + 6f, 5f, 5f);
                AvLay.Place(liveWord.rectTransform, tx + 68f, ky, 40f, 18f);
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card");
                card.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                card.BracketColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
                card.SetVerticesDirty();
                Color hairline = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
                planBack.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
                gridV.color = gridA.color = hairline.WithAlpha(0.4f);
                gridB.color = hairline.WithAlpha(0.25f);
                leader.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                Color dim = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                leaderText.color = slotWord.color = liveWord.color = dim;
                foreach (Image d in slotDots) d.color = hairline;
                legendSlot.color = hairline;
                legendLive.color = AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
                foreach (TMP_Text k in keyText) k.color = dim;
                foreach (TMP_Text v in valueText) v.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            }

            /// <summary>Slots from the shape at the wing's spacing; live dots are each member's offset from its element's leader —
            /// the task lead while the element has a task, else A's anchor or you — in the leader's heading frame.</summary>
            public void RefreshPlanView(WmcContext c, FormationDefinition shape, float spacing, int e, int members)
            {
                PlanView.Fit(shape.Slots, spacing, PlanSize, out float mpp);
                int slots = Mathf.Min(shape.Slots.Length, Mathf.Min(members, MaxDots));
                for (int i = 0; i < MaxDots; i++)
                {
                    bool on = i < slots;
                    if (slotDots[i].gameObject.activeSelf != on) slotDots[i].gameObject.SetActive(on);
                    if (!on) continue;
                    var (px, py) = PlanView.Point(shape.Slots[i].Right * spacing, shape.Slots[i].Aft * spacing, mpp, PlanSize);
                    slotDots[i].rectTransform.anchoredPosition = new Vector2(px - 4f, -py + 4f);
                }

                bool haveLeader = LeaderFrame(c.Wing, e, out Vec3 at, out float heading);
                Vec3 fwd = Vec3.FromHeading(heading), right = new Vec3(fwd.Z, 0f, -fwd.X);
                int k = 0;
                if (haveLeader)
                    foreach (WingMember m in c.Wing.Members)
                    {
                        if (k >= MaxDots) break;
                        if (m.Released || !m.Alive || (object)m.Aircraft == null || c.Wing.ElementOf(m) != e) continue;
                        Vec3 d = m.Last.Pos - at;
                        var (px, py) = PlanView.Point(Vec3.Dot(d, right), -Vec3.Dot(d, fwd), mpp, PlanSize);
                        Image dot = liveDots[k++];
                        if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
                        dot.rectTransform.anchoredPosition = new Vector2(px - 2.5f, -py + 2.5f);
                        bool picked = c.Selection.Contains(m.Aircraft.persistentID.Id);
                        dot.color = AvStyleHost.FuiColor(picked ? "select" : "friendly", picked ? AvTheme.Accent : AvTheme.Friendly);
                    }
                for (; k < MaxDots; k++)
                    if (liveDots[k].gameObject.activeSelf) liveDots[k].gameObject.SetActive(false);
            }
        }

        private static bool LeaderFrame(WingService w, int e, out Vec3 at, out float heading)
        {
            at = Vec3.Zero;
            heading = 0f;
            // Review R2 I2: A forms on its task lead while it has a task, else on its anchor (FORM ON / ESCORT) or you.
            if (e == 0 && !w.Planner.Active)
            {
                Unit u = w.LeaderUnit != null ? w.LeaderUnit : w.Player;
                if (u == null) return false;
                at = u.GlobalPosition().ToVec3();
                Vector3 f = u.transform.forward;
                heading = Vec3.HeadingDeg(new Vec3(f.x, 0f, f.z));
                return true;
            }
            WingPlanner planner = e == 0 ? w.Planner : w.Roster.InUse(e) ? w.PlannerOf(e) : null;
            if (planner?.Lead == null) return false;
            at = planner.Lead.Position;
            heading = planner.Lead.HeadingDeg;
            return true;
        }
    }
}
