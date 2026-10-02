using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Chrome-icon lookup for the old free-text glyph keys (kit v2 spec 5.4: "MfdGlyph ... move to AvIcon
    /// where a Tabler match exists"). Callers building an <see cref="AvControl.Spec"/>, an
    /// <see cref="AvConsole.Tabs"/> entry or an <see cref="AvFlow.Section"/> pick the closest entry here
    /// instead of hand-picking an <see cref="AvIcon"/> per call site, so the same label always reads the
    /// same symbol across FAC/MAP/HUD/TGT/MIS. Unmapped keys (mostly literal platform/unit-type names —
    /// AIRCRAFT, SHIPS, VEHICLES, BUILDINGS, MISSILES, CONVOY) have no acceptable Tabler stand-in and stay
    /// on <see cref="MfdGlyph"/>'s procedural renderer below, which is genuinely data (a NATO-adjacent
    /// platform silhouette), not chrome.
    /// </summary>
    internal static class MfdChromeIcon
    {
        public static AvIcon For(string text)
        {
            string key = (text ?? "").ToUpperInvariant().Split(' ')[0];
            switch (key)
            {
                case "SEAD": case "EW": case "JAMMING": case "LASER": case "SENSOR": return AvIcon.Radar2;
                case "THEATER": case "FRONTAGE": case "STR": return AvIcon.Map2;
                case "CONTROL": case "SECTOR": return AvIcon.Scale;
                case "FRONT": case "FRONTLINE": return AvIcon.WaveSine;
                case "GRID": case "COORDINATES": case "LAYERS": return AvIcon.LayersSubtract;
                case "RECON": case "SURVEY": case "SCOUT": case "EYE": return AvIcon.Eye;
                case "REPAIR": case "ENGINEER": case "ENGINEERS": return AvIcon.Bolt;
                case "FRIENDLY": case "FORCES": case "BDF": case "PALA": case "SHIELD": case "FACTION": return AvIcon.Shield;
                case "ENEMY": case "HOSTILE": case "TARGETS": case "SELECTED": case "TARGET": return AvIcon.Target;
                case "RESET": return AvIcon.Refresh;
                case "CLEAR": case "HIDE": return AvIcon.X;
                case "LEDGER": case "VALUE": case "RESERVES": case "LOSSES": case "CHART": return AvIcon.ChartLine;
                case "FUNDS": case "FUND": case "CREDITS": case "RESOURCES": return AvIcon.Coins;
                case "PILOTS": case "PLAYERS": case "MANPOWER": case "SQD": case "SQUAD": case "PERSON": return AvIcon.UsersGroup;
                case "MORALE": case "GAUGE": return AvIcon.Gauge;
                case "NAV": case "ORDERS": return AvIcon.ArrowUpRight;
                case "HUD": case "MODE": case "SMALL": case "MEDIUM": case "LARGE": case "READABILITY": return AvIcon.Focus2;
                case "MISSION": case "OBJECTIVES": case "FLAG": return AvIcon.Flag;
                case "FILTERS": case "PRESETS": case "FILTER": return AvIcon.Filter;
                case "MAP": return AvIcon.Map2;
                case "RAD": case "RADIO": return AvIcon.Radio;
                case "COM": case "COMMS": case "CHAT": return AvIcon.Message2;
                case "ENV": case "WEATHER": return AvIcon.Cloud;
                case "SET": case "SETTINGS": case "CONFIG": return AvIcon.Settings;
                case "OPS": case "SUPPORT": case "AIRDROP": return AvIcon.Shield;
                case "PULSE": case "ACTIVITY": return AvIcon.Activity;
                case "DOT": return AvIcon.Circle;
                default: return AvIcon.None;
            }
        }
    }

    /// <summary>
    /// Small vector renderer for platform/unit-type glyphs that have no Tabler equivalent (AIRCRAFT, SHIPS,
    /// VEHICLES, BUILDINGS, MISSILES, CONVOY): genuinely data, kept procedural per the brief's data-viz
    /// exemption. Every kind that had a decent chrome match moved to <see cref="MfdChromeIcon"/>/AvIcon.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class MfdGlyph : MaskableGraphic
    {
        private string kind;
        private string sourceText;
        private Color restColor = Color.white;
        public Image NativeIcon;
        public Image Selection;

        public void Set(string text, Sprite sprite = null, bool selected = false)
        {
            if (sourceText != text)
            {
                sourceText = text;
                string next = Kind(text);
                if (next != kind) { kind = next; SetVerticesDirty(); }
                restColor = AvTheme.RailInfo;
            }
            if (NativeIcon != null)
            {
                NativeIcon.sprite = sprite;
                NativeIcon.enabled = sprite != null;
            }
            enabled = sprite == null;
            SetSelected(selected);
        }

        /// <summary>
        /// Set a glyph shape directly instead of deriving it from a label. The rail brands
        /// each button from a catalog entry, where the code ("SET") is not the shape name.
        /// </summary>
        public void SetKind(string glyphKind, Color tint)
        {
            sourceText = null;
            string next = string.IsNullOrEmpty(glyphKind) ? "list" : glyphKind;
            if (next != kind) { kind = next; SetVerticesDirty(); }
            restColor = tint;
            color = tint;
            // A glyph wearing a native sprite is hidden; a direct kind must bring it back.
            enabled = NativeIcon == null || NativeIcon.sprite == null;
        }

        /// <summary>Latched controls brighten their glyph; colour is never the only signal.</summary>
        public void SetSelected(bool selected)
        {
            if (Selection != null) Selection.enabled = selected;
            color = selected ? AvTheme.Accent : restColor;
            if (NativeIcon != null) NativeIcon.color = AvTheme.TextPrimary;
        }

        /// <summary>Only the platform/unit-type kinds that have no acceptable Tabler stand-in
        /// (see <see cref="MfdChromeIcon"/> for everything that moved to AvIcon).</summary>
        private static string Kind(string text)
        {
            string key = (text ?? "").ToUpperInvariant().Split(' ')[0];
            switch (key)
            {
                case "AIR": case "AIRCRAFT": case "A2A": case "WING": return "air";
                case "SHP": case "SHIPS": case "SEA": return "ship";
                case "BLD": case "BUILDINGS": case "AIRBASES": return "building";
                case "GROUND": case "GND": case "VEH": case "VEHICLES": case "ARMOR": case "A2G": return "ground";
                case "MISSILES": case "MSL": case "AMMO": case "WARHEADS": case "GUN": return "missile";
                case "SUPPLY": case "CONVOY": case "ESCORT": return "convoy";
                default: return "list";
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            switch (kind)
            {
                case "air":
                    Path(mesh,.5f,.9f,.58f,.55f,.84f,.42f,.84f,.33f,.57f,.39f,
                        .57f,.14f,.5f,.2f,.43f,.14f,.43f,.39f,.16f,.33f,.16f,.42f,
                        .42f,.55f,.5f,.9f); break;
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
                case "convoy":
                    Path(mesh,.04f,.36f,.04f,.72f,.52f,.72f,.52f,.36f,.04f,.36f);
                    Path(mesh,.58f,.36f,.58f,.62f,.96f,.62f,.96f,.36f,.58f,.36f);
                    Circle(mesh,.17f,.26f,.08f,8);
                    Circle(mesh,.4f,.26f,.08f,8);
                    Circle(mesh,.78f,.26f,.08f,8); break;
                default:
                    Line(mesh,.1f,.8f,.9f,.8f); Line(mesh,.1f,.5f,.75f,.5f); Line(mesh,.1f,.2f,.9f,.2f); break;
            }
        }

        private void Path(VertexHelper mesh, params float[] points)
        {
            for (int i = 2; i < points.Length; i += 2)
                Line(mesh, points[i-2], points[i-1], points[i], points[i+1]);
        }

        private void Circle(VertexHelper mesh, float cx, float cy, float radius, int segments)
        {
            Rect r = rectTransform.rect;
            float rx = radius * r.width;
            float ry = radius * r.height;
            Vector2 centre = new Vector2(r.x + cx * r.width, r.y + cy * r.height);
            int start = mesh.currentVertCount;
            mesh.AddVert(centre, color, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                mesh.AddVert(new Vector2(centre.x + Mathf.Cos(angle) * rx, centre.y + Mathf.Sin(angle) * ry),
                             color, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
                mesh.AddTriangle(start, start + 1 + i, start + 2 + i);
        }

        private void Ring(VertexHelper mesh, float cx, float cy, float radius, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float b = (i + 1) / (float)segments * Mathf.PI * 2f;
                Line(mesh, cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius,
                           cx + Mathf.Cos(b) * radius, cy + Mathf.Sin(b) * radius);
            }
        }

        private void Arc(VertexHelper mesh, float cx, float cy, float radius, float fromDeg, float toDeg, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)segments) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(fromDeg, toDeg, (i + 1) / (float)segments) * Mathf.Deg2Rad;
                Line(mesh, cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius,
                           cx + Mathf.Cos(b) * radius, cy + Mathf.Sin(b) * radius);
            }
        }

        private void Line(VertexHelper mesh, float x1, float y1, float x2, float y2)
        {
            Rect r = rectTransform.rect;
            Vector2 a = new Vector2(r.x + x1*r.width, r.y + y1*r.height);
            Vector2 b = new Vector2(r.x + x2*r.width, r.y + y2*r.height);
            Vector2 d = (b-a).normalized;
            Vector2 n = new Vector2(-d.y,d.x) * 0.95f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a-n, color, Vector2.zero); mesh.AddVert(a+n, color, Vector2.zero);
            mesh.AddVert(b+n, color, Vector2.zero); mesh.AddVert(b-n, color, Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2); mesh.AddTriangle(start,start+2,start+3);
        }
    }

}
