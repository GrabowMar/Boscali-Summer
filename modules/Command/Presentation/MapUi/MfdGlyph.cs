using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    // Small vector fallback for actions without a native game sprite. No textures,
    // font glyph dependencies, frame callbacks, or external icon package.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class MfdGlyph : MaskableGraphic
    {
        private string kind;
        private string sourceText;
        public Image NativeIcon;
        public Image Selection;

        public void Set(string text, Sprite sprite = null, bool selected = false)
        {
            if (sourceText != text)
            {
                sourceText = text;
                string next = Kind(text);
                if (next != kind) { kind = next; SetVerticesDirty(); }
                color = next == "shield" ? AvTheme.Friendly :
                    text == "ENEMY" || text == "HOSTILE" ? AvTheme.Warning : AvTheme.RailInfo;
            }
            if (NativeIcon != null)
            {
                NativeIcon.sprite = sprite;
                NativeIcon.enabled = sprite != null;
            }
            enabled = sprite == null;
            if (Selection != null) Selection.enabled = selected;
        }

        private static string Kind(string text)
        {
            string key = (text ?? "").ToUpperInvariant().Split(' ')[0];
            switch (key)
            {
                case "AIR": case "AIRCRAFT": case "A2A": return "air";
                case "SHP": case "SHIPS": case "SEA": return "ship";
                case "BLD": case "BUILDINGS": case "AIRBASES": return "building";
                case "GROUND": case "GND": case "VEH": case "VEHICLES": case "ARMOR": case "A2G": return "ground";
                case "MISSILES": case "MSL": case "AMMO": case "GUN": return "missile";
                case "SEAD": case "EW": case "JAMMING": case "LASER": return "radar";
                case "FRIENDLY": case "FORCES": return "shield";
                case "ENEMY": case "HOSTILE": case "TARGETS": case "SELECTED": return "target";
                case "RESET": return "reset";
                case "CLEAR": case "HIDE": return "clear";
                case "LEDGER": case "VALUE": case "RESERVES": case "LOSSES": return "chart";
                case "PILOTS": case "PLAYERS": case "MANPOWER": return "person";
                case "NAV": case "ORDERS": return "nav";
                case "HUD": case "MODE": case "SMALL": case "MEDIUM": case "LARGE": return "hud";
                case "MISSION": case "OBJECTIVES": return "flag";
                case "FILTERS": case "PRESETS": return "filter";
                default: return "list";
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            switch (kind)
            {
                case "air":
                    Path(mesh, .5f,.95f, .6f,.55f, .95f,.3f, .6f,.36f, .6f,.13f, .72f,.04f, .5f,.12f, .28f,.04f, .4f,.13f, .4f,.36f, .05f,.3f, .4f,.55f, .5f,.95f); break;
                case "ship":
                    Path(mesh, .06f,.4f, .25f,.15f, .75f,.15f, .94f,.4f, .06f,.4f);
                    Path(mesh, .3f,.4f, .3f,.65f, .65f,.65f, .65f,.4f); Line(mesh,.5f,.65f,.5f,.9f); break;
                case "building":
                    Path(mesh,.15f,.1f,.15f,.8f,.5f,.95f,.85f,.8f,.85f,.1f,.15f,.1f);
                    Line(mesh,.35f,.35f,.35f,.65f); Line(mesh,.65f,.35f,.65f,.65f); break;
                case "ground":
                    Path(mesh,.1f,.25f,.1f,.55f,.9f,.55f,.9f,.25f,.1f,.25f);
                    Path(mesh,.35f,.55f,.35f,.75f,.6f,.75f,.6f,.55f); Line(mesh,.6f,.75f,.98f,.85f); break;
                case "missile":
                    Path(mesh,.2f,.1f,.3f,.5f,.85f,.95f,.8f,.6f,.45f,.2f,.2f,.1f); Line(mesh,.15f,.05f,.05f,.25f); break;
                case "radar":
                    Path(mesh,.1f,.6f,.22f,.85f,.5f,.95f,.78f,.85f,.9f,.6f);
                    Path(mesh,.28f,.5f,.38f,.67f,.62f,.67f,.72f,.5f);
                    Path(mesh,.3f,.05f,.5f,.45f,.7f,.05f); break;
                case "shield":
                    Path(mesh,.15f,.9f,.85f,.9f,.8f,.4f,.5f,.08f,.2f,.4f,.15f,.9f); break;
                case "target":
                    Path(mesh,.5f,.9f,.9f,.5f,.5f,.1f,.1f,.5f,.5f,.9f);
                    Line(mesh,.5f,.3f,.5f,.7f); Line(mesh,.3f,.5f,.7f,.5f); break;
                case "reset":
                    Path(mesh,.85f,.35f,.85f,.75f,.65f,.9f,.25f,.9f,.1f,.65f,.1f,.3f,.35f,.1f,.7f,.1f);
                    Path(mesh,.02f,.88f,.1f,.65f,.35f,.72f); break;
                case "clear": Line(mesh,.15f,.15f,.85f,.85f); Line(mesh,.15f,.85f,.85f,.15f); break;
                case "chart":
                    Path(mesh,.1f,.95f,.1f,.1f,.95f,.1f); Line(mesh,.3f,.15f,.3f,.4f); Line(mesh,.55f,.15f,.55f,.65f); Line(mesh,.8f,.15f,.8f,.9f); break;
                case "person":
                    Path(mesh,.35f,.9f,.65f,.9f,.65f,.6f,.35f,.6f,.35f,.9f);
                    Path(mesh,.15f,.1f,.2f,.4f,.5f,.5f,.8f,.4f,.85f,.1f); break;
                case "nav": Path(mesh,.5f,.95f,.9f,.15f,.5f,.35f,.1f,.15f,.5f,.95f); break;
                case "hud":
                    Path(mesh,.3f,.9f,.1f,.9f,.1f,.1f,.3f,.1f); Path(mesh,.7f,.9f,.9f,.9f,.9f,.1f,.7f,.1f);
                    Line(mesh,.25f,.5f,.75f,.5f); Line(mesh,.5f,.35f,.5f,.65f); break;
                case "flag": Path(mesh,.15f,.05f,.15f,.95f,.85f,.8f,.15f,.55f); break;
                case "filter": Path(mesh,.05f,.9f,.95f,.9f,.6f,.5f,.6f,.15f,.4f,.05f,.4f,.5f,.05f,.9f); break;
                default:
                    Line(mesh,.1f,.8f,.9f,.8f); Line(mesh,.1f,.5f,.75f,.5f); Line(mesh,.1f,.2f,.9f,.2f); break;
            }
        }

        private void Path(VertexHelper mesh, params float[] points)
        {
            for (int i = 2; i < points.Length; i += 2)
                Line(mesh, points[i-2], points[i-1], points[i], points[i+1]);
        }

        private void Line(VertexHelper mesh, float x1, float y1, float x2, float y2)
        {
            Rect r = rectTransform.rect;
            Vector2 a = new Vector2(r.x + x1*r.width, r.y + y1*r.height);
            Vector2 b = new Vector2(r.x + x2*r.width, r.y + y2*r.height);
            Vector2 d = (b-a).normalized;
            Vector2 n = new Vector2(-d.y,d.x) * .7f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a-n, color, Vector2.zero); mesh.AddVert(a+n, color, Vector2.zero);
            mesh.AddVert(b+n, color, Vector2.zero); mesh.AddVert(b-n, color, Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2); mesh.AddTriangle(start,start+2,start+3);
        }
    }

    internal static partial class VanillaMfdRebuild
    {
        private static AvButton PanelButton(RectTransform parent, Rect area, string text,
            string classes, System.Action action, AvButtonStyle style = AvButtonStyle.Default)
        {
            AvButton button = AvStyled.Button(parent, area, text, classes, action, style);
            var root = (RectTransform)button.transform;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            AvKit.Place(label.rectTransform, new Rect(28f, 0f, area.width - 38f, area.height));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = AvTokens.FontMicro;
            label.enableAutoSizing = true;
            var go = new GameObject("Symbol", typeof(RectTransform), typeof(MfdGlyph));
            go.transform.SetParent(root, false);
            var glyph = go.GetComponent<MfdGlyph>();
            AvKit.Place(glyph.rectTransform, new Rect(7f, -(area.height-16f)/2f, 16f, 16f));
            glyph.color = AvTheme.RailInfo;
            glyph.raycastTarget = false;
            glyph.NativeIcon = AvKit.Panel(root, new Rect(7f, -(area.height-18f)/2f, 18f, 18f), AvTheme.TextPrimary);
            glyph.NativeIcon.preserveAspect = true;
            glyph.Selection = AvKit.Rule(root, new Rect(area.width-6f, -6f, 2f, area.height-12f), AvTheme.Accent);
            glyph.Set(text);
            button.WithTooltip(text);
            return button;
        }

        private static void PaintButton(AvButton button, string text, bool selected, Sprite sprite = null)
        {
            button.SetText(text);
            button.SetLatched(selected);
            button.GetComponentInChildren<MfdGlyph>(true)?.Set(text, sprite, selected);
        }
    }
}
