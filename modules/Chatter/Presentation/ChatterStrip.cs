using System.Text;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Chatter.Presentation
{
    /// <summary>
    /// The radio on the glass: a COMMS block in the left column of the screen, under the game's event log and in its row grammar (a short caption shelf, a 3 x 12 tick
    /// at the left of every row, the words 12 in, a soft readability shade). The shelf names the net and frequency of the line being spoken; each row is the net's tag, the callsign and the words in capitals, typed on as they are
    /// spoken and wrapped when long. The tick says which row: BRIGHT for the newest, DIM for older ones, AMBER for an emergency or intercepted enemy traffic (SIGINT, set in guillemets). There is no red on this block. A row stays while it is spoken
    /// and a little after, then fades; older rows sit back so the live one reads first. Nothing here takes input.
    /// </summary>
    internal sealed class ChatterStrip
    {
        private const int Rows = 5;
        /// <summary>The column's width on this screen (HudZones.ColumnWidth): 562 at 16:9, less on a narrower one; the lines wrap to it.</summary>
        private float width = HudZones.WideLog;
        private float Width => width;
        private const float Left = LeftHudStack.Left, Indent = LeftHudStack.Inset, ShelfHeight = 24f, RowGap = 8f, MaxHeight = HudZones.CommsHeight;
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

        // The surface: a holder inside the HUD layer's Data root (HudLayer on), else the old overlay host (A/B switch off or no HUD canvas yet).
        private AvHudHost host;
        private GameObject holderGo;
        private RectTransform holder, dataRoot, staticRoot;
        private Canvas holderCanvas;
        private CanvasGroup holderGroup;
        private bool inLayer, shown, subscribed;
        private float ownScale = 1f;
        private readonly Row[] rows = new Row[Rows];
        private readonly TextMeshProUGUI shelf, channel, prompt;
        private readonly RectTransform shelfLine, promptTrack, promptBar;
        private readonly RectTransform[] ticks = new RectTransform[Rows];
        /// <summary>The net the shelf's flag was last written for (-1: none yet): the flag is rewritten only when the net changes.</summary>
        private int paintedNet = -1;
        private const float PromptHeight = 34f;
        private bool promptOn;
        private float promptFill;
        private readonly StringBuilder sb = new StringBuilder(320);
        private int used;
        private TMP_FontAsset face;
        private Material faceMaterial;

        public ChatterStrip(Transform parent)
        {
            this.parent = parent;
            Mount();
            shelf = Label("Shelf", LeftHudStack.HeaderSize);
            shelf.fontStyle = FontStyles.Bold;      // caption family: 12 bold DIM like the board's shelves
            shelf.text = "COMMS";
            shelf.color = VanillaHudStyle.InkDim;
            channel = Label("Channel", LeftHudStack.HeaderSize);
            channel.alignment = TextAlignmentOptions.TopRight;
            channel.color = VanillaHudStyle.InkDim;
            shelfLine = Bar("ShelfLine");
            for (int i = 0; i < Rows; i++) ticks[i] = Bar("Tick" + i);
            prompt = Label("Prompt", LeftHudStack.BodySize);
            promptTrack = Bar("PromptTrack");
            promptBar = Bar("PromptBar");
            for (int i = 0; i < Rows; i++)
            {
                TextMeshProUGUI text = Label("Row" + i, LeftHudStack.BodySize);
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Truncate;
                rows[i].Text = text;
            }
            RefreshFace();
            SetVisible(false);
            subscribed = true;
            HudLayer.Paint += OnPaint;
        }

        private readonly Transform parent;
        private float ScaleFactor => ownScale;

        private RectTransform DataRoot => host != null ? host.Data : dataRoot;
        private RectTransform StaticRoot => host != null ? host.Static : staticRoot;

        private void SetVisible(bool visible)
        {
            if (host != null) host.SetVisible(visible);
            else if (holderCanvas != null && holderCanvas.enabled != visible) holderCanvas.enabled = visible;
        }

        private float Alpha { set { CanvasGroup g = host != null ? host.Group : holderGroup; if (g != null && g.alpha != value) g.alpha = value; } }

        /// <summary>Keeps the surface in the mode the A/B switch wants; on a change the labels and bars move to the new one.</summary>
        private void Mount()
        {
            bool want = HudLayer.Enabled && HudLayer.Ensure();
            if (want == inLayer && (host != null || holder != null)) return;
            inLayer = want;
            host?.Destroy();
            host = null;
            if (holderGo != null) Object.Destroy(holderGo);
            holderGo = null; holder = dataRoot = staticRoot = null;
            holderCanvas = null; holderGroup = null;
            if (want)
            {
                holderGo = new GameObject("Boscali / Chatter", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
                holder = (RectTransform)holderGo.transform;
                holder.SetParent(HudLayer.Data, false);
                holder.anchorMin = holder.anchorMax = holder.pivot = Vector2.zero;
                holderCanvas = holderGo.GetComponent<Canvas>();
                holderCanvas.overrideSorting = false;
                holderGroup = holderGo.GetComponent<CanvasGroup>();
                holderGroup.blocksRaycasts = false;
                holderGroup.interactable = false;
                staticRoot = Layer("Static");
                dataRoot = Layer("Data");
            }
            else host = AvHudHost.Create("Boscali / Chatter", parent, 3);
            if (shelf == null) return;      // the first mount: the labels are built next
            Move(shelf); Move(channel); Move(prompt);
            for (int i = 0; i < Rows; i++) Move(rows[i].Text);
            Move(shelfLine, true); Move(promptTrack, true); Move(promptBar, true);
            for (int i = 0; i < Rows; i++) Move(ticks[i], true);
            SetVisible(false);
        }

        private RectTransform Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.SetParent(holder, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Canvas>().overrideSorting = false;
            return rt;
        }

        private void Move(TextMeshProUGUI text) => text.rectTransform.SetParent(DataRoot, false);
        private void Move(RectTransform rect, bool bar) => rect.SetParent(StaticRoot, false);

        private TextMeshProUGUI Label(string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(DataRoot, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            AvType.Apply(text, AvTextRole.DataStrong);
            text.characterSpacing = HudType.Tracking(HudType.StepOf(size));
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
            go.transform.SetParent(StaticRoot, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        public void Show(in ChatterAired line)
        {
            RefreshFace();
            if (used == Rows)
            {
                int victim = Victim(used);
                if (ChatterShed.Rank(line.Scripted, line.Priority) < Rank(rows[victim])) return;
                Drop(victim);
            }
            bool enemy = line.Net == ChatterNet.Enemy, emergency = line.Priority == ChatterPriority.Emergency;
            string tag = Tag(line.Net), who = Plain(line.Speaker).ToUpperInvariant();
            string heading = tag + "  " + who + (string.IsNullOrEmpty(line.Role) ? "" : " / " + Plain(line.Role).ToUpperInvariant());
            // Head: the net tag bold (AMBER for SIGINT, else DIM), the callsign and role DIM, a little under the words' size. Words: BODY capitals, INK, AMBER for an emergency or the enemy's.
            sb.Clear();
            sb.Append("<size=86%><b><color=#").Append(enemy ? Hex(VanillaHudStyle.Caution) : Hex(VanillaHudStyle.InkDim)).Append('>').Append(tag).Append("</color></b>  <color=#").Append(Hex(VanillaHudStyle.InkDim)).Append('>').Append(heading, tag.Length + 2, heading.Length - tag.Length - 2).Append("</color></size>\n");
            sb.Append("<color=#").Append(emergency || enemy ? Hex(VanillaHudStyle.Caution) : Hex(VanillaHudStyle.Ink)).Append('>');
            if (enemy) sb.Append("« ");
            sb.Append(Plain(line.Text).ToUpperInvariant());
            if (enemy) sb.Append(" »");
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

        /// <summary>
        /// The YES/NO prompt under the rows (2026-11-15): while a question is open, YES and NO with their keys and a bar that drains over the answer window; for a moment
        /// after it closes, the answer that was given (YES, NO or NO ANSWER). Words and colour both carry the state.
        /// </summary>
        public void Question(bool open, float left, float window, string yesKey, string noKey, string echo)
        {
            promptOn = open || !string.IsNullOrEmpty(echo);
            if (!promptOn) { prompt.text = ""; return; }
            promptFill = open && window > 0f ? Mathf.Clamp01(left / window) : 0f;
            while (used > 1 && Height() > MaxHeight) DropOldest();      // the prompt row counts against the zone too: the oldest lines make room for it
            sb.Clear();
            if (open)
            {
                // The choice is INK-HI (YES) and INK (NO), the keys INK-DIM at LABEL: no colour of its own (HudStandard).
                sb.Append("<color=#").Append(Hex(VanillaHudStyle.InkHi)).Append(">YES</color> <color=#").Append(Hex(VanillaHudStyle.InkDim)).Append("><size=86%>[").Append(Plain(yesKey)).Append("]</size></color>     ")
                  .Append("<color=#").Append(Hex(VanillaHudStyle.Ink)).Append(">NO</color> <color=#").Append(Hex(VanillaHudStyle.InkDim)).Append("><size=86%>[").Append(Plain(noKey)).Append("]</size></color>");
            }
            else
                sb.Append("<color=#").Append(Hex(VanillaHudStyle.InkDim)).Append("><size=86%>ANSWER</size></color>  <color=#")
                  .Append(Hex(echo == "YES" ? VanillaHudStyle.InkHi : echo == "NO" ? VanillaHudStyle.Ink : VanillaHudStyle.InkDim)).Append('>').Append(echo).Append("</color>");
            prompt.text = sb.ToString();
        }

        private float Height()
        {
            float h = ShelfHeight + (promptOn ? PromptHeight : 0f);
            for (int i = 0; i < used; i++) h += rows[i].Height + RowGap;
            return h;
        }

        /// <summary>Make room: shed the lowest-ranked row that is not the newest (<see cref="ChatterShed"/>).</summary>
        private void DropOldest() => Drop(Victim(used - 1));

        /// <summary>The row to shed among the first <paramref name="count"/>: routine traffic by priority before any scripted line, the oldest among equals.</summary>
        private int Victim(int count)
        {
            int victim = 0;
            for (int i = 1; i < count; i++)
                if (Rank(rows[i]) < Rank(rows[victim])) victim = i;
            return victim;
        }

        public void Tick(bool flying)
        {
            // Rows use their own clock: a paused mission holds dialogue while normal radio fades in real time.
            for (int i = used - 1; i >= 0; i--)
                if (Clock(rows[i]) > rows[i].Until + FadeSeconds) Drop(i);
            // The maximised map owns the screen (its left host slot sits where this column is): stand down under it; rows keep their clocks.
            bool visible = used > 0 && !DynamicMap.mapMaximized && (flying || HasScripted());
            Mount();
            shown = visible;
            if (!visible) { SetVisible(false); LeftHudStack.CommsBottom = 0f; return; }
            // The layer's late pass lays the block out on the rendered pose (OnPaint); the old path does it here.
            if (!inLayer) Layout(default);
        }

        private void OnPaint(HudView view)
        {
            if (!inLayer || !HudLayer.Enabled || !shown || holder == null || !view.Valid) return;
            Layout(view);
        }

        private void Layout(HudView view)
        {
            SetVisible(true);
            Alpha = LeftHudStack.BootAlpha;      // the visor's power-on brings the column up with the rest
            RefreshFace();
            float fit = Mathf.Max(0.1f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f));
            float step = Mathf.Max(0.85f, Screen.height / 1080f) / fit;
            if (host != null) { host.Refresh(step); ownScale = host.ScaleFactor; }
            else
            {
                // The holder keeps this block's own design scale inside the HUD canvas: its units stay the 1080 grid the layout is written in.
                ownScale = Mathf.Max(0.1f, fit * step);
                holder.localScale = Vector3.one * (ownScale / Mathf.Max(0.01f, view.Scale));
                holder.sizeDelta = new Vector2(Screen.width / ownScale, Screen.height / ownScale);
                holder.anchoredPosition = new Vector2(LeftHudStack.ShiftX, LeftHudStack.ShiftY);
            }
            // The 1080-high reference width, as the event log uses: the host's scale has a 0.85 floor, so dividing by it disagrees below 918 px of height.
            float column = HudZones.ColumnWidth(Screen.width / (float)Mathf.Max(1, Screen.height) * 1080f);
            if (column != width) { width = column; Rewrap(); }
            float canvasHeight = Screen.height / Mathf.Max(0.1f, ScaleFactor);
            Paint(canvasHeight);
        }

        private void Paint(float canvasHeight)
        {
            // The game's own feed is up in our log's place (free camera hides the HUD canvas the log lives on): start under it.
            float top = Mathf.Max(LeftHudStack.CommsTop, LeftHudStack.NativeFeedBottomPx > 0f ? LeftHudStack.NativeFeedBottomPx / Mathf.Max(0.1f, ScaleFactor) + 16f : 0f);
            float y = canvasHeight - top;
            Color frame = VanillaHudStyle.Ink;
            frame.a = HudInk.LineMid;

            ChatterNet live = rows[used - 1].Net;
            if ((int)live != paintedNet)
            {
                paintedNet = (int)live;
                channel.text = Tag(live) + " " + Frequency(live);      // the flag is written when the net changes, not every frame
            }
            // The HUD's one section head (ScopeCanvas.CaptionShelf): caption left, flag right, one hairline under both; no lead or end marks.
            AvHudHost.Place(shelf.rectTransform, Left, y - 16f, 60f, 20f);
            AvHudHost.Place(channel.rectTransform, Left + Width - 148f, y - 16f, 148f, 20f);
            SetBar(shelfLine, Left, y - 15f, Width, 1f, frame);

            float cursor = y - ShelfHeight - 4f;
            for (int i = 0; i < used; i++)
            {
                ref Row row = ref rows[i];
                cursor -= row.Height;
                AvHudHost.Place(row.Text.rectTransform, Left + Indent, cursor, Width - Indent - 8f, row.Height);
                cursor -= RowGap;
                float now = Clock(row);
                row.Text.maxVisibleCharacters = row.Head + (int)((now - row.Shown) * CharsPerSecond);
                float alpha = now <= row.Until ? 1f : Mathf.Clamp01(1f - (now - row.Until) / FadeSeconds);
                row.Text.alpha = alpha;
                // The row's tick (3 x 12, 3 in from the edge, on the head line): AMBER for an emergency or the enemy, BRIGHT for the newest row, DIM for the older ones.
                Color tick = row.Net == ChatterNet.Enemy || row.Priority == ChatterPriority.Emergency ? VanillaHudStyle.Caution : i == used - 1 ? VanillaHudStyle.InkHi : VanillaHudStyle.InkDim;
                tick.a *= alpha;
                SetBar(ticks[i], Left + 3f, cursor + RowGap + row.Height - 13f, 3f, 12f, tick);
            }
            for (int i = used; i < Rows; i++) SetBar(ticks[i], 0f, 0f, 0f, 0f, Color.clear);
            if (promptOn)
            {
                float barWidth = Width - Indent - 8f;
                cursor -= PromptHeight - RowGap;
                AvHudHost.Place(prompt.rectTransform, Left + Indent, cursor + 6f, barWidth, PromptHeight - 8f);
                prompt.maxVisibleCharacters = 99999;
                prompt.alpha = 1f;
                Color track = frame;
                track.a = promptFill > 0f ? HudInk.LineLow : 0f;
                SetBar(promptTrack, Left + Indent, cursor + 2f, barWidth, 2f, track);
                Color fill = promptFill > 0.33f ? VanillaHudStyle.Ink : VanillaHudStyle.Caution;
                fill.a = promptFill > 0f ? 0.9f : 0f;
                SetBar(promptBar, Left + Indent, cursor + 2f, barWidth * promptFill, 2f, fill);
                cursor -= RowGap;
            }
            else
            {
                SetBar(promptTrack, 0f, 0f, 0f, 0f, Color.clear);
                SetBar(promptBar, 0f, 0f, 0f, 0f, Color.clear);
            }
            LeftHudStack.CommsBottom = top + Height();
            paintedTop = top;
        }

        /// <summary>The rows' heights at the current width (the column changed: a narrower screen wraps more).</summary>
        private void Rewrap()
        {
            for (int i = 0; i < used; i++) rows[i].Height = Mathf.Max(36f, rows[i].Text.GetPreferredValues(rows[i].Text.text, Width - Indent - 8f, 0f).y + 2f);
        }

        private float paintedTop = LeftHudStack.CommsTop;

        private static float Clock(in Row row) => row.Scripted ? Time.time : Time.unscaledTime;
        private static int Rank(in Row row) => ChatterShed.Rank(row.Scripted, row.Priority);
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
            shelf.font = channel.font = prompt.font = face;
            shelf.fontSharedMaterial = channel.fontSharedMaterial = prompt.fontSharedMaterial = material != null ? material : face.material;
            shelf.fontSize = 100f;
            float advance = shelf.GetPreferredValues("MMMMMMMM", 0f, 0f).x / 8f / 100f;
            float scale = 0.65f / Mathf.Max(0.1f, advance);
            shelf.fontSize = channel.fontSize = LeftHudStack.HeaderSize * scale;
            prompt.fontSize = LeftHudStack.BodySize * scale;
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
            promptOn = false;
            prompt.text = "";
            LeftHudStack.CommsBottom = 0f;
            shown = false;      // the Paint subscription outlives a clear (host disabled, scene reset): it must not lay out an empty block again
            SetVisible(false);
        }

        public void ClearScripted()
        {
            for (int i = used - 1; i >= 0; i--) if (rows[i].Scripted) Drop(i);
            if (used == 0) Clear();
            else LeftHudStack.CommsBottom = paintedTop + Height();
        }

        public void Destroy()
        {
            if (subscribed) { subscribed = false; HudLayer.Paint -= OnPaint; }
            Clear();
            host?.Destroy();
            if (holderGo != null) Object.Destroy(holderGo);
        }

        public static string Tag(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Awacs: return "AWACS";
                case ChatterNet.Tower: return "TWR";
                case ChatterNet.Command: return "CMD";
                case ChatterNet.Enemy: return "SIGINT";
                case ChatterNet.Allied: return "ALLY";
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
                case ChatterNet.Enemy: return "9.4 GHZ";
                case ChatterNet.Allied: return "255.400";
                default: return "251.000";
            }
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
