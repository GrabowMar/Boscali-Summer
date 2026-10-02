using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Viz
{
    /// <summary>Up to 60 samples as a polyline; the newest value is written, not only drawn.</summary>
    internal sealed class Sparkline
    {
        public const int Capacity = 60;
        private Skin skin;
        private AvLineGraphic line;
        private readonly float[] samples = new float[Capacity];
        private readonly float[] xs = new float[Capacity];
        private readonly float[] ys = new float[Capacity];
        private Image head;
        private TMP_Text value;
        private Rect plot;
        private int count;
        private int start;
        private string shownValue;

        public void Build(RectTransform parent, Rect area, Skin skin)
        {
            this.skin = skin;
            plot = new Rect(area.x, area.y - 14f, area.width, Mathf.Max(8f, area.height - 14f));
            Chrome.Rule(parent, new Rect(plot.x, plot.y - plot.height, plot.width, 1f), skin.Track);
            line = Chrome.Graphic<AvLineGraphic>(parent, plot, "Trace");
            line.Thickness = 1.5f;
            line.FillUnder = false;
            line.LineColor = skin.Fill;
            head = Chrome.Panel(parent, new Rect(0f, 0f, 5f, 5f), skin.Mark, OpsSprites.Dot);
            head.type = Image.Type.Simple;
            head.enabled = false;
            value = PrimitiveText.Label(parent, new Rect(area.x, area.y, area.width, 13f), skin, TextAlignmentOptions.MidlineRight);
        }

        /// <summary>Append a sample (call at most a few times a second) and redraw against [min, max].</summary>
        public void Push(float sample, float min, float max, string valueText)
        {
            if (float.IsNaN(sample)) sample = min;
            if (count < Capacity) samples[(start + count++) % Capacity] = sample;
            else
            {
                samples[start] = sample;
                start = (start + 1) % Capacity;
            }
            float span = Mathf.Max(0.0001f, max - min);
            for (int i = 0; i < count; i++)
            {
                // Right-aligned: the newest sample sits on the right edge, older ones step left.
                xs[i] = 1f - (count - 1 - i) / (float)(Capacity - 1);
                ys[i] = Mathf.Clamp01((samples[(start + i) % Capacity] - min) / span);
            }
            line.SetPoints(xs, ys, count);
            head.enabled = count > 0;
            if (count > 0) Lines.Centre(head.rectTransform, plot.x + plot.width, plot.y - plot.height + ys[count - 1] * plot.height, 5f);
            PrimitiveText.Write(value, skin, valueText, ref shownValue);
        }
    }
}
