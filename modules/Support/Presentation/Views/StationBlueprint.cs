using System;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// The station drawn as a blueprint on the flight-control wall: the core hub, docked modules as
    /// glyph tiles shaped by category (solar wings as long panels, radiators as fins, payloads as
    /// blocks), truss struts between docked neighbours, only the legal docking ports as dashed "+"
    /// ghosts, and the grid utilities as links — radiator cooling, relay boost, shield cover — read
    /// from <see cref="OrbitalPlatform"/>, never re-derived. With no station it draws the pad and the
    /// launch tower. Pooled at build; repainted on data change.
    /// </summary>
    internal sealed class StationBlueprint
    {
        private const int Cells = OrbitalPlatform.CellCount;
        private const int Links = 16;
        private const int Struts = 44;
        private const float Gap = 22f;

        private sealed class Tile
        {
            public RectTransform Root;
            public RoomControl Control;
            public Image Fill, Pattern, Glyph;
            public Image[] Edge;
            public Image[] Fins;
            public TMP_Text Code, State;
            public Image[] Port;
            public TMP_Text PortName;
            public Image PortPlus, PortPlus2;
            public Image Shield;
        }

        private readonly Tile[] tiles = new Tile[Cells];
        private readonly Image[] struts = new Image[Struts];
        private readonly Image[] cooling = new Image[Links];
        private readonly Image[] boosts = new Image[Links];
        private readonly int[] around = new int[4];
        private Image select;
        private RectTransform ghost;
        private Image ghostGlyph;
        private TMP_Text ghostText;
        private RectTransform pad;
        private float cell;
        private Vector2 origin;
        private Action<int> onCell;

        /// <summary>The cells' popping order for the entrance, and how far the strokes have drawn.</summary>
        private float drawn = 1f;
        private readonly bool[] occupied = new bool[Cells];

        public void Build(RectTransform parent, Rect at, Action<int> clickCell)
        {
            onCell = clickCell;
            cell = Mathf.Floor(Mathf.Min((at.width - Gap * 4f - 40f) / OrbitalPlatform.Columns,
                (at.height - Gap * 2f - 70f) / OrbitalPlatform.Rows));
            float gridW = cell * OrbitalPlatform.Columns + Gap * (OrbitalPlatform.Columns - 1);
            float gridH = cell * OrbitalPlatform.Rows + Gap * (OrbitalPlatform.Rows - 1);
            origin = new Vector2(at.x + (at.width - gridW) * 0.5f, at.y - 26f - (at.height - 70f - gridH) * 0.5f);

            for (int i = 0; i < Struts; i++)
            {
                struts[i] = Lines.Make(parent, StationStyle.Line.WithAlpha(0.55f));
                struts[i].enabled = false;
            }
            for (int i = 0; i < Links; i++)
            {
                cooling[i] = Lines.Make(parent, StationStyle.Limb);
                boosts[i] = Lines.Make(parent, StationStyle.Ink.WithAlpha(0.85f), OpsSprites.Dash);
                cooling[i].enabled = boosts[i].enabled = false;
            }
            for (int i = 0; i < Cells; i++) tiles[i] = BuildTile(parent, i);
            select = AvKit.Panel(parent, new Rect(0f, 0f, cell + 20f, cell + 20f), StationStyle.Ink, OpsSprites.Brackets);
            select.enabled = false;
            BuildGhost(parent);
            BuildPad(parent);
        }

        public Rect CellRect(int index)
        {
            int col = OrbitalPlatform.Column(index), row = OrbitalPlatform.Row(index);
            return new Rect(origin.x + col * (cell + Gap), origin.y - row * (cell + Gap), cell, cell);
        }

        private Vector2 Centre(int index)
        {
            Rect r = CellRect(index);
            return new Vector2(r.x + r.width * 0.5f, r.y - r.height * 0.5f);
        }

        private Tile BuildTile(RectTransform parent, int index)
        {
            Rect r = CellRect(index);
            var tile = new Tile();
            tile.Control = RoomControl.Create(parent, r, () => onCell?.Invoke(index), "Cell");
            tile.Root = tile.Control.Rect;
            tile.Root.pivot = new Vector2(0.5f, 0.5f);
            tile.Root.anchoredPosition = new Vector2(r.x + r.width * 0.5f, r.y - r.height * 0.5f);
            var local = new Rect(0f, 0f, r.width, r.height);
            // Docking-port ghost: a dashed square, a plus and the cell name.
            tile.Port = new Image[4];
            for (int e = 0; e < 4; e++) tile.Port[e] = Lines.Make(tile.Root, StationStyle.Line.WithAlpha(0.8f), OpsSprites.Dash);
            float m = r.width * 0.18f, w = r.width;
            Lines.Set(tile.Port[0], m, -m, w - m, -m, 1.5f);
            Lines.Set(tile.Port[1], m, -(w - m), w - m, -(w - m), 1.5f);
            Lines.Set(tile.Port[2], m, -m, m, -(w - m), 1.5f);
            Lines.Set(tile.Port[3], w - m, -m, w - m, -(w - m), 1.5f);
            tile.PortPlus = AvKit.Rule(tile.Root, new Rect(w * 0.5f - 12f, -w * 0.5f + 1f, 24f, 2f), StationStyle.Line);
            tile.PortPlus2 = AvKit.Rule(tile.Root, new Rect(w * 0.5f - 1f, -w * 0.5f + 12f, 2f, 24f), StationStyle.Line);
            tile.PortName = StationStyle.Text(tile.Root, "", new Rect(0f, -(w - m) - 2f, w, 16f), 10f, StationStyle.Dim,
                StationStyle.LabelTracking, TextAlignmentOptions.Center);

            tile.Shield = AvKit.Panel(tile.Root, new Rect(-6f, 6f, w + 12f, w + 12f), StationStyle.Limb.WithAlpha(0.18f));
            tile.Fill = AvKit.Panel(tile.Root, local, StationStyle.Console);
            tile.Pattern = AvKit.Panel(tile.Root, local, StationStyle.Line.WithAlpha(0.35f));
            tile.Pattern.sprite = OpsSprites.Blueprint;
            tile.Pattern.type = Image.Type.Tiled;
            tile.Edge = AvKit.Outline(tile.Root, local, StationStyle.Line);
            tile.Fins = new Image[5];
            for (int f = 0; f < tile.Fins.Length; f++)
                tile.Fins[f] = AvKit.Rule(tile.Root, new Rect(0f, 0f, 6f, 10f), StationStyle.Line);
            tile.Glyph = AvKit.Panel(tile.Root, new Rect(w * 0.5f - 18f, -w * 0.5f + 26f, 36f, 36f), StationStyle.Ink);
            tile.Code = StationStyle.Text(tile.Root, "", new Rect(0f, -w * 0.5f - 12f, w, 18f), 12f, StationStyle.Ink,
                StationStyle.LabelTracking, TextAlignmentOptions.Center, true);
            tile.State = StationStyle.Text(tile.Root, "", new Rect(0f, -w + 20f, w, 14f), 10f, StationStyle.Dim,
                4f, TextAlignmentOptions.Center);
            tile.Control.Changed = c => tile.Fill.color = c.Hovered ? Color.Lerp(StationStyle.Console, StationStyle.Line, 0.18f)
                : StationStyle.Console;
            return tile;
        }

        private void BuildGhost(RectTransform parent)
        {
            var go = new GameObject("Ghost", typeof(RectTransform));
            ghost = (RectTransform)go.transform;
            ghost.SetParent(parent, false);
            AvKit.Place(ghost, new Rect(0f, 0f, cell, cell));
            var local = new Rect(0f, 0f, cell, cell);
            AvKit.Panel(ghost, local, StationStyle.Limb.WithAlpha(0.14f));
            AvKit.Outline(ghost, local, StationStyle.Limb);
            ghostGlyph = AvKit.Panel(ghost, new Rect(cell * 0.5f - 18f, -cell * 0.5f + 26f, 36f, 36f), StationStyle.Limb);
            ghostText = StationStyle.Text(ghost, "", new Rect(-20f, -cell - 2f, cell + 40f, 16f), 10f, StationStyle.Limb,
                4f, TextAlignmentOptions.Center);
            go.SetActive(false);
        }

        /// <summary>A launch-stack elevation on its pad, for the no-station state.</summary>
        private void BuildPad(RectTransform parent)
        {
            var go = new GameObject("Pad", typeof(RectTransform));
            pad = (RectTransform)go.transform;
            pad.SetParent(parent, false);
            Rect core = CellRect(OrbitalPlatform.CoreCell);
            float cx = core.x + core.width * 0.5f;
            float baseY = core.y - core.height - Gap;
            AvKit.Place(pad, new Rect(0f, 0f, 1f, 1f));
            Color line = StationStyle.Line, ink = StationStyle.Ink;
            AvKit.Rule(pad, new Rect(cx - 150f, baseY, 300f, 2f), ink);
            for (int i = 0; i < 7; i++) AvKit.Rule(pad, new Rect(cx - 150f + i * 50f, baseY - 2f, 1f, 8f), line.WithAlpha(0.65f));
            float towerX = cx + 68f, towerTop = core.y + 70f;
            float towerH = towerTop - baseY;
            AvKit.Rule(pad, new Rect(towerX, towerTop, 2f, towerH), line);
            AvKit.Rule(pad, new Rect(towerX + 25f, towerTop, 2f, towerH), line);
            for (int i = 0; i < 8; i++)
            {
                float y0 = towerTop - i * towerH / 8f, y1 = towerTop - (i + 1) * towerH / 8f;
                Image brace = Lines.Make(pad, line.WithAlpha(0.7f));
                Lines.Set(brace, towerX + 1f, y0, towerX + 26f, y1, 1f);
            }
            AvKit.Rule(pad, new Rect(cx + 24f, towerTop - 50f, towerX - cx - 22f, 2f), line);
            AvKit.Rule(pad, new Rect(cx + 24f, baseY + 64f, towerX - cx - 22f, 2f), line);
            float rocketTop = towerTop + 10f, bodyTop = rocketTop - 42f;
            float bodyH = bodyTop - baseY - 8f;
            var bodyRect = new Rect(cx - 24f, bodyTop, 48f, bodyH);
            AvKit.Panel(pad, bodyRect, StationStyle.Console);
            AvKit.Outline(pad, bodyRect, ink);
            Image nose = AvKit.Panel(pad, new Rect(cx - 24f, rocketTop, 48f, 42f), ink);
            nose.sprite = OpsSprites.Triangle;
            // Rotate about the nose's centre, not its corner, so it sits on the body.
            Lines.Centre(nose.rectTransform, cx, rocketTop - 21f, 48f, 42f);
            nose.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
            for (int i = 0; i < 3; i++)
            {
                float y = bodyTop - 47f - i * Mathf.Max(20f, (bodyH - 88f) / 2f);
                AvKit.Rule(pad, new Rect(cx - 24f, y, 48f, 3f), ink.WithAlpha(0.85f));
                AvKit.Rule(pad, new Rect(cx - 16f, y - 10f, 32f, 1f), line.WithAlpha(0.65f));
            }
            AvKit.Rule(pad, new Rect(cx, bodyTop - 8f, 1f, 26f), line.WithAlpha(0.7f));
            Image core2 = AvKit.Panel(pad, new Rect(cx - 13f, bodyTop - 37f, 26f, 26f), ink);
            core2.sprite = OpsSprites.Glyph((int)ModuleKind.Core);
            for (int side = -1; side <= 1; side += 2)
            {
                float bx = cx + side * 42f - 10f;
                float boosterTop = baseY + Mathf.Min(132f, bodyH * 0.66f);
                var booster = new Rect(bx, boosterTop, 20f, boosterTop - baseY - 8f);
                AvKit.Panel(pad, booster, StationStyle.Console);
                AvKit.Outline(pad, booster, line);
                Image cap = AvKit.Panel(pad, new Rect(bx, boosterTop + 18f, 20f, 18f), line);
                cap.sprite = OpsSprites.Triangle;
                Lines.Centre(cap.rectTransform, bx + 10f, boosterTop + 9f, 20f, 18f);
                cap.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
                AvKit.Rule(pad, new Rect(side < 0 ? bx + 20f : cx + 24f, baseY + 56f, 8f, 2f), ink);
            }
            for (int i = -1; i <= 1; i++)
                AvKit.Outline(pad, new Rect(cx + i * 15f - 5f, baseY + 4f, 10f, 12f), line);
            StationStyle.Text(pad, "LV-01  /  BASTION CORE", new Rect(cx - 145f, baseY - 20f, 290f, 14f),
                10f, StationStyle.Limb, 3f, TextAlignmentOptions.Center);
            go.SetActive(false);
        }

        // ---- Paint ------------------------------------------------------------------------------

        /// <summary>Repaint from the station. <paramref name="preview"/> ghosts a module on <paramref name="previewCell"/>.</summary>
        public void Paint(OrbitalPlatform platform, double now, int selected, ModuleKind preview, int previewCell, string previewText)
        {
            bool station = platform != null && platform.Exists;
            if (pad.gameObject.activeSelf == station) pad.gameObject.SetActive(!station);
            int strut = 0, cool = 0, boost = 0;
            for (int i = 0; i < Cells; i++)
            {
                Tile tile = tiles[i];
                ModuleKind kind = station ? platform.Cell(i) : ModuleKind.None;
                bool pendingHere = station && platform.Pending != ModuleKind.None && platform.Pending != ModuleKind.Cargo &&
                                   platform.PendingCell == i;
                bool port = station && kind == ModuleKind.None && platform.CanAttach(i);
                occupied[i] = kind != ModuleKind.None;
                bool show = kind != ModuleKind.None || port || pendingHere;
                if (tile.Root.gameObject.activeSelf != show) tile.Root.gameObject.SetActive(show);
                if (!show) continue;
                SetPort(tile, port && !pendingHere, i);
                SetModule(tile, pendingHere ? platform.Pending : kind, platform, i, now, pendingHere);
                tile.Control.WithTooltip(kind != ModuleKind.None
                    ? OrbitalPlatform.CellName(i) + " · " + PlatformModules.Info(kind).Name + " — click to inspect; J jettisons."
                    : pendingHere ? OrbitalPlatform.CellName(i) + " · " + PlatformModules.Info(platform.Pending).Name + " docking."
                    : OrbitalPlatform.CellName(i) + " · legal docking port — click to select it for the next launch.");

                if (kind == ModuleKind.None || !station) continue;
                OrbitalPlatform.Neighbours(i, around);
                for (int n = 0; n < 4; n++)
                {
                    int other = around[n];
                    if (!OrbitalPlatform.InGrid(other) || other < i || platform.Cell(other) == ModuleKind.None) continue;
                    if (strut + 1 >= Struts) break;
                    Vector2 a = Centre(i), b = Centre(other);
                    Vector2 d = (b - a).normalized, p = new Vector2(-d.y, d.x) * 5f;
                    Lines.Set(struts[strut++], a.x + p.x, a.y + p.y, b.x + p.x, b.y + p.y, 1.5f);
                    Lines.Set(struts[strut++], a.x - p.x, a.y - p.y, b.x - p.x, b.y - p.y, 1.5f);
                    ModuleKind k1 = kind, k2 = platform.Cell(other);
                    // Utilities as links: radiator cooling (solid) and relay boost (dashed).
                    bool cools = (k1 == ModuleKind.Radiator && PlatformModules.Info(k2).Hot) ||
                                 (k2 == ModuleKind.Radiator && PlatformModules.Info(k1).Hot);
                    bool boosts2 = (k1 == ModuleKind.Relay && (k2 == ModuleKind.Imager || k2 == ModuleKind.Sigint)) ||
                                   (k2 == ModuleKind.Relay && (k1 == ModuleKind.Imager || k1 == ModuleKind.Sigint));
                    if (cools && cool < Links) Lines.Set(cooling[cool++], a.x, a.y, b.x, b.y, 3f);
                    if (boosts2 && boost < Links) Lines.Set(boosts[boost++], a.x, a.y, b.x, b.y, 3f);
                }
            }
            for (int i = strut; i < Struts; i++) struts[i].enabled = false;
            for (int i = cool; i < Links; i++) cooling[i].enabled = false;
            for (int i = boost; i < Links; i++) boosts[i].enabled = false;

            bool hasSelection = station && OrbitalPlatform.InGrid(selected);
            select.enabled = hasSelection;
            if (hasSelection)
            {
                Rect r = CellRect(selected);
                AvKit.Place(select.rectTransform, new Rect(r.x - 10f, r.y + 10f, r.width + 20f, r.height + 20f));
            }

            bool showGhost = station && preview != ModuleKind.None && OrbitalPlatform.InGrid(previewCell) &&
                             platform.Cell(previewCell) == ModuleKind.None;
            if (ghost.gameObject.activeSelf != showGhost) ghost.gameObject.SetActive(showGhost);
            if (showGhost)
            {
                Rect r = CellRect(previewCell);
                AvKit.Place(ghost, r);
                ghostGlyph.sprite = OpsSprites.Glyph((int)preview);
                if (ghostText.text != previewText) ghostText.text = previewText ?? "";
            }
            ApplyDraw();
        }

        private static void SetPort(Tile tile, bool on, int index)
        {
            for (int e = 0; e < 4; e++) tile.Port[e].enabled = on;
            tile.PortPlus.enabled = tile.PortPlus2.enabled = on;
            string name = on ? OrbitalPlatform.CellName(index) : "";
            if (tile.PortName.text != name) tile.PortName.text = name;
        }

        private void SetModule(Tile tile, ModuleKind kind, OrbitalPlatform platform, int index, double now, bool pending)
        {
            bool module = kind != ModuleKind.None;
            tile.Fill.enabled = module;
            tile.Glyph.enabled = module;
            tile.Pattern.enabled = false;
            for (int e = 0; e < 4; e++) tile.Edge[e].enabled = module;
            for (int f = 0; f < tile.Fins.Length; f++) tile.Fins[f].enabled = false;
            tile.Shield.enabled = false;
            if (!module)
            {
                if (tile.Code.text.Length > 0) tile.Code.text = "";
                if (tile.State.text.Length > 0) tile.State.text = "";
                return;
            }
            ModuleInfo info = PlatformModules.Info(kind);
            float w = cell;
            // The shape says the category before the code does.
            Rect body;
            switch (info.Category)
            {
                case ModuleCategory.Power: body = new Rect(0f, -w * 0.28f, w, w * 0.44f); break;
                case ModuleCategory.Core: body = new Rect(w * 0.08f, -w * 0.08f, w * 0.84f, w * 0.84f); break;
                default: body = new Rect(w * 0.14f, -w * 0.14f, w * 0.72f, w * 0.72f); break;
            }
            bool fins = kind == ModuleKind.Radiator;
            if (fins) body = new Rect(w * 0.1f, -w * 0.06f, w * 0.8f, w * 0.88f);
            AvKit.Place(tile.Fill.rectTransform, body);
            AvKit.Place(tile.Edge[0].rectTransform, new Rect(body.x, body.y, body.width, 1.5f));
            AvKit.Place(tile.Edge[1].rectTransform, new Rect(body.x, body.y - body.height + 1.5f, body.width, 1.5f));
            AvKit.Place(tile.Edge[2].rectTransform, new Rect(body.x, body.y, 1.5f, body.height));
            AvKit.Place(tile.Edge[3].rectTransform, new Rect(body.x + body.width - 1.5f, body.y, 1.5f, body.height));
            if (info.Category == ModuleCategory.Power || info.Category == ModuleCategory.Weapon)
            {
                tile.Pattern.enabled = true;
                AvKit.Place(tile.Pattern.rectTransform, body);
                tile.Pattern.sprite = info.Category == ModuleCategory.Power ? OpsSprites.Blueprint : OpsSprites.Guard;
            }
            if (fins)
            {
                tile.Fill.enabled = false;
                for (int e = 0; e < 4; e++) tile.Edge[e].enabled = false;
                for (int f = 0; f < tile.Fins.Length; f++)
                {
                    tile.Fins[f].enabled = true;
                    AvKit.Place(tile.Fins[f].rectTransform, new Rect(body.x + f * (body.width - 6f) / 4f, body.y, 6f, body.height));
                }
            }
            tile.Glyph.sprite = OpsSprites.Glyph((int)kind);
            bool online = pending || platform.IsOnline(index, now);
            bool hot = !pending && info.Hot && !platform.IsCooled(index, now);
            Color ink = pending ? StationStyle.Limb : !online ? AvTheme.RailDanger : hot ? AvTheme.RailCaution : StationStyle.Ink;
            tile.Glyph.color = ink;
            for (int e = 0; e < 4; e++) tile.Edge[e].color = pending ? StationStyle.Limb : online ? StationStyle.Line : AvTheme.RailDanger;
            for (int f = 0; f < tile.Fins.Length; f++) tile.Fins[f].color = StationStyle.Line;
            string code = info.Code;
            if (tile.Code.text != code) tile.Code.text = code;
            tile.Code.color = ink;
            string state = pending ? "DOCKING " + PlatformWords.Clock(platform.DockAt - now)
                : !online ? "OFFLINE " + PlatformWords.Clock(platform.OfflineRemaining(index, now))
                : hot ? "HOT · NEEDS RAD"
                : (kind == ModuleKind.Imager || kind == ModuleKind.Sigint) && platform.IsBoosted(index, now) ? "+35% REL"
                : info.Hot ? "COOLED" : "";
            if (tile.State.text != state) tile.State.text = state;
            tile.State.color = !online ? AvTheme.RailDanger : hot ? AvTheme.RailCaution : StationStyle.Dim;
            if (kind != ModuleKind.Shield && platform.IsShielded(index))
            {
                tile.Shield.enabled = true;
                tile.Shield.color = StationStyle.Limb.WithAlpha(0.16f);
            }
        }

        // ---- Entrance ---------------------------------------------------------------------------

        /// <summary>Strokes draw in first, then modules pop onto their ports.</summary>
        public void Entrance(float p)
        {
            drawn = p;
            ApplyDraw();
        }

        private void ApplyDraw()
        {
            float strokes = Mathf.Clamp01(drawn / 0.5f);
            for (int i = 0; i < Struts; i++) struts[i].rectTransform.localScale = new Vector3(strokes, 1f, 1f);
            for (int i = 0; i < Links; i++)
            {
                cooling[i].rectTransform.localScale = new Vector3(strokes, 1f, 1f);
                boosts[i].rectTransform.localScale = new Vector3(strokes, 1f, 1f);
            }
            int order = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (tiles[i] == null) continue;
                float start = 0.35f + (occupied[i] ? order++ * 0.04f : 0f);
                float p = Mathf.Clamp01((drawn - start) / 0.3f);
                float eased = Domain.Layout.Motion.EaseOutCubic(p);
                float scale = Mathf.Lerp(0.55f, 1f, eased);
                tiles[i].Root.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
