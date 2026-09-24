using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>Up to 60 samples as a polyline; the newest value is written, not only drawn.</summary>
    internal sealed class Sparkline
    {
        public const int Capacity = 60;
        private Skin skin;
        private readonly Image[] strokes = new Image[Capacity - 1];
        private readonly float[] samples = new float[Capacity];
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
            AvKit.Rule(parent, new Rect(plot.x, plot.y - plot.height, plot.width, 1f), skin.Track);
            for (int i = 0; i < strokes.Length; i++)
            {
                strokes[i] = Lines.Make(parent, skin.Fill);
                strokes[i].enabled = false;
            }
            head = AvKit.Panel(parent, new Rect(0f, 0f, 5f, 5f), skin.Mark, OpsSprites.Dot);
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
            float step = plot.width / (Capacity - 1);
            Vector2 previous = default;
            for (int i = 0; i < count; i++)
            {
                float v = Mathf.Clamp01((samples[(start + i) % Capacity] - min) / span);
                var p = new Vector2(plot.x + plot.width - (count - 1 - i) * step, plot.y - plot.height + v * plot.height);
                if (i > 0) Lines.Set(strokes[i - 1], previous.x, previous.y, p.x, p.y, 1.5f);
                previous = p;
            }
            for (int i = Mathf.Max(0, count - 1); i < strokes.Length; i++) strokes[i].enabled = false;
            head.enabled = count > 0;
            if (count > 0) Lines.Centre(head.rectTransform, previous.x, previous.y, 5f);
            PrimitiveText.Write(value, skin, valueText, ref shownValue);
        }
    }
}
