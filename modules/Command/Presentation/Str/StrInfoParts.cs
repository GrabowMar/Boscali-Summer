using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The INFO tab's chain of command: an OWN / ENEMY switch, the side's officers as a top-down org chart (theater on top, components under it, bases under their own component), and the dossier of the
    /// selected officer. Read only: the one action is LOCATE on an enemy officer whose post is on the strike list. The selection and the side are the
    /// panel's own state; every word comes from the model. When there is no staff board the part shows the one stated reason instead.
    /// </summary>
    internal sealed class StrChainPart : StrPart
    {
        private const float Pad = 8f, CardH = 84f, CardGap = 8f, CardVGap = 26f, CardMaxW = 180f, ButtonW = 74f, ButtonH = 26f, PortraitSize = 56f, TraitH = 20f, DossierPad = 10f;

        private sealed class Card
        {
            public AvFrame Frame, PortraitFrame;
            public Image Top, Portrait, Light;
            public TMP_Text Icon, Rank, Name, Office, Status;
        }

        private readonly TMP_Text count, reasonHead, reasonWhy, reasonDo, note, locateWhy, dossierName, dossierRole, stationKey, stationValue, intelKey, intelValue, noTraits;
        private readonly AvFrame reasonFrame, dossierFrame, portraitFrame;
        private readonly Image dossierRail, portrait, dossierRule;
        private readonly TMP_Text portraitIcon;
        private readonly Card[] cards = new Card[StrOrgPlan.MaxCards];
        private readonly int[] cardRow = new int[StrOrgPlan.MaxCards];
        private readonly StrOrgPlan plan = new StrOrgPlan();
        private readonly AvQuadGraphic lines;
        private readonly TMP_Text[] traits = new TMP_Text[3];
        private readonly StrButton own, enemy, locate;
        private bool enemySide;
        private int selected = -1, shown, selectedRow = -1;
        private StrOfficerRow[] list;

        internal StrChainPart(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Chain of command", controls, hub)
        {
            own = new StrButton(Rect, "OWN", AvButtonStyle.Default, () => ChooseSide(false));
            enemy = new StrButton(Rect, "ENEMY", AvButtonStyle.Default, () => ChooseSide(true));
            count = Text(Rect, "Count", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            reasonFrame = AvFrame.Add(Rect, "ReasonFrame", AvChamfer.Diagonal(6f));
            AvLay.Fill(reasonFrame.rectTransform);
            reasonHead = Text(Rect, "ReasonHead", AvTextRole.Title, TextAlignmentOptions.TopLeft, true);
            reasonWhy = Text(Rect, "ReasonWhy", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            reasonDo = Text(Rect, "ReasonDo", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            var go = new GameObject("Lines", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            lines = go.AddComponent<AvQuadGraphic>();
            lines.raycastTarget = false;
            for (int i = 0; i < cards.Length; i++) cards[i] = MakeCard(i);
            note = Text(Rect, "Note", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);

            dossierFrame = AvFrame.Add(Rect, "Dossier", AvChamfer.Diagonal(8f));
            dossierRail = AvLay.Solid(Rect, "DossierRail", Color.clear);
            portraitFrame = AvFrame.Add(Rect, "PortraitFrame", AvChamfer.Diagonal(5f));
            portrait = AvLay.Solid(portraitFrame.rectTransform, "Portrait", Color.white);
            portrait.preserveAspect = true;
            AvLay.Fill(portrait.rectTransform, 2f);
            portrait.raycastTarget = false;
            portraitIcon = AvIcons.Make(Rect, AvIcon.User, 30f, Color.white);
            dossierName = Text(Rect, "DossierName", AvTextRole.Title, TextAlignmentOptions.TopLeft, true);
            dossierRole = Text(Rect, "DossierRole", AvTextRole.Label, TextAlignmentOptions.TopLeft, true);
            dossierRule = AvLay.Solid(Rect, "DossierRule", Color.clear);
            stationKey = Text(Rect, "StationKey", AvTextRole.Micro, TextAlignmentOptions.TopLeft);
            stationValue = Text(Rect, "StationValue", AvTextRole.DataStrong, TextAlignmentOptions.TopLeft);
            intelKey = Text(Rect, "IntelKey", AvTextRole.Micro, TextAlignmentOptions.TopLeft);
            intelValue = Text(Rect, "IntelValue", AvTextRole.DataStrong, TextAlignmentOptions.TopLeft);
            stationKey.text = "STATION";
            intelKey.text = "INTEL";
            for (int i = 0; i < traits.Length; i++) traits[i] = Text(Rect, "Trait " + i, AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            noTraits = Text(Rect, "NoTraits", AvTextRole.ProseSmall, TextAlignmentOptions.MidlineLeft);
            locateWhy = Text(Rect, "LocateWhy", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            locate = new StrButton(Rect, "LOCATE", AvButtonStyle.Default, () => LocateSelected());
            Restyle();
        }

        private Card MakeCard(int index)
        {
            var c = new Card();
            c.Frame = AvFrame.Add(Rect, "Officer " + index, AvChamfer.Diagonal(5f));
            c.Frame.raycastTarget = true;
            AvHit.On(c.Frame).Click = e => Choose(cardRow[index]);
            RectTransform f = c.Frame.rectTransform;
            c.Top = AvLay.Solid(f, "Top", Color.clear);
            c.PortraitFrame = AvFrame.Add(f, "PortraitFrame", AvChamfer.Diagonal(3f));
            c.Portrait = AvLay.Solid(c.PortraitFrame.rectTransform, "Portrait", Color.white);
            c.Portrait.preserveAspect = true;
            AvLay.Fill(c.Portrait.rectTransform, 2f);
            c.Light = AvLay.Solid(f, "Light", Color.clear);
            c.Top.raycastTarget = c.PortraitFrame.raycastTarget = c.Portrait.raycastTarget = c.Light.raycastTarget = false;
            c.Icon = AvIcons.Make(f, AvIcon.User, 20f, Color.white);
            c.Rank = Text(f, "Rank", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            c.Name = Text(f, "Name", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            c.Office = Text(f, "Office", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            c.Status = Text(f, "Status", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            c.Rank.overflowMode = c.Name.overflowMode = c.Office.overflowMode = c.Status.overflowMode = TextOverflowModes.Ellipsis;
            c.Frame.gameObject.SetActive(false);
            return c;
        }

        private StrAct ChooseSide(bool toEnemy)
        {
            if (enemySide != toEnemy) { enemySide = toEnemy; selected = -1; }
            if (Vm != null) Bind(Vm);   // Bind, not Apply: a placed part places its children again, or the rows that appeared would sit unplaced
            return Allowed;
        }

        private void Choose(int index)
        {
            if (list == null || index < 0 || index >= shown) return;
            selected = list[index].Id;
            Bind(Vm);
        }

        private StrAct LocateSelected()
        {
            StrOfficerRow o = SelectedRow();
            if (o == null) return new StrAct(StrWhy.Gone, "NO OFFICER IS SELECTED");
            return o.Locate.IsNone ? new StrAct(StrWhy.Gone, o.LocateWhy) : Locate(o.Locate);
        }

        private StrOfficerRow SelectedRow() => selectedRow >= 0 && selectedRow < shown && list != null ? list[selectedRow] : null;

        protected override void Apply(StrViewModel vm)
        {
            StrStaffView s = vm.Staff;
            bool roster = s.Mode == StrStaffMode.Roster;
            foreach (string id in new[] { StrControlIds.InfoSide(false), StrControlIds.InfoSide(true), StrControlIds.InfoLocate }) Controls.Remove(id);
            for (int i = 1; i <= StrViewModel.MaxOfficers; i++) Controls.Remove(StrControlIds.InfoPost(i));
            list = enemySide ? s.Enemy : s.Own;
            shown = roster ? (enemySide ? s.EnemyCount : s.OwnCount) : 0;
            selectedRow = -1;
            for (int i = 0; i < shown; i++) if (list[i].Id == selected) selectedRow = i;
            if (selectedRow < 0 && shown > 0) { selectedRow = 0; selected = list[0].Id; }

            // With no board there is nothing to switch: the buttons go, rather than stay as controls that do nothing.
            own.SetActive(roster);
            enemy.SetActive(roster);
            if (roster)
            {
                own.Bind(Controls, StrControlIds.InfoSide(false), "OWN", () => Allowed, "Show your own chain of command.", !enemySide);
                enemy.Bind(Controls, StrControlIds.InfoSide(true), "ENEMY", () => Allowed, "Show the enemy posts your side has confirmed. The rest stay sealed.", enemySide);
            }
            AvText.Set(count, roster ? StrPanelText.StaffCount(s, enemySide) : "");

            AvText.Set(reasonHead, roster ? "" : s.Head);
            AvText.Set(reasonWhy, roster ? "" : s.Why);
            AvText.Set(reasonDo, roster ? "" : s.Do);
            string noteText = !roster ? "" : enemySide ? s.EnemyNote : s.OwnNote;
            AvText.Set(note, noteText);

            // The cards stand where the chart puts them: Lay shows the ones the side needs, so a side with no officers shows none.
            for (int i = 0; i < cards.Length; i++) cards[i].Frame.gameObject.SetActive(false);
            lines.gameObject.SetActive(false);
            for (int i = 0; i < shown; i++)
            {
                int index = i;
                Controls.Register(StrControlIds.InfoPost(i + 1), null, () => { Choose(index); return Allowed; });
            }
            ApplyDossier(SelectedRow());
            Paint();
            Changed();
        }

        private void ApplyDossier(StrOfficerRow o)
        {
            bool on = o != null;
            dossierFrame.gameObject.SetActive(on);
            dossierRail.gameObject.SetActive(on);
            portraitFrame.gameObject.SetActive(on);
            portraitIcon.gameObject.SetActive(on);
            dossierRule.gameObject.SetActive(on);
            foreach (TMP_Text t in new[] { dossierName, dossierRole, stationKey, stationValue, intelKey, intelValue, noTraits, locateWhy }) t.gameObject.SetActive(on);
            for (int i = 0; i < traits.Length; i++) traits[i].gameObject.SetActive(on && i < o.TraitCount);
            bool button = on && !o.Own;
            locate.SetActive(button);
            if (!on) return;
            AvText.Set(dossierName, o.Name);
            AvText.Set(dossierRole, string.IsNullOrEmpty(o.Rank) ? o.Role : o.Rank + StrWords.Dot + o.Role);
            AvText.Set(stationValue, o.Site);
            AvText.Set(intelValue, o.SeenText);
            for (int i = 0; i < o.TraitCount; i++) AvText.Set(traits[i], o.Traits[i]);
            AvText.Set(noTraits, o.TraitCount > 0 ? "" : o.Sealed ? "FILE SEALED: YOUR SIDE HAS NOT CONFIRMED THIS POST" : "NO NOTABLE TRAITS");
            AvText.Set(locateWhy, button && !o.Locate.IsNone ? "" : o.LocateWhy);
            bool hasPortrait = o.Portrait != null;
            portrait.sprite = hasPortrait ? o.Portrait : null;
            portrait.enabled = hasPortrait;
            portraitIcon.gameObject.SetActive(!hasPortrait);
            if (button)
                locate.Bind(Controls, StrControlIds.InfoLocate, "LOCATE", () => { StrOfficerRow s = SelectedRow(); return s == null ? new StrAct(StrWhy.Gone, "NO OFFICER IS SELECTED") : s.Locate.IsNone ? new StrAct(StrWhy.Gone, s.LocateWhy) : LocateGate(s.Locate); },
                    "Fly the map to this post.");
        }

        public override float Measure(float width) => Lay(width, false);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            Lay(s.W, true);
        }

        private float Lay(float width, bool place)
        {
            if (Vm == null) return 0f;
            StrStaffView s = Vm.Staff;
            float y = 0f;
            if (s.Mode == StrStaffMode.Roster)
            {
                if (place)
                {
                    own.Place(Pad, y, ButtonW, ButtonH);
                    enemy.Place(Pad + ButtonW + 2f, y, ButtonW, ButtonH);
                    AvLay.Place(count, width - Pad - 170f, y, 170f, ButtonH);
                }
                y += ButtonH + 6f;
            }
            if (s.Mode != StrStaffMode.Roster)
            {
                float w = width - 2f * Pad;
                float h = Pad + Mathf.Max(22f, AvText.Height(reasonHead, w)) + 4f + AvText.Height(reasonWhy, w) + 4f + AvText.Height(reasonDo, w) + Pad;
                if (place)
                {
                    reasonFrame.gameObject.SetActive(true);
                    AvLay.Place(reasonFrame.rectTransform, 0f, y, width, h);
                    float yy = y + Pad;
                    AvLay.Place(reasonHead, Pad, yy, w, Mathf.Max(22f, AvText.Height(reasonHead, w)));
                    yy += Mathf.Max(22f, AvText.Height(reasonHead, w)) + 4f;
                    AvLay.Place(reasonWhy, Pad, yy, w, AvText.Height(reasonWhy, w));
                    yy += AvText.Height(reasonWhy, w) + 4f;
                    AvLay.Place(reasonDo, Pad, yy, w, AvText.Height(reasonDo, w));
                }
                return y + h;
            }
            if (place) reasonFrame.gameObject.SetActive(false);

            if (shown > 0)
            {
                plan.Build(list, shown, width, CardH, CardGap, CardVGap, CardMaxW);
                if (place) PlaceChart(y, width);
                y += plan.Height;
            }

            float noteW = width - 2f * Pad;
            if (note.text.Length > 0)
            {
                float h = AvText.Height(note, noteW);
                if (place) AvLay.Place(note, Pad, y + 6f, noteW, h);
                y += 6f + h;
            }

            StrOfficerRow sel = SelectedRow();
            if (sel != null) y += 8f + Dossier(width, y + 8f, sel, place);
            return y + 2f;
        }

        // The cards and the connector lines the plan laid out; an empty card (an office nobody holds) is a dashed outline.
        private void PlaceChart(float top, float width)
        {
            lines.gameObject.SetActive(true);
            AvLay.Place(lines.rectTransform, 0f, top, width, plan.Height);
            lines.Begin();
            for (int i = 0; i < plan.SegCount; i++) lines.Add(plan.Segs[i].X, plan.Segs[i].Y, plan.Segs[i].W, plan.Segs[i].H);
            for (int i = 0; i < cards.Length; i++)
            {
                Card c = cards[i];
                bool on = i < plan.CardCount;
                c.Frame.gameObject.SetActive(on);
                if (!on) continue;
                StrOrgCard g = plan.Cards[i];
                cardRow[i] = g.Row;
                bool vacant = g.Row < 0;
                StrOfficerRow o = vacant ? null : list[g.Row];
                AvLay.Place(c.Frame.rectTransform, g.X, top + g.Y, g.W, g.H);
                float inner = g.W - 14f, text = g.W - 55f;
                AvLay.Place(c.Top.rectTransform, 0f, 0f, g.W, 2f);
                AvLay.Place(c.PortraitFrame.rectTransform, 7f, 8f, 36f, 36f);
                AvLay.Place(c.Icon.rectTransform, 7f, 8f, 36f, 36f);
                AvLay.Place(c.Rank.rectTransform, 49f, 5f, text, 16f);   // a Micro line needs 16 high or the ellipsis swallows it
                AvLay.Place(c.Name.rectTransform, vacant ? 7f : 49f, vacant ? 22f : 21f, vacant ? inner : text, 18f);
                AvLay.Place(c.Office.rectTransform, 7f, vacant ? 44f : 48f, inner, 16f);
                AvLay.Place(c.Light.rectTransform, 7f, 70f, 6f, 6f);
                AvLay.Place(c.Status.rectTransform, 17f, 65f, g.W - 24f, 16f);
                c.PortraitFrame.gameObject.SetActive(!vacant);
                c.Light.gameObject.SetActive(!vacant);
                c.Icon.gameObject.SetActive(!vacant && o.Portrait == null);
                AvText.Set(c.Rank, vacant ? "" : o.Rank);
                AvText.Set(c.Name, vacant ? "VACANT" : o.Name);
                AvText.Set(c.Office, vacant ? StrWords.OfficeOf(g.Tier) : o.Office);
                AvText.Set(c.Status, vacant ? "" : o.StatusText);
                if (!vacant)
                {
                    c.Portrait.sprite = o.Portrait;
                    c.Portrait.enabled = o.Portrait != null;
                }
                else Dashed(g.X, g.Y, g.W, g.H);
            }
            lines.End();
            PaintCards();
        }

        private void Dashed(float x, float y, float w, float h)
        {
            for (float d = 0f; d < w; d += 9f) { float len = Mathf.Min(5f, w - d); lines.Add(x + d, y, len, 1f); lines.Add(x + d, y + h - 1f, len, 1f); }
            for (float d = 0f; d < h; d += 9f) { float len = Mathf.Min(5f, h - d); lines.Add(x, y + d, 1f, len); lines.Add(x + w - 1f, y + d, 1f, len); }
        }

        // The cards' colours: the selected one is raised, framed and capped in the selection colour; an empty one is faint.
        private void PaintCards()
        {
            lines.color = AvInk.Frame.WithAlpha(0.75f);
            lines.SetVerticesDirty();
            for (int i = 0; i < plan.CardCount && i < cards.Length; i++)
            {
                Card c = cards[i];
                StrOrgCard g = plan.Cards[i];
                if (g.Row < 0 || g.Row >= shown || list == null)
                {
                    c.Frame.Paint(Color.clear, Color.clear);
                    c.Top.color = Color.clear;
                    c.Name.color = AvInk.Muted;
                    c.Office.color = AvInk.Muted;
                    continue;
                }
                StrOfficerRow o = list[g.Row];
                Color ink = StrInk.Of(o.Ink);
                bool chosen = g.Row == selectedRow;
                c.Frame.Paint(chosen ? AvInk.Raised : AvInk.Inert, chosen ? AvInk.Select : AvInk.Hairline);
                c.Top.color = chosen ? AvInk.Select : ink.WithAlpha(0.9f);
                c.PortraitFrame.Paint(AvInk.Inert, (chosen ? AvInk.Select : ink).WithAlpha(0.8f));
                c.Icon.color = AvInk.Muted;
                c.Light.color = ink;
                c.Rank.color = o.Sealed ? AvInk.Muted : AvInk.Key;
                c.Name.color = o.Sealed ? AvInk.Dim : AvInk.Ink;
                c.Office.color = AvInk.Dim;
                c.Status.color = o.Ink == StrInkRole.Own || o.Ink == StrInkRole.Muted ? AvInk.Dim : o.Ink == StrInkRole.Hostile ? Color.Lerp(ink, AvInk.Ink, 0.5f) : ink;
            }
        }

        // The dossier card of the selected officer; returns its height.
        private float Dossier(float width, float top, StrOfficerRow o, bool place)
        {
            float textX = DossierPad + PortraitSize + 12f, textW = width - textX - DossierPad;
            float nameH = Mathf.Max(22f, AvText.Height(dossierName, textW)), roleH = AvText.Height(dossierRole, textW);
            float head = Mathf.Max(PortraitSize, nameH + 2f + roleH);
            float colW = (width - 2f * DossierPad - 8f) * 0.5f;
            int lines = Mathf.Max(1, o.TraitCount);
            bool button = !o.Own;
            float whyW = width - 2f * DossierPad - ButtonW - 8f;
            float whyH = button && locateWhy.text.Length > 0 ? AvText.Height(locateWhy, whyW) : 0f;
            float foot = button ? Mathf.Max(ButtonH, whyH) : 0f;
            float h = DossierPad + head + 7f + 1f + 7f + 15f + 18f + 6f + lines * TraitH + (button ? 8f + foot : 0f) + DossierPad;
            if (!place) return h;

            AvLay.Place(dossierFrame.rectTransform, 0f, top, width, h);
            AvLay.Place(dossierRail.rectTransform, 0f, top, 3f, h);
            float y = top + DossierPad;
            AvLay.Place(portraitFrame.rectTransform, DossierPad, y, PortraitSize, PortraitSize);
            AvLay.Place(portraitIcon.rectTransform, DossierPad, y, PortraitSize, PortraitSize);
            AvLay.Place(dossierName, textX, y, textW, nameH);
            AvLay.Place(dossierRole, textX, y + nameH + 2f, textW, roleH);
            y += head + 7f;
            AvLay.Place(dossierRule.rectTransform, DossierPad, y, width - 2f * DossierPad, 1f);
            y += 8f;
            AvLay.Place(stationKey, DossierPad, y, colW, 15f);
            AvLay.Place(stationValue, DossierPad, y + 15f, colW, 18f);
            AvLay.Place(intelKey, DossierPad + colW + 8f, y, colW, 15f);
            AvLay.Place(intelValue, DossierPad + colW + 8f, y + 15f, colW, 18f);
            y += 15f + 18f + 6f;
            for (int i = 0; i < lines; i++)
            {
                TMP_Text t = o.TraitCount > 0 ? traits[i] : noTraits;
                if (o.TraitCount > 0 || i == 0) AvLay.Place(t, DossierPad, y + i * TraitH, width - 2f * DossierPad, TraitH);
            }
            y += lines * TraitH;
            if (button)
            {
                y += 8f;
                locate.Place(width - DossierPad - ButtonW, y, ButtonW, ButtonH);
                AvLay.Place(locateWhy, DossierPad, y, whyW, Mathf.Max(whyH, 15f));
            }
            return h;
        }

        private void Paint()
        {
            reasonFrame.Paint(AvInk.Inert, AvInk.Hairline);
            reasonHead.color = AvInk.Ink;
            reasonWhy.color = AvInk.Ink;
            reasonDo.color = AvInk.Dim;
            note.color = AvInk.Dim;
            count.color = AvInk.Dim;
            StrOfficerRow sel = SelectedRow();
            PaintCards();
            Color rail = sel == null ? Color.clear : StrInk.Of(sel.Ink);
            dossierFrame.Paint(AvInk.Raised, AvInk.Frame.WithAlpha(0.8f));
            dossierRail.color = rail;
            portraitFrame.Paint(AvInk.Inert, rail.WithAlpha(0.8f));
            portraitIcon.color = AvInk.Muted;
            dossierName.color = sel != null && sel.Sealed ? AvInk.Dim : AvInk.Ink;
            dossierRole.color = AvInk.Dim;
            dossierRule.color = AvInk.Hairline;
            stationKey.color = intelKey.color = AvInk.Muted;
            stationValue.color = intelValue.color = AvInk.Ink;
            foreach (TMP_Text t in traits) t.color = AvInk.Ink;
            noTraits.color = AvInk.Muted;
            locateWhy.color = AvInk.Dim;
        }

        public override void Restyle()
        {
            own.Restyle();
            enemy.Restyle();
            locate.Restyle();
            Paint();
        }
    }
}
