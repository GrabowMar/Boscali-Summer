using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The INFO tab's chain of command: an OWN / ENEMY switch, the side's officers as a compact tree (theater, components, bases), and the dossier of the
    /// selected officer. Read only: the one action is LOCATE on an enemy officer whose post is on the strike list. The selection and the side are the
    /// panel's own state; every word comes from the model. When there is no staff board the part shows the one stated reason instead.
    /// </summary>
    internal sealed class StrChainPart : StrPart
    {
        private const float Pad = 8f, RowH = 40f, RowGap = 3f, Indent = 14f, ButtonW = 74f, ButtonH = 26f, PortraitSize = 56f, TraitH = 20f, DossierPad = 10f;

        private sealed class Row
        {
            public AvFrame Frame;
            public Image Rail, Down, Across;
            public TMP_Text Rank, Name, Status, Detail;
        }

        private readonly TMP_Text count, reasonHead, reasonWhy, reasonDo, note, locateWhy, dossierName, dossierRole, stationKey, stationValue, intelKey, intelValue, noTraits;
        private readonly AvFrame reasonFrame, dossierFrame, portraitFrame;
        private readonly Image dossierRail, portrait, dossierRule;
        private readonly TMP_Text portraitIcon;
        private readonly Row[] rows = new Row[StrViewModel.MaxOfficers];
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
            for (int i = 0; i < rows.Length; i++) rows[i] = MakeRow(i);
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

        private Row MakeRow(int index)
        {
            var r = new Row();
            r.Down = AvLay.Solid(Rect, "Down " + index, Color.clear);
            r.Across = AvLay.Solid(Rect, "Across " + index, Color.clear);
            r.Down.raycastTarget = r.Across.raycastTarget = false;
            r.Frame = AvFrame.Add(Rect, "Officer " + index, AvChamfer.Diagonal(5f));
            r.Frame.raycastTarget = true;
            AvHit.On(r.Frame).Click = e => Choose(index);
            r.Rail = AvLay.Solid(r.Frame.rectTransform, "Rail", Color.clear);
            r.Rail.raycastTarget = false;
            r.Rank = Text(r.Frame.rectTransform, "Rank", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            r.Name = Text(r.Frame.rectTransform, "Name", AvTextRole.Head, TextAlignmentOptions.MidlineLeft);
            r.Status = Text(r.Frame.rectTransform, "Status", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            r.Detail = Text(r.Frame.rectTransform, "Detail", AvTextRole.ProseSmall, TextAlignmentOptions.MidlineLeft);
            r.Name.overflowMode = r.Detail.overflowMode = r.Status.overflowMode = r.Rank.overflowMode = TextOverflowModes.Ellipsis;
            return r;
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

            for (int i = 0; i < rows.Length; i++)
            {
                Row r = rows[i];
                bool on = i < shown;
                r.Frame.gameObject.SetActive(on);
                r.Down.gameObject.SetActive(on);
                r.Across.gameObject.SetActive(on);
                if (!on) continue;
                StrOfficerRow o = list[i];
                AvText.Set(r.Rank, o.Rank);
                AvText.Set(r.Name, o.Name);
                AvText.Set(r.Status, o.StatusText);
                AvText.Set(r.Detail, o.Role + StrWords.Dot + o.Site);
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

            for (int i = 0; i < shown; i++)
            {
                StrOfficerRow o = list[i];
                float x = Pad + o.Tier * Indent, w = width - Pad - x;
                if (place) PlaceRow(rows[i], o, i, x, y, w);
                y += RowH + RowGap;
            }
            if (shown > 0) y -= RowGap;

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

        private void PlaceRow(Row r, StrOfficerRow o, int index, float x, float y, float w)
        {
            AvLay.Place(r.Frame.rectTransform, x, y, w, RowH);
            AvLay.Place(r.Rail.rectTransform, 0f, 0f, 3f, RowH);
            float inner = w - 14f, statusW = Mathf.Min(Mathf.Ceil(AvText.Width(r.Status)) + 4f, inner * 0.5f);
            float rankW = string.IsNullOrEmpty(o.Rank) ? 0f : Mathf.Ceil(AvText.Width(r.Rank)) + 6f;
            AvLay.Place(r.Rank.rectTransform, 12f, 3f, rankW, 20f);
            AvLay.Place(r.Name.rectTransform, 12f + rankW, 3f, Mathf.Max(30f, inner - rankW - statusW - 6f), 20f);
            AvLay.Place(r.Status.rectTransform, 12f + inner - statusW, 3f, statusW, 20f);
            AvLay.Place(r.Detail.rectTransform, 12f, 22f, inner, 16f);

            // The tree: a trunk down from the post above and a tick into this row; the trunk carries on while the next row is as deep or deeper.
            bool child = o.Tier > 0;
            r.Down.gameObject.SetActive(child);
            r.Across.gameObject.SetActive(child);
            if (!child) return;
            float tx = Pad + (o.Tier - 1) * Indent + 6f;
            bool carries = index + 1 < shown && list[index + 1].Tier >= o.Tier;
            AvLay.Place(r.Down.rectTransform, tx, y - RowGap, 1f, RowGap + (carries ? RowH + RowGap : RowH * 0.5f));
            AvLay.Place(r.Across.rectTransform, tx, y + RowH * 0.5f, x - tx, 1f);
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
            for (int i = 0; i < shown && list != null; i++)
            {
                Row r = rows[i];
                StrOfficerRow o = list[i];
                Color ink = StrInk.Of(o.Ink);
                bool chosen = i == selectedRow;
                r.Frame.Paint(chosen ? AvInk.Raised : AvInk.Inert, chosen ? AvInk.Select : AvInk.Hairline);
                r.Rail.color = chosen ? AvInk.Select : ink.WithAlpha(0.9f);
                r.Rank.color = o.Sealed ? AvInk.Muted : AvInk.Key;
                r.Name.color = o.Sealed ? AvInk.Dim : AvInk.Ink;
                r.Status.color = o.Ink == StrInkRole.Own || o.Ink == StrInkRole.Muted ? AvInk.Dim : o.Ink == StrInkRole.Hostile ? Color.Lerp(ink, AvInk.Ink, 0.5f) : ink;
                r.Detail.color = AvInk.Dim;
                r.Down.color = r.Across.color = AvInk.Frame.WithAlpha(0.75f);
            }
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
