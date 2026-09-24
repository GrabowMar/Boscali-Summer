using System;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// What every OPS page shares inside the MFD shell: one title row carrying a compact
    /// STATUS / ACTIONS toggle (it replaces the second full-width tab row, M1), the pinned ARMED
    /// banner with ABORT, a guarded switch, and the adaptive stack that fits a page to the panel
    /// height (420 / 596 / 896) by priority tier instead of scrolling a fixed column (M2). The pages
    /// themselves stay unique per domain.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float PageRowHeight = 28f;
        private const float ToggleWidth = 168f;
        private const float BannerRowHeight = 30f;

        /// <summary>The title row with the STATUS / ACTIONS toggle; returns the rect under it.</summary>
        private static Rect PageFrame(RectTransform root, Rect body, OpsDomain domain, string title, int sub, Action<int> select,
            string[] tips, out TMP_Text caption)
        {
            OpsSprites.Ensure();
            AvKit.Rule(root, new Rect(body.x, body.y - 3f, 3f, PageRowHeight - 3f), AvTheme.RailInfo);
            int mark = domain == OpsDomain.Space ? OpsSprites.G.Space
                : domain == OpsDomain.Cyber ? OpsSprites.G.Cyber : OpsSprites.G.SpecOps;
            Image glyph = AvKit.Panel(root, new Rect(body.x + 10f, body.y - 6f, 16f, 16f),
                AvTheme.RailInfo, OpsSprites.Glyph(mark));
            glyph.raycastTarget = false;
            caption = AvKit.Label(root, title, new Rect(body.x + 32f, body.y - 2f,
                body.width - ToggleWidth - 40f, PageRowHeight - 2f),
                AvTheme.TextPrimary, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            caption.characterSpacing = 1f;
            caption.enableAutoSizing = true;
            caption.fontSizeMin = AvTokens.FontMicro;
            caption.fontSizeMax = AvTokens.FontLead;
            float half = ToggleWidth * 0.5f;
            for (int i = 0; i < SubLabels.Length; i++)
            {
                int index = i;
                AvButton segment = AvStyled.Button(root,
                    new Rect(body.x + body.width - ToggleWidth + i * half,
                        body.y, half - (i == 0 ? 2f : 0f), PageRowHeight),
                    SubLabels[i], "tab", () => select(index), AvButtonStyle.Tab);
                if (tips != null && i < tips.Length) segment.WithTooltip(tips[i]);
                segment.SetLatched(i == sub);
            }
            AvKit.Rule(root, new Rect(body.x, body.y - PageRowHeight - 3f, body.width, 1f), AvTheme.RailInfo.WithAlpha(0.7f));
            AvKit.Rule(root, new Rect(body.x, body.y - PageRowHeight - 3f, 42f, 2f), AvTheme.RailInfo);
            return new Rect(body.x + 4f, body.y - PageRowHeight - 8f,
                body.width - 8f, body.height - PageRowHeight - 8f);
        }

        /// <summary>A black-glass instrument with a semantic left rail and calibrated frame ticks.</summary>
        private static Image InstrumentPlate(RectTransform parent, Rect at, Color tone)
        {
            AvKit.Panel(parent, at, AvTheme.SurfaceInert);
            AvKit.Rule(parent, new Rect(at.x, at.y, at.width, 1f), AvTheme.RailInfo.WithAlpha(0.65f));
            AvKit.Rule(parent, new Rect(at.x + at.width - 1f, at.y - 10f, 1f, 10f), AvTheme.RailInfo.WithAlpha(0.65f));
            AvKit.Rule(parent, new Rect(at.x + 12f, at.y - at.height + 1f, at.width - 24f, 1f), AvTheme.Hairline);
            return AvKit.Rule(parent, new Rect(at.x, at.y, 3f, at.height), tone);
        }

        /// <summary>A section container placed at a stack height; hidden when the stack gave it none.</summary>
        private static RectTransform Section(RectTransform root, string name, Rect at)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            AvKit.Place(rect, at);
            go.SetActive(at.height > 0.5f);
            return rect;
        }

        /// <summary>
        /// Stack the pieces into <paramref name="content"/> top to bottom in index order. Every piece
        /// after the first carries its gap, so a piece the height cannot afford leaves no hole; the
        /// fill piece (last tier) takes what is left down to the bottom edge.
        /// </summary>
        private static Rect[] Stack(Rect content, StackPiece[] pieces, float gap)
        {
            var padded = new StackPiece[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                float g = i == 0 ? 0f : gap;
                padded[i] = new StackPiece(pieces[i].Tier, pieces[i].Preferred + g, pieces[i].Minimum + g, pieces[i].Fill);
            }
            var heights = new float[pieces.Length];
            AdaptiveStack.Fit(padded, padded.Length, content.height, heights);
            var rects = new Rect[pieces.Length];
            float y = content.y;
            bool placed = false;
            float bottom = content.y - content.height;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (heights[i] <= 0f)
                {
                    rects[i] = new Rect(content.x, y, content.width, 0f);
                    continue;
                }
                float g = i == 0 ? 0f : gap;
                if (placed) y -= g;
                float h = pieces[i].Fill ? y - bottom : heights[i] - g;
                rects[i] = new Rect(content.x, y, content.width, Mathf.Max(0f, h));
                y -= rects[i].height;
                placed = true;
            }
            return rects;
        }

        // ---- ARMED banner -------------------------------------------------------------------------

        /// <summary>A strip that either says what the page is for, or pins the armed ability with ABORT.</summary>
        private sealed class ArmedBanner
        {
            public Image Fill, Rail;
            public TMP_Text Text;
            public AvButton Abort, Next;
        }

        private ArmedBanner BuildArmedBanner(RectTransform parent, Rect at, string nextLabel, Action nextAction, string nextTip)
        {
            var banner = new ArmedBanner
            {
                Fill = AvKit.Panel(parent, at, AvTheme.SurfaceInert),
                Rail = AvKit.Rule(parent, new Rect(at.x, at.y, 3f, at.height), AvTheme.RailInfo)
            };
            AvKit.Rule(parent, new Rect(at.x + 3f, at.y - at.height + 1f, at.width - 3f, 1f), AvTheme.RailInfo.WithAlpha(0.6f));
            banner.Text = SingleLine(AvKit.Label(parent, "", new Rect(at.x + 10f, at.y, at.width - 124f, at.height), AvTheme.Dim,
                AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft));
            // Hints vary in length with live figures; shrink toward the 10 px floor before any ellipsis.
            banner.Text.enableAutoSizing = true;
            banner.Text.fontSizeMin = AvTokens.FontMicro;
            banner.Text.fontSizeMax = AvTokens.FontSmall;
            banner.Abort = AvStyled.Button(parent, new Rect(at.x + at.width - 88f, at.y - 2f, 84f, at.height - 4f), "ABORT", "btn",
                () =>
                {
                    support.Disarm();
                    nextRefresh = 0f;
                }, AvButtonStyle.Danger).WithTooltip("Disarm the pending order; nothing is spent.");
            banner.Abort.gameObject.SetActive(false);
            banner.Next = AvStyled.Button(parent, new Rect(at.x + at.width - 106f, at.y - 2f, 102f, at.height - 4f),
                nextLabel, "btn", nextAction, AvButtonStyle.Primary).WithTooltip(nextTip);
            banner.Next.gameObject.SetActive(false);
            return banner;
        }

        /// <summary>Pins the ability of this tab that is armed, else shows the page's hint.</summary>
        private void PaintArmedBanner(ArmedBanner banner, int tab, string hint, Color hintTone, bool showNext = true)
        {
            SupportActionDefinition armed = null;
            if (support.ArmedAction.HasValue)
                foreach (SupportActionDefinition action in support.Actions)
                    if (action.Id == support.ArmedAction.Value && HomeTab(action) == tab) armed = action;
            bool on = armed != null;
            if (banner.Abort.gameObject.activeSelf != on) banner.Abort.gameObject.SetActive(on);
            bool next = showNext && !on && !support.CommandArmed && !support.ArmedAction.HasValue && !support.LocalPickArmed;
            if (banner.Next.gameObject.activeSelf != next) banner.Next.gameObject.SetActive(next);
            string text = on ? "ARMED · " + armed.Name + " · RIGHT-CLICK THE MAP" : hint ?? "";
            if (banner.Text.text != text) banner.Text.text = text;
            banner.Text.color = on ? AvTheme.RailCaution : hintTone;
            banner.Rail.color = on ? AvTheme.RailCaution : hintTone == AvTheme.Dim ? AvTheme.RailInert : hintTone;
            banner.Fill.color = on ? AvTheme.RailCaution.WithAlpha(0.1f) : AvTheme.SurfaceInert;
        }

        // ---- Guarded switch ---------------------------------------------------------------------

        /// <summary>
        /// A switch under a hinged cover: the cover (hatched) is down while the order cannot go, lifted
        /// when it can; latched when the order is armed. One press arms, as before; the cover is how
        /// readiness reads at a glance.
        /// </summary>
        private sealed class GuardedSwitch
        {
            public RoomControl Control;
            public Image Body, Cover, Hinge;
            public Image[] Edge;
            public TMP_Text Label;
            public float Height;
        }

        private static GuardedSwitch BuildGuardedSwitch(RectTransform parent, Rect at, Action click)
        {
            var sw = new GuardedSwitch { Height = at.height };
            sw.Control = RoomControl.Create(parent, at, click, "GuardedSwitch");
            RectTransform host = sw.Control.Rect;
            sw.Body = AvKit.Panel(host, new Rect(0f, 0f, at.width, at.height), AvTheme.SurfaceInert, AvSprites.Control);
            sw.Edge = AvKit.Outline(host, new Rect(0f, 0f, at.width, at.height), AvTheme.Frame);
            sw.Label = AvKit.Label(host, "", new Rect(0f, 0f, at.width, at.height), AvTheme.TextPrimary, AvTokens.FontSmall,
                FontStyles.Bold, TextAlignmentOptions.Center);
            OpsSprites.Ensure();
            sw.Cover = AvKit.Panel(host, new Rect(0f, 0f, at.width, at.height), AvTheme.RailInert.WithAlpha(0.45f));
            sw.Cover.sprite = OpsSprites.Guard;
            sw.Cover.type = Image.Type.Tiled;
            sw.Hinge = AvKit.Rule(host, new Rect(0f, 0f, at.width, 3f), AvTheme.RailInfo);
            sw.Control.Changed = _ => PaintSwitch(sw);
            return sw;
        }

        private static void SetSwitch(GuardedSwitch sw, string text, bool enabled, bool latched, string tip)
        {
            if (sw.Label.text != text) sw.Label.text = text;
            sw.Control.SetEnabled(enabled);
            sw.Control.SetLatched(latched);
            sw.Control.WithTooltip(tip);
            PaintSwitch(sw);
        }

        private static void PaintSwitch(GuardedSwitch sw)
        {
            RoomControl c = sw.Control;
            bool open = c.Enabled || c.Latched;
            // Closed: the hatched cover sits over the switch. Open: it is lifted to a strip at the hinge.
            RectTransform cover = sw.Cover.rectTransform;
            cover.sizeDelta = new Vector2(cover.sizeDelta.x, open ? 6f : sw.Height);
            Color coverTone = c.Latched ? AvTheme.RailCaution : c.Enabled ? AvTheme.RailInfo : AvTheme.RailInert;
            sw.Cover.color = coverTone.WithAlpha(open ? 0.85f : 0.4f);
            sw.Hinge.color = coverTone;
            sw.Body.color = c.Latched ? AvTheme.RailCaution.WithAlpha(0.35f)
                : !c.Enabled ? AvTheme.SurfaceInert
                : c.Pressed ? AvTheme.Accent.WithAlpha(0.45f) : c.Hovered ? AvTheme.Accent.WithAlpha(0.25f) : AvTheme.SurfaceRaised;
            Color edge = c.Latched ? AvTheme.RailCaution : c.Enabled ? AvTheme.Accent : AvTheme.Hairline;
            for (int i = 0; i < sw.Edge.Length; i++) sw.Edge[i].color = edge;
            sw.Label.color = c.Enabled || c.Latched ? AvTheme.TextPrimary : AvTheme.Disabled;
        }

        /// <summary>Monospaced text for the terminal-flavoured CYBER pages.</summary>
        private static string Mono(string text) => string.IsNullOrEmpty(text) ? "" : "<mspace=0.6em>" + text;
    }
}
