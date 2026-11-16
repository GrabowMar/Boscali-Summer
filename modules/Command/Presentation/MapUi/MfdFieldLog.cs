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
    /// FIELD LOG above the left MFD panel, sharing the column's width and layout authority. The newest lines of
    /// <see cref="ITacticalLog"/> use <see cref="LogLine"/>; the chevron returns the body's height to the panel.
    /// Folding the column hides the log too, leaving the whole map clear. Read only and client-local; text redraws on a log or width change.
    /// </summary>
    internal static class MfdFieldLog
    {
        private const float HeadH = 32f, BodyH = 120f, Pad = 8f;
        private const int Lines = 8;

        private static RectTransform root;
        private static AvFrame back;
        private static TMP_Text caption, body;
        private static AvControl fold;
        private static bool folded;
        private static int revision = int.MinValue;
        private static float width = -1f;
        private static readonly StringBuilder text = new StringBuilder(1024);

        /// <summary>Known before construction, so the log and dock reserve the same space on first open.</summary>
        internal static float Height => folded ? HeadH : HeadH + BodyH;

        /// <summary>Read-back for the sims.</summary>
        internal static bool Shown => root != null && root.gameObject.activeInHierarchy;
        internal static string BodyText => body != null ? body.text : null;

        /// <summary>The dock places both parts of the left bay in one layout pass.</summary>
        internal static void Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null || columns.Log.width <= 0f) { Hide(); return; }
            if (root == null || root.parent != canvas.transform) Build((RectTransform)canvas.transform);
            root.anchoredPosition = MfdLayout.TopLeftOf(columns.Log);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            float w = columns.Log.width;
            if (Mathf.Abs(w - width) > 0.5f) { width = w; Apply(); revision = int.MinValue; }   // a new width refits the lines
        }

        internal static void Tick(DynamicMap map)
        {
            if (!MfdRailPatch.IsApplied || MfdColumnFold.Folded) { Hide(); return; }
            if (!Shown || folded) return;

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
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.pivot = new Vector2(0f, 1f);
            back = MfdChromeLay.Panel(root, "Back", new Rect(0f, 0f, AvTokens.PanelWidth, Height),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.92f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(.9f), AvChamfer.Diagonal(5f));
            back.raycastTarget = true;   // the pointer guard keeps wheel and drag off the map behind it

            Color key = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            TMP_Text icon = AvIcons.Make(root, AvIcon.ListDetails, AvGridTokens.IconHead, key);
            MfdChromeLay.Place(icon.rectTransform, new Rect(Pad, -(HeadH - 14f) * 0.5f, 14f, 14f));
            TMP_Text title = Label("Title", AvTextRole.Head, "FIELD LOG", AvInk.Ink);
            MfdChromeLay.Place(title.rectTransform, new Rect(Pad + 20f, 0f, 76f, HeadH));
            caption = Label("Caption", AvTextRole.Micro, "", AvStyleHost.FuiColor("ink-dim", AvTheme.Dim));

            fold = AvControl.Make(root, new AvControl.Spec("", Fold, AvButtonStyle.Quiet, AvIcon.ChevronUp));
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
            float w = width > 0f ? width : AvTokens.PanelWidth, h = Height;
            root.sizeDelta = new Vector2(w, h);
            back.rectTransform.sizeDelta = new Vector2(w, h);
            float foldX = w - HeadH;
            MfdChromeLay.Place(fold.Rect, new Rect(foldX, 0f, HeadH, HeadH));
            float captionX = Pad + 100f;
            MfdChromeLay.Place(caption.rectTransform, new Rect(captionX, 0f, Mathf.Max(0f, foldX - 6f - captionX), HeadH));
            MfdChromeLay.Place(body.rectTransform, new Rect(Pad + 2f, -HeadH, w - 2f * Pad - 2f, BodyH - Pad));
            body.gameObject.SetActive(!folded);
            if (folded) AvText.Set(caption, "EXPAND TO READ");
            fold.SetIcon(folded ? AvIcon.ChevronDown : AvIcon.ChevronUp);
        }

        private static void Fold()
        {
            folded = !folded;
            Apply();
            revision = int.MinValue;
            MfdRailPatch.ReLayoutNow();
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
