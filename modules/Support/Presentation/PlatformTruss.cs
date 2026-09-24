using System;
using BoscaliSummer.Features.Support.Domain.Orbital;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The station truss, drawn cell by cell with connectors between docked neighbours.
    ///
    /// <para>One renderer serves both sizes: the glanceable schematic on the OPS ›
    /// SPACE › STATUS page and the large design surface in the full-screen station
    /// console. Compact cells carry the module code and its one-word condition; large
    /// cells add the cell name, the module name and its mass, so the console needs no
    /// legend. Nothing here decides anything — it paints the station model and reports
    /// clicks.</para>
    /// </summary>
    internal sealed class PlatformTruss
    {
        private const float Gap = 6f;
        private const float LargeGap = 12f;

        private readonly Image[] fill = new Image[OrbitalPlatform.CellCount];
        private readonly Image[][] frame = new Image[OrbitalPlatform.CellCount][];
        private readonly TMP_Text[] code = new TMP_Text[OrbitalPlatform.CellCount];
        private readonly TMP_Text[] tag = new TMP_Text[OrbitalPlatform.CellCount];
        private readonly TMP_Text[] cellName = new TMP_Text[OrbitalPlatform.CellCount];
        private readonly TMP_Text[] moduleName = new TMP_Text[OrbitalPlatform.CellCount];
        private readonly string[] lastCode = new string[OrbitalPlatform.CellCount];
        private readonly string[] lastTag = new string[OrbitalPlatform.CellCount];
        private readonly string[] lastName = new string[OrbitalPlatform.CellCount];
        private readonly Image[] across = new Image[OrbitalPlatform.Rows * (OrbitalPlatform.Columns - 1)];
        private readonly Image[] down = new Image[(OrbitalPlatform.Rows - 1) * OrbitalPlatform.Columns];
        private readonly bool large;

        /// <summary>Height a truss needs for a given cell height, including the gaps between rows.</summary>
        public static float Height(float cellHeight, bool large) =>
            OrbitalPlatform.Rows * cellHeight + (OrbitalPlatform.Rows - 1) * (large ? LargeGap : Gap);

        /// <summary>Category tint, shared with the catalogue and the ability cards.</summary>
        public static Color Colour(ModuleCategory category)
        {
            switch (category)
            {
                case ModuleCategory.Power: return AvTheme.RailCaution;
                case ModuleCategory.Utility: return AvTheme.RailInfo;
                case ModuleCategory.Sensor: return AvTheme.RailReady;
                case ModuleCategory.Weapon: return AvTheme.RailDanger;
                case ModuleCategory.Mobility: return AvTheme.Warning;
                default: return AvTheme.TextPrimary;
            }
        }

        public PlatformTruss(RectTransform parent, Rect area, float cellHeight, bool large, Action<int> onCell)
        {
            this.large = large;
            float gap = large ? LargeGap : Gap;
            float cellWidth = (area.width - gap * (OrbitalPlatform.Columns - 1)) / OrbitalPlatform.Columns;

            for (int cell = 0; cell < OrbitalPlatform.CellCount; cell++)
            {
                float cx = area.x + OrbitalPlatform.Column(cell) * (cellWidth + gap);
                float cy = area.y - OrbitalPlatform.Row(cell) * (cellHeight + gap);
                var box = new Rect(cx, cy, cellWidth, cellHeight);

                fill[cell] = AvKit.Panel(parent, box, Color.clear, AvSprites.Card);
                frame[cell] = AvKit.Outline(parent, box, AvTheme.Hairline);

                if (large)
                {
                    cellName[cell] = AvKit.Label(parent, OrbitalPlatform.CellName(cell),
                        new Rect(cx + 8f, cy - 6f, cellWidth - 16f, 12f), AvTheme.Disabled,
                        AvTokens.FontMicro, FontStyles.Bold);
                    code[cell] = AvKit.Label(parent, "", new Rect(cx, cy - cellHeight * 0.5f + 18f, cellWidth, 30f),
                        AvTheme.TextPrimary, 26f, FontStyles.Bold, TextAlignmentOptions.Center);
                    moduleName[cell] = AvKit.Label(parent, "",
                        new Rect(cx + 4f, cy - cellHeight * 0.5f - 14f, cellWidth - 8f, 14f), AvTheme.Dim,
                        AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);
                    tag[cell] = AvKit.Label(parent, "", new Rect(cx + 4f, cy - cellHeight + 22f, cellWidth - 8f, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
                }
                else
                {
                    code[cell] = AvKit.Label(parent, "", new Rect(cx, cy - 2f, cellWidth, 14f),
                        AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
                    tag[cell] = AvKit.Label(parent, "", new Rect(cx + 2f, cy - 14f, cellWidth - 4f, 11f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
                }

                if (onCell == null) continue;
                int index = cell;
                AvKit.HitButton(parent, box, () => onCell(index))
                    .WithTooltip(OrbitalPlatform.CellName(cell) + " — select this cell.");
            }

            for (int row = 0; row < OrbitalPlatform.Rows; row++)
            {
                for (int column = 0; column < OrbitalPlatform.Columns - 1; column++)
                {
                    float cx = area.x + (column + 1) * (cellWidth + gap) - gap;
                    float cy = area.y - row * (cellHeight + gap) - cellHeight * 0.5f + 2f;
                    across[row * (OrbitalPlatform.Columns - 1) + column] =
                        AvKit.Rule(parent, new Rect(cx, cy, gap, 4f), Color.clear);
                }
            }
            for (int row = 0; row < OrbitalPlatform.Rows - 1; row++)
            {
                for (int column = 0; column < OrbitalPlatform.Columns; column++)
                {
                    float cx = area.x + column * (cellWidth + gap) + cellWidth * 0.5f - 2f;
                    float cy = area.y - (row + 1) * (cellHeight + gap) + gap;
                    down[row * OrbitalPlatform.Columns + column] =
                        AvKit.Rule(parent, new Rect(cx, cy, 4f, gap), Color.clear);
                }
            }
        }

        /// <summary>Paint every cell from the station. <paramref name="selected"/> outlines one cell white.</summary>
        public void Paint(OrbitalPlatform platform, double now, int selected)
        {
            bool station = platform != null && platform.Exists;
            for (int cell = 0; cell < OrbitalPlatform.CellCount; cell++)
            {
                ModuleKind kind = station ? platform.Cell(cell) : ModuleKind.None;
                bool pending = station && platform.Pending != ModuleKind.None &&
                               platform.Pending != ModuleKind.Cargo && platform.PendingCell == cell;
                string glyph, condition, name;
                Color border, wash, text;

                if (kind != ModuleKind.None)
                {
                    ModuleInfo info = PlatformModules.Info(kind);
                    Color colour = Colour(info.Category);
                    bool online = platform.IsOnline(cell, now);
                    glyph = info.Code;
                    name = info.Name;
                    condition = Condition(platform, cell, kind, now);
                    border = online ? colour.WithAlpha(0.85f) : AvTheme.RailDanger;
                    wash = online ? colour.WithAlpha(0.16f) : AvTheme.RailDanger.WithAlpha(0.12f);
                    text = online ? AvTheme.TextPrimary : AvTheme.RailDanger;
                }
                else if (pending)
                {
                    glyph = PlatformModules.Info(platform.Pending).Code;
                    name = PlatformModules.Info(platform.Pending).Name;
                    condition = "DOCK " + PlatformWords.Clock(platform.DockAt - now);
                    border = AvTheme.RailInfo;
                    wash = AvTheme.RailInfo.WithAlpha(0.08f + 0.06f * Mathf.PingPong(Time.unscaledTime * 2f, 1f));
                    text = AvTheme.RailInfo;
                }
                else if (station && platform.CanAttach(cell))
                {
                    glyph = "+";
                    name = large ? "FREE CELL" : "";
                    condition = large ? "DOCKS HERE" : "";
                    border = AvTheme.Hairline.WithAlpha(0.9f);
                    wash = Color.clear;
                    text = AvTheme.Dim;
                }
                else
                {
                    glyph = "";
                    name = "";
                    condition = station || cell != OrbitalPlatform.CoreCell ? "" : large ? "CORE GOES HERE" : "";
                    border = AvTheme.Hairline.WithAlpha(0.25f);
                    wash = Color.clear;
                    text = AvTheme.Disabled;
                }

                if (cell == selected)
                {
                    border = Color.white;
                    wash = wash.a > 0f ? wash.WithAlpha(wash.a + 0.08f) : AvTheme.Accent.WithAlpha(0.08f);
                }

                fill[cell].color = wash;
                for (int i = 0; i < frame[cell].Length; i++) frame[cell][i].color = border;
                if (lastCode[cell] != glyph) code[cell].text = lastCode[cell] = glyph;
                if (lastTag[cell] != condition) tag[cell].text = lastTag[cell] = condition;
                code[cell].color = text;
                bool offline = kind != ModuleKind.None && !platform.IsOnline(cell, now);
                tag[cell].color = offline ? AvTheme.RailDanger : AvTheme.Dim;
                if (!large) continue;
                if (lastName[cell] != name) moduleName[cell].text = lastName[cell] = name;
                moduleName[cell].color = kind != ModuleKind.None ? AvTheme.Dim : AvTheme.Disabled;
                cellName[cell].color = cell == selected ? AvTheme.TextPrimary : AvTheme.Disabled;
            }

            for (int row = 0; row < OrbitalPlatform.Rows; row++)
            {
                for (int column = 0; column < OrbitalPlatform.Columns - 1; column++)
                {
                    int a = row * OrbitalPlatform.Columns + column;
                    bool linked = station && platform.Cell(a) != ModuleKind.None &&
                                  platform.Cell(a + 1) != ModuleKind.None;
                    across[row * (OrbitalPlatform.Columns - 1) + column].color = linked ? AvTheme.Frame : Color.clear;
                }
            }
            for (int row = 0; row < OrbitalPlatform.Rows - 1; row++)
            {
                for (int column = 0; column < OrbitalPlatform.Columns; column++)
                {
                    int a = row * OrbitalPlatform.Columns + column;
                    bool linked = station && platform.Cell(a) != ModuleKind.None &&
                                  platform.Cell(a + OrbitalPlatform.Columns) != ModuleKind.None;
                    down[a].color = linked ? AvTheme.Frame : Color.clear;
                }
            }
        }

        /// <summary>The one-word condition a cell shows: outage, heat, relay boost, shielding.</summary>
        private string Condition(OrbitalPlatform platform, int cell, ModuleKind kind, double now)
        {
            if (!platform.IsOnline(cell, now)) return "OFF " + PlatformWords.Clock(platform.OfflineRemaining(cell, now));
            ModuleInfo info = PlatformModules.Info(kind);
            if (info.Hot && !platform.IsCooled(cell, now)) return "HOT · NEEDS RAD";
            if ((kind == ModuleKind.Imager || kind == ModuleKind.Sigint) && platform.IsBoosted(cell, now)) return "+REL";
            if (info.Hot) return "COOLED";
            if (platform.IsShielded(cell) && kind != ModuleKind.Shield) return "SHIELDED";
            return large ? PlatformWords.Tonnes(info.Mass) : "";
        }
    }
}
