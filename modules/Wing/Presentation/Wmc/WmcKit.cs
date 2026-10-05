using NOAvionics;
using System;
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
using BoscaliSummer.Core.Game;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Kit v2 drawing primitives for the WMC's Rect/top-left, y-down-negative layout convention (phase A: the tab
    /// files that call into <see cref="WmcKit"/>/<see cref="WmcUi"/>/<see cref="WmcFui"/> still pass absolute Rects; phase B
    /// converts them to <see cref="AvFlow"/>). Every visual here is built from <see cref="AvFrame"/>/<see cref="AvText"/>/
    /// <see cref="AvStyleHost"/> — never the retired v1 primitives.</summary>
    internal static class WmcDraw
    {
        /// <summary>WMC Rects are top-left, y growing downward as a <em>negative</em> anchoredPosition.y (v1's Place helper
        /// convention); kit v2's <see cref="AvLay.Place(RectTransform,float,float,float,float)"/> negates y itself, so this
        /// flips it back once, here, instead of at every call site.</summary>
        public static void Place(RectTransform t, Rect r) => AvLay.Place(t, r.x, -r.y, r.width, r.height);

        public static RectTransform Container(RectTransform parent, string name, Rect r)
        {
            RectTransform t = AvLay.Child(parent, name);
            Place(t, r);
            return t;
        }

        /// <summary>A flat fill, no border (v1's Panel helper).</summary>
        public static AvFrame Panel(RectTransform parent, Rect r, Color fill)
        {
            AvFrame f = AvFrame.Add(parent, "Panel", default);
            Place(f.rectTransform, r);
            f.Paint(fill, Color.clear);
            f.raycastTarget = false;
            return f;
        }

        /// <summary>A 1 px stroke, no fill (v1's Outline helper, one mesh instead of four rules).</summary>
        public static AvFrame Outline(RectTransform parent, Rect r, Color color)
        {
            AvFrame f = AvFrame.Add(parent, "Outline", default);
            Place(f.rectTransform, r);
            f.Fill = false;
            f.Paint(Color.clear, color);
            f.raycastTarget = false;
            return f;
        }

        /// <summary>A thin filled line (a hairline, a tick, a spine): a plain Image, not a mesh frame.</summary>
        public static Image Rule(RectTransform parent, Rect r, Color color)
        {
            Image img = AvLay.Solid(parent, "Rule", color);
            Place(img.rectTransform, r);
            return img;
        }

        /// <summary>Top-left and bottom-right L-brackets (the FUI card-corner mark).</summary>
        public static void Corners(RectTransform parent, Rect r, Color color, float len = WmcFui.Corner)
        {
            Rule(parent, new Rect(r.x, r.y, len, 1f), color);
            Rule(parent, new Rect(r.x, r.y, 1f, len), color);
            Rule(parent, new Rect(r.x + r.width - len, r.y - r.height + 1f, len, 1f), color);
            Rule(parent, new Rect(r.x + r.width - 1f, r.y - r.height + len, 1f, len), color);
        }

        /// <summary>A card fill + border resolved from the fui stylesheet class (e.g. "card", "card inert", "row"), falling
        /// back to the given colours when the class has no rule.</summary>
        public static AvFrame Box(RectTransform parent, Rect r, string classes, Color fallbackFill, Color fallbackBorder, string state = null)
        {
            AvStyle s = AvStyleHost.FuiStyle(classes, state);
            AvFrame f = AvFrame.Add(parent, "Box", default);
            Place(f.rectTransform, r);
            f.Paint(AvStyleHost.Resolve(s.Background, fallbackFill), s.Border.HasValue ? AvStyleHost.Resolve(s.Border, fallbackBorder) : Color.clear);
            f.raycastTarget = false;
            return f;
        }

        /// <summary>A flat, borderless fill as a plain <see cref="Image"/> (not an <see cref="AvFrame"/> mesh).</summary>
        public static Image Fill(RectTransform parent, Rect r, Color color)
        {
            Image img = AvLay.Solid(parent, "Fill", color);
            Place(img.rectTransform, r);
            return img;
        }

        /// <summary>A single-line, non-wrapping label at a Rect, sized/cased by <paramref name="role"/> and coloured from
        /// the fui stylesheet class <paramref name="classes"/> (falling back to <paramref name="fallback"/>).</summary>
        public static TMP_Text Label(RectTransform parent, Rect r, string text, AvTextRole role, string classes, Color fallback,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text t = AvText.Make(parent, "Label", role, text ?? "", align);
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            Place(t.rectTransform, r);
            t.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle(classes).Color, fallback);
            return t;
        }

        /// <summary>A semantic rail colour ("ready"/"caution"/"danger"/"info"/"inert" — the same words <see cref="WmcStyle"/>
        /// normalises to) resolved against the theme's `:root` roles.</summary>
        public static Color RailColor(string railClass)
        {
            switch (railClass)
            {
                case "ready":
                case "live": return AvStyleHost.FuiColor("ready", AvTheme.RailReady);
                case "caution":
                case "armed":
                case "warn": return AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
                case "danger": return AvStyleHost.FuiColor("danger", AvTheme.RailDanger);
                case "info": return AvStyleHost.FuiColor("info", AvTheme.RailInfo);
                default: return AvStyleHost.FuiColor("hairline", AvTheme.RailInert);
            }
        }
    }

    /// <summary>The widgets WMC pages share that the NOAvionics toolkit does not have (spec WMC rebuild §widget kit), built
    /// from kit v2 primitives (<see cref="WmcDraw"/>) — WC never edits the toolkit. Pages use kit v2 parts and
    /// <see cref="WmcControls"/> (see <see cref="IWmcPage"/>).</summary>
    internal static class WmcKit
    {
        /// <summary>The tallest empty band (px) inside <paramref name="body"/> between visible graphics on the page on show, the
        /// trailing space under the last one excluded (spec bezel v2 §10 <c>gap_px</c>). Backdrops, spines and viewports (taller
        /// than 120 px) do not count as content. Automation only: it allocates.</summary>
        public static int LargestGap(RectTransform content, Rect body)
        {
            var spans = new List<Vector2>();
            Rect cr = content.rect;
            var corners = new Vector3[4];
            float bodyBottom = body.y - body.height;
            foreach (Graphic g in content.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.enabled || g.color.a <= 0.01f) continue;
                if (g is TMP_Text t && string.IsNullOrEmpty(t.text)) continue;
                g.rectTransform.GetWorldCorners(corners);
                float top = content.InverseTransformPoint(corners[1]).y - cr.yMax;
                float bottom = content.InverseTransformPoint(corners[0]).y - cr.yMax;
                if (top - bottom > 120f) continue;
                top = Mathf.Min(top, body.y);
                bottom = Mathf.Max(bottom, bodyBottom);
                if (top - bottom <= 0.5f) continue;
                spans.Add(new Vector2(top, bottom));
            }
            spans.Sort((a, b) => b.x.CompareTo(a.x));
            float cursor = body.y;
            int gap = 0;
            foreach (Vector2 span in spans)
            {
                if (span.x < cursor) gap = Mathf.Max(gap, Mathf.RoundToInt(cursor - span.x));
                cursor = Mathf.Min(cursor, span.y);
            }
            return gap;
        }

        /// <summary>No "…" anywhere (spec WMC rebuild): every label under <paramref name="root"/> overflows instead of cutting
        /// and shrinks to the 10 px floor before it does. Once after a build (and after a popup opens).</summary>
        public static void FitAll(RectTransform root)
        {
            if (root == null) return;
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.GetComponentInParent<TMP_InputField>(true) != null) continue;
                if (t.overflowMode == TextOverflowModes.Ellipsis || (!t.enableWordWrapping && t.overflowMode == TextOverflowModes.Truncate))
                    t.overflowMode = TextOverflowModes.Overflow;
                if (t.enableAutoSizing) continue;
                t.fontSizeMax = t.fontSize;
                t.fontSizeMin = Mathf.Min(AvTokens.FontMicro, t.fontSize);
                t.enableAutoSizing = true;
            }
        }

        /// <summary>Labels that would still spill out of their box at their smallest size (the automation's text-fit audit).
        /// ponytail: estimated from the preferred width at the largest size scaled to the smallest; measure per size if the
        /// estimate ever disagrees with a screenshot.</summary>
        public static int Overflow(RectTransform root)
        {
            if (root == null) return 0;
            int n = 0;
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(t.text) || t.enableWordWrapping) continue;
                float width = t.rectTransform.rect.width;
                if (width <= 1f) continue;
                float scale = t.enableAutoSizing && t.fontSizeMax > 0f ? t.fontSizeMin / t.fontSizeMax : 1f;
                if (t.GetPreferredValues(t.text).x * scale > width + 1f) n++;
            }
            return n;
        }

        /// <summary>A small, single-line text label (kit v2 <see cref="AvTextRole.ProseSmall"/>, coloured by the legacy class
        /// name where the fui sheet defines it).</summary>
        public static TMP_Text Text(RectTransform parent, Rect r, string classes, TextAlignmentOptions? align = null) =>
            WmcDraw.Label(parent, r, "", RoleFor(classes), classes, ColorFor(classes), align ?? TextAlignmentOptions.MidlineLeft);

        /// <summary>The nearest kit v2 type role for a legacy WmcKit/WmcFui class name (sizes come only from
        /// <see cref="AvTextRole"/>, never a literal font size).</summary>
        internal static AvTextRole RoleFor(string classes)
        {
            switch (classes)
            {
                case "section-title":
                case "section-title-note":
                case "metric-key":
                case "kv-key":
                    return AvTextRole.Label;
                case "row-name":
                    return AvTextRole.Label;
                case "kv-value":
                    return AvTextRole.DataStrong;
                default:
                    return AvTextRole.ProseSmall;
            }
        }

        /// <summary>The legacy class's fallback ink colour when the fui sheet has no matching rule.</summary>
        internal static Color ColorFor(string classes)
        {
            switch (classes)
            {
                case "section-title":
                case "section-title-note":
                    return AvTheme.RailInfo;
                case "metric-key":
                case "kv-key":
                    return AvTheme.Dim;
                case "row-name":
                case "kv-value":
                    return AvTheme.TextPrimary;
                case "hint":
                    return AvTheme.Dim;
                default:
                    return AvTheme.Dim;
            }
        }

        /// <summary>Sets a label only when its text changed (TMP relays out on every assignment).</summary>
        public static void Set(TMP_Text t, string text)
        {
            if (t != null && t.text != text) t.text = text;
        }

        /// <summary>A numbered step's head (the 0.9 SUPPLY steps): a boxed digit (never a U+24xx circled one), the title, and a
        /// state chip on the right with its rail; returns the chip's label (<see cref="SetStep"/> writes it).</summary>
        public static TMP_Text StepHeader(RectTransform p, Rect r, int n, string title, out Image rail)
        {
            const float box = 16f, chip = 96f;
            var digit = new Rect(r.x, r.y - 1f, box, box);
            WmcDraw.Box(p, digit, "chip", AvTheme.SurfaceInert, AvTheme.Frame);
            WmcDraw.Label(p, digit, n.ToString(System.Globalization.CultureInfo.InvariantCulture), AvTextRole.Head, "section-title",
                AvTheme.RailInfo, TextAlignmentOptions.Center);
            WmcDraw.Label(p, new Rect(r.x + box + 8f, r.y, r.width - box - 8f - chip - 4f, r.height), title, AvTextRole.Head,
                "section-title", AvTheme.RailInfo);
            var state = new Rect(r.x + r.width - chip, r.y, chip, r.height);
            WmcDraw.Box(p, state, "chip", AvTheme.SurfaceInert, AvTheme.Frame);
            rail = WmcDraw.Rule(p, new Rect(state.x, state.y, 3f, state.height), WmcDraw.RailColor("inert"));
            return Text(p, new Rect(state.x + 8f, state.y, chip - 10f, state.height), "row-sub");
        }

        /// <summary>A step chip's words and rail class ("live", "info", "warn", "inert").</summary>
        public static void SetStep(TMP_Text state, Image rail, string text, string railClass)
        {
            Set(state, text);
            WmcUi.SetRail(rail, railClass);
        }

        /// <summary>Where <paramref name="target"/> sits inside <paramref name="root"/> (a popup opens beside its button, parented
        /// to the page root and never inside a scroll viewport).</summary>
        public static Rect RectIn(RectTransform root, RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector3 tl = root.InverseTransformPoint(corners[1]);
            Rect r = root.rect;
            return new Rect(tl.x - r.xMin, tl.y - r.yMax, target.rect.width, target.rect.height);
        }
    }

    /// <summary>A pilot's portrait in a frame (SUPPLY's pilot card; WING's dossier reuses it): the sprite is looked up only when
    /// the pilot or the roster's <see cref="WingPilotRoster.LookVersion"/> changes (a studio or interop edit).</summary>
    internal sealed class WmcPortrait
    {
        private Image image;
        private WingPilot shown;
        private int look = int.MinValue;
        private int faction = int.MinValue;
        private bool set;

        public static WmcPortrait Build(RectTransform p, Rect r)
        {
            AvFrame frame = WmcDraw.Panel(p, r, AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert));
            WmcDraw.Outline(p, r, AvStyleHost.FuiColor("frame", AvTheme.Frame));
            RectTransform go = AvLay.Child(frame.rectTransform, "Portrait");
            AvLay.Fill(go, 1f);
            var img = go.gameObject.AddComponent<Image>();
            img.color = Color.white;
            var w = new WmcPortrait { image = img };
            w.image.preserveAspect = true;
            w.image.raycastTarget = false;
            return w;
        }

        /// <summary>The pilot's face; with nobody, the generic one faded (a pilot drafted at launch).</summary>
        public void Set(WingPilot pilot)
        {
            int currentFaction = pilot != null && pilot.PortraitFaction >= 0 ? pilot.PortraitFaction : PortraitFactions.Local;
            if (set && ReferenceEquals(pilot, shown) && look == WingPilotRoster.LookVersion && faction == currentFaction) return;
            set = true;
            shown = pilot;
            look = WingPilotRoster.LookVersion;
            faction = currentFaction;
            image.sprite = PilotPortrait.For(pilot);
            image.enabled = image.sprite != null;
            image.color = pilot != null ? Color.white : Color.white.WithAlpha(0.3f);
        }

        public void Invalidate() => set = false;
    }
}
