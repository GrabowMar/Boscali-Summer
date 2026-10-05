using BoscaliSummer.Modules.Support.Domain.C2;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// The host console: <c>lines</c> fixed mono rows (14 px each) showing the newest events of a <see cref="C2Console"/>, newest last.
    /// Green = info, amber = warning, red = danger; an empty console reads <c>&gt; link idle</c>. Repaints only when the console version moved.
    /// </summary>
    internal sealed class C2ConsoleView : AvPart
    {
        public const float LineH = 14f, Pad = 4f;
        private readonly AvFrame frame;
        private readonly TMP_Text[] rows;
        private readonly C2Line[] buffer;
        private readonly C2Tone[] tones;
        private readonly string[] raw;
        private readonly int count;
        private C2Console shown;
        private int shownVersion = -1, shownCount;
        private float width = AvTokens.PanelWidth;

        public C2ConsoleView(RectTransform parent, int lines)
        {
            count = Mathf.Max(1, lines);
            Rect = AvLay.Child(parent, "C2Console");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rows = new TMP_Text[count];
            buffer = new C2Line[count];
            tones = new C2Tone[count];
            raw = new string[count];
            for (int i = 0; i < count; i++)
            {
                rows[i] = C2Kit.Mono(Rect, "Line" + i, 10.5f, TextAlignmentOptions.MidlineLeft);
                rows[i].gameObject.SetActive(false);
            }
            Restyle();
            Layout();
        }

        public static float HeightFor(int lines) => Mathf.Max(1, lines) * LineH + 2f * Pad;

        public override float Measure(float w) => HeightFor(count);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            Layout();
            Repaint();
        }

        /// <summary>Show the newest lines of <paramref name="console"/>; cheap when nothing changed.</summary>
        public void Show(C2Console console)
        {
            if (console != null && console == shown && console.Version == shownVersion) return;
            shown = console;
            shownVersion = console != null ? console.Version : -1;
            shownCount = console != null ? console.CopyNewest(buffer, count) : 0;
            for (int i = 0; i < shownCount; i++) { tones[i] = buffer[i].Tone; raw[i] = C2Console.Render(buffer[i]); }
            if (shownCount == 0) { tones[0] = C2Tone.Info; raw[0] = "> link idle"; }
            Repaint();
        }

        private void Layout()
        {
            AvLay.Place(frame.rectTransform, 0f, 0f, width, HeightFor(count));
            for (int i = 0; i < count; i++) C2Kit.Place(rows[i], 8f, Pad + i * LineH, width - 16f, LineH);
        }

        private void Repaint()
        {
            int n = shownCount == 0 ? 1 : shownCount;
            if (raw[0] == null) return;
            for (int i = 0; i < count; i++)
            {
                bool on = i < n;
                rows[i].gameObject.SetActive(on);
                if (!on) continue;
                OpsText.Set(rows[i], C2Kit.FitTo(rows[i], raw[i], width - 16f));
                rows[i].color = shownCount == 0 ? OpsInk.Muted : OpsInk.Word(C2Kit.StateOf(tones[i]));
            }
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("console");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), OpsInk.Hairline);
            Repaint();
        }
    }
}
