using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The tug-of-war lane (hostile share nearby, a notch, a ghost trail of the last change) as one pooled mesh. Shared by the hero lane, the proposal
    /// tickets, the compact lanes and nothing else draws it. Unknown draws the hatched track, never a number. A part element, not a flow part.
    /// </summary>
    internal sealed class StrLaneBar
    {
        /// <summary>Room the notch head needs above the track and the notch line below it.</summary>
        internal const float PadTop = 9f, PadBottom = 3f;

        private readonly RectTransform rect;
        private readonly AvVector vector;
        private float w = -1f, h = -1f, value = float.NaN, trend = float.NaN;
        private bool dirty = true;

        internal StrLaneBar(RectTransform parent, string name)
        {
            rect = AvLay.Child(parent, name);
            vector = AvVector.Create(rect, "Lane", StrStrokes.LaneQuads);
        }

        /// <summary>Hostile share 0..1 and its change over the last review; NaN for either is "unknown".</summary>
        internal void Set(float share, float change)
        {
            if (share.Equals(value) && change.Equals(trend)) return;
            value = share; trend = change; dirty = true;
        }

        /// <summary>Places the track (x, y = top of the track, in the parent's top-left units) and redraws when it changed.</summary>
        internal void Place(float x, float y, float width, float height)
        {
            AvLay.Place(rect, x, y - PadTop, width, height + PadTop + PadBottom);
            if (!width.Equals(w) || !height.Equals(h)) { w = width; h = height; dirty = true; }
            Draw();
        }

        internal void Restyle() { dirty = true; Draw(); }

        internal void SetActive(bool on)
        {
            if (rect.gameObject.activeSelf != on) rect.gameObject.SetActive(on);
        }

        private void Draw()
        {
            if (!dirty || w <= 0f || h <= 0f) return;
            dirty = false;
            vector.Buffer.Clear();
            var lane = new StrLane
            {
                X = 0f, Y = PadBottom, W = w, H = h, Value = value, Trend = trend,
                Own = AvInk.Friendly.WithAlpha(0.85f).ToRgba(), Hostile = AvInk.Hostile.WithAlpha(0.85f).ToRgba(),
                Notch = AvInk.Ink.ToRgba(), Ghost = AvInk.Ink.WithAlpha(0.7f).ToRgba(), Tick = AvInk.Ink.WithAlpha(0.25f).ToRgba(),
            };
            StrStrokes.LaneBar(vector.Buffer, in lane);
            vector.Commit();
        }
    }
}
