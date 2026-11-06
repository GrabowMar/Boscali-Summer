using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Core.Math;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL › FORMATION, the Station Board (spec 2026-10-04 §4.2; mockup board/formation.html #a): a plan view of the scope
    /// element's slots with an error ring each and its members live, one row per member (slot, error, phase), a strip of favourite
    /// shapes with ALL SHAPES › (a window of drawn shape cards by family), a spacing stepper over the shape's own range and a stack row
    /// in metres. The scope is the wing table's. The rows come from what the snapshot carries (<see cref="SnapshotMember"/>.Err10,
    /// Phase), so a client reads them too; the plan view and the shapes are the host's. The maneuvers are ORDERS' REACT row.</summary>
    internal sealed class WmcForm : IWmcPage
    {
        private const int MaxShapes = 12, MaxFamilies = 4, Favourites = 5;
        private static readonly string[] FavouriteIds = { "finger-four-right", "vic", "diamond", "echelon-right", "trail" };
        private static readonly float[] StackMetres = { 30f, 10f, 0f, -10f, -30f };
        private static readonly string[] StackWords = { "+30", "+10", "LEVEL", "-10", "-30" };
        private static readonly string[] StackTexts = { "Going up thirty metres", "Going up ten metres", "Level with you", "Going down ten metres", "Going down thirty metres" };

        private AvFlow flow;
        private WmcContext last;
        private AvSection stationSection, shapeSection, spacingSection;
        private WmcPlanCard planCard;
        private WmcStationRows memberRows;
        private AvSection memberSection;
        private WmcShapeCells favourites;
        private AvControl allShapes;
        private WmcSpacingRow spacingRow;
        private WmcLabeledButtons stackRow;
        private readonly StationData data = new StationData();
        private readonly List<FormationDefinition> shapes = new List<FormationDefinition>();
        private readonly List<FormationDefinition> favourite = new List<FormationDefinition>(Favourites);
        private readonly List<string> families = new List<string>();
        private readonly float[] presets = { FormationCatalog.Close, FormationCatalog.Standard, FormationCatalog.Open, FormationCatalog.Spread };
        private FormationDefinition shownShape;
        private string captionShown, memberCaptionShown, spacingCaptionShown;
        private int shapesKey = int.MinValue, stackShown = -1;
        private ShapeWindow window;

        public string Hint => "Shapes are the scope element's; spacing and stack are the whole wing's.";

        public string Alert => null;

        public void Build(AvFlow pageFlow, AvTicker ticker, int pageIndex)
        {
            flow = pageFlow;
            stationSection = flow.Section(AvIcon.LayersSubtract, "STATION", "");
            planCard = flow.Add(new WmcPlanCard(flow.Content));

            memberSection = flow.Section(AvIcon.Stack2, "MEMBERS", "ERROR · STATE");
            memberRows = flow.Add(new WmcStationRows(flow.Content));

            shapeSection = flow.Section(AvIcon.LayersSubtract, "SHAPE", "FAVOURITES");
            favourites = flow.Add(new WmcShapeCells(flow.Content, Favourites, Favourites, 58f, i => () => PickFavourite(i)));
            for (int i = 0; i < Favourites; i++)
            {
                favourites[i].Help = "Fly this shape with the scope's element.";
            }
            allShapes = flow.Buttons(new AvControl.Spec("ALL SHAPES ›", OpenShapes, AvButtonStyle.Default, AvIcon.LayersSubtract)).Controls[0];
            allShapes.Help = "Every shape that suits the wing, by family, drawn: pick one and apply it.";

            spacingSection = flow.Section(AvIcon.LayersSubtract, "SPACING", "WHOLE WING");
            spacingRow = flow.Add(new WmcSpacingRow(flow.Content, () => StepSpacing(-1), () => StepSpacing(1)));
            spacingRow.Minus.Help = "Close the formation up to the next closer spacing.";
            spacingRow.Plus.Help = "Open the formation out to the next wider spacing.";

            var stackSpecs = new AvControl.Spec[StackWords.Length];
            for (int i = 0; i < stackSpecs.Length; i++)
            {
                int k = i;
                stackSpecs[i] = new AvControl.Spec(StackWords[i], () => PickStack(k));
            }
            stackRow = flow.Add(new WmcLabeledButtons(flow.Content, "STACK", AvState.Info, stackSpecs));
            for (int i = 0; i < StackWords.Length; i++)
            {
                stackRow.Controls[i].Help = "The wing flies " + (StackMetres[i] > 0f ? AvNum.Fixed(StackMetres[i], 0) + " m above you." : StackMetres[i] < 0f ? AvNum.Fixed(-StackMetres[i], 0) + " m below you." : "level with you.");
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

        private void PickFavourite(int i)
        {
            if (i < 0 || i >= favourite.Count) return;
            WmcPostureActions.Shape(last, favourite[i].Id);
        }

        private void PickStack(int i)
        {
            float metres = StackMetres[i];
            string what = StackTexts[i];
            WmcUi.Order(last, () => WingCommands.Stack(metres, what));
        }

        /// <summary>The next closer (<paramref name="dir"/> -1) or wider (+1) spacing preset that fits the shape's range and moves the
        /// spacing shown (the preset is the wing's, the shown metres are clamped to the shape's range).</summary>
        private void StepSpacing(int dir)
        {
            WingService w = last?.Wing;
            FormationSelection sel = w?.Selection;
            if (sel == null || last.Client) return;
            FormationDefinition shape = w.ShapeOf(last.ScopeElement) ?? sel.Current;
            float shown = shape.ClampSpacing(FormationSelection.Metres(sel.Spacing));
            for (int k = (int)sel.Spacing + dir; k >= 0 && k < presets.Length; k += dir)
                if (presets[k] >= shape.SpacingMin - 0.5f && presets[k] <= shape.SpacingMax + 0.5f && (presets[k] - shown) * dir > 0.5f)
                {
                    WmcPostureActions.Spacing(last, k);
                    return;
                }
            WingToast.Show(dir < 0 ? "Already as close as this shape goes" : "Already as wide as this shape goes");
        }

        private void RefreshFormation(WmcContext c)
        {
            WingService w = c.Wing;
            FormationSelection sel = w?.Selection;
            bool host = w != null && !c.Client && sel != null;
            int e = c.ScopeElement;
            FormationDefinition shape = host ? w.ShapeOf(e) ?? sel.Current : null;
            float spacing = host ? shape.ClampSpacing(FormationSelection.Metres(sel.Spacing)) : 0f;
            Collect(c, host ? w : null, shape, spacing, e);
            Show(c, data, host, e);

            bool stackOn = c.CanOrder;
            int stackNow = -1;
            if (host)
            {
                float stack = w.Stack;
                for (int i = 0; i < StackMetres.Length; i++)
                    if (Mathf.Abs(stack - StackMetres[i]) < 0.5f) stackNow = i;
            }
            if (stackNow != stackShown)
            {
                stackShown = stackNow;
                for (int i = 0; i < StackWords.Length; i++) stackRow.Controls[i].Latched = i == stackNow;
            }
            foreach (AvControl b in stackRow.Controls)
                if (b.Interactable != stackOn) b.Interactable = stackOn;
            spacingRow.Minus.Interactable = spacingRow.Plus.Interactable = stackOn;
            allShapes.Interactable = host && c.CanOrder;
            if (host) RefreshShapes(sel, shape);
            else ClearShapes();
        }

        /// <summary>The scope element's members as <see cref="StationData"/>: rows from the snapshot, positions from the host.</summary>
        private void Collect(WmcContext c, WingService w, FormationDefinition shape, float spacing, int e)
        {
            data.Reset();
            data.Shape = shape;
            data.Spacing = spacing;
            // Members of the element in slot order.
            int n = 0;
            for (int i = 0; i < c.Count && n < StationData.Max; i++)
            {
                if (c.Rows[i].Element != e) continue;
                SnapshotMember m = c.Rows[i];
                int at = n++;
                while (at > 0 && data.Slot[at - 1] > m.Slot)
                {
                    Move(at - 1, at);
                    at--;
                }
                data.Slot[at] = m.Slot;
                data.Who[at] = WingRows.Number(m.Slot);
                data.Phase[at] = m.Phase;
                data.ErrorM[at] = StationMath.Error(m.Err10);
                data.Picked[at] = c.Selection.Contains(m.Id);
                data.Right[at] = data.Aft[at] = 0f;
                ids3[at] = m.Id;
            }
            data.Count = n;
            data.HaveLive = n > 0;
            if (w == null || n == 0 || shape == null) return;
            if (!LeaderFrame(w, e, out Vec3 at0, out float heading, out bool you)) return;
            data.HeadingDeg = heading;
            data.LeadIsYou = you;
            data.HavePos = true;
            Vec3 fwd = Vec3.FromHeading(heading), right = new Vec3(fwd.Z, 0f, -fwd.X);
            for (int i = 0; i < n; i++)
            {
                WingMember m = c.MemberOf(ids3[i]);
                if (m == null)
                {
                    data.Right[i] = data.Aft[i] = 0f;
                    continue;
                }
                Vec3 d = m.Last.Pos - at0;
                data.Right[i] = Vec3.Dot(d, right);
                data.Aft[i] = -Vec3.Dot(d, fwd);
            }
        }

        private readonly uint[] ids3 = new uint[StationData.Max];

        private void Move(int from, int to)
        {
            data.Slot[to] = data.Slot[from];
            data.Who[to] = data.Who[from];
            data.Phase[to] = data.Phase[from];
            data.ErrorM[to] = data.ErrorM[from];
            data.Picked[to] = data.Picked[from];
            ids3[to] = ids3[from];
        }

        /// <summary>Draws and lists <paramref name="d"/>: the plan view, the member rows (also the offline preview's entry: it fills
        /// <see cref="StationData"/> by hand).</summary>
        internal void Show(WmcContext c, StationData d, bool host, int e)
        {
            string note = host ? null : c != null && c.Client ? "Formation is the host's" : "NO WING";
            planCard.Show(d, note);
            memberRows.Show(d);

            string stationCaption = d.Shape != null ? d.Shape.Name.ToUpperInvariant() + " · " + AvNum.Fixed(d.Spacing, 0) + " M · "
                + (c != null && c.CanOrder ? StackWord(c.Wing.Stack) : "LEVEL") : note != null ? note.ToUpperInvariant() : "";
            if (stationCaption != captionShown)
            {
                captionShown = stationCaption;
                stationSection.SetCaption(stationCaption);
            }
            string memberCaption = d.Count == 0 ? "NO MEMBERS" : "ELEMENT " + ElementRoster.Letter(e) + " · ERROR · STATE";
            if (memberCaption != memberCaptionShown)
            {
                memberCaptionShown = memberCaption;
                memberSection.SetCaption(memberCaption);
            }
            if (d.Shape != null)
            {
                string range = "SHAPE RANGE " + AvNum.Fixed(d.Shape.SpacingMin, 0) + " - " + AvNum.Fixed(d.Shape.SpacingMax, 0) + " M";
                if (range != spacingCaptionShown)
                {
                    spacingCaptionShown = range;
                    spacingSection.SetCaption(range);
                }
                spacingRow.Show(d.Shape.SpacingMin, d.Shape.SpacingMax, d.Spacing, presets);
            }
        }

        private static string StackWord(float stack) => Mathf.Abs(stack) < 0.5f ? "LEVEL" : (stack > 0f ? "+" : "") + AvNum.Fixed(stack, 0) + " M";

        private void ClearShapes()
        {
            if (shapesKey == -2) return;
            shapesKey = -2;
            shownShape = null;
            favourite.Clear();
            favourites.SetShown(0);
            families.Clear();
            shapeSection.SetCaption("HOST ONLY");
            flow.RequestRelayout();
        }

        private void RefreshShapes(FormationSelection sel, FormationDefinition current)
        {
            // The suitable list follows the wing's shape use (jet, rotary, escort): the wing's own shape names it. The caption names
            // the scope element, so a new element with the same shape redraws it too.
            int key = ((current.Id?.GetHashCode() ?? 0) * 31 + (sel.Current?.Id?.GetHashCode() ?? 0)) * 31 + last.ScopeElement;
            if (key == shapesKey) return;
            shapesKey = key;
            shownShape = current;
            sel.Suitable(shapes);
            FormationSelection.Families(shapes, families);
            SetFavourites(current);
            shapeSection.SetCaption("FAVOURITES · ELEMENT " + ElementRoster.Letter(last.ScopeElement));
            if (window != null && window.Visible) window.Fill(shapes, families, current);
            flow.RequestRelayout();
        }

        /// <summary>The favourites: the usual five that this wing's shapes include (the scope's own shape always among them).</summary>
        private void SetFavourites(FormationDefinition current)
        {
            favourite.Clear();
            foreach (string id in FavouriteIds)
            {
                FormationDefinition d = shapes.Find(s => s.Id == id);
                if (d != null) favourite.Add(d);
            }
            foreach (FormationDefinition d in shapes)
                if (favourite.Count < Favourites && !favourite.Contains(d)) favourite.Add(d);
            if (!favourite.Contains(current) && favourite.Count > 0) favourite[favourite.Count - 1] = current;
            else if (!favourite.Contains(current)) favourite.Add(current);
            FillFavourites(current);
        }

        /// <summary>Shows <paramref name="list"/> in the strip (offline preview and tests too).</summary>
        internal void FillFavourites(FormationDefinition current)
        {
            favourites.SetShown(favourite.Count);
            for (int i = 0; i < favourite.Count; i++)
            {
                FormationDefinition d = favourite[i];
                favourites.Set(i, d, WmcWords.Shape(d.Id, d.Name), current != null && d.Id == current.Id);
            }
        }

        private void OpenShapes()
        {
            if (last == null || !last.CanOrder) return;
            OpenShapeWindow(shapes, families, shownShape);
        }

        /// <summary>Opens the shape-cards window on <paramref name="list"/> (offline preview and tests too).</summary>
        internal void OpenShapeWindow(List<FormationDefinition> list, List<string> familyList, FormationDefinition current)
        {
            if (window == null)
            {
                Canvas root = flow.Content.GetComponentInParent<Canvas>();
                window = new ShapeWindow(root != null ? root.rootCanvas.transform : flow.Content, PickFromWindow);
            }
            window.Fill(list, familyList, current);
            window.Show();
        }

        /// <summary>Closes the ALL SHAPES window (the formation page left the screen, or the panel hid) and, with
        /// <paramref name="drop"/>, destroys it (the panel reset). It hangs off the root canvas, so nothing else takes it down.</summary>
        public void CloseShapes(bool drop = false)
        {
            if (window == null) return;
            window.Close(drop);
            if (drop) window = null;
        }

        private void PickFromWindow(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            WmcPostureActions.Shape(last, id);
        }

        /// <summary>The frame the plan view is drawn in; <paramref name="you"/>: it is the player's own (else the element's lead).</summary>
        private static bool LeaderFrame(WingService w, int e, out Vec3 at, out float heading, out bool you)
        {
            at = Vec3.Zero;
            heading = 0f;
            you = false;
            // Review R2 I2: A forms on its task lead while it has a task, else on its anchor (FORM ON / ESCORT) or you.
            if (e == 0 && !w.Planner.Active)
            {
                Unit u = w.LeaderUnit != null ? w.LeaderUnit : w.Player;
                if (u == null) return false;
                you = ReferenceEquals(u, w.Player);
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

        /// <summary>ALL SHAPES (a window): family tabs, the family's shapes as drawn cards, APPLY and CLOSE. A card picks; APPLY sends it to
        /// the scope's element.</summary>
        private sealed class ShapeWindow
        {
            private readonly AvWindow window;
            private readonly System.Action<string> apply;
            private readonly WmcButtonGrid familyTabs;
            private readonly WmcShapeCells cards;
            private readonly AvSection title;
            private readonly AvControl applyButton;
            private readonly List<FormationDefinition> all = new List<FormationDefinition>();
            private readonly List<string> familyNames = new List<string>();
            private readonly List<FormationDefinition> inFamily = new List<FormationDefinition>(MaxShapes);
            private string family, picked, current;

            public bool Visible => window.Visible;

            public ShapeWindow(Transform root, System.Action<string> applied)
            {
                apply = applied;
                window = AvWindow.Build(root, "wmc-shapes", "FORMATION / ALL SHAPES", 440f, 560f, 220);
                AvFlow body = window.Body;
                title = body.Section(AvIcon.LayersSubtract, "FAMILY", "");
                familyTabs = body.Add(new WmcButtonGrid(body.Content, MaxFamilies, MaxFamilies, i => new AvControl.Spec("", () => PickFamilyTab(i))));
                body.Section(AvIcon.LayersSubtract, "SHAPES", "TAP A CARD");
                cards = body.Add(new WmcShapeCells(body.Content, 3, MaxShapes, 84f, i => () => PickCard(i)));
                AvControl[] buttons = body.Buttons(new AvControl.Spec("APPLY", Apply, AvButtonStyle.Primary, AvIcon.CircleCheck),
                    new AvControl.Spec("CLOSE", window.Hide, AvButtonStyle.Quiet)).Controls;
                applyButton = buttons[0];
                applyButton.Help = "Fly the picked shape with the scope's element.";
                buttons[1].Help = "Close this window.";
            }

            public void Show() => window.Show();

            public void Close(bool drop)
            {
                if (window.Visible) window.Hide();
                if (drop) Object.Destroy(window.Root.gameObject);
            }

            public void Fill(List<FormationDefinition> list, List<string> familyList, FormationDefinition now)
            {
                all.Clear();
                all.AddRange(list);
                familyNames.Clear();
                familyNames.AddRange(familyList);
                current = now != null ? now.Id : null;
                picked = current;
                if (family == null || !familyNames.Contains(family)) family = now != null ? now.Family : familyNames.Count > 0 ? familyNames[0] : null;
                Paint();
            }

            private void PickFamilyTab(int i)
            {
                if (i >= familyNames.Count) return;
                family = familyNames[i];
                Paint();
            }

            private void PickCard(int i)
            {
                if (i >= inFamily.Count) return;
                picked = inFamily[i].Id;
                Paint();
            }

            private void Apply()
            {
                if (picked == null) return;
                apply(picked);
                window.Hide();
            }

            private void Paint()
            {
                int fam = Mathf.Min(familyNames.Count, MaxFamilies);
                familyTabs.SetShownCount(fam);
                for (int i = 0; i < fam; i++)
                {
                    familyTabs[i].Label = familyNames[i].ToUpperInvariant();
                    familyTabs[i].Latched = familyNames[i] == family;
                }
                inFamily.Clear();
                foreach (FormationDefinition d in all)
                    if (d.Family == family && inFamily.Count < MaxShapes) inFamily.Add(d);
                cards.SetShown(inFamily.Count);
                for (int i = 0; i < inFamily.Count; i++)
                {
                    FormationDefinition d = inFamily[i];
                    cards.Set(i, d, WmcWords.Shape(d.Id, d.Name) + (d.Id == current ? " · NOW" : ""), d.Id == picked);
                    cards[i].Help = d.Name + ": " + AvNum.Fixed(d.SpacingMin, 0) + " - " + AvNum.Fixed(d.SpacingMax, 0) + " m between slots.";
                }
                title.SetCaption(string.IsNullOrEmpty(family) ? "" : family.ToUpperInvariant());
                applyButton.Interactable = picked != null && picked != current;
                window.Body.RequestRelayout();
            }
        }
    }
}
