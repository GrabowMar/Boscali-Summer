using System.Text;
using BoscaliSummer.Core.Chatter;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Chatter.Presentation
{
    /// <summary>
    /// The radio on the glass: a COMMS block in the left column of the screen, under the game's event log and in its look (a caption shelf, a bracket down the left
    /// edge, no plate). The shelf names the net and frequency of the line being spoken; each row is the net's tag, the callsign and the words, typed on as they are
    /// spoken and wrapped when long. The net colours a row, an emergency is amber, intercepted enemy traffic is red and set in guillemets. A row stays while it is spoken
    /// and a little after, then fades; older rows sit back so the live one reads first. Nothing here takes input.
    /// </summary>
    internal sealed class ChatterStrip
    {
        private const int Rows = 5;
        private const float Left = 12f, Top = 132f, Width = 640f, Indent = 10f, ShelfHeight = 20f, RowGap = 3f, MaxHeight = 250f;
        private const float LingerSeconds = 4f, FadeSeconds = 0.7f, CharsPerSecond = 55f;
        private static readonly Color Frame = new Color32(0x5C, 0xFF, 0x8A, 0xD0);

        private struct Row
        {
            public TextMeshProUGUI Text;
            public float Shown, Until, Height;
            public int Head;
            public ChatterNet Net;
        }

        private readonly AvHudHost host;
        private readonly Row[] rows = new Row[Rows];
        private readonly TextMeshProUGUI shelf;
        private readonly RectTransform shelfLine, bracket;
        private readonly StringBuilder sb = new StringBuilder(320);
        private int used;

        public ChatterStrip(Transform parent)
        {
            host = AvHudHost.Create("Boscali / Chatter", parent, 3);
            shelf = Label("Shelf", 13f);
            shelf.color = Frame;
            shelf.characterSpacing = 6f;
            shelfLine = Bar("ShelfLine");
            bracket = Bar("Bracket");
            for (int i = 0; i < Rows; i++)
            {
                TextMeshProUGUI text = Label("Row" + i, 18f);
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Truncate;
                rows[i].Text = text;
            }
            host.SetVisible(false);
        }

        private TextMeshProUGUI Label(string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(host.Data, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            AvType.Apply(text, AvTextRole.DataStrong);
            text.characterSpacing = 0f;
            text.fontSize = size;
            text.fontStyle = FontStyles.Normal;
            text.richText = true;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = false;
            text.outlineWidth = 0.26f;
            text.outlineColor = new Color32(0, 0, 0, 220);
            text.text = "";
            return text;
        }

        private RectTransform Bar(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(host.Static, false);
            var image = go.AddComponent<Image>();
            image.color = Frame;
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        public void Show(in ChatterAired line)
        {
            if (used == Rows) DropOldest();
            bool enemy = line.Net == ChatterNet.Enemy, emergency = line.Priority == ChatterPriority.Emergency;
            string tag = Tag(line.Net), who = string.IsNullOrEmpty(line.Speaker) ? "" : line.Speaker.ToUpperInvariant();
            sb.Clear();
            sb.Append("<color=#").Append(Colour(line.Net)).Append("><size=72%>").Append(tag).Append("</size> ").Append(who).Append("</color>  ");
            sb.Append("<color=#").Append(emergency ? "FFE66B" : Words(line.Net)).Append('>');
            if (enemy) sb.Append("<< ");
            sb.Append("<noparse>").Append(line.Text).Append("</noparse>");
            if (enemy) sb.Append(" >>");
            sb.Append("</color>");

            ref Row row = ref rows[used++];
            string text = sb.ToString();
            row.Text.text = text;
            row.Head = tag.Length + who.Length + 3;
            row.Text.maxVisibleCharacters = row.Head;
            row.Height = Mathf.Max(22f, row.Text.GetPreferredValues(text, Width - Indent, 0f).y);
            row.Shown = Time.unscaledTime;
            row.Until = Time.unscaledTime + line.Seconds + LingerSeconds;
            row.Net = line.Net;
            // The block never grows past its box: the oldest lines leave first.
            while (used > 1 && Height() > MaxHeight) DropOldest();
        }

        private float Height()
        {
            float h = ShelfHeight;
            for (int i = 0; i < used; i++) h += rows[i].Height + RowGap;
            return h;
        }

        private void DropOldest()
        {
            TextMeshProUGUI recycled = rows[0].Text;
            recycled.text = "";
            for (int i = 1; i < Rows; i++) rows[i - 1] = rows[i];
            rows[Rows - 1] = new Row { Text = recycled };
            used--;
        }

        public void Tick(bool flying)
        {
            float now = Time.unscaledTime;
            while (used > 0 && now > rows[0].Until + FadeSeconds) DropOldest();
            bool visible = flying && used > 0;
            host.SetVisible(visible);
            if (!visible) return;
            host.Refresh(1f);
            float canvasHeight = Screen.height / Mathf.Max(0.1f, host.ScaleFactor);
            float y = canvasHeight - Top;                       // the block's top edge, from the bottom of the canvas

            ChatterNet live = rows[used - 1].Net;
            shelf.text = "COMMS  <color=#" + Colour(live) + ">" + Tag(live) + " " + Frequency(live) + "</color>";
            AvHudHost.Place(shelf.rectTransform, Left + 4f, y - ShelfHeight + 2f, Width, ShelfHeight);
            AvHudHost.Place(shelfLine, Left, y - ShelfHeight, 190f, 2f);

            float cursor = y - ShelfHeight - 4f;
            float bodyTop = cursor;
            for (int i = 0; i < used; i++)
            {
                ref Row row = ref rows[i];
                cursor -= row.Height;
                AvHudHost.Place(row.Text.rectTransform, Left + Indent, cursor, Width - Indent, row.Height);
                cursor -= RowGap;
                row.Text.maxVisibleCharacters = row.Head + (int)((now - row.Shown) * CharsPerSecond);
                float alpha = now <= row.Until ? 1f : Mathf.Clamp01(1f - (now - row.Until) / FadeSeconds);
                row.Text.alpha = alpha * (i == used - 1 ? 1f : 0.82f);
            }
            AvHudHost.Place(bracket, Left, cursor + RowGap, 2f, bodyTop - cursor - RowGap);
        }

        public void Clear()
        {
            for (int i = 0; i < Rows; i++) if (rows[i].Text != null) rows[i].Text.text = "";
            used = 0;
            host.SetVisible(false);
        }

        public void Destroy() => host.Destroy();

        public static string Tag(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Awacs: return "AWACS";
                case ChatterNet.Tower: return "TWR";
                case ChatterNet.Command: return "CMD";
                case ChatterNet.Enemy: return "SIGINT";
                default: return "FLT";
            }
        }

        /// <summary>The channel each net is heard on: set dressing, the same every time so it reads as a place.</summary>
        private static string Frequency(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Awacs: return "364.200";
                case ChatterNet.Tower: return "118.700";
                case ChatterNet.Command: return "30.550 FM";
                case ChatterNet.Enemy: return "9.4 GHz";
                default: return "251.000";
            }
        }

        private static string Colour(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Awacs: return "8FD4FF";
                case ChatterNet.Tower: return "FFCF70";
                case ChatterNet.Command: return "E6EDF2";
                case ChatterNet.Enemy: return "FF5A4E";
                default: return "5CFF8A";
            }
        }

        private static string Words(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Awacs: return "D6EEFF";
                case ChatterNet.Tower: return "FFE9BF";
                case ChatterNet.Command: return "F4F7F9";
                case ChatterNet.Enemy: return "FFB4AC";
                default: return "A8FFC4";
            }
        }
    }
}
