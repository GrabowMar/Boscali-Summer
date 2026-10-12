using System.Text;
using BoscaliSummer.Core.Chatter;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Chatter.Presentation
{
    /// <summary>
    /// The radio on the glass: the last few lines that went on air, low in the middle of the screen, no plate behind them. Each row is the net's tag, the callsign and
    /// the words, typed on as they are spoken; the net colours the tag and the callsign, an emergency colours the words too, intercepted enemy traffic is set in guillemets.
    /// A row stays while it is spoken and a little after, then fades. Nothing here takes input.
    /// </summary>
    internal sealed class ChatterStrip
    {
        private const int Rows = 4;
        private const float Width = 1180f, RowHeight = 30f, Bottom = 170f;
        private const float LingerSeconds = 3.5f, FadeSeconds = 0.7f, CharsPerSecond = 55f;

        private struct Row
        {
            public TextMeshProUGUI Text;
            public float Shown, Until;
            public int Head;
        }

        private readonly AvHudHost host;
        private readonly Row[] rows = new Row[Rows];
        private readonly StringBuilder sb = new StringBuilder(320);
        private int used;

        public ChatterStrip(Transform parent)
        {
            host = AvHudHost.Create("Boscali / Chatter", parent, 3);
            for (int i = 0; i < Rows; i++)
            {
                var go = new GameObject("Row" + i, typeof(RectTransform));
                go.transform.SetParent(host.Data, false);
                var text = go.AddComponent<TextMeshProUGUI>();
                AvType.Apply(text, AvTextRole.Data);
                text.fontSize = 21f;
                text.fontStyle = FontStyles.Normal;
                text.richText = true;
                text.alignment = TextAlignmentOptions.BottomLeft;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.outlineWidth = 0.22f;
                text.outlineColor = new Color32(0, 0, 0, 220);
                text.text = "";
                rows[i].Text = text;
            }
            host.SetVisible(false);
        }

        public void Show(in ChatterAired line)
        {
            // Newest at the bottom: everything moves up one row, the oldest falls off.
            if (used == Rows)
            {
                TextMeshProUGUI recycled = rows[0].Text;
                for (int i = 1; i < Rows; i++) rows[i - 1] = rows[i];
                rows[Rows - 1] = new Row { Text = recycled };
                used--;
            }
            string net = Colour(line.Net);
            sb.Clear();
            sb.Append("<color=#").Append(net).Append("><size=70%>").Append(Tag(line.Net)).Append("</size>  ");
            int head = Tag(line.Net).Length + 2;
            string who = string.IsNullOrEmpty(line.Speaker) ? "" : line.Speaker.ToUpperInvariant();
            sb.Append(who).Append("</color>  ");
            head += who.Length + 2;
            bool enemy = line.Net == ChatterNet.Enemy;
            if (line.Priority == ChatterPriority.Emergency) sb.Append("<color=#FFE66B>");
            else if (enemy) sb.Append("<color=#FFB4AC>");
            if (enemy) sb.Append("<< ");
            sb.Append("<noparse>").Append(line.Text).Append("</noparse>");
            if (enemy) sb.Append(" >>");
            if (line.Priority == ChatterPriority.Emergency || enemy) sb.Append("</color>");

            ref Row row = ref rows[used++];
            row.Text.text = sb.ToString();
            row.Text.maxVisibleCharacters = head;
            row.Head = head;
            row.Shown = Time.unscaledTime;
            row.Until = Time.unscaledTime + line.Seconds + LingerSeconds;
        }

        public void Tick(bool flying)
        {
            float now = Time.unscaledTime;
            while (used > 0 && now > rows[0].Until + FadeSeconds)
            {
                TextMeshProUGUI recycled = rows[0].Text;
                recycled.text = "";
                for (int i = 1; i < Rows; i++) rows[i - 1] = rows[i];
                rows[Rows - 1] = new Row { Text = recycled };
                used--;
            }
            bool visible = flying && used > 0;
            host.SetVisible(visible);
            if (!visible) return;
            host.Refresh(1f);
            float canvasWidth = Screen.width / Mathf.Max(0.1f, host.ScaleFactor);
            float x = (canvasWidth - Width) * 0.5f;
            for (int i = 0; i < used; i++)
            {
                ref Row row = ref rows[i];
                AvHudHost.Place(row.Text.rectTransform, x, Bottom + (used - 1 - i) * RowHeight, Width, RowHeight);
                row.Text.maxVisibleCharacters = row.Head + (int)((now - row.Shown) * CharsPerSecond);
                float alpha = now <= row.Until ? 1f : Mathf.Clamp01(1f - (now - row.Until) / FadeSeconds);
                // Older rows sit back a little so the line being spoken reads first.
                row.Text.alpha = alpha * (i == used - 1 ? 1f : 0.72f);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Rows; i++) if (rows[i].Text != null) rows[i].Text.text = "";
            used = 0;
            host.SetVisible(false);
        }

        public void Destroy() => host.Destroy();

        private static string Tag(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Awacs: return "AWACS";
                case ChatterNet.Tower: return "TWR";
                case ChatterNet.Command: return "CMD";
                case ChatterNet.Enemy: return "INTCPT";
                default: return "FLT";
            }
        }

        private static string Colour(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Awacs: return "9AD8FF";
                case ChatterNet.Tower: return "FFD27A";
                case ChatterNet.Command: return "D7E0E8";
                case ChatterNet.Enemy: return "FF5A4E";
                default: return "7CFFB2";
            }
        }
    }
}
