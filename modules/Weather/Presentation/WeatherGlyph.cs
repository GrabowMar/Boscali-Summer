using BoscaliSummer.Features.Weather.Domain;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>Small forecast symbology, drawn once per regime change with no texture or update loop.</summary>
    internal sealed class WeatherGlyph : MaskableGraphic
    {
        private static readonly float[] CloudOutline =
        {
            .13f,.34f, .15f,.46f, .24f,.51f, .31f,.49f,
            .36f,.64f, .49f,.69f, .60f,.61f, .67f,.62f,
            .79f,.55f, .83f,.42f, .79f,.32f, .13f,.32f
        };
        private WeatherRegimeType kind = WeatherRegimeType.Clear;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public static WeatherGlyph Create(RectTransform parent, Rect area)
        {
            var obj = new GameObject("WeatherGlyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(WeatherGlyph));
            var glyph = obj.GetComponent<WeatherGlyph>();
            glyph.transform.SetParent(parent, false);
            AvKit.Place((RectTransform)glyph.transform, area);
            return glyph;
        }

        public void SetKind(WeatherRegimeType value)
        {
            if (kind == value) return;
            kind = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (kind == WeatherRegimeType.Clear)
            {
                Sun(mesh, .5f, .5f, .22f);
                return;
            }

            if (kind == WeatherRegimeType.Fair || kind == WeatherRegimeType.Scattered)
                Sun(mesh, .34f, .62f, kind == WeatherRegimeType.Fair ? .18f : .14f);

            Cloud(mesh);
            if (kind == WeatherRegimeType.Broken)
                Line(mesh, .3f, .12f, .73f, .12f);
            if (kind == WeatherRegimeType.Overcast)
            {
                Line(mesh, .18f, .14f, .82f, .14f);
                Line(mesh, .29f, .06f, .71f, .06f);
            }
            if (kind == WeatherRegimeType.RainSquall)
            {
                Line(mesh, .32f, .24f, .25f, .04f);
                Line(mesh, .53f, .24f, .46f, .04f);
                Line(mesh, .74f, .24f, .67f, .04f);
            }
            if (kind == WeatherRegimeType.Storm)
            {
                Line(mesh, .54f, .28f, .43f, .12f);
                Line(mesh, .43f, .12f, .55f, .12f);
                Line(mesh, .55f, .12f, .46f, .01f);
            }
        }

        private void Sun(VertexHelper mesh, float cx, float cy, float radius)
        {
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                float b = (i + 1) * Mathf.PI / 8f;
                Line(mesh, cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius,
                    cx + Mathf.Cos(b) * radius, cy + Mathf.Sin(b) * radius);
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Line(mesh, cx + Mathf.Cos(a) * radius * 1.35f,
                    cy + Mathf.Sin(a) * radius * 1.35f,
                    cx + Mathf.Cos(a) * radius * 1.8f,
                    cy + Mathf.Sin(a) * radius * 1.8f);
            }
        }

        private void Cloud(VertexHelper mesh)
        {
            for (int i = 2; i < CloudOutline.Length; i += 2)
                Line(mesh, CloudOutline[i - 2], CloudOutline[i - 1], CloudOutline[i], CloudOutline[i + 1]);
        }

        private void Line(VertexHelper mesh, float x1, float y1, float x2, float y2)
        {
            Rect r = rectTransform.rect;
            Vector2 a = new Vector2(r.x + x1 * r.width, r.y + y1 * r.height);
            Vector2 b = new Vector2(r.x + x2 * r.width, r.y + y2 * r.height);
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .001f) return;
            Vector2 n = new Vector2(-delta.y, delta.x).normalized *
                Mathf.Clamp(Mathf.Min(r.width, r.height) * .035f, .7f, 1.9f);
            int start = mesh.currentVertCount;
            mesh.AddVert(a - n, color, Vector2.zero);
            mesh.AddVert(a + n, color, Vector2.zero);
            mesh.AddVert(b + n, color, Vector2.zero);
            mesh.AddVert(b - n, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
