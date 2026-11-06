using BoscaliSummer.Modules.Support.Domain.Fronts;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// A room's FRONT tab (spec 8), read only and in the room's own look (ORBIT console, NETWORK terminal, SHADOW field tablet), top to bottom: SUPERIORITY (value,
    /// word, POWER, the bar, the three terms that make it with our own count and the host's lean, what +40 does); MONEY (budget against the cap, income, the build
    /// in work, the queue); the STANDING ORDER and its lock; the PERKS of this front (grade, READY or what each needs, AP). Nothing here takes a click: every
    /// line the pilot can change says which tab changes it. Fog: own counts and the term leans only, never an enemy count.
    /// </summary>
    internal sealed class FrontInfoPanel
    {
        private const float SecX = 12f, InX = 24f, SupY = 30f, SupH = 192f, MonY = 228f, MonH = 118f, OrdY = 352f, OrdH = 100f, PerkY = 458f, RowH = 18f;
        private const int Rungs = 8; // SPACE has 7 perks, the others fewer

        /// <summary>One room's inks: its section glass, edge and rail, the heading, body and quiet words, the standing-order accent, the bar's well.</summary>
        private readonly struct Ink
        {
            public readonly Color Slab, Edge, Rail, Head, Text, Dim, Order, Well;
            public Ink(Color slab, Color edge, Color rail, Color head, Color text, Color dim, Color order, Color well)
            {
                Slab = slab; Edge = edge; Rail = rail; Head = head; Text = text; Dim = dim; Order = order; Well = well;
            }
        }

        private static Ink InkOf(Front f) =>
            f == Front.Space ? new Ink(RoomInk.Plate.WithAlpha(0.92f), RoomInk.Bezel.WithAlpha(0.85f), RoomInk.Ice, RoomInk.Ice, RoomInk.Engrave, RoomInk.Steel, RoomInk.Telemetry, RoomInk.ConsoleBody)
            : f == Front.Cyber ? new Ink(RoomInk.Crt.WithAlpha(0.88f), RoomInk.Phosphor.WithAlpha(0.28f), RoomInk.Phosphor, RoomInk.Phosphor, RoomInk.PhosphorInk, RoomInk.PhosphorDim, RoomInk.Telemetry, RoomInk.Crt)
            : new Ink(RoomInk.OliveGlass.WithAlpha(0.88f), RoomInk.Gold.WithAlpha(0.3f), RoomInk.Gold, RoomInk.Gold, RoomInk.FieldInk, RoomInk.BoardInk, RoomInk.Gold, RoomInk.Board);

        private readonly Front front;
        private readonly Ink ink;
        private readonly FrontBox box;
        private readonly FrontVector cards;
        private readonly float w, perkH, barX, barW;

        private readonly TMP_Text supValue, power, counter, monMeta, income, building, queue, order, orderLine, lockLine, perkMeta;
        private readonly TMP_Text[] terms = new TMP_Text[3], leans = new TMP_Text[3];
        private readonly Image[] termRail = new Image[3];
        private readonly Image supFill, supNeedle;
        private readonly PerkRow[] perkRows = new PerkRow[Rungs];
        private int perkShown = -1;

        public RectTransform Rect => box.Rect;

        public FrontInfoPanel(RectTransform column, FrontSkin skin, Front front, float cw, float ch, string firstTab)
        {
            this.front = front;
            ink = InkOf(front);
            w = cw;
            FrontLook look = front == Front.Space ? FrontLook.Console : front == Front.Cyber ? FrontLook.Terminal : FrontLook.Folder;
            string tag = front == Front.Space ? "FLIGHT" : front == Front.Cyber ? "tty1" : "FILE 07-F";
            box = new FrontBox(column, skin, "Front", 0f, 0f, cw, ch, "FRONT · " + FrontRules.Name(front), AvIcon.Gauge, look).Tagged(tag);
            box.SetMeta("NET " + FrontWords.Callsign(front) + " · READ ONLY", Color.clear);
            RectTransform b = box.Rect;
            float secW = cw - 2f * SecX, inW = cw - 2f * InX;
            perkH = ch - PerkY - 8f;

            FrontVector slabs = FrontVector.Add(b, "Sections");
            AvLay.Place(slabs.rectTransform, 0f, 0f, cw, ch);
            cards = FrontVector.Add(b, "PerkCards");
            AvLay.Place(cards.rectTransform, 0f, 0f, cw, ch);
            skin.Add(() =>
            {
                slabs.Clear();
                Slab(slabs, SecX, SupY, secW, SupH, ink.Rail);
                Slab(slabs, SecX, MonY, secW, MonH, ink.Rail);
                Slab(slabs, SecX, OrdY, secW, OrdH, ink.Order);
                Slab(slabs, SecX, PerkY, secW, perkH, ink.Rail);
                slabs.Flush();
            });

            // ---- SUPERIORITY ----
            Head(b, SupY, "SUPERIORITY", ink.Head);
            supValue = Right(b, SupY, secW, 11.5f, true);
            power = Line(b, SupY + 26f, inW, 10.5f);
            barX = InX; barW = inW;
            Image well = FrontKit.Solid(b, "SupWell", barX, SupY + 48f, barW, 16f, Color.clear);
            supFill = FrontKit.Solid(b, "SupFill", barX + barW * 0.5f, SupY + 50f, 0f, 12f, Color.clear);
            var rules = new Image[4];
            for (int i = 0; i < 4; i++) rules[i] = FrontKit.Solid(b, "BandRule", barX + barW * (i + 1) / 5f, SupY + 48f, 1f, 16f, Color.clear);
            supNeedle = FrontKit.Solid(b, "SupNeedle", barX + barW * 0.5f - 1.5f, SupY + 45f, 3f, 22f, Color.clear);
            string[] bands = { "LOST", "BEHIND", "CONTESTED", "AHEAD", "DOMINANT" };
            var bandText = new TMP_Text[5];
            for (int i = 0; i < 5; i++)
            {
                bandText[i] = FrontKit.Mono(b, "Band" + i, barX + barW * i / 5f, SupY + 65f, barW / 5f, 14f, 10f, TextAlignmentOptions.Midline);
                bandText[i].text = bands[i];
            }
            for (int i = 0; i < 3; i++)
            {
                float y = SupY + 86f + i * 24f;
                termRail[i] = FrontKit.Solid(b, "TermRail" + i, InX, y + 3f, 3f, 16f, Color.clear);
                terms[i] = FrontKit.Mono(b, "Term" + i, InX + 10f, y, inW - 10f - 130f, 22f, 10.5f, TextAlignmentOptions.MidlineLeft);
                leans[i] = FrontKit.Mono(b, "Lean" + i, InX + inW - 126f, y, 126f, 22f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            }
            counter = Line(b, SupY + 162f, inW, 10.5f);

            // ---- MONEY ----
            Head(b, MonY, "MONEY", ink.Head);
            monMeta = Right(b, MonY, secW, 11.5f, true);
            var keys = new TMP_Text[3];
            string[] words = { "INCOME", "BUILDING", "QUEUE" };
            TMP_Text[] values = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float y = MonY + 28f + i * 20f;
                keys[i] = FrontKit.Mono(b, "MoneyKey" + i, InX, y, 110f, RowH, 10.5f, TextAlignmentOptions.MidlineLeft, false, 1f);
                keys[i].text = words[i];
                values[i] = FrontKit.Mono(b, "MoneyValue" + i, InX + 110f, y, inW - 110f, RowH, 10.5f, TextAlignmentOptions.MidlineRight, i == 1);
            }
            income = values[0]; building = values[1]; queue = values[2];
            TMP_Text monFoot = Line(b, MonY + 94f, inW, 10.5f);
            monFoot.text = "› QUEUE AND CHIP IN ON " + firstTab;

            // ---- STANDING ORDER ----
            Head(b, OrdY, "STANDING ORDER", ink.Order);
            order = Right(b, OrdY, secW, 11.5f, true);
            orderLine = Line(b, OrdY + 28f, inW, 10.5f);
            lockLine = Line(b, OrdY + 48f, inW, 10.5f);
            TMP_Text ordFoot = Line(b, OrdY + 74f, inW, 10.5f);
            ordFoot.text = "› SET ON " + firstTab;

            // ---- PERKS ----
            Head(b, PerkY, "PERKS · " + FrontRules.Name(front), ink.Head);
            perkMeta = Right(b, PerkY, secW, 10.5f, false);
            for (int i = 0; i < Rungs; i++) perkRows[i] = new PerkRow(b, InX, inW);
            TMP_Text perkFoot = Line(b, PerkY + perkH - 24f, inW, 10.5f);
            perkFoot.text = "› USE THEM FROM THE OPS PAGE OR THE C MENU";

            skin.Add(() =>
            {
                well.color = ink.Well;
                foreach (Image r in rules) r.color = ink.Edge;
                foreach (TMP_Text t in bandText) t.color = ink.Dim;
                foreach (TMP_Text k in keys) k.color = ink.Dim;
                monFoot.color = ink.Head; ordFoot.color = ink.Order; perkFoot.color = ink.Head;
            });
        }

        /// <summary>A section in the room's own glass and edge with a 3 px rail, as the room's first tab draws its sections.</summary>
        private void Slab(FrontVector g, float x, float y, float sw, float sh, Color rail)
        {
            g.Rect(x, y, sw, sh, ink.Slab);
            g.Frame(x, y, sw, sh, 1f, ink.Edge);
            g.Rect(x, y, 3f, sh, rail.WithAlpha(0.9f));
        }

        private TMP_Text Head(RectTransform p, float y, string text, Color k)
        {
            TMP_Text t = FrontKit.Mono(p, "Head", SecX + 10f, y + 5f, 260f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            t.text = text;
            t.color = k;
            return t;
        }

        private static TMP_Text Right(RectTransform p, float y, float secW, float size, bool strong) =>
            FrontKit.Mono(p, "Meta", SecX + 270f, y + 5f, secW - 280f, 18f, size, TextAlignmentOptions.MidlineRight, strong);

        private static TMP_Text Line(RectTransform p, float y, float inW, float size) =>
            FrontKit.Mono(p, "Line", InX, y, inW, RowH, size, TextAlignmentOptions.MidlineLeft);

        private static Color Lean(int lean, Color even) => lean == 1 ? FrontKit.Tone(AvState.Ready) : lean == 2 ? FrontKit.Tone(AvState.Danger) : even;

        // ---- Paint -----------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            FrontRow r = v.Row;
            bool wait = FrontViews.IsWaiting(r); // the host has not told us this front yet: every host figure is a dash, never a made-up zero

            // Superiority: the band, POWER, the bar, then why (each term's own count and lean), then what +40 does to the enemy.
            int sup = wait ? 0 : r.Superiority;
            AvState tone = sup >= 20 ? AvState.Ready : sup > -20 ? AvState.Info : sup > -60 ? AvState.Caution : AvState.Danger;
            Color supInk = wait ? ink.Dim : tone == AvState.Info ? ink.Text : FrontKit.Tone(tone);
            FrontKit.Set(supValue, wait ? "WAITING FOR HOST" : FrontKit.Signed(sup) + " · " + FrontSuperiority.Word(sup), supInk);
            float pw = FrontSuperiority.Power(sup);
            string effect = pw > 1.001f ? " · YOUR PERKS HIT HARDER" : pw < 0.999f ? " · YOUR PERKS HIT SOFTER" : " · YOUR PERKS AT FULL STRENGTH";
            FrontKit.Set(power, wait ? "POWER — · WAITING FOR THE HOST" : FrontKit.Fit(power, v.PowerLine + effect, 10.5f), wait ? ink.Dim : ink.Text);
            float half = barW * 0.5f, len = Mathf.Abs(sup) / 100f * half;
            supFill.color = wait ? Color.clear : tone == AvState.Info ? ink.Head.WithAlpha(0.6f) : AvInk.State(tone).WithAlpha(0.85f);
            supNeedle.color = wait ? Color.clear : ink.Text;
            AvLay.Place(supFill.rectTransform, barX + half + (sup >= 0 ? 0f : -len), SupY + 50f, len, 12f);
            AvLay.Place(supNeedle.rectTransform, barX + half + sup / 100f * half - 1.5f, SupY + 45f, 3f, 22f);
            for (int i = 0; i < 3; i++)
            {
                int lean = wait ? 0 : FrontViews.Lean(r, i);
                Color k = wait ? ink.Dim : Lean(lean, ink.Dim);
                termRail[i].color = wait || lean == 0 ? ink.Edge : AvInk.State(lean == 1 ? AvState.Ready : AvState.Danger); // a lamp in the state colour, the word in the scheme's
                FrontKit.Set(terms[i], FrontKit.Fit(terms[i], wait ? "—" : v.Terms[i], 10.5f), wait ? ink.Dim : ink.Text);
                FrontKit.Set(leans[i], wait ? "—" : lean == 1 ? "AHEAD" : lean == 2 ? "BEHIND" : "EVEN", k);
            }
            bool on = !wait && FrontSuperiority.CounterActive(sup);
            FrontKit.Set(counter, FrontKit.Fit(counter, (on ? "COUNTER ON · " : "AT +40 THE COUNTER HITS · ") + FrontWords.CounterEffect(front), 10.5f), on ? FrontKit.Tone(AvState.Ready) : ink.Dim);

            // Money: the budget against its cap, the local income estimate (the first tab's funds line), the build in work, the queue.
            int cap = (int)FrontRules.BudgetCap;
            FrontKit.Set(monMeta, wait ? "FUNDS —" : "FUNDS " + r.Budget + " / " + cap, wait ? ink.Dim : ink.Text);
            FrontKit.Set(income, wait ? "—" : "+" + v.FundsRate + " / MIN" + (r.Budget >= cap ? " · AT THE CAP" : ""), wait ? ink.Dim : ink.Text);
            string work = "NOTHING QUEUED";
            Color workInk = ink.Dim;
            if (!wait && v.Queue.Count > 0)
            {
                FrontQueueView q = v.Queue[0];
                work = q.Name + " · " + (q.Building ? FrontPanels.Clocked("", q.BuildLeft) : "FUNDING " + Mathf.RoundToInt(Mathf.Clamp01(q.Progress) * 100f) + " %");
                workInk = q.Building ? FrontKit.Tone(AvState.Caution) : ink.Text;
            }
            FrontKit.Set(building, FrontKit.Fit(building, wait ? "—" : work, 10.5f), workInk);
            FrontKit.Set(queue, wait ? "—" : v.Queue.Count + " OF " + FrontRules.MaxQueue, wait ? ink.Dim : ink.Text);

            // The standing order and its lock (who changed it last, and for how long).
            float left = r.DirectiveLockUntil - v.Now;
            bool locked = !wait && left > 0f;
            FrontKit.Set(order, wait ? "—" : FrontRules.Name(r.Directive), wait ? ink.Dim : ink.Text);
            FrontKit.Set(orderLine, FrontKit.Fit(orderLine, wait ? "WAITING FOR THE HOST" : "> " + FrontPanels.Describe(front, r.Directive).ToUpperInvariant(), 10.5f), wait ? ink.Dim : ink.Text);
            FrontKit.Set(lockLine, FrontKit.Fit(lockLine, locked ? FrontPanels.LockWord(FrontPanels.SameName(r.DirectiveBy, v.Callsign), r.DirectiveBy, left)
                : "FREE · THE DIRECTORS FOLLOW IT WHEN NOBODY IS DRIVING · ANY PILOT MAY CHANGE IT", 10.5f), locked ? FrontKit.Tone(AvState.Caution) : ink.Dim);

            // Perks: what each needs, read only.
            int ready = 0, count = Mathf.Min(Rungs, v.Perks.Count);
            LayoutPerks(count);
            for (int i = 0; i < count; i++)
            {
                FrontPerkView p = v.Perks[i];
                if (p.Unlocked && string.IsNullOrEmpty(p.Reason) && string.IsNullOrEmpty(p.Gate)) ready++;
                perkRows[i].Paint(p, wait, ink);
            }
            FrontKit.Set(perkMeta, ready + " OF " + v.Perks.Count + " READY · YOUR SKILL " + Mathf.Clamp(v.Grade, 1, 6) + "/6", ink.Dim);
        }

        /// <summary>The perk rows share the section: a 40 px card each while they fit (fewer rows, same pitch), a tighter pitch otherwise; spare rows hide.</summary>
        private void LayoutPerks(int count)
        {
            if (count == perkShown) return;
            perkShown = count;
            float pitch = Mathf.Min(40f, (perkH - 28f - 30f) / Mathf.Max(1, count)), cardW = w - 2f * InX + 8f;
            cards.Clear();
            for (int i = 0; i < Rungs; i++)
            {
                float y = PerkY + 28f + i * pitch;
                perkRows[i].Layout(y, pitch, i < count);
                if (i < count) { cards.Rect(InX - 4f, y, cardW, pitch - 4f, ink.Well.WithAlpha(0.7f)); cards.Frame(InX - 4f, y, cardW, pitch - 4f, 1f, ink.Edge); }
            }
            cards.Flush();
        }

        /// <summary>One perk: its grade and name, READY with the price or the price it will cost, and under them what it does or what it still needs.</summary>
        private sealed class PerkRow
        {
            private readonly TMP_Text grade, name, state, line;
            private readonly float x, rw;

            public PerkRow(RectTransform p, float x, float width)
            {
                this.x = x; rw = width;
                grade = FrontKit.Mono(p, "PerkGrade", x, 0f, 28f, 18f, 11f, TextAlignmentOptions.MidlineLeft, true);
                name = FrontKit.Mono(p, "PerkName", x + 30f, 0f, rw - 30f - 170f, 18f, 11f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(p, "PerkState", x + rw - 168f, 0f, 164f, 18f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                line = FrontKit.Mono(p, "PerkLine", x + 30f, 0f, rw - 34f, 16f, 10f, TextAlignmentOptions.MidlineLeft);
            }

            public void Layout(float y, float pitch, bool shown)
            {
                foreach (TMP_Text t in new[] { grade, name, state, line }) t.gameObject.SetActive(shown);
                if (!shown) return;
                bool two = pitch >= 34f; // a tight pitch drops the second line
                AvLay.Place(grade.rectTransform, x + 4f, y + 2f, 28f, 18f);
                AvLay.Place(name.rectTransform, x + 34f, y + 2f, rw - 34f - 170f, 18f);
                AvLay.Place(state.rectTransform, x + rw - 170f, y + 2f, 166f, 18f);
                AvLay.Place(line.rectTransform, x + 34f, y + 19f, rw - 40f, 15f);
                line.gameObject.SetActive(two);
            }

            public void Paint(in FrontPerkView v, bool wait, Ink ink)
            {
                bool blocked = !string.IsNullOrEmpty(v.Reason), gated = !blocked && !string.IsNullOrEmpty(v.Gate), ready = v.Unlocked && !blocked && !gated;
                FrontKit.Set(grade, v.Rung > 0 ? "G" + v.Rung : "—", v.Unlocked ? FrontKit.Tone(AvState.Ready) : ink.Dim);
                FrontKit.Set(name, FrontKit.Fit(name, v.Name, 11f), ready ? ink.Text : ink.Dim);
                if (wait) FrontKit.Set(state, "—", ink.Dim);
                else if (ready) FrontKit.Set(state, "READY · " + v.Price + " AP", FrontKit.Tone(AvState.Ready));
                else FrontKit.Set(state, (gated ? "RELOCATE · " : !v.Unlocked ? "LOCKED · " : "") + v.Price + " AP", gated || !v.Unlocked ? FrontKit.Tone(AvState.Caution) : ink.Dim);
                FrontKit.Set(line, FrontKit.Fit(line, blocked ? v.Reason : gated ? v.Gate : v.Effect.ToUpperInvariant(), 10f), blocked || gated ? FrontKit.Tone(AvState.Caution) : ink.Dim);
            }
        }
    }
}
