using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    internal sealed partial class CrewOperationsView
    {
        private sealed class Meter
        {
            public AvGaugeGraphic Bar;
            public TMP_Text Value;
            public void Set(float value, float maximum, string label)
            { Bar.Value = maximum > 0 ? value / maximum : 0; Value.text = label; }
        }
        private readonly Meter[] meters = new Meter[3];
        private readonly TMP_Text[] circuitTelemetry = new TMP_Text[3];
        private readonly AvStepProgress[] linkBars = new AvStepProgress[3];
        private readonly Image[] portraits = new Image[4];
        private AvStepProgress routeBar;
        private TMP_Text hardware, deliveryStatus;
        private AvFx acquisition, commandFx;
        private bool commandWasReady;
        private readonly TMP_Text[] assemblyLabels = new TMP_Text[3];
        private static readonly ModuleKind[] AssemblyKinds = { ModuleKind.Relay, ModuleKind.Imager, ModuleKind.Emp };
        private Color BranchSurface => Domain == OpsDomain.Cyber ? CyberStyle.Surface : Domain == OpsDomain.SpecialOperations ? DeskStyle.Map : StationStyle.Surface;
        private Color BranchPane => Domain == OpsDomain.Cyber ? CyberStyle.Pane : Domain == OpsDomain.SpecialOperations ? DeskStyle.Paper : StationStyle.Console;
        private Color BranchInk => Domain == OpsDomain.Cyber ? CyberStyle.Ink : Domain == OpsDomain.SpecialOperations ? DeskStyle.Ink : StationStyle.Ink;
        private Color BranchDim => Domain == OpsDomain.Cyber ? CyberStyle.Dim : Domain == OpsDomain.SpecialOperations ? DeskStyle.Khaki : StationStyle.Dim;
        private Color BranchEdge => Domain == OpsDomain.Cyber ? CyberStyle.PaneEdge : Domain == OpsDomain.SpecialOperations ? DeskStyle.Khaki.WithAlpha(.55f) : StationStyle.ConsoleEdge;
        private Color BranchAccent => Domain == OpsDomain.Cyber ? CyberStyle.Title : Domain == OpsDomain.SpecialOperations ? DeskStyle.Stamp : StationStyle.Line;
        private bool ReducedMotion => (support?.Settings?.ReduceMotion.Value ?? false) || AvFxDriver.ReducedMotion;

        private void BindCommandFeedback()
        {
            Key command = execute ?? fieldOrders[4];
            if (command != null) commandFx = AvFx.On(command.Frame);
        }

        private void BuildAssemblyLabels(RectTransform root, float w, float h)
        {
            for (int i = 0; i < 3; i++)
            {
                float x = i == 1 ? w - 172 : 10, y = i == 0 ? h * .22f : i == 1 ? h * .46f : h * .79f;
                Chrome.Panel(root, new Rect(x, -y, 162, 49), Surface.WithAlpha(.88f));
                assemblyLabels[i] = Mono(root, "", x + 8, y + 5, 148, 44, 16, Dim);
                Chrome.Rule(root, new Rect(x + 8, -y - 49, 144, 1), RoomPaint.Instrument.WithAlpha(.6f));
            }
        }

        private string FieldReport(FieldTeam team, FieldObjective objective)
        {
            if (log != null)
                for (int i = 0; i < System.Math.Min(log.Length, 6); i++)
                    if (!string.IsNullOrEmpty(log[i])) return "NET: " + log[i];
            if (team.State == TeamState.Holding) return FieldWords.Callsign(selectedTeam) + ": post established. Quality " + team.Quality + ". " + team.Charges + " support charges available.\nUse DELIVER for tasking or SUPPLY for espionage stock.";
            if (team.Deployed) return FieldWords.Callsign(selectedTeam) + ": " + (team.Target ?? "objective") + ". " + team.State.ToString().ToUpperInvariant() + ".\nAircrew: clear local threats. Ground support: scout and cover the next leg.";
            return "DESK: " + (objective.Name ?? "select a theater objective") + ". Awaiting mission tasking.\nRecon feeds orbital guidance. Espionage opens shared black-market supply.";
        }

        private void BuildHousing(RectTransform root, float w, float h)
        {
            // Shared kit materials and typography; no room-owned shader or animated simulation.
            Canvas canvas = root.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
                if (canvas.rootCanvas != null) canvas.rootCanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            }
            RoomPaint.Inset(root, new Rect(0, 0, w - 4, h - 4), BranchSurface, BranchEdge);
            Plate(root, new Rect(7, -7, 93, h - 18), BranchPane, BranchEdge.WithAlpha(.7f), 4);
            Plate(root, new Rect(12, -14, 86, 82), BranchSurface, BranchEdge, 2);
            Icon(root, Domain == OpsDomain.Space ? AvIcon.Satellite : Domain == OpsDomain.Cyber ? AvIcon.ShieldLock : AvIcon.UsersGroup, 31, 29, 46, BranchInk);
            Mono(root, Domain == OpsDomain.Space ? "FLT" : Domain == OpsDomain.Cyber ? "SIG" : "FLD", 32, h - 100, 58, 25, 18, BranchDim);
            Mono(root, Domain == OpsDomain.Space ? "01\nBUS\n\n02\nPASS\n\n03\nFIRE" : Domain == OpsDomain.Cyber ? "01\nNET\n\n02\nCODE\n\n03\nEW" : "01\nSOF\n\n02\nINT\n\n03\nLOG", 28, 370, 60, 250, 15, BranchDim);
            Image sweep = Chrome.Rule(root, new Rect(110, -98, w - 128, 1), BranchAccent.WithAlpha(.65f));
            acquisition = AvFx.On(sweep);
            // Static register marks frame the display without competing with the map.
            for (int i = 0; i < 30; i++)
            {
                float x = 114 + (w - 150) * i / 29;
                Chrome.Rule(root, new Rect(x, -h + 9, 1, i % 5 == 0 ? 6 : 3), Edge.WithAlpha(.6f));
            }
        }

        private void Instrument(RectTransform root, float x, float y, float w, float h, string title, AvIcon icon)
        {
            RoomPaint.Inset(root, new Rect(x, -y, w - 3, h - 4), BranchSurface, BranchEdge);
            var cap = Plate(root, new Rect(x + 1, -y - 1, w - 5, 32), BranchPane, BranchEdge.WithAlpha(.65f), 2);
            cap.Stroke = .7f;
            Icon(root, icon, x + 10, y + 7, 19, BranchAccent);
            Text(root, title.ToUpperInvariant(), x + 36, y + 6, w - 46, 24, 16, BranchInk);
            RoomPaint.Brackets(root, new Rect(x, -y, w, h), 9, BranchEdge.WithAlpha(.8f));
        }

        private static void Icon(RectTransform root, AvIcon icon, float x, float y, float size, Color tint)
        {
            TMP_Text glyph = AvIcons.Make(root, icon, size, tint);
            Chrome.Place(glyph.rectTransform, new Rect(x, -y, size, size));
        }

        private static TMP_Text Mono(RectTransform root, string value, float x, float y, float w, float h, float size, Color color)
        {
            TMP_Text text = Text(root, value, x, y, w, h, size, color);
            text.font = AvType.Face(AvFace.Mono); return text;
        }

        private Meter MakeMeter(RectTransform root, float x, float y, float w, string title, bool caution)
        {
            Plate(root, new Rect(x, -y, w, 65), BranchPane, BranchEdge, 2);
            Text(root, title, x + 10, y + 6, w - 20, 22, 16, BranchDim);
            var meter = new Meter { Value = Mono(root, "--", x + 10, y + 28, w - 20, 24, 18, caution ? RoomPaint.Command : BranchInk) };
            meter.Bar = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(x + w * .53f, -y - 38, w * .42f, 9), "TelemetryMeter");
            meter.Bar.Shape = AvGaugeShape.Segments; meter.Bar.Segments = 12; meter.Bar.SegmentGap = 2;
            meter.Bar.Track = BranchEdge.WithAlpha(.25f);
            meter.Bar.FillColor = meter.Bar.FillEnd = caution ? RoomPaint.Command : BranchAccent;
            meter.Bar.raycastTarget = false;
            // Values occupy the left half; the right half is the bounded segmented meter.
            Chrome.Place(meter.Value.rectTransform, new Rect(x + 10, -y - 28, w * .5f - 10, 26));
            return meter;
        }

        private static void DialScale(RectTransform root, float x, float y, float size)
        {
            var inner = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(x + 17, -y - 17, size - 34, size - 34), "GaugeInnerBezel");
            inner.Shape = AvGaugeShape.Ring; inner.Thickness = 2; inner.Track = Edge.WithAlpha(.55f); inner.raycastTarget = false;
            Mono(root, "%", x + size * .44f, y + size * .72f, 30, 25, 16, Dim);
            Mono(root, "0", x + 14, y + size - 27, 25, 22, 16, Dim);
            Mono(root, "100", x + size - 42, y + size - 27, 42, 22, 16, Dim);
        }

        private static void Drafting(RectTransform root, float w, float h)
        {
            var grid = Chrome.Graphic<CrewDraftingGraphic>(root, new Rect(0, 0, w, h), "AssemblyRegister");
            grid.color = Edge.WithAlpha(.2f); grid.raycastTarget = false;
            RoomPaint.Brackets(root, new Rect(4, -4, w - 8, h - 8), 23, RoomPaint.Instrument.WithAlpha(.55f));
        }

        private void PaintFeedback()
        {
            Key command = execute ?? fieldOrders[4];
            bool ready = command != null && command.Control.Enabled;
            if (commandFx != null)
            {
                if (ready && !commandWasReady && !ReducedMotion) commandFx.Play(AvFxKind.Shine, .3f);
                else if (!ready || ReducedMotion) commandFx.Clear();
            }
            commandWasReady = ready;
        }

        private void PaintInstruments(OrbitalPlatform p, CyberNetwork c, SpecOpsDetachment d, double now)
        {
            if (Domain == OpsDomain.Space)
            {
                bool exists = p != null && p.Exists;
                PlatformStats stats = exists ? p.Stats(now) : default;
                for (int i = 0; i < AssemblyKinds.Length; i++)
                {
                    ModuleKind kind = AssemblyKinds[i];
                    bool fitted = exists && p.Fitted(kind), online = fitted && p.FittedOnline(kind, now);
                    assemblyLabels[i].text = (i == 0 ? "COMMS RELAY" : i == 1 ? "SENSOR ARRAY" : "EMP PAYLOAD") + "\n" + (online ? "ONLINE" : fitted ? "OFFLINE" : "NOT FITTED");
                    assemblyLabels[i].color = online ? RoomPaint.Ready : Dim;
                }
                dials[2].Gauge.FillColor = dials[2].Gauge.FillEnd = exists && p.Heat > 50 ? RoomPaint.Command : RoomPaint.Instrument;
                hardware.text = exists ? "ONLINE " + stats.Online + "/" + stats.Modules + " MODULES\nBUS " + stats.SolarKw.ToString("0.0") + " KW / SOLAR" : "NO PLATFORM\nASSEMBLY REFERENCE";
                circuitTelemetry[0].text = exists && p.SolutionRemaining(now) > 0 ? "RECON " + Mathf.CeilToInt((float)p.SolutionRemaining(now)) + " S" : "RECON / NO SOLUTION";
                circuitTelemetry[1].text = "STORAGE " + (exists ? Mathf.FloorToInt(p.Energy) : 0) + " KJ";
                circuitTelemetry[2].text = "COMMIT LIMIT / 50%";
            }
            else if (Domain == OpsDomain.Cyber)
            {
                bool active = c != null && c.BreachActive;
                float trace = active ? c.BreachTrace * 100 : 0;
                meters[0].Set(trace, 100, Mathf.RoundToInt(trace) + "%");
                meters[1].Set(c?.Computing ?? 0, c?.ComputingCapacity() ?? 1, Mathf.FloorToInt(c?.Computing ?? 0).ToString());
                int quality = c == null ? 0 : active ? c.WorkQuality : c.AccessQuality;
                meters[2].Set(quality, 6, quality + "/6");
                for (int i = 0; i < 3; i++) linkBars[i].SetProgress(active && c.LinkSolved(i) ? 1 : 0, 1);
            }
            else
            {
                FieldTeam team = d != null ? d.Team(selectedTeam) : default;
                meters[0].Set(team.Preparation, 100, team.Preparation + "%");
                meters[1].Set(team.Intel, 100, team.Intel + "%");
                meters[2].Set(team.Exposure, 100, team.Exposure + "%");
                routeBar.SetProgress(team.RouteStep, 3);
                for (int i = 0; i < portraits.Length; i++) portraits[i].gameObject.SetActive(i == selectedTeam && team.Formed);
            }
        }
    }

    /// <summary>Static cosmetic drawing registration, bounded to 40 lines. It carries no contacts.</summary>
    internal sealed class CrewDraftingGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect r = rectTransform.rect;
            for (int i = 1; i < 20; i++)
            {
                float x = r.xMin + r.width * i / 20f, y = r.yMin + r.height * i / 20f;
                Quad(mesh, x, r.yMin, 0.6f, r.height, color);
                Quad(mesh, r.xMin, y, r.width, 0.6f, color);
            }
        }
        private static void Quad(VertexHelper m, float x, float y, float w, float h, Color tint)
        {
            int n = m.currentVertCount;
            m.AddVert(new Vector3(x, y), tint, Vector2.zero); m.AddVert(new Vector3(x + w, y), tint, Vector2.zero);
            m.AddVert(new Vector3(x + w, y + h), tint, Vector2.zero); m.AddVert(new Vector3(x, y + h), tint, Vector2.zero);
            m.AddTriangle(n, n + 1, n + 2); m.AddTriangle(n, n + 2, n + 3);
        }
    }
}
