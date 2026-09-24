using System;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Presentation.Board;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// The compact CYBER wire netmap on OPS › CYBER › STATUS: nodes as small hexes by state, links
    /// from Cyber Command, in-reach targets ringed, incidents as red marks, on a
    /// <see cref="BoardSurface"/>. A click on a node opens the console on it, and the caption says so.
    /// </summary>
    internal sealed class MiniNetmap
    {
        private const int Slots = CyberNetwork.SlotCount;
        private const int Incidents = CyberNetwork.IncidentSlots;
        private const float Hex = 13f;

        private BoardSurface board;
        private RectTransform layer;
        private readonly Image[] hexes = new Image[Slots];
        private readonly Image[] rings = new Image[Slots];
        private readonly Image[] links = new Image[Slots];
        private readonly Image[] threats = new Image[Incidents];
        private readonly float[] xs = new float[Slots];
        private readonly float[] zs = new float[Slots];
        private Image selection;
        private TMP_Text empty;
        private Action<int> onNode;

        public void Build(RectTransform parent, Rect view, Action<int> clickNode)
        {
            onNode = clickNode;
            OpsSprites.Ensure();
            AvKit.Panel(parent, view, AvTheme.Ground);
            Image lattice = AvKit.Panel(parent, view, AvTheme.RailInfo.WithAlpha(0.09f), OpsSprites.Lattice);
            lattice.type = Image.Type.Tiled;
            lattice.raycastTarget = false;
            AvKit.Outline(parent, view, AvTheme.Hairline);
            var map = new Rect(view.x, view.y, view.width, view.height - 18f);
            board = new BoardSurface(parent, map, map, true, true);
            board.Clicked = (local, button) =>
            {
                int hit = board.Hit(local);
                if (hit >= 0) onNode?.Invoke(hit);
            };
            var go = new GameObject("MiniWire", typeof(RectTransform));
            layer = (RectTransform)go.transform;
            layer.SetParent(board.InputLayer, false);
            AvKit.Place(layer, new Rect(-map.x, -map.y, map.x + map.width, map.height - map.y));
            for (int i = 0; i < Slots; i++)
            {
                links[i] = Lines.Make(layer, AvTheme.Hairline);
                links[i].enabled = false;
            }
            for (int i = 0; i < Slots; i++)
            {
                rings[i] = AvKit.Panel(layer, new Rect(0f, 0f, 22f, 22f), AvTheme.RailInfo, OpsSprites.DottedRing);
                rings[i].type = Image.Type.Simple;
                rings[i].enabled = false;
                hexes[i] = AvKit.Panel(layer, new Rect(0f, 0f, Hex, Hex), AvTheme.Dim, OpsSprites.HexFill);
                hexes[i].type = Image.Type.Simple;
                hexes[i].enabled = false;
            }
            selection = AvKit.Panel(layer, new Rect(0f, 0f, 24f, 24f), AvTheme.TextPrimary, OpsSprites.HexLine);
            selection.type = Image.Type.Simple;
            selection.enabled = false;
            for (int i = 0; i < Incidents; i++)
            {
                threats[i] = AvKit.Panel(layer, new Rect(0f, 0f, 12f, 12f), AvTheme.RailDanger);
                threats[i].sprite = OpsSprites.Glyph(OpsSprites.G.Alert);
                threats[i].enabled = false;
            }
            AvKit.Label(parent, "CLICK A NODE TO OPEN IT IN THE CONSOLE", new Rect(view.x + 6f, view.y - view.height + 17f,
                view.width - 12f, 16f), AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Image osd = AvKit.Panel(parent, new Rect(view.x + 1f, view.y - 1f, view.width - 2f, 16f),
                AvTheme.SurfaceInert.WithAlpha(0.88f));
            osd.raycastTarget = false;
            AvKit.Label(parent, "AEGIS NET / NODE MESH", new Rect(view.x + 8f, view.y - 3f,
                view.width - 16f, 14f), AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Bold);
            empty = AvKit.Label(parent, "", new Rect(view.x + 8f, view.y - (view.height - 18f) * 0.5f + 8f, view.width - 16f, 16f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        public void Paint(CyberNetwork network, double now, int selected)
        {
            int count = 0;
            for (int i = 0; i < Slots && network != null; i++)
            {
                if (!network.Exists(i)) continue;
                xs[count] = network.Node(i).X;
                zs[count++] = network.Node(i).Z;
            }
            board.Fit(xs, zs, count, 20000f, count >= 8 ? 10f : 0f, 12f);
            string emptyText = network == null ? "AWAITING THEATER DATA" : !network.HasCommand ? "NO AIRBASE HELD · NO NETWORK" : "";
            if (empty.text != emptyText) empty.text = emptyText;
            int command = network != null ? network.CommandSlot : -1;
            Vector2 c2 = command >= 0 ? board.Project(network.Node(command).X, network.Node(command).Z) : default;
            board.ClearMarkers();
            for (int i = 0; i < Slots; i++)
            {
                bool show = network != null && network.Exists(i);
                hexes[i].enabled = show;
                if (!show)
                {
                    rings[i].enabled = false;
                    links[i].enabled = false;
                    continue;
                }
                CyberNode n = network.Node(i);
                Vector2 p = board.Project(n.X, n.Z);
                Lines.Centre(hexes[i].rectTransform, p.x, p.y, Hex);
                board.AddMarker(i, p, 12f);
                bool mine = n.Static || n.Hacked;
                bool bad = n.Compromised || (n.Static && n.Down);
                hexes[i].color = bad ? AvTheme.RailDanger : n.Isolated ? AvTheme.Disabled
                    : n.Static ? AvTheme.TextPrimary : n.Hacked ? AvTheme.RailInfo : AvTheme.Dim;
                bool reach = !mine && network.CheckBreach(i, now) == BreachDenial.None;
                rings[i].enabled = reach;
                if (reach) Lines.Centre(rings[i].rectTransform, p.x, p.y, 22f);
                if (mine && i != command && command >= 0 && !n.Isolated)
                {
                    Lines.Set(links[i], c2.x, c2.y, p.x, p.y, 1f);
                    links[i].color = bad ? AvTheme.RailDanger.WithAlpha(0.7f) : AvTheme.Hairline;
                }
                else links[i].enabled = false;
            }
            bool picked = network != null && network.Exists(selected);
            selection.enabled = picked;
            if (picked)
            {
                Vector2 p = board.Project(network.Node(selected).X, network.Node(selected).Z);
                Lines.Centre(selection.rectTransform, p.x, p.y, 24f);
            }
            for (int i = 0; i < Incidents; i++)
            {
                bool on = network != null && network.IncidentActive(i);
                threats[i].enabled = on;
                if (!on) continue;
                CyberIncident incident = network.Incident(i);
                Vector2 p = board.Project(incident.X, incident.Z);
                Lines.Centre(threats[i].rectTransform, p.x + 8f, p.y + 8f, 12f);
            }
        }
    }
}
