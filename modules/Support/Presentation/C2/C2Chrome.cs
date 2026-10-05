using System;
using BoscaliSummer.Modules.Support.Domain.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// The top of every OPS surface: classification banner (16), header (36), session line (18) and numbered tabs (36).
    /// Fixed 106 px. Every string is fitted to its slot with an ellipsis, so a 24-character callsign or a 4-digit balance
    /// can never overlap its neighbour. Colours are palette roles only; <see cref="Restyle"/> repaints after a theme change.
    /// </summary>
    internal sealed class C2Chrome : AvPart
    {
        public const float Height = 106f;
        private const float BannerH = 16f, HeaderH = 36f, SessionH = 18f, TabsH = 36f, PadX = 8f, LedgerW = 74f;
        private const int TabCount = 5;

        private readonly Image bannerBack;
        private readonly TMP_Text bannerText;
        private readonly RectTransform header, session, tabRow;
        private readonly AvFrame headerBack, plate;
        private readonly TMP_Text plateText, title, sub, ledgerValue, ledgerKey, alertText, sessionLeft, sessionRight;
        private readonly Image alertBack, sessionRule, boardEdge;
        private readonly AvControl[] tabs = new AvControl[TabCount];
        private readonly Action<C2Tab> onTab;

        private float width = AvTokens.PanelWidth;
        private AvState bannerTone = AvState.Caution, uplinkTone = AvState.Ready;
        private string bannerRaw = "", titleRaw = "", subRaw = "", alertRaw = "", opRaw = "", sessionId = "", keyRot = "", uplinks = "", space = "";
        private int credit, boardCount;
        private bool link = true;
        private C2Tab active = C2Tab.Cap;

        public C2Chrome(RectTransform parent, Action<C2Tab> onTab)
        {
            this.onTab = onTab;
            Rect = AvLay.Child(parent, "C2Chrome");

            bannerBack = AvLay.Solid(Rect, "BannerBack", Color.clear);
            bannerText = C2Kit.Mono(Rect, "Banner", 10f, TextAlignmentOptions.Center, true, 10f);

            header = AvLay.Child(Rect, "Header");
            headerBack = AvFrame.Add(header, "Back", default(AvChamfer));
            AvLay.Fill(headerBack.rectTransform);
            plate = AvFrame.Add(header, "Plate", default(AvChamfer));
            plateText = C2Kit.Mono(header, "PlateText", 13f, TextAlignmentOptions.Center, true, 4f);
            plateText.text = "C2";
            title = C2Kit.Cond(header, "Title", AvTextRole.Title, 16f, TextAlignmentOptions.MidlineLeft);
            sub = C2Kit.Mono(header, "Sub", 10f, TextAlignmentOptions.MidlineLeft);
            alertBack = AvLay.Solid(header, "AlertBack", Color.clear);
            alertText = C2Kit.Mono(header, "Alert", 10f, TextAlignmentOptions.Center, true, 4f);
            ledgerValue = C2Kit.Mono(header, "Ledger", 15f, TextAlignmentOptions.MidlineRight, true);
            ledgerKey = C2Kit.Mono(header, "LedgerKey", 10f, TextAlignmentOptions.MidlineRight, false, 3f);
            ledgerKey.text = "CR LEDGER";

            session = AvLay.Child(Rect, "Session");
            sessionLeft = C2Kit.Mono(session, "SessionLeft", 10f, TextAlignmentOptions.MidlineLeft);
            sessionRight = C2Kit.Mono(session, "SessionRight", 10f, TextAlignmentOptions.MidlineRight);
            sessionRule = AvLay.Solid(session, "Rule", Color.clear);

            tabRow = AvLay.Child(Rect, "Tabs");
            for (int i = 0; i < TabCount; i++)
            {
                C2Tab tab = (C2Tab)(i + 1);
                tabs[i] = AvControl.Make(tabRow, new AvControl.Spec(C2Words.TabLabel(tab, 0), () => this.onTab?.Invoke(tab)), "tab");
                tabs[i].SingleLine();
            }
            boardEdge = AvLay.Solid(tabRow, "BoardEdge", Color.clear);
            SetTabs(C2Tab.Cap, 0);
            Restyle();
            Layout();
        }

        public override float Measure(float w) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            Layout();
        }

        // ---- Setters ------------------------------------------------------------------------

        /// <summary>The classification banner; Caution normally, Danger while an alert is up.</summary>
        public void SetBanner(string text, AvState tone)
        {
            bannerRaw = text ?? "";
            bannerTone = tone;
            Restyle();
            Layout();
        }

        /// <summary>Page title, one mono sub-line and an optional red alert slab (null or empty hides it).</summary>
        public void SetHeader(string titleText, string subText, string alert)
        {
            titleRaw = titleText ?? "";
            subRaw = subText ?? "";
            alertRaw = alert ?? "";
            Layout();
        }

        public void SetLedger(int value)
        {
            credit = value;
            Layout();
        }

        /// <summary>
        /// The session line. <paramref name="op"/> is the callsign (fitted with an ellipsis); session id and key rotation are
        /// cosmetic strings from <see cref="C2Words"/>; uplinks, space and link are real state.
        /// </summary>
        public void SetSession(string op, string sess, string keyRotation, string uplinkText, AvState tone, string spaceText, bool linkUp)
        {
            opRaw = op ?? "";
            sessionId = sess ?? "";
            keyRot = keyRotation ?? "";
            uplinks = uplinkText ?? "";
            uplinkTone = tone;
            space = spaceText ?? "";
            link = linkUp;
            Layout();
        }

        public void SetTabs(C2Tab activeTab, int count)
        {
            active = activeTab;
            boardCount = Mathf.Max(0, count);
            for (int i = 0; i < TabCount; i++)
            {
                C2Tab tab = (C2Tab)(i + 1);
                tabs[i].Label = C2Words.TabLabel(tab, boardCount);
                tabs[i].Latched = tab == activeTab;
            }
            PaintBoardEdge();
        }

        // ---- Layout -------------------------------------------------------------------------

        private void Layout()
        {
            float w = width;
            AvLay.Place(bannerBack.rectTransform, 0f, 0f, w, BannerH);
            C2Kit.Place(bannerText, PadX, 0f, w - 2f * PadX, BannerH);
            OpsText.Set(bannerText, C2Kit.FitTo(bannerText, bannerRaw, w - 2f * PadX));

            AvLay.Place(header, 0f, BannerH, w, HeaderH);
            AvLay.Place(plate.rectTransform, PadX, 6f, 28f, 24f);
            AvLay.Place(plateText.rectTransform, PadX, 6f, 28f, 24f);

            string ledgerText = credit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            OpsText.Set(ledgerValue, ledgerText);
            float ledgerX = w - PadX - LedgerW;
            C2Kit.Place(ledgerValue, ledgerX, 1f, LedgerW, 21f);
            C2Kit.Place(ledgerKey, ledgerX - 10f, 22f, LedgerW + 10f, 12f);

            bool alert = alertRaw.Length > 0;
            float titleX = PadX + 28f + 8f, right = ledgerX - 8f;
            float alertW = 0f;
            alertBack.gameObject.SetActive(alert);
            alertText.gameObject.SetActive(alert);
            if (alert)
            {
                float room = Mathf.Max(60f, Mathf.Min(170f, (right - titleX) * 0.55f));
                OpsText.Set(alertText, C2Kit.FitTo(alertText, alertRaw, room - 12f));
                alertW = Mathf.Min(room, C2Kit.Width(alertText, alertText.text) + 14f);
                float ax = right - alertW;
                AvLay.Place(alertBack.rectTransform, ax, 8f, alertW, 20f);
                C2Kit.Place(alertText, ax, 8f, alertW, 20f);
                alertW += 6f;
            }
            float textW = Mathf.Max(20f, right - titleX - alertW);
            OpsText.Set(title, C2Kit.FitTo(title, titleRaw, textW));
            OpsText.Set(sub, C2Kit.FitTo(sub, subRaw, textW));
            C2Kit.Place(title, titleX, 1f, textW, 20f);
            C2Kit.Place(sub, titleX, 21f, textW, 13f);

            AvLay.Place(session, 0f, BannerH + HeaderH, w, SessionH);
            RebuildSession(w);
            AvLay.Place(sessionRule.rectTransform, 0f, SessionH - 1f, w, 1f);

            AvLay.Place(tabRow, 0f, BannerH + HeaderH + SessionH, w, TabsH);
            float tw = (w - 2f * 2f - 4f * 2f) / TabCount;
            for (int i = 0; i < TabCount; i++) AvLay.Place(tabs[i].Rect, 2f + i * (tw + 2f), 2f, tw, TabsH - 4f);
            PaintBoardEdge();
        }

        private void RebuildSession(float w)
        {
            float avail = w - 2f * PadX;
            Color ok = OpsInk.Word(AvState.Ready), bad = OpsInk.Word(AvState.Danger);
            // ASCII words, not check / cross marks: the game font has neither glyph (see the kit render glyph probe).
            string linkWord = link ? C2Kit.Tint("LINK OK", ok) : C2Kit.Tint("LINK DOWN", bad);
            string sp = space.Length > 0 ? "SPACE " + space : "";
            for (int pass = 0; pass < 2; pass++)
            {
                // Pass 1 shortens the labels (UPL, KEY) when a long callsign would otherwise be cut to a few letters.
                string up = uplinks.Length > 0 ? C2Kit.Tint((pass == 0 ? "UPLINKS " : "UPL ") + uplinks, OpsInk.Word(uplinkTone)) : "";
                string right = Join("  ", up, sp, linkWord);
                sessionRight.text = right;
                float rightW = C2Kit.Width(sessionRight, right);
                string tail = " \u00B7 SESS " + sessionId + " \u00B7 " + (pass == 0 ? "KEY ROT " : "KEY ") + keyRot;
                float fixedW = C2Kit.Width(sessionLeft, "OPR " + tail);
                float budget = avail - rightW - 10f - fixedW;
                float need = Mathf.Min(C2Kit.Width(sessionLeft, opRaw), 72f);
                if (pass == 0 && budget < need) continue;
                string op = C2Kit.FitTo(sessionLeft, opRaw, Mathf.Max(24f, budget));
                sessionLeft.text = "OPR " + op + tail;
                float leftW = Mathf.Min(avail, C2Kit.Width(sessionLeft, sessionLeft.text));
                C2Kit.Place(sessionLeft, PadX, 0f, leftW + 2f, SessionH - 1f);
                C2Kit.Place(sessionRight, PadX + leftW + 6f, 0f, Mathf.Max(10f, avail - leftW - 6f), SessionH - 1f);
                return;
            }
        }

        private static string Join(string sep, params string[] parts)
        {
            string s = "";
            foreach (string p in parts) if (!string.IsNullOrEmpty(p)) s += (s.Length > 0 ? sep : "") + p;
            return s;
        }

        private void PaintBoardEdge()
        {
            bool on = boardCount > 0 && active != C2Tab.Board;
            boardEdge.gameObject.SetActive(on);
            boardEdge.color = OpsInk.Rail(AvState.Caution);
            AvControl board = tabs[(int)C2Tab.Board - 1];
            AvLay.Place(boardEdge.rectTransform, board.Rect.anchoredPosition.x, 2f, board.Rect.sizeDelta.x, 2f);
        }

        // ---- Paint --------------------------------------------------------------------------

        public override void Restyle()
        {
            bannerBack.color = C2Kit.SlabFill(bannerTone);
            bannerText.color = C2Kit.SlabInk;

            AvStyle h = AvStyleHost.FuiStyle("header");
            headerBack.Paint(AvStyleHost.Resolve(h.Background, AvTheme.Surface), AvStyleHost.Resolve(h.Border, AvTheme.Hairline));
            AvStyle id = AvStyleHost.FuiStyle("id-plate");
            plate.Paint(AvStyleHost.Resolve(id.Background, AvTheme.SurfaceRaised), OpsInk.Key);
            plateText.color = OpsInk.Key;
            title.color = OpsInk.Ink;
            sub.color = OpsInk.Dim;
            ledgerValue.color = OpsInk.Ink;
            ledgerKey.color = OpsInk.Muted;
            alertBack.color = C2Kit.SlabFill(AvState.Danger);
            alertText.color = C2Kit.SlabInk;
            sessionLeft.color = OpsInk.Dim;
            sessionRight.color = OpsInk.Dim;
            sessionRule.color = OpsInk.Hairline;
            foreach (AvControl t in tabs) t.Restyle();
            if (tabs[0] != null && (width > 0f)) { RebuildSession(width); PaintBoardEdge(); }
        }
    }
}
