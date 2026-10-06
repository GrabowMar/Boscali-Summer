using NOAvionics;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Core.Game;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Kit v2 drawing primitives for the WMC's Rect/top-left, y-down-negative layout convention, built from
    /// <see cref="AvFrame"/>/<see cref="AvText"/>/<see cref="AvStyleHost"/>.</summary>
    internal static class WmcDraw
    {
        /// <summary>WMC Rects are top-left, y growing downward as a <em>negative</em> anchoredPosition.y; kit v2's <see cref="AvLay.Place(RectTransform,float,float,float,float)"/> negates y itself, so this
        /// flips it back once, here, instead of at every call site.</summary>
        public static void Place(RectTransform t, Rect r) => AvLay.Place(t, r.x, -r.y, r.width, r.height);

        /// <summary>A flat fill, no border.</summary>
        public static AvFrame Panel(RectTransform parent, Rect r, Color fill)
        {
            AvFrame f = AvFrame.Add(parent, "Panel", default);
            Place(f.rectTransform, r);
            f.Paint(fill, Color.clear);
            f.raycastTarget = false;
            return f;
        }

        /// <summary>A 1 px stroke, no fill.</summary>
        public static AvFrame Outline(RectTransform parent, Rect r, Color color)
        {
            AvFrame f = AvFrame.Add(parent, "Outline", default);
            Place(f.rectTransform, r);
            f.Fill = false;
            f.Paint(Color.clear, color);
            f.raycastTarget = false;
            return f;
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
            t.color = AvStyleHost.FuiInk(classes, fallback);
            return t;
        }

        /// <summary>A semantic rail colour ("ready"/"caution"/"danger"/"info"/"inert" — the same words <see cref="WmcStyle"/>
        /// normalises to) resolved against the theme's `:root` roles.</summary>
        public static Color RailColor(string railClass)
        {
            switch (railClass)
            {
                case "ready":
                case "live": return AvInk.State(AvState.Ready);
                case "caution":
                case "armed":
                case "warn": return AvInk.State(AvState.Caution);
                case "danger": return AvInk.State(AvState.Danger);
                case "info": return AvInk.State(AvState.Info);
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

        /// <summary>The nearest kit v2 type role for a legacy WmcKit class name (sizes come only from
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
