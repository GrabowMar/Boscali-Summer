using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using BoscaliSummer.Modules.Support.Runtime;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>Faction workspaces: parallel contributions, shared outcomes, native targeting.
    /// Only the host changes work. The room keeps local navigation and target selection.</summary>
    internal sealed partial class CrewOperationsView : IOpsView
    {
        private sealed class Key
        {
            public RoomControl Control;
            public TMP_Text Text;
            public AvFrame Frame;
            public Image Indicator, Lamp;
            public AvFx Feedback;
            public Func<bool> MotionAllowed;
            private bool wasHovered, wasPressed, wasFocused, painted;
            public bool Primary;
            public Color Accent, Border, Fill, ActiveFill, Letter, Muted;
            public void Set(string text, bool enabled, bool selected = false, string help = null)
            {
                Text.text = text;
                Control.WithTooltip(help ?? text.Replace("\n", " / "));
                Control.SetEnabled(enabled);
                Control.SetLatched(selected);
                Paint();
            }
            public void Paint()
            {
                Color color = Primary && Control.Enabled ? RoomPaint.Command : Control.Latched ? Accent : Border.WithAlpha(.8f);
                if (Control.Focused) color = Accent;
                float stroke = Control.Focused || Control.Pressed ? 2f : 1f;
                float bracket = Control.Focused || Control.Latched ? 7f : Primary ? 4f : 0f;
                if (Frame.Stroke != stroke || Frame.Bracket != bracket || Frame.BracketColor != color)
                { Frame.Stroke = stroke; Frame.Bracket = bracket; Frame.BracketColor = color; Frame.SetVerticesDirty(); }
                Color fill = Control.Pressed ? Color.Lerp(ActiveFill, color, .22f)
                    : Primary && Control.Enabled ? Color.Lerp(Fill, RoomPaint.Command, .17f)
                    : Control.Latched || Control.Focused || (Control.Hovered && Control.Enabled) ? ActiveFill : Fill;
                Frame.Paint(fill, color);
                Indicator.color = color;
                Indicator.enabled = Control.Latched || Primary;
                Text.color = Control.Latched ? Accent : Primary && Control.Enabled ? RoomPaint.Command : Control.Enabled ? Letter : Muted;
                if (Lamp != null) Lamp.color = Control.Enabled ? Control.Latched || Primary ? color : Letter.WithAlpha(.55f) : Muted.WithAlpha(.25f);
                if (Feedback != null)
                {
                    if (!(MotionAllowed?.Invoke() ?? false)) Feedback.Clear();
                    else if (painted && Control.Enabled && ((Control.Pressed && !wasPressed) || (Control.Hovered && !wasHovered) || (Control.Focused && !wasFocused)))
                        Feedback.Play(AvFxKind.Shine, Control.Pressed ? .3f : .12f);
                }
                wasHovered = Control.Hovered; wasPressed = Control.Pressed; wasFocused = Control.Focused; painted = true;
            }
        }
        private sealed class Dial
        {
            public AvGaugeGraphic Gauge;
            public TMP_Text Value;
            public void Set(float value) { Gauge.Value = value / 100f; Value.text = Mathf.RoundToInt(value).ToString(); }
        }
        private static Color Surface => RoomPaint.Fill("room-space-surface", AvTheme.Ground);
        private static Color Raised => RoomPaint.Fill("room-space-console", AvTheme.Surface);
        private static Color Ink => RoomPaint.Ink("room-space-ink", AvTheme.TextPrimary);
        private static Color Dim => RoomPaint.Ink("room-space-dim", AvTheme.Dim);
        private static Color Edge => RoomPaint.Edge("room-space-console", AvTheme.Hairline);
        private readonly SupportManager support;
        private readonly Action engineering, sensors, close;
        private readonly string[] log;
        private readonly Rect[] sections = new Rect[4];
        private readonly RectTransform[] pages = new RectTransform[3];
        private readonly Key[] navigation = new Key[3];
        private readonly Key[] circuits = new Key[9], profiles = new Key[3], gates = new Key[9], teams = new Key[4], missions = new Key[4];
        private readonly int[] defenseTargets = new int[5];
        private readonly Key[] upgrades = new Key[4], defenses = new Key[5], payloads = new Key[3];
        private readonly Key[] fieldOrders = new Key[6];
        private readonly Dial[] dials = new Dial[3];
        private readonly TMP_Text[] clues = new TMP_Text[3];
        private readonly List<Key> deliveryKeys = new List<Key>();
        private readonly List<SupportActionDefinition> deliveries = new List<SupportActionDefinition>();
        private Key execute, scan, scrub, begin, force, defenceWatch, marketSignal, marketCargo, raise;
        private TMP_Text header, situation, task, outcome, readback, logText, marketText;
        private DeskMap fieldMap;
        private CyberNetmap networkMap;
        private CyberNetwork network;
        private OrbitalPlatform currentPlatform;
        private readonly TMP_Text[] deliveryRegisters = new TMP_Text[3];
        private SpecOpsDetachment detachment;
        private int page, selectedTeam, selectedNode = -1, selectedObjective;
        private int selectedAnchor;
        private double clock;
        private bool dirty = true, packageAvailable;
        public OpsDomain Domain { get; }
        public float EntranceSeconds => 0f;
        public Rect Hero => sections[1];
        public IReadOnlyList<Rect> Sections => sections;
        public object Map => (object)fieldMap ?? (object)networkMap ?? spaceCoverage;
        private bool CanSend => support == null || (support.OpsStateFresh && !support.CommandPending);

        public CrewOperationsView(SupportManager support, OpsDomain domain, string[] log, Action engineering, Action sensors, Action close)
        { this.support = support; Domain = domain; this.log = log; this.engineering = engineering; this.sensors = sensors; this.close = close; }

        public void Build(RectTransform room, Rect area)
        {
            keys.Clear(); focusIndex = -1;
            StationStyle.Resolve(); CyberStyle.Resolve(); DeskStyle.Resolve(); OpsSprites.Ensure();
            float w = area.width, h = area.height, x = 110f, bodyY = 114f, bodyW = w - 128f, bodyH = h - 184f;
            BuildHousing(room, w, h);
            Text(room, Domain == OpsDomain.Space ? "BASTION" : Domain == OpsDomain.Cyber ? "AEGIS-NET" : "DETACHMENT", x, 12, 460, 45, 36, BranchInk).font = AvType.Face(AvFace.CondStrong);
            Text(room, Domain == OpsDomain.Space ? "ORBITAL RECON / KINETIC / ELECTROMAGNETIC" : Domain == OpsDomain.Cyber ? "INTRUSION / ELECTRONIC WARFARE" : "RECON / DIRECT ACTION / ESPIONAGE", x, 62, 620, 24, 15, BranchAccent);
            header = Mono(room, "", w * .52f, 18, w * .48f - 22, 66, 16, BranchDim);
            Chrome.Rule(room, new Rect(x, -99, bodyW, 1), Edge);
            string[] tabs = { "WORKSPACE", "DELIVER", Domain == OpsDomain.Space ? "SYSTEMS" : Domain == OpsDomain.Cyber ? "DEFENCE" : "SUPPLY" };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                navigation[i] = Button(room, 12, 116 + i * 72, 86, 56, (i + 1) + "\n" + tabs[i], () => SelectPage(index));
                pages[i] = Group(room, x, bodyY, bodyW, bodyH);
            }
            if (Domain == OpsDomain.Space) BuildSpace(pages[0], bodyW, bodyH);
            else if (Domain == OpsDomain.Cyber) BuildCyber(pages[0], bodyW, bodyH);
            else BuildField(pages[0], bodyW, bodyH);
            BindCommandFeedback();
            BuildDeliveries(pages[1], bodyW, bodyH);
            BuildSystems(pages[2], bodyW, bodyH);
            readback = Text(room, "", x, h - 55, bodyW * .73f, 43, 16, Dim);
            Mono(room, "TAB / SHIFT TAB   FOCUS\nENTER   ORDER    1 2 3   PAGE", x + bodyW * .75f, h - 55, bodyW * .25f, 43, 14, Dim);
            sections[0] = new Rect(x, 0, bodyW, 100);
            sections[1] = new Rect(x, bodyY, bodyW, bodyH);
            sections[2] = new Rect(0, h - 65, w, 65);
            sections[3] = new Rect(0, 0, 104, h - 65);
            AvDisplayGlass.AttachFullDisplay(room);
            SelectPage(page);
        }

        private void BuildDeliveries(RectTransform root, float w, float h)
        {
            float lw = w * .60f, sx = lw + 14, sw = w - sx;
            float registerY = h * .70f;
            Instrument(root, 0, 0, lw, registerY - 12, "01 / EFFECTS AUTHORIZATION", AvIcon.Target);
            Instrument(root, 0, registerY, lw, h - registerY, "CAPABILITY TREE / SHARED TEAM HANDOFF", AvIcon.Link);
            for (int i = 0; i < 3; i++)
            {
                float col = (lw - 40) / 3;
                deliveryRegisters[i] = Mono(root, "", 14 + i * (col + 6), registerY + 48, col, h - registerY - 61, 16, BranchInk);
            }
            Instrument(root, sx, 0, sw, h, "02 / RELEASE PROCEDURE", AvIcon.Radio);
            deliveryTitle = Text(root, "SELECT AN EFFECT", sx + 22, 49, sw - 44, 40, 25, Ink);
            deliveryDossier = Text(root, "", sx + 22, 101, sw - 44, 126, 20, Dim);
            if (Domain == OpsDomain.Space) OpsArtwork.DrawPayload(root, new Rect(sx + 22, -235, sw - 44, 156));
            else if (Domain == OpsDomain.SpecialOperations) OpsArtwork.Draw(root, new Rect(sx + 22, -225, sw - 44, 220), 3);
            else {
                Instrument(root, sx + 14, 251, sw - 28, 166, "EXFIL / LEASE CONDITION", AvIcon.Link);
                deliveryLease = Mono(root, "", sx + 30, 297, sw - 60, 106, 18, BranchAccent);
            }
            Text(root, "TEAM PACKAGE / CURRENT STATUS", sx + 22, 463, sw - 44, 29, 20, BranchAccent);
            deliveryStatus = Text(root, "", sx + 22, 511, sw - 44, h - 539, 21, Ink);
            deliveryKeys.Clear(); deliveries.Clear();
            RectTransform list = Chrome.Scroll(root, new Rect(8, -44, lw - 20, registerY - 70), 1200, out Rect content);
            if (support == null) { Text(list, "Awaiting capability catalogue from command.", 12, 12, content.width - 24, 66, 19, Dim); return; }
            foreach (SupportActionDefinition action in support.Actions)
            {
                OpsDomain home = action.IsCyber || action.Id == SupportActionId.FlareMissile ? OpsDomain.Cyber :
                    SupportManager.OrbitalAbility(action.Id).HasValue ? OpsDomain.Space : OpsDomain.SpecialOperations;
                if (home != Domain) continue;
                SupportActionDefinition captured = action;
                int i = deliveries.Count;
                deliveries.Add(action);
                Key order = Button(list, 4, i * 70, content.width - 8, 62, action.Name, () =>
                {
                    if (!CanSend || !AbilityStatus.For(support, captured, support.BypassRequirements).Enabled) return;
                    support.Arm(captured.Id);
                    if (support.ArmedAction == captured.Id) close?.Invoke();
                });
                Icon(order.Control.Rect, AvIcon.Target, 14, 17, 27, BranchAccent);
                Chrome.Place(order.Text.rectTransform, new Rect(55, -5, content.width - 80, 52));
                order.Control.Changed = control => { order.Paint(); if (control.Hovered || control.Focused) { selectedDelivery = i; dirty = true; } };
                deliveryKeys.Add(order);
            }
            list.sizeDelta = new Vector2(list.sizeDelta.x, Mathf.Max(registerY - 70, deliveries.Count * 70));
        }

        public void Refresh(double now, float time, bool textTick)
        {
            if (support != null && page == 0 && (networkMap != null || fieldMap != null || spaceCoverage != null))
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                int faction = player != null && player.HQ != null ? player.HQ.GetInstanceID() : 0;
                if (networkMap != null && networkMap.Board.RefreshFrontline(faction, time)) { networkMap.FrontChanged(); dirty = true; }
                if (fieldMap != null && fieldMap.Board.RefreshFrontline(faction, time)) { fieldMap.FrontChanged(); dirty = true; }
                if (spaceCoverage != null && spaceCoverage.Board.RefreshFrontline(faction, time)) { spaceCoverage.FrontChanged(); dirty = true; }
            }
            if (page == 0 && !ReducedMotion)
            { networkMap?.Animate(network, clock, time); fieldMap?.Animate(detachment, support?.OrbitNow ?? now, time); }
            if (!textTick && !dirty && !(networkMap?.FrameChanged ?? false) && !(fieldMap?.FrameChanged ?? false) && !(spaceCoverage?.FrameChanged ?? false)) return;
            Paint(support?.LocalPlatform, support?.LocalCyber, support?.LocalDetachment, support?.OrbitNow ?? now);
        }
        internal void Paint(OrbitalPlatform p, CyberNetwork c, SpecOpsDetachment d, double now)
        {
            currentPlatform = p; network = c; detachment = d; clock = now; dirty = false;
            string reserve = support == null ? "OFFLINE PREVIEW" : Mathf.FloorToInt(support.LocalOpsReserve) + " OPS RESERVE";
            header.text = (Domain == OpsDomain.Space ? "FLIGHT CONTROL" : Domain == OpsDomain.Cyber ? "SIGNALS EXPLOITATION" : "EXPEDITIONARY FIELD DESK") + " / " + reserve + "\n" + (CanSend ? "HOST LINK / FACTION SHARED" : "UPLINK PENDING / AWAIT HOST") + " / T+" + TheaterGrid.Clock(now);
            readback.text = support?.Status ?? "Contribute to the shared operation, then deliver the advantage to the battlefield.";
            PaintInstruments(p, c, d, now);
            if (Domain == OpsDomain.Space) PaintSpace(p, now);
            else if (Domain == OpsDomain.Cyber) PaintCyber(c, now);
            else PaintField(d, now);
            PaintFeedback();
            deliveryStatus.text = Domain == OpsDomain.SpecialOperations && d != null ? FieldWords.OperatorAdvice(d.Team(selectedTeam), now) : outcome.text;
            for (int i = 0; i < deliveries.Count; i++)
            {
                var facts = AbilityStatus.For(support, deliveries[i], support.BypassRequirements);
                deliveryKeys[i].Set(deliveries[i].Name + "  /  " + DeliveryRequirement(deliveries[i]) + "  /  " + facts.CostText + "\n" + facts.Readiness, CanSend && facts.Enabled, facts.Armed, deliveries[i].Description + " / " + facts.Readiness);
            }
            PaintServiceInventory(p, c, d, now);
            PaintMarket(p, c, d, now);
            PaintDefence(c, now);
            PaintDeliveryDossier();
            if (focusIndex >= 0 && !keys[focusIndex].Control.gameObject.activeInHierarchy) FocusFirst();
            if (logText != null)
            {
                string words = "FACTION NET / LATEST READBACK";
                if (log != null) for (int i = 0; i < Math.Min(3, log.Length); i++)
                    if (!string.IsNullOrEmpty(log[i])) words += "\n\n" + (log[i].Length <= 120 ? log[i] : log[i].Substring(0, 117) + "...");
                logText.text = words;
            }
        }

        private bool AffordableAssistance(int order) => support == null || support.BypassRequirements || support.LocalOpsReserve >= support.CrewAssistancePrice(order);
        private void Launch(FieldMission mission)
        { if (CanSend && detachment != null) support?.RequestSpecOpsLaunch(selectedTeam, mission, selectedAnchor); }
        private void CycleObjective(int direction)
        { if (detachment == null || detachment.ObjectiveCount == 0) return; selectedObjective = (selectedObjective + direction + detachment.ObjectiveCount) % detachment.ObjectiveCount; selectedAnchor = detachment.Objective(selectedObjective).Anchor; dirty = true; }
        private void SelectPage(int value)
        {
            ClearFocus();
            page = Mathf.Clamp(value, 0, 2);
            for (int i = 0; i < pages.Length; i++) { pages[i]?.gameObject.SetActive(i == page); navigation[i]?.Control.SetLatched(i == page); }
            if (acquisition != null && !ReducedMotion) acquisition.Play(AvFxKind.Shine, .18f);
            FocusFirst();
            dirty = true;
        }
        public void Show(object context) { if (Domain == OpsDomain.Cyber && context is int slot) selectedNode = slot; FocusFirst(); dirty = true; }
        public void Hide() { ClearFocus(); }
        public void Entrance(float progress) { }
        public void RightClickInside() { }
        private static RectTransform Group(RectTransform parent, float x, float y, float w, float h)
        { var go = new GameObject("CrewPage", typeof(RectTransform)); var rt = (RectTransform)go.transform; rt.SetParent(parent, false); Chrome.Place(rt, new Rect(x, -y, w, h)); return rt; }
        private static TMP_Text Text(RectTransform root, string text, float x, float y, float w, float h, float size, Color color) =>
            Chrome.Label(root, text, new Rect(x, -y, w, h), color, size, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        private static void Panel(RectTransform root, float x, float y, float w, float h)
        { Plate(root, new Rect(x, -y, w, h), Raised, Edge, 8); }
        private static AvFrame Plate(RectTransform parent, Rect area, Color fill, Color edge, float cut)
        {
            AvFrame plate = AvFrame.Add(parent, "InstrumentPlate", AvChamfer.All(cut));
            Chrome.Place(plate.rectTransform, area); plate.Stroke = 1; plate.Paint(fill, edge); return plate;
        }
        private Key Button(RectTransform root, float x, float y, float w, float h, string title, Action action, bool primary = false, float textSize = 18)
        {
            var key = new Key { Control = RoomControl.Create(root, new Rect(x, -y, w, h), action, "CrewOrder"), Primary = primary, Accent = BranchAccent, Border = BranchEdge, Fill = BranchSurface, ActiveFill = BranchPane, Letter = BranchInk, Muted = BranchDim };
            key.Frame = Plate(key.Control.Rect, new Rect(0, 0, w, h), BranchSurface, primary ? RoomPaint.Command : BranchEdge, primary ? 7 : 3);
            key.Feedback = AvFx.On(key.Frame);
            key.MotionAllowed = () => !ReducedMotion;
            key.Indicator = Chrome.Rule(key.Control.Rect, new Rect(3, -12, 2, h - 24), RoomPaint.Instrument);
            if (w >= 100 && h >= 38)
            {
                key.Lamp = Chrome.Panel(key.Control.Rect, new Rect(w - 10, -7, 4, 4), BranchDim);
                key.Lamp.name = "KeyStateLamp";
                key.Lamp.raycastTarget = false;
            }
            key.Text = Chrome.Label(key.Control.Rect, title, new Rect(12, -5, w - 24, h - 10), Ink, textSize, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, true);
            int keyIndex = keys.Count;
            key.Control.FocusRequested = _ => FocusKey(keyIndex);
            key.Control.Changed = _ => key.Paint(); key.Control.WithTooltip(title.Replace("\n", " / ")); keys.Add(key); key.Paint();
            return key;
        }
        private static Dial MakeDial(RectTransform root, float x, float y, float size, string title, bool caution)
        {

            var dial = new Dial();
            dial.Gauge = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(x, -y, size, size), "CrewInstrument");
            dial.Gauge.Shape = AvGaugeShape.Arc; dial.Gauge.StartDeg = 225; dial.Gauge.SweepDeg = 270;
            dial.Gauge.Thickness = 10; dial.Gauge.Ticks = 48; dial.Gauge.Track = Edge.WithAlpha(.35f);
            dial.Gauge.FillColor = dial.Gauge.FillEnd = caution ? RoomPaint.Command : RoomPaint.Instrument;
            DialScale(root, x, y, size);
            dial.Gauge.TickColor = Dim; dial.Gauge.raycastTarget = false;
            Chrome.Label(root, title, new Rect(x + 15, -y - 43, size - 30, 24), Dim, 15, FontStyles.Normal, TextAlignmentOptions.Center);
            dial.Value = Chrome.Label(root, "0", new Rect(x + 15, -y - 68, size - 30, 49), Ink, 39, FontStyles.Bold, TextAlignmentOptions.Center);
            return dial;
        }
    }
}
