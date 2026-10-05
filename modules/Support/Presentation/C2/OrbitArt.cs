using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// The constellation drawing in the ORBIT box: a planet limb and three concentric orbit arcs with one bird marker each,
    /// drawn with the kit's line graphic. cosmetic: fixed geometry and fixed marker angles, no animation, never derived from
    /// hidden state. Only the marker colours follow the three real bird states (green ready, amber waiting, red no bird).
    /// </summary>
    internal sealed class OrbitArt : AvPart
    {
        private const int Points = 72;
        private const float MarkerSize = 7f;
        // cosmetic: marker angles along each orbit (OPTICAL, RADAR, KINETIC), in degrees from the right-hand horizon.
        private static readonly float[] MarkerAngle = { 112f, 74f, 46f };
        private readonly AvLineGraphic limb;
        private readonly AvLineGraphic[] orbits = new AvLineGraphic[3];
        private readonly Image[] markers = new Image[3];
        private readonly AvState[] tones = { AvState.Inert, AvState.Inert, AvState.Inert };
        // cosmetic: the drawing is designed for 70 px and scales to whatever height the layout gives it.
        private const float DesignHeight = 70f;
        private readonly float width, height, k;

        public OrbitArt(RectTransform parent, float width, float height)
        {
            this.width = width;
            this.height = height;
            Rect = AvLay.Child(parent, "OrbitArt");
            AvLay.Place(Rect, 0f, 0f, width, height);
            limb = Line("Limb");
            for (int i = 0; i < orbits.Length; i++) orbits[i] = Line("Orbit" + i);
            k = height / DesignHeight;
            Draw(limb, 0.72f, 14f * k, 8f * k);
            for (int i = 0; i < orbits.Length; i++) Draw(orbits[i], Radius(i), Rise(i) * k, 8f * k);
            for (int i = 0; i < markers.Length; i++)
            {
                markers[i] = AvLay.Solid(Rect, "Bird" + i, Color.clear);
                markers[i].raycastTarget = false;
                float phi = MarkerAngle[i] * Mathf.Deg2Rad;
                float x = width * 0.5f + width * Radius(i) * Mathf.Cos(phi);
                float y = 8f * k + Rise(i) * k * Mathf.Sin(phi);
                AvLay.Place(markers[i].rectTransform, x - MarkerSize * 0.5f, height - y - MarkerSize * 0.5f, MarkerSize, MarkerSize);
            }
            Restyle();
        }

        // cosmetic: orbit i spans this share of the width and rises this far above the base line.
        private static float Radius(int i) => 0.64f - 0.05f * i;
        private static float Rise(int i) => 28f + 14f * i;

        private AvLineGraphic Line(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            var g = go.AddComponent<AvLineGraphic>();
            g.raycastTarget = false;
            g.FillUnder = false;
            g.Thickness = 1.2f;
            AvLay.Fill(g.rectTransform);
            return g;
        }

        /// <summary>An upper half-ellipse (half-width a of the board width, rise b above the base y0), clipped to the board.</summary>
        private void Draw(AvLineGraphic g, float a, float b, float y0)
        {
            var xs = new float[Points + 1];
            var ys = new float[Points + 1];
            int n = 0;
            for (int i = 0; i <= Points; i++)
            {
                float phi = Mathf.PI * i / Points;
                float x = width * 0.5f + width * a * Mathf.Cos(phi);
                float y = y0 + b * Mathf.Sin(phi);
                if (x < 0f || x > width || y < 0f || y > height) continue;
                xs[n] = x / width;
                ys[n] = y / height;
                n++;
            }
            g.SetPoints(xs, ys, n);
        }

        /// <summary>The three marker tones: OPTICAL, RADAR, KINETIC.</summary>
        public void SetStates(AvState optical, AvState radar, AvState kinetic)
        {
            if (tones[0] == optical && tones[1] == radar && tones[2] == kinetic) return;
            tones[0] = optical; tones[1] = radar; tones[2] = kinetic;
            Restyle();
        }

        public override float Measure(float w) => height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
        }

        public override void Restyle()
        {
            if (limb == null) return;
            limb.LineColor = OpsInk.Dim;
            limb.Thickness = 1.6f;
            for (int i = 0; i < orbits.Length; i++) orbits[i].LineColor = OpsInk.Hairline;
            for (int i = 0; i < markers.Length; i++)
                markers[i].color = tones[i] == AvState.Inert ? OpsInk.Muted : OpsInk.Rail(tones[i]);
            limb.SetVerticesDirty();
            foreach (AvLineGraphic o in orbits) o.SetVerticesDirty();
        }
    }
}
