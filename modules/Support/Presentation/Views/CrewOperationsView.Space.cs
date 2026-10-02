using NOAvionics;
using System;
using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Presentation.Board;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    internal sealed partial class CrewOperationsView
    {
        private SpaceCoverageBoard spaceCoverage;
        private TMP_Text spaceOrbit, spacePayload, spaceCircuitStatus, spaceBusStatus, spaceAssemblyRegister;
        private readonly TMP_Text[] spaceResources = new TMP_Text[4];
        private readonly TMP_Text[] spaceResourceValues = new TMP_Text[4];
        private readonly AvGaugeGraphic[] spaceResourceBars = new AvGaugeGraphic[4];
        private readonly SpaceDialFaceGraphic[] spaceDialFaces = new SpaceDialFaceGraphic[3];
        private readonly TMP_Text[] spaceCircuitStates = new TMP_Text[3];
        private Key spaceEngineering, spaceSensor, spaceFrame;
        private AvStepProgress spaceSequence;

        private void BuildSpace(RectTransform root, float w, float h)
        {
            const float gap = 12f;
            float lw = w * .29f, rw = w * .235f, cx = lw + gap, rx = w - rw, cw = rx - cx - gap;
            float assemblyH = h - 248f, mapH = Mathf.Clamp(h * .34f, 250f, 325f);
            float rackY = mapH + gap, rackH = 206f, routeY = rackY + rackH + gap, channelW = (cw - 16f) / 3f;

            Instrument(root, 0, 0, lw, assemblyH, "BASTION / FLIGHT HARDWARE", AvIcon.Satellite);
            hardware = Mono(root, "", 18, 43, lw - 36, 48, 16, Ink);
            var drawing = Group(root, 10, 100, lw - 20, assemblyH - 131);
            OpsArtwork.Draw(drawing, new Rect(0, 0, lw - 20, assemblyH - 131), 0);
            Drafting(drawing, lw - 20, assemblyH - 131);
            BuildAssemblyLabels(drawing, lw - 20, assemblyH - 131);
            for (int i = 0; i < 3; i++)
            {
                float sy = (assemblyH - 131) * (i == 0 ? .28f : i == 1 ? .53f : .8f);
                Image leader = Lines.Make(drawing, RoomPaint.Instrument.WithAlpha(.55f), null, "AssemblyCallout");
                Lines.Set(leader, i == 1 ? lw - 189 : 170, -sy, lw * .51f, -sy - 22, .8f);
            }
            spaceAssemblyRegister = Mono(root, "CONFIGURATION REFERENCE / HOST MODULE STATUS", 16, assemblyH - 29, lw - 32, 23, 12, Dim);
            Instrument(root, 0, assemblyH + gap, lw, h - assemblyH - gap, "POWER / STORES / SERVICE", AvIcon.Activity);
            for (int i = 0; i < spaceResources.Length; i++)
            {
                float y = assemblyH + 53 + i * 35;
                Mono(root, "0" + (i + 1), 16, y + 1, 26, 18, 12, BranchAccent);
                spaceResources[i] = Mono(root, "", 49, y, lw * .47f - 44, 20, 13, Dim);
                spaceResourceValues[i] = Mono(root, "", lw * .46f, y - 1, lw * .54f - 16, 23, 17, Ink);
                spaceResourceValues[i].alignment = TextAlignmentOptions.TopRight;
                Chrome.Rule(root, new Rect(42, -y + 1, 1, 23), Edge.WithAlpha(.5f));
                spaceResourceBars[i] = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(49, -y - 24, lw - 65, 5), "StationResourceBus");
                spaceResourceBars[i].Shape = AvGaugeShape.Segments; spaceResourceBars[i].Segments = 24;
                spaceResourceBars[i].SegmentGap = 2; spaceResourceBars[i].Track = Edge.WithAlpha(.25f);
                spaceResourceBars[i].FillColor = spaceResourceBars[i].FillEnd = RoomPaint.Ready;
                spaceResourceBars[i].raycastTarget = false;
            }
            spaceEngineering = Button(root, 10, h - 47, (lw - 28) / 2, 39, "ENGINEERING", () => engineering?.Invoke(), false, 16);
            spaceSensor = Button(root, 18 + (lw - 28) / 2, h - 47, (lw - 28) / 2, 39, "SENSOR FEED", () => sensors?.Invoke(), false, 16);

            Instrument(root, cx, 0, cw, mapH, "THEATER FOOTPRINT / FIXED SECTOR", AvIcon.MapPin);
            var plot = Group(root, cx + 8, 39, cw - 16, mapH - 70);
            spaceCoverage = new SpaceCoverageBoard(); spaceCoverage.Build(plot, cw - 16, mapH - 70);
            spaceFrame = Button(root, cx + cw - 100, 4, 90, 24, "REACH", () => { spaceCoverage.ToggleFrame(); dirty = true; }, false, 14);
            spaceBusStatus = Mono(root, "", cx + 12, mapH - 27, cw - 24, 21, 13, Dim);

            Instrument(root, cx, rackY, cw, rackH, "FIRE CONTROL / THREE INDEPENDENT CIRCUITS", AvIcon.Target);
            for (int i = 0; i < 3; i++)
            {
                float x = cx + i * (channelW + 8);
                if (i > 0) Chrome.Rule(root, new Rect(x - 4, -rackY - 38, 1, rackH - 48), Edge.WithAlpha(.4f));
                Mono(root, "0" + (i + 1) + " / " + OrbitalPlatform.CircuitName(i), x + 8, rackY + 38, channelW - 16, 22, 14, Dim);
                spaceCircuitStates[i] = Mono(root, "OPEN", x + channelW - 72, rackY + 39, 64, 20, 11, Dim);
                spaceCircuitStates[i].alignment = TextAlignmentOptions.TopRight;
                float dialX = x + (channelW - 126) / 2;
                spaceDialFaces[i] = Chrome.Graphic<SpaceDialFaceGraphic>(root, new Rect(dialX, -rackY - 57, 126, 126), "OrbitalInstrumentBezel");
                spaceDialFaces[i].color = BranchEdge; spaceDialFaces[i].HeatLimit = i == 2;
                spaceDialFaces[i].raycastTarget = false;
                dials[i] = BuildSpaceDial(root, dialX, rackY + 57, 126, i == 0 ? "ALIGN" : i == 1 ? "CHARGE" : "HEAT", i == 2);
                circuitTelemetry[i] = Mono(root, "", x + 8, rackY + 183, channelW - 16, 20, 12, Dim);
                for (int j = 0; j < 3; j++)
                {
                    int channel = i, route = j + 1;
                    circuits[i * 3 + j] = Button(root, x, routeY + j * 55, channelW, 49,
                        OrbitalPlatform.RouteName(i, route), () => support?.RequestCircuit(channel, route), false, 16);
                }
            }
            float trayY = routeY + 166, trayH = h - trayY;
            Instrument(root, cx, trayY, cw, trayH, "PACKAGE AUDIT / FACTION HANDOFF", AvIcon.Radio);
            OpsArtwork.DrawPayload(root, new Rect(cx + 10, -trayY - 38, cw * .29f, Mathf.Max(44, trayH - 74)));
            spaceSequence = new AvStepProgress(root, "CrewCircuitSequence");
            Chrome.Place(spaceSequence.Rect, new Rect(cx + 12, -h + 25, cw * .27f, 9));
            spaceSequence.Paint(RoomPaint.Ready, Edge.WithAlpha(.3f));
            outcome = Text(root, "", cx + cw * .32f, trayY + 40, cw * .68f - 16, trayH - 46, 17, Ink);

            float orbitH = 206, observerY = orbitH + gap, observerH = 182, payloadY = observerY + observerH + gap;
            Instrument(root, rx, 0, rw, orbitH, "ORBIT / STATION KEEPING", AvIcon.Satellite);
            float earth = Mathf.Min(rw * .46f, 154);
            OpsArtwork.Draw(root, new Rect(rx + 10, -44, earth, earth), 2);
            spaceOrbit = Mono(root, "", rx + earth + 19, 43, rw - earth - 30, orbitH - 51, 14, Ink);
            Instrument(root, rx, observerY, rw, observerH, "RECON / OBSERVER HANDOFF", AvIcon.Eye);
            situation = Mono(root, "", rx + 14, observerY + 42, rw - 28, observerH - 51, 15, Ink);
            Instrument(root, rx, payloadY, rw, h - payloadY - 69, "PAYLOAD / FIRING ENVELOPE", AvIcon.Bolt);
            for (int i = 0; i < 3; i++)
            {
                int focus = i; float bw = (rw - 28) / 3;
                profiles[i] = Button(root, rx + 8 + i * (bw + 6), payloadY + 41, bw, 32,
                    i == 0 ? "RECON" : i == 1 ? "KINETIC" : "EMP", () => support?.RequestPlatformFocus((PlatformFocus)focus), false, 14);
            }
            spacePayload = Mono(root, "", rx + 14, payloadY + 86, rw - 28, h - payloadY - 170, 15, Ink);
            spaceCircuitStatus = Mono(root, "", rx + 14, h - 113, rw - 28, 34, 13, RoomPaint.Command);
            execute = Button(root, rx, h - 58, rw, 58, "COMMIT PACKAGE", () => { if (packageAvailable) SelectPage(1); else support?.RequestCircuitCommit(); }, true, 19);
        }

        private Dial BuildSpaceDial(RectTransform root, float x, float y, float size, string title, bool caution)
        {
            var dial = new Dial();
            dial.Gauge = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(x, -y, size, size), "OrbitalCircuitDial");
            dial.Gauge.Shape = AvGaugeShape.Arc; dial.Gauge.StartDeg = 225; dial.Gauge.SweepDeg = 270;
            dial.Gauge.Thickness = 6; dial.Gauge.Ticks = 31; dial.Gauge.Track = BranchEdge.WithAlpha(.35f);
            dial.Gauge.FillColor = dial.Gauge.FillEnd = caution || title == "CHARGE" ? RoomPaint.Command : BranchAccent;
            dial.Gauge.TickColor = BranchDim; dial.Gauge.raycastTarget = false;
            var label = Mono(root, title, x + 16, y + 25, size - 32, 18, 12, BranchDim);
            label.alignment = TextAlignmentOptions.Center;
            dial.Value = Text(root, "0", x + 15, y + 43, size - 30, 38, 36, BranchInk);
            dial.Value.font = AvType.Face(AvFace.CondStrong); dial.Value.alignment = TextAlignmentOptions.Center;
            var units = Mono(root, "%", x + 16, y + 82, size - 32, 18, 11, BranchDim);
            units.alignment = TextAlignmentOptions.Center;
            Mono(root, "0", x + 8, y + size - 15, 20, 16, 11, BranchDim);
            var limit = Mono(root, "100", x + size - 35, y + size - 15, 29, 16, 11, BranchDim);
            limit.alignment = TextAlignmentOptions.TopRight;
            return dial;
        }

        private void SetSpaceStore(int index, float value, float maximum, string name, string unit)
        {
            string format = index == 3 ? "0.0" : "0";
            spaceResources[index].text = name;
            spaceResourceValues[index].text = maximum <= 0 ? index == 1 ? "NO TANKS FITTED" : index == 2 ? "NO MAGAZINE FITTED" : "NO PLATFORM"
                : value.ToString(format) + " / " + maximum.ToString(format) + unit;
            spaceResourceBars[index].Value = maximum > 0 ? value / maximum : 0;
            bool caution = maximum > 0 && (index == 3 ? value / maximum >= .9f : index == 0 && value / maximum < .15f);
            Color tone = maximum <= 0 ? Dim : caution ? RoomPaint.Command : index == 3 ? BranchAccent : RoomPaint.Ready;
            spaceResourceValues[index].color = maximum <= 0 ? Dim : Ink;
            spaceResourceBars[index].FillColor = spaceResourceBars[index].FillEnd = tone;
        }

        private void PaintSpace(OrbitalPlatform p, double now)
        {
            bool exists = p != null && p.Exists, banked = exists && p.BoostRemaining(now) > 0;
            packageAvailable = banked;
            PlatformStats stats = exists ? p.Stats(now) : default;
            OrbitState state = exists ? p.State(now) : default;
            spaceCoverage.Paint(p, now);
            spaceFrame.Set(spaceCoverage.FullCoverage ? "DETAIL" : "REACH", true, spaceCoverage.FullCoverage,
                spaceCoverage.FullCoverage ? "Show a detailed crop around the station and fresh observer geometry." : "Fit the entire nominal service boundary. Wheel and drag remain local map framing.");
            string service = !exists ? "NO STATION" : p.Brownout ? "BROWNOUT" : !p.IsOnline(OrbitalPlatform.CoreCell, now) ? "CORE OFFLINE" : !state.InPass ? "HOLD " + Mathf.CeilToInt((float)state.TimeToPass) + " S" : now < p.RetaskUntil ? "RETASKING" : "CONTINUOUS";
            spaceBusStatus.text = !exists ? "NO STATION / SECTORS ARE REFERENCE GEOMETRY" : (spaceCoverage.FullCoverage ? "REACH VIEW" : "DETAIL CROP") + " / SERVICE " + service + " / FULL REACH " + (p.CoverageRadius(now) / 1000).ToString("0.0") + " KM / WHEEL + DRAG";
            spaceOrbit.text = !exists ? "NOT COMMISSIONED\nLEO REFERENCE\n500 KM BAND" : p.Orbit.Code + " / " + (state.Altitude / 1000).ToString("0") + " KM\nINC " + p.Orbit.InclinationDeg.ToString("0.0") + " DEG\nE " + (state.SubX / 1000).ToString("0.0") + " KM\nN " + (state.SubZ / 1000).ToString("0.0") + " KM\n" + StationKeeping.Name(p.PositionIndex) + "\n" + (state.InPass ? "STATION KEEPING" : "SERVICE HOLD");
            bool observation = exists && p.SolutionRemaining(now) > 0;
            spaceAssemblyRegister.text = !exists ? "CONFIGURATION REFERENCE / NOT COMMISSIONED" : "REFERENCE / LOAD " + stats.LoadKw.ToString("0.0") + " KW / SUN " + stats.NetSunKw.ToString("+0.0;-0.0;0.0") + " / ECL " + stats.NetEclipseKw.ToString("+0.0;-0.0;0.0") + " KW";
            situation.text = !exists ? "NO OBSERVER LINK\nCommission BASTION in engineering.\n\nThe faction shares this station." : !observation ? "NO FRESH HANDOFF\nRADAR / MTI / ELINT or field recon can supply a solution.\n\nUnassisted weapons remain available." : (p.ObserverPackage ? "BANKED AREA / FIXED" : "FRESH TARGET GEOMETRY") + "\nGRID E " + (p.SolutionX / 1000).ToString("0.0") + " / N " + (p.SolutionZ / 1000).ToString("0.0") + " KM\nFOOTPRINT " + (p.SolutionRadius / 1000).ToString("0.0") + " KM\nVALID " + Mathf.CeilToInt((float)p.SolutionRemaining(now)) + " S\n" + (p.ObserverPackage ? "MATCHING PAYLOAD ONLY" : "OBSERVER GUIDANCE AVAILABLE");
            SetSpaceStore(0, exists ? p.Energy : 0, stats.StorageKj, "ENERGY", " KJ");
            SetSpaceStore(1, exists ? p.Fuel : 0, stats.FuelCapacity, "PROPELLANT", "");
            SetSpaceStore(2, exists ? p.Rods : 0, stats.RodCapacity, "ROD MAGAZINE", "");
            SetSpaceStore(3, stats.Mass, OrbitalPlatform.MassLimit, "TRUSS MASS", " T");
            spaceEngineering.Set(exists ? "ENGINEERING / FIT" : "ENGINEERING / LAUNCH", true, false, "Fit real modules, resupply ammunition or relocate the station through host-validated orders.");
            PlatformDenial uplink = exists ? p.Check(PlatformAbility.Uplink, now) : PlatformDenial.NoPlatform;
            spaceSensor.Set("SENSOR FEED", uplink == PlatformDenial.None, false, exists ? PlatformWords.Denial(uplink, p, PlatformAbility.Uplink, now) : "Commission a station and fit an online spy imager.");
            dials[0].Set(exists ? p.Alignment : 0); dials[1].Set(exists ? p.Capacitor : 0); dials[2].Set(exists ? p.Heat : 0);
            int routed = 0;
            for (int i = 0; i < 3; i++)
            {
                if (exists && p.Circuit(i) > 0) routed++;
                bool routeClosed = exists && p.Circuit(i) > 0;
                spaceCircuitStates[i].text = routeClosed ? "ROUTED" : "OPEN";
                spaceCircuitStates[i].color = routeClosed ? RoomPaint.Ready : Dim;
                spaceDialFaces[i].Set(exists ? i == 0 ? p.Alignment : i == 1 ? p.Capacitor : p.Heat : 0, exists);
                profiles[i].Set(i == 0 ? "RECON" : i == 1 ? "KINETIC" : "EMP", exists && CanSend && (support == null || support.LocalMayStationControl) && now >= p.RetaskUntil && !banked, exists && (int)p.Focus == i);
                for (int j = 0; j < 3; j++)
                {
                    int route = j + 1;
                    bool selected = exists && p.Circuit(i) == route;
                    PlatformCircuitForecast forecast = exists ? p.ForecastCircuit(i, route, now) : default;
                    PlatformWorkDenial denial = exists ? forecast.Denial : PlatformWorkDenial.NoPlatform;
                    string prediction = exists ? "Q" + Mathf.RoundToInt(forecast.Quality * 100) + " / H" + Mathf.RoundToInt(forecast.Heat) : "NO PLATFORM";
                    string title = OrbitalPlatform.RouteName(i, route).Replace("EMERGENCY QUENCH", "QUENCH");
                    circuits[i * 3 + j].Set(title + " / " + OrbitalPlatform.RouteCost(i, route) + " KJ\n" + (selected ? "ROUTED / " : "NEXT / ") + prediction,
                        CanSend && denial == PlatformWorkDenial.None, selected,
                        OrbitalPlatform.RouteEffect(i, route) + " / " + prediction + " / " + (selected ? "Already routed. Switching circuits spends energy again." : OrbitalPlatform.WorkWords(denial)));
                }
            }
            spaceSequence.SetProgress(banked ? 3 : routed, 3);
            PlatformWorkDenial commit = exists ? p.CheckCommitCircuits(p.WorkRevision, now) : PlatformWorkDenial.NoPlatform;
            execute.Set(banked ? "PACKAGE READY / DELIVER" : "COMMIT / 40 KJ", banked || CanSend && commit == PlatformWorkDenial.None, banked,
                banked ? "One accepted matching effect consumes this faction package. Teammates can designate in DELIVER." : OrbitalPlatform.WorkWords(commit));
            spaceCircuitStatus.text = banked ? "PACKAGE READY / " + Mathf.CeilToInt((float)p.BoostRemaining(now)) + " S\nONE ACCEPTED EFFECT / SHARED" : OrbitalPlatform.WorkWords(commit);
            float quality = exists ? banked ? p.Boost : p.PreparedQuality : 0;
            bool screen = exists && p.Focus == PlatformFocus.Screen;
            string benefit = screen ? "EMP RADIUS +" + Mathf.RoundToInt(quality * 25) + "% / DURATION +" + Mathf.RoundToInt(quality * 25) + "%" : "ROD DISPERSION -" + Mathf.RoundToInt(quality * 40) + "%";
            outcome.text = !exists ? "ENGINEERING: commission the core.\nRADAR operates on first launch." : p.Focus == PlatformFocus.Survey ? "RECON: wide coverage / faster scans.\nDELIVER radar, MTI or ELINT tasking.\nField observers supply weapon geometry." : (banked ? "PACKAGE READY / Q" : "DRAFT QUALITY / Q") + Mathf.RoundToInt(quality * 100) + "%\n" + benefit + "\n" + (banked ? "FACTION PACKAGE / NEXT MATCHING EFFECT" : "TEAM CREW / INDEPENDENT CIRCUIT ORDERS");
            if (!exists) spacePayload.text = "PAYLOAD: NOT FITTED\nWEAPONS: UNAVAILABLE\n\nBuild a sensor, kinetic or EMP loadout in engineering.";
            else if (p.Focus == PlatformFocus.Survey) spacePayload.text = "SURVEY BUS / SHARED RADAR\nSAR: STATIONARY CONTACTS\nMTI: MOVING CONTACTS\nELINT: ACTIVE SURFACE RADARS\n\nREACH " + (p.CoverageRadius(now) / 1000).ToString("0.0") + " KM\nSCANS SEED OBSERVER GUIDANCE";
            else
            {
                PlatformAbility ability = screen ? PlatformAbility.EmpBurst : PlatformAbility.RodStrike;
                ModuleKind kind = screen ? ModuleKind.Emp : ModuleKind.Rods;
                PlatformDenial denial = p.Check(ability, now);
                float scatter = p.RodScatter(now) * (1 - quality * .4f);
                spacePayload.text = (screen ? "EMP EMITTER" : "TUNGSTEN MAGAZINE") + " / " + (p.FittedOnline(kind, now) ? "ONLINE" : p.Fitted(kind) ? "OFFLINE" : "NOT FITTED") + "\n" + PlatformWords.Denial(denial, p, ability, now) + "\nREACH " + (p.CoverageRadius(now) / 1000).ToString("0.0") + " KM\nENERGY " + PlatformAbilities.Info(ability).EnergyKj.ToString("0") + " KJ / EFFECT\n" + (screen ? "PULSE SCALE " + (p.EmpScaleAt(now) * (1 + quality * .25f)).ToString("0.00") + " X\nDURATION " + (1 + quality * .25f).ToString("0.00") + " X" : "SALVO " + p.RodSalvoCount(now) + " ROD(S)\nDISPERSION " + scatter.ToString("0.0") + " M") + "\n" + (banked && p.ObserverPackage ? "OBSERVER FOOTPRINT REQUIRED" : "BASELINE FIRE REMAINS AVAILABLE");
            }
        }
    }

    /// <summary>Engraved orbital dial face. The needle mirrors a published percentage; it never animates independently.</summary>
    internal sealed class SpaceDialFaceGraphic : MaskableGraphic
    {
        public bool HeatLimit;
        private float reading;
        private bool available;
        public void Set(float value, bool exists)
        {
            float next = Mathf.Clamp01(value / 100f);
            if (Mathf.Abs(next - reading) < .001f && available == exists) return;
            reading = next; available = exists; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect r = rectTransform.rect; Vector2 centre = r.center;
            float radius = Mathf.Min(r.width, r.height) * .5f;
            for (int i = 0; i < 60; i++)
            {
                float a = i * Mathf.PI / 30, b = (i + 1) * Mathf.PI / 30;
                Line(mesh, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * .73f,
                    centre + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius * .73f, .65f, color.WithAlpha(.38f));
            }
            // Major register marks are static references. Heat's amber index is the real 50% commit limit.
            for (int i = 0; i <= 10; i++)
            {
                float angle = (225 - 27 * i) * Mathf.Deg2Rad;
                Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Line(mesh, centre + axis * radius * .9f, centre + axis * radius * .79f, 1.15f, color.WithAlpha(.7f));
            }
            if (HeatLimit)
            {
                Vector2 axis = Vector2.up;
                Line(mesh, centre + axis * radius * .99f, centre + axis * radius * .8f, 2.1f, RoomPaint.Command);
            }
            if (!available) return;
            float pointerAngle = (225 - 270 * reading) * Mathf.Deg2Rad;
            Vector2 pointer = new Vector2(Mathf.Cos(pointerAngle), Mathf.Sin(pointerAngle));
            Line(mesh, centre + pointer * radius * 1.035f, centre + pointer * radius * .965f, 2.5f,
                HeatLimit && reading > .5f ? RoomPaint.Command : RoomPaint.Instrument);
        }
        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .0001f) return;
            Vector2 tangent = delta.normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x) * width * .5f;
            int at = mesh.currentVertCount;
            mesh.AddVert(a + normal, tint, Vector2.zero); mesh.AddVert(b + normal, tint, Vector2.zero);
            mesh.AddVert(b - normal, tint, Vector2.zero); mesh.AddVert(a - normal, tint, Vector2.zero);
            mesh.AddTriangle(at, at + 1, at + 2); mesh.AddTriangle(at, at + 2, at + 3);
        }
    }

    /// <summary>Local orbital planning display: native terrain, fixed sector reference and host-confirmed geometry only.</summary>
    internal sealed class SpaceCoverageBoard
    {
        public BoardSurface Board { get; private set; }
        public bool FullCoverage { get; private set; }
        private BoardTerrain terrain;
        private readonly float[] fitX = new float[13], fitZ = new float[13];
        private readonly Image[] front = new Image[96];
        private readonly Image[] sectorMarks = new Image[StationKeeping.Count];
        private readonly Image[] labelPlates = new Image[2], leaders = new Image[2];
        private readonly TMP_Text[] labels = new TMP_Text[2];
        private readonly Vector2[] anchors = new Vector2[2], sizes = { new Vector2(155, 39), new Vector2(155, 39) };
        private readonly int[] priorities = { 2, 1 };
        private readonly float[] radii = { 12, 12 };
        private readonly PlacedLabel[] placed = new PlacedLabel[2];
        internal Rect[] PlacedRects { get; } = new Rect[2];
        internal int PlacedCount { get; private set; }
        private Image station, observation, connection;
        private readonly Image[] scaleMarks = new Image[3];
        private readonly Rect[] mapRegisters = new Rect[2];
        private TMP_Text scaleLabel;
        private RingLine reach, solution;
        private TMP_Text missing;
        private int frontRevision = -1;
        private static Color Ink => RoomPaint.Ink("room-space-ink", AvTheme.TextPrimary);
        private static Color Dim => RoomPaint.Ink("room-space-dim", AvTheme.Dim);
        private static Color Edge => RoomPaint.Edge("room-space-console", AvTheme.Hairline);
        private static Color Surface => RoomPaint.Fill("room-space-surface", AvTheme.Ground);

        public void Build(RectTransform parent, float w, float h)
        {
            Board = new BoardSurface(parent, new Rect(0, 0, w, h), new Rect(12, -12, w - 24, h - 24), true, true);
            RectTransform layer = Board.InputLayer;
            terrain = new BoardTerrain(layer, Board, true);
            var grid = Chrome.Graphic<CrewDraftingGraphic>(layer, new Rect(0, 0, w, h), "OrbitalPlotRegister");
            grid.color = Edge.WithAlpha(.21f); grid.raycastTarget = false;
            for (int i = 0; i < front.Length; i++) { front[i] = Lines.Make(layer, RoomPaint.Ready.WithAlpha(.55f), null, "OrbitalFrontline"); front[i].enabled = false; }
            for (int i = 0; i < sectorMarks.Length; i++)
            { sectorMarks[i] = Chrome.Panel(layer, new Rect(0, 0, 5, 5), Edge, OpsSprites.Dot); sectorMarks[i].raycastTarget = false; }
            reach = new RingLine(layer, 72, RoomPaint.Instrument.WithAlpha(.8f), true);
            solution = new RingLine(layer, 48, RoomPaint.Command, false);
            connection = Lines.Make(layer, RoomPaint.Instrument.WithAlpha(.4f), null, "ObserverUplink");
            station = Chrome.Panel(layer, new Rect(0, 0, 16, 16), RoomPaint.Instrument, OpsSprites.Diamond); station.raycastTarget = false;
            observation = Chrome.Panel(layer, new Rect(0, 0, 13, 13), RoomPaint.Command, OpsSprites.Diamond); observation.raycastTarget = false;
            for (int i = 0; i < 2; i++)
            {
                labelPlates[i] = Chrome.Panel(layer, new Rect(0, 0, 155, 39), Surface.WithAlpha(.94f));
                labelPlates[i].raycastTarget = false;
                labels[i] = Chrome.Label(labelPlates[i].rectTransform, "", new Rect(7, -4, 141, 32), Ink, 13, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
                labels[i].font = AvType.Face(AvFace.Mono);
                leaders[i] = Lines.Make(layer, Edge.WithAlpha(.6f), null, "OrbitalLabelLeader");
            }
            missing = Chrome.Label(parent, "TERRAIN IMAGERY UNAVAILABLE", new Rect(12, -h + 25, w - 24, 20), Dim, 13, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
            missing.raycastTarget = false;
            // Fixed north and a true geographic scale. These plates reserve label space, never map input.
            mapRegisters[0] = new Rect(7, -h + 37, 143, 31);
            mapRegisters[1] = new Rect(w - 51, -7, 42, 66);
            for (int i = 0; i < mapRegisters.Length; i++)
            {
                Image register = Chrome.Panel(layer, mapRegisters[i], Surface.WithAlpha(.88f)); register.raycastTarget = false;
            }
            scaleLabel = Chrome.Label(layer, "", new Rect(13, -h + 36, 125, 18), Ink, 11, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
            scaleLabel.font = AvType.Face(AvFace.Mono); scaleLabel.alignment = TextAlignmentOptions.TopRight; scaleLabel.raycastTarget = false;
            var scaleZero = Chrome.Label(layer, "0", new Rect(13, -h + 36, 12, 18), Ink, 11, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
            scaleZero.font = AvType.Face(AvFace.Mono); scaleZero.raycastTarget = false;
            for (int i = 0; i < scaleMarks.Length; i++) { scaleMarks[i] = Lines.Make(layer, Ink.WithAlpha(.8f), null, "OrbitalRangeScale"); scaleMarks[i].raycastTarget = false; }
            var north = Chrome.Label(layer, "N", new Rect(w - 43, -10, 28, 19), Ink, 12, FontStyles.Normal, TextAlignmentOptions.Top, true);
            north.font = AvType.Face(AvFace.Mono); north.raycastTarget = false;
            Image axis = Lines.Make(layer, Ink.WithAlpha(.7f), null, "OrbitalNorthAxis");
            Lines.Set(axis, w - 30, -32, w - 30, -60, 1.1f);
            Image arrowL = Lines.Make(layer, Ink, null, "OrbitalNorthArrow"), arrowR = Lines.Make(layer, Ink, null, "OrbitalNorthArrow");
            Lines.Set(arrowL, w - 30, -32, w - 36, -41, 1.1f); Lines.Set(arrowR, w - 30, -32, w - 24, -41, 1.1f);
            axis.raycastTarget = arrowL.raycastTarget = arrowR.raycastTarget = false;
        }

        public bool FrameChanged => Board != null && frontRevision != Board.Revision;
        public void FrontChanged() { frontRevision = -1; }
        public void ToggleFrame() { FullCoverage = !FullCoverage; Board.ResetFraming(); frontRevision = -1; }

        public void Paint(OrbitalPlatform p, double now)
        {
            bool exists = p != null && p.Exists, seen = exists && p.SolutionRemaining(now) > 0;
            OrbitState state = exists ? p.State(now) : default;
            int n = 0;
            float cx = exists ? (float)state.SubX : 0, cz = exists ? (float)state.SubZ : 0, radius = exists ? p.CoverageRadius(now) : 30000;
            fitX[n] = cx; fitZ[n++] = cz;
            if (FullCoverage)
            {
                fitX[n] = cx - radius; fitZ[n++] = cz - radius;
                fitX[n] = cx + radius; fitZ[n++] = cz + radius;
            }
            if (seen)
            {
                fitX[n] = p.SolutionX - p.SolutionRadius; fitZ[n++] = p.SolutionZ - p.SolutionRadius;
                fitX[n] = p.SolutionX + p.SolutionRadius; fitZ[n++] = p.SolutionZ + p.SolutionRadius;
            }
            // The default is a geographic detail crop, not a stretched theatre image.
            // All circles and marks retain the same scale; REACH explicitly fits the full boundary.
            Board.Fit(fitX, fitZ, n, FullCoverage ? 50000 : 12000, 0, 20); terrain.Refresh();
            Board.SetObstacles(mapRegisters, mapRegisters.Length);
            float wanted = 102f * Board.MetresPerPixel;
            float decade = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(Mathf.Max(1, wanted))));
            float scaled = wanted / decade;
            float scaleMetres = decade * (scaled >= 5 ? 5 : scaled >= 2 ? 2 : 1);
            float scalePixels = Board.Pixels(scaleMetres), baseY = -Board.View.height + 12;
            scaleLabel.text = (scaleMetres / 1000).ToString(scaleMetres < 1000 ? "0.0" : "0") + " KM";
            Chrome.Place(scaleLabel.rectTransform, new Rect(27, -Board.View.height + 36, scalePixels - 14, 18));
            Lines.Set(scaleMarks[0], 13, baseY, 13 + scalePixels, baseY, 2);
            Lines.Set(scaleMarks[1], 13, baseY - 3, 13, baseY + 3, 1);
            Lines.Set(scaleMarks[2], 13 + scalePixels, baseY - 3, 13 + scalePixels, baseY + 3, 1);
            missing.gameObject.SetActive(!terrain.Available);
            for (int i = 0; i < sectorMarks.Length; i++)
            { Vector2 at = Board.Project(StationKeeping.X(i), StationKeeping.Z(i)); Lines.Centre(sectorMarks[i].rectTransform, at.x, at.y, 5); }
            station.enabled = exists;
            observation.enabled = seen;
            Vector2 origin = Board.Project(cx, cz);
            if (exists) { Lines.Centre(station.rectTransform, origin.x, origin.y, 15); reach.Set(origin, Board.Pixels(radius), 1.2f, RoomPaint.Instrument.WithAlpha(.75f)); }
            else reach.Hide();
            if (seen)
            {
                Vector2 target = Board.Project(p.SolutionX, p.SolutionZ);
                Lines.Centre(observation.rectTransform, target.x, target.y, 12);
                solution.Set(target, Board.Pixels(p.SolutionRadius), 1.5f, RoomPaint.Command);
                Lines.Set(connection, origin.x, origin.y, target.x, target.y, .8f);
                anchors[1] = target;
            }
            else { solution.Hide(); connection.enabled = false; }
            anchors[0] = origin;
            Board.PlaceLabels(anchors, sizes, priorities, radii, exists ? seen ? 2 : 1 : 0, placed);
            PlacedCount = 0;
            for (int i = 0; i < 2; i++)
            {
                bool show = exists && (i == 0 || seen) && placed[i].Visible;
                labelPlates[i].gameObject.SetActive(show); leaders[i].enabled = false;
                if (!show) continue;
                PlacedLabel at = placed[i]; var rect = new Rect(at.X, at.Y, at.Width, at.Height);
                Chrome.Place(labelPlates[i].rectTransform, rect); PlacedRects[PlacedCount++] = rect;
                labels[i].text = i == 0 ? "BASTION / " + StationKeeping.Name(p.PositionIndex) + "\nREACH " + (radius / 1000).ToString("0.0") + " KM" : "OBSERVER GEOMETRY\nT- " + Mathf.CeilToInt((float)p.SolutionRemaining(now)) + " S VALID";
                if (at.Leader) Lines.Set(leaders[i], anchors[i].x, anchors[i].y, at.X + 77, at.Y - 19, .8f);
            }
            if (frontRevision == Board.Revision) return;
            frontRevision = Board.Revision; int used = 0, offset = 0;
            for (int t = 0; t < Board.TraceCount && used < front.Length; t++)
            {
                int count = Board.TraceLengths[t];
                for (int i = 1; i < count && used < front.Length; i++)
                {
                    Vector2 a = Board.Project(Board.TracePoints[offset + i - 1].X, Board.TracePoints[offset + i - 1].Z);
                    Vector2 b = Board.Project(Board.TracePoints[offset + i].X, Board.TracePoints[offset + i].Z);
                    if (Board.InView(a) && Board.InView(b)) Lines.Set(front[used++], a.x, a.y, b.x, b.y, 1.2f);
                }
                offset += count;
            }
            for (int i = used; i < front.Length; i++) front[i].enabled = false;
        }
    }
}
