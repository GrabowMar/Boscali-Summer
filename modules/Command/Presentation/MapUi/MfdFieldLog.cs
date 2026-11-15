using System.Text;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// FIELD LOG, the game log in the big map's bottom-right corner (2026-11-15: phase 6 had taken the log off the map onto EVN LOG; the user
    /// wants it back, in the corner, so the panel column keeps its full height). The newest lines of <see cref="ITacticalLog"/> in the one log
    /// look (<see cref="LogLine"/>), newest on top; EVN LOG holds all of them with filters and LOCATE; the chevron folds it to its header. It lives
    /// in the map chrome layer (<see cref="MapSelectionDeck.ChromeLayer"/>); the STR legend wraps short of it (<see cref="WidthTaken"/>) and the
    /// game's grid readouts in that corner stand above it (<see cref="Reserve"/>). Read only and client-local; it redraws when the log moves.
    /// </summary>
    internal static class MfdFieldLog
    {
        private const float MaxWidth = 390f, HeadH = 26f, BodyH = 120f, Pad = 8f, Margin = 10f, Inset = 12f;
        private const int Lines = 8;

        /// <summary>Width taken from the viewport's right edge (inset and a gap included), for the STR legend to wrap short of.</summary>
        internal const float WidthTaken = MaxWidth + Inset + 12f;

        private static RectTransform root;
        private static AvFrame back;
        private static TMP_Text caption, body;
        private static AvControl fold;
        private static bool folded;
        private static int revision = int.MinValue;
        private static float width = -1f;
        private static readonly StringBuilder text = new StringBuilder(1024);

        /// <summary>Height taken from the viewport's bottom edge (with its margin), for the game's grid readouts to stand above. Known before
        /// the box is first built, so the layout pass that places the readouts on open already agrees.</summary>
        internal static float Reserve => Margin + Height;

        private static float Height => folded ? HeadH : HeadH + BodyH;

        /// <summary>Read-back for the sims.</summary>
        internal static bool Shown => root != null && root.gameObject.activeInHierarchy;
        internal static string BodyText => body != null ? body.text : null;

        internal static void Tick(DynamicMap map)
        {
            RectTransform viewport = map.mapBackground.rectTransform;
            if (root == null || root.parent == null || root.parent.parent != viewport) Build(MapSelectionDeck.ChromeLayer(viewport));
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            float w = Mathf.Min(MaxWidth, viewport.rect.width - 2f * Inset);
            if (Mathf.Abs(w - width) > 0.5f) { width = w; Apply(); revision = int.MinValue; }   // a new width refits the lines

            ITacticalLog log = ModuleServices.TryGet(out ITacticalLog found) ? found : null;
            int now = log == null ? int.MinValue + 1 : log.Revision;
            if (now == revision) return;
            revision = now;
            Fill(log);
        }

        private static void Fill(ITacticalLog log)
        {
            text.Length = 0;
            int total = log == null ? 0 : log.Entries.Count;
            float fitW = body.rectTransform.rect.width, fitH = BodyH - Pad;
            for (int i = 0; i < total && i < Lines; i++)
            {
                int mark = text.Length;
                if (i > 0) text.Append('\n');
                text.Append(LogLine.Rich(log.Entries[i]));
                // Whole lines only: the first one that would run past the box goes (TMP's truncate cuts mid-word).
                if (i > 0 && body.GetPreferredValues(text.ToString(), fitW, 0f).y > fitH) { text.Length = mark; break; }
            }
            if (total == 0)
                text.Append("<color=#").Append(LogLine.StampHex).Append(">")
                    .Append(log == null ? "AVIONICS OFF · NO GAME LOG" : "NO LINES YET · KILLS, CAPTURES, CHAT, THE RADIO AND THE WIRE FILE HERE").Append("</color>");
            body.text = text.ToString();
            AvText.Set(caption, AvNum.Fixed(total, 0) + " LINES · NEWEST FIRST");
        }

        private static void Build(RectTransform layer)
        {
            Reset();
            root = AvLay.Child(layer, "NOAvionics.FieldLog");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
            root.anchoredPosition = new Vector2(-Inset, Margin);
            back = MfdChromeLay.Panel(root, "Back", new Rect(0f, 0f, MaxWidth, Height),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.92f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(.9f), AvChamfer.Diagonal(5f));
            back.raycastTarget = true;   // the pointer guard keeps wheel and drag off the map behind it

            Color key = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            TMP_Text icon = AvIcons.Make(root, AvIcon.ListDetails, AvGridTokens.IconHead, key);
            MfdChromeLay.Place(icon.rectTransform, new Rect(Pad, -(HeadH - 14f) * 0.5f, 14f, 14f));
            TMP_Text title = Label("Title", AvTextRole.Head, "FIELD LOG", key);
            MfdChromeLay.Place(title.rectTransform, new Rect(Pad + 20f, 0f, 76f, HeadH));
            caption = Label("Caption", AvTextRole.Micro, "", AvStyleHost.FuiColor("ink-dim", AvTheme.Dim));

            fold = AvControl.Make(root, new AvControl.Spec("", Fold, AvButtonStyle.Quiet, AvIcon.ChevronDown));
            fold.Bracket = 0f;
            fold.Help = "Fold the field log to its header, or open it again.";

            body = AvText.Make(root, "Body", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            body.richText = true;
            body.raycastTarget = false;
            body.paragraphSpacing = 3f;
            body.overflowMode = TextOverflowModes.Masking;   // Fill keeps whole lines; this only guards a font swap mid-session

            MapUiPointer.Register(root);
            revision = int.MinValue;
            width = -1f;
        }

        private static TMP_Text Label(string name, AvTextRole role, string value, Color color)
        {
            TMP_Text t = AvText.Make(root, name, role, value, TextAlignmentOptions.MidlineLeft);
            AvText.Fit(t, false);
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Places everything for the current width and fold.</summary>
        private static void Apply()
        {
            if (root == null) return;
            float w = width > 0f ? width : MaxWidth, h = Height;
            root.sizeDelta = new Vector2(w, h);
            back.rectTransform.sizeDelta = new Vector2(w, h);
            float foldX = w - Pad - 22f;
            MfdChromeLay.Place(fold.Rect, new Rect(foldX, -3f, 22f, 20f));
            float captionX = Pad + 100f;
            MfdChromeLay.Place(caption.rectTransform, new Rect(captionX, 0f, Mathf.Max(0f, foldX - 6f - captionX), HeadH));
            MfdChromeLay.Place(body.rectTransform, new Rect(Pad + 2f, -HeadH, w - 2f * Pad - 2f, BodyH - Pad));
            body.gameObject.SetActive(!folded);
            fold.SetIcon(folded ? AvIcon.ChevronUp : AvIcon.ChevronDown);
        }

        private static void Fold()
        {
            folded = !folded;
            Apply();
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null) Patches.GridLabelsPatch.RepositionCornerReadouts(map.gridLabels);
        }

        /// <summary>Map closed: the viewport goes back into the cockpit's small map; the box hides and is kept for the next open.</summary>
        internal static void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        /// <summary>Scene reset: the box goes (the fold is kept until the game closes).</summary>
        internal static void Reset()
        {
            if (root != null)
            {
                MapUiPointer.Unregister(root);
                Object.Destroy(root.gameObject);
            }
            root = null;
            back = null;
            caption = body = null;
            fold = null;
            revision = int.MinValue;
            width = -1f;
        }
    }
}
