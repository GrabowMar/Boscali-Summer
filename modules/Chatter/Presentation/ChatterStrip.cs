using System.Text;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Chatter.Presentation
{
    /// <summary>
    /// The radio on the glass: a COMMS block in the left column of the screen, under the game's event log and in its look (a caption shelf, a bracket down the left
    /// edge, a soft readability shade). The shelf names the net and frequency of the line being spoken; each row is the net's tag, the callsign and the words, typed on as they are
    /// spoken and wrapped when long. The net colours a row, an emergency is amber, intercepted enemy traffic is red and set in guillemets. A row stays while it is spoken
    /// and a little after, then fades; older rows sit back so the live one reads first. Nothing here takes input.
    /// </summary>
    internal sealed class ChatterStrip
    {
        private const int Rows = 5;
        private const float Left = LeftHudStack.Left, Width = LeftHudStack.Width, Indent = LeftHudStack.Inset, ShelfHeight = 24f, RowGap = 8f, MaxHeight = 264f;
        private const float LingerSeconds = 4f, FadeSeconds = 0.7f, CharsPerSecond = 55f;

        private struct Row
        {
            public TextMeshProUGUI Text;
            public float Shown, Until, Height;
            public int Head;
            public ChatterNet Net;
            public ChatterPriority Priority;
            public bool Scripted;
        }

        private readonly AvHudHost host;
        private readonly Row[] rows = new Row[Rows];
        private readonly TextMeshProUGUI shelf, channel;
        private readonly RectTransform shelfLead, shelfLine, shelfEnd, bracket;
        private readonly StringBuilder sb = new StringBuilder(320);
        private int used;
        private TMP_FontAsset face;
        private Material faceMaterial;

        public ChatterStrip(Transform parent)
        {
            host = AvHudHost.Create("Boscali / Chatter", parent, 3);
            shelf = Label("Shelf", LeftHudStack.HeaderSize);
            channel = Label("Channel", LeftHudStack.HeaderSize);
            channel.alignment = TextAlignmentOptions.TopRight;
            shelfLead = Bar("ShelfLead");
            shelfLine = Bar("ShelfLine");
            shelfEnd = Bar("ShelfEnd");
            bracket = Bar("Bracket");
            for (int i = 0; i < Rows; i++)
            {
                TextMeshProUGUI text = Label("Row" + i, LeftHudStack.BodySize);
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Truncate;
                rows[i].Text = text;
            }
            RefreshFace();
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
            text.raycastTarget = false;
            text.enableKerning = false;
            text.richText = true;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = false;
            text.text = "";
            return text;
        }

        private RectTransform Bar(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(host.Static, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        public void Show(in ChatterAired line)
        {
            RefreshFace();
            if (used == Rows)
            {
                int oldest = Oldest();
                ChatterPriority priority = line.Scripted ? ChatterPriority.Tactical : line.Priority;
                if (priority < Priority(rows[oldest]) || (priority == Priority(rows[oldest]) && !line.Scripted && rows[oldest].Scripted)) return;
                Drop(oldest);
            }
            bool enemy = line.Net == ChatterNet.Enemy, emergency = line.Priority == ChatterPriority.Emergency;
            string tag = Tag(line.Net), who = Plain(line.Speaker).ToUpperInvariant();
            string heading = tag + "  " + who + (string.IsNullOrEmpty(line.Role) ? "" : " / " + Plain(line.Role).ToUpperInvariant());
            sb.Clear();
            sb.Append("<color=#").Append(Colour(line.Net)).Append("><size=80%>").Append(heading).Append("</size></color>\n");
            sb.Append("<color=#").Append(emergency ? "FFE66B" : Words(line.Net)).Append('>');
            if (enemy) sb.Append("<< ");
            sb.Append(Plain(line.Text));
            if (enemy) sb.Append(" >>");
            sb.Append("</color>");

            ref Row row = ref rows[used++];
            string text = sb.ToString();
            row.Text.text = text;
            row.Head = heading.Length + 1;
            row.Text.maxVisibleCharacters = row.Head;
            row.Height = Mathf.Max(36f, row.Text.GetPreferredValues(text, Width - Indent - 8f, 0f).y + 2f);
            row.Scripted = line.Scripted;
            row.Priority = line.Priority;
            row.Shown = Clock(row);
            row.Until = row.Shown + line.Seconds + (line.Scripted ? 0f : LingerSeconds);
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

        private void DropOldest() => Drop(Oldest());

        private int Oldest()
        {
            // Shed routine traffic before dialogue, and dialogue before an emergency.
            int oldest = 0;
            for (int i = 1; i < used; i++)
                if (Priority(rows[i]) < Priority(rows[oldest]) ||
                    (Priority(rows[i]) == Priority(rows[oldest]) && !rows[i].Scripted && rows[oldest].Scripted)) oldest = i;
            return oldest;
        }

        public void Tick(bool flying)
        {
            // Rows use their own clock: a paused mission holds dialogue while normal radio fades in real time.
            for (int i = used - 1; i >= 0; i--)
                if (Clock(rows[i]) > rows[i].Until + FadeSeconds) Drop(i);
            bool visible = used > 0 && (flying || HasScripted());
            host.SetVisible(visible);
            if (!visible) { LeftHudStack.CommsBottom = 0f; return; }
            RefreshFace();
            float fit = Mathf.Max(0.1f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f));
            host.Refresh(Mathf.Max(0.85f, Screen.height / 1080f) / fit);
            float canvasHeight = Screen.height / Mathf.Max(0.1f, host.ScaleFactor);
            Paint(canvasHeight);
        }

        private void Paint(float canvasHeight)
        {
            float top = LeftHudStack.CommsTop;
            float y = canvasHeight - top;
            Color frame = VanillaHudStyle.Colours.AllClear;
            frame.a = 0.70f;

            ChatterNet live = rows[used - 1].Net;
            shelf.text = "COMMS";
            shelf.color = frame;
            channel.text = "<color=#" + Colour(live) + ">" + Tag(live) + " " + Frequency(live) + "</color>";
            AvHudHost.Place(shelf.rectTransform, Left + Indent, y - 16f, 60f, 20f);
            AvHudHost.Place(channel.rectTransform, Left + Width - 160f, y - 16f, 148f, 20f);
            SetBar(shelfLead, Left, y - 6f, 8f, 1f, frame);
            SetBar(shelfLine, Left + 56f, y - 6f, Width - 220f, 1f, frame);
            SetBar(shelfEnd, Left + Width - 2f, y - 8f, 4f, 4f, frame);

            float cursor = y - ShelfHeight - 4f;
            float bodyTop = cursor;
            for (int i = 0; i < used; i++)
            {
                ref Row row = ref rows[i];
                cursor -= row.Height;
                AvHudHost.Place(row.Text.rectTransform, Left + Indent, cursor, Width - Indent - 8f, row.Height);
                cursor -= RowGap;
                float now = Clock(row);
                row.Text.maxVisibleCharacters = row.Head + (int)((now - row.Shown) * CharsPerSecond);
                float alpha = now <= row.Until ? 1f : Mathf.Clamp01(1f - (now - row.Until) / FadeSeconds);
                row.Text.alpha = alpha * (row.Scripted || i == used - 1 ? 1f : 0.92f);
            }
            SetBar(bracket, Left + 1f, cursor + RowGap, 1f, bodyTop - cursor - RowGap, frame);
            LeftHudStack.CommsBottom = top + Height();
        }

        private static float Clock(in Row row) => row.Scripted ? Time.time : Time.unscaledTime;
        private static ChatterPriority Priority(in Row row) => row.Scripted ? ChatterPriority.Tactical : row.Priority;
        private bool HasScripted() { for (int i = 0; i < used; i++) if (rows[i].Scripted) return true; return false; }
        private static string Plain(string text) => (text ?? "").Replace('<', '‹').Replace('>', '›');

        private void Drop(int index)
        {
            TextMeshProUGUI recycled = rows[index].Text;
            recycled.text = "";
            for (int i = index + 1; i < used; i++) rows[i - 1] = rows[i];
            rows[--used] = new Row { Text = recycled };
        }

        private static void SetBar(RectTransform rect, float x, float y, float width, float height, Color colour)
        {
            AvHudHost.Place(rect, x, y, width, height);
            rect.GetComponent<Image>().color = colour;
        }

        private void RefreshFace()
        {
            TMP_FontAsset next = LeftHudStack.Font != null ? LeftHudStack.Font : AvType.Face(AvFace.Mono);
            Material material = LeftHudStack.FontMaterial;
            if (face == next && faceMaterial == material) return;
            face = next; faceMaterial = material;
            shelf.font = channel.font = face;
            shelf.fontSharedMaterial = channel.fontSharedMaterial = material != null ? material : face.material;
            shelf.fontSize = 100f;
            float advance = shelf.GetPreferredValues("MMMMMMMM", 0f, 0f).x / 8f / 100f;
            float scale = 0.65f / Mathf.Max(0.1f, advance);
            shelf.fontSize = channel.fontSize = LeftHudStack.HeaderSize * scale;
            for (int i = 0; i < Rows; i++)
            {
                TextMeshProUGUI text = rows[i].Text;
                text.font = face;
                text.fontSharedMaterial = material != null ? material : face.material;
                text.fontSize = LeftHudStack.BodySize * scale;
                if (i < used) rows[i].Height = Mathf.Max(36f, text.GetPreferredValues(text.text, Width - Indent - 8f, 0f).y + 2f);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Rows; i++) if (rows[i].Text != null) rows[i].Text.text = "";
            used = 0;
            LeftHudStack.CommsBottom = 0f;
            host.SetVisible(false);
        }

        public void ClearScripted()
        {
            for (int i = used - 1; i >= 0; i--) if (rows[i].Scripted) Drop(i);
            if (used == 0) Clear();
            else LeftHudStack.CommsBottom = LeftHudStack.CommsTop + Height();
        }

        public void Destroy() { Clear(); host.Destroy(); }

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
                default: return ColorUtility.ToHtmlStringRGB(VanillaHudStyle.Colours.AllClear);
            }
        }

        private static string Words(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Enemy: return "FFB4AC";
                default: return ColorUtility.ToHtmlStringRGB(Color.Lerp(VanillaHudStyle.Colours.AllClear, Color.white, 0.82f));
            }
        }
    }
}
