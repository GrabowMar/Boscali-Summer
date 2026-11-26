using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using static BoscaliSummer.Modules.Support.Presentation.Fronts.FrontPanels;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    internal sealed partial class OrbitRoom
    {
        private const float ConsoleInset = 12f;
        private const float ManifestY = 64f, ManifestH = 339f, PadsY = 415f, PadsH = 222f;
        private FrontBox launch;
        private TMP_Text launchFunds, launchAllocation, padHint, padHead, payloadReadback, busReadback, stationReadback, launchReceipt;
        private readonly AvControl[] payloadButtons = new AvControl[FrontRules.BirdCap];
        private readonly TMP_Text[] payloadStates = new TMP_Text[FrontRules.BirdCap];
        private readonly AvControl[] busButtons = new AvControl[3];
        private readonly PadCard[] padCards = new PadCard[FrontRules.PadCap];
        private AvControl buildPad, queueSatellite, chipSatellite, stationPick, autoStation;
        private FrontBar launchProgress;
        private string padWhy = "";

        private static readonly string[] PayloadJob = { "SAT CAMERA · OPTICAL", "OVERWATCH · RADAR", "ORBITAL ROD · KINETIC" };
        private static readonly string[] KindTag = { "OPT", "RDR", "KIN" };
        private static readonly string[] BusNames = { "BALANCED", "AGILE", "WIDEBAND" };
        private static readonly string[] BusTradeoffs = { "STANDARD THRUST AND COVERAGE", "FASTER TRANSFERS · NARROWER FOOTPRINT", "WIDER FOOTPRINT · SLOWER TRANSFERS" };

        private void BuildLaunch(RectTransform column, float w, float h)
        {
            launch = new FrontBox(column, skin, "Launch", 0f, 0f, w, h, "LAUNCH CONTROL", AvIcon.Satellite, FrontLook.Console).Tagged("LCO");
            RectTransform root = launch.Rect;
            AvionicsSections(root, w, h, (ManifestY, ManifestH, Own), (PadsY, PadsH, RoomInk.Telemetry));
            float x = ConsoleInset + 10f, width = w - 2f * x, cell = (width - 12f) / 3f;
            launchFunds = ConsoleReadout(root, "Funds", x, 31f, width * 0.65f, true);
            launchAllocation = ConsoleReadout(root, "Allocation", x + width * 0.65f, 31f, width * 0.35f, false, TextAlignmentOptions.MidlineRight);
            ConsoleHeading(root, "Manifest", x, ManifestY + 8f, width, "FLIGHT MANIFEST", Own);
            ConsoleHeading(root, "Payload", x, ManifestY + 39f, width, "PAYLOAD", Mc.Steel);
            ConsoleHeading(root, "Bus", x, ManifestY + 122f, width, "BUS", Mc.Steel);
            for (int k = 0; k < payloadButtons.Length; k++)
            {
                int slot = k;
                float columnX = x + k * (cell + 6f);
                payloadButtons[k] = Small(root, FrontViews.BirdCallsign(k), columnX, ManifestY + 61f, cell, () => PickPayload(slot), PayloadJob[k]);
                payloadStates[k] = ConsoleReadout(root, "PayloadState" + k, columnX, ManifestY + 87f, cell, false, TextAlignmentOptions.Midline);
                busButtons[k] = Small(root, BusNames[k], columnX, ManifestY + 144f, cell, () => PickBus(slot), BusTradeoffs[k]);
            }
            payloadReadback = ConsoleReadout(root, "PayloadReadback", x, ManifestY + 180f, width, true);
            busReadback = ConsoleReadout(root, "BusReadback", x, ManifestY + 205f, width);
            stationReadback = ConsoleReadout(root, "StationReadback", x, ManifestY + 232f, width - 227f);
            autoStation = Small(root, "AUTO", x + width - 215f, ManifestY + 230f, 63f, UseHomeStation,
                "Clear the picked station and use the payload's automatic station.");
            stationPick = Small(root, "PICK STATION", x + width - 144f, ManifestY + 230f, 144f, ArmLaunchStation, "Pick this bird's initial station on the coverage map. Esc cancels.");
            launchReceipt = ConsoleReadout(root, "LaunchReceipt", x, ManifestY + 260f, width, true);
            launchProgress = new FrontBar(root, skin, x, ManifestY + 285f, width, 6f);
            queueSatellite = Small(root, "QUEUE LAUNCH", x, ManifestY + 302f, width, QueueLaunch, "Send this manifest to the SPACE programme queue.");
            queueSatellite.SetStyle(AvButtonStyle.Primary);
            chipSatellite = Small(root, "CHIP IN", x, ManifestY + 302f, width, ChipLaunch, "Donate pilot allocation to this manifest.");

            padHead = ConsoleReadout(root, "PadHead", x, PadsY + 8f, width - 152f, true);
            buildPad = Small(root, "BUILD PAD", x + width - 144f, PadsY + 6f, 144f, ArmPadPick,
                "Pick a construction site within the ring around a held airbase. Press again or Esc to cancel.");
            for (int i = 0; i < padCards.Length; i++) padCards[i] = new PadCard(this, root, x, PadsY + 44f + 71f * i, width, i);
            padHint = ConsoleReadout(root, "PadReadback", x, PadsY + 188f, width);
        }

        private void PickPayload(int slot)
        {
            actions.Touch();
            launchPreviewKind = (BirdKind)slot;
            Repaint();
        }

        private void PickBus(int slot)
        {
            actions.Touch();
            launchPreviewBus = (SatelliteBus)slot;
            Repaint();
        }

        private void UseHomeStation()
        {
            actions.Touch();
            launchStation = null;
            launchStationPick = false;
            Repaint();
        }

        private void QueueLaunch()
        {
            int kind = (int)launchPreviewKind;
            if (last == null || kind >= last.Fleet.Count || last.Fleet[kind].Up ||
                !string.IsNullOrEmpty(last.Fleet[kind].Launch.Refusal) || LaunchRow(last, kind, out _)) return;
            actions.Touch();
            Vector2 station = launchStation.GetValueOrDefault();
            if (actions.Queue(Front.Space, ProgrammeId.LaunchSatellite, GeoSpace.PackLaunch(launchPreviewKind, launchPreviewBus),
                launchStation.HasValue, station.x, station.y))
            {
                launchStation = null;
                launchStationPick = false;
                Repaint();
            }
        }

        private void ChipLaunch()
        {
            if (last != null && LaunchRow(last, (int)launchPreviewKind, out FrontQueueView q)) Donate(q.Row);
        }

        private void PaintLaunch(FrontRoomView v)
        {
            int kind = (int)launchPreviewKind;
            bool waiting = FrontViews.IsWaiting(v.Row);
            launch.SetMeta(v.BirdsUp + "/" + v.BirdCap + " BIRDS · " + v.PadsUp + "/" + FrontRules.PadCap + " PADS", Mc.Steel);
            Readout(launchFunds, waiting ? "FRONT FUNDS · AWAITING HOST" : "FRONT FUNDS " + v.Row.Budget + " · +" + v.FundsRate + " / MIN", RoomInk.Engrave);
            Readout(launchAllocation, "YOUR ALLOCATION " + v.Allocation + " AP", Mc.Steel);
            FleetCardView selected = Fleet(v, kind);
            bool queued = LaunchRow(v, kind, out FrontQueueView row);
            SatelliteBus manifestBus = selected.Up ? selected.Bus : launchPreviewBus;
            if (queued)
                foreach (FrontQueueRow programme in v.Row.Queue)
                    if (programme.Row == row.Row && GeoSpace.TryUnpackLaunch(programme.Kind, out _, out SatelliteBus queuedBus))
                    {
                        manifestBus = queuedBus;
                        break;
                    }
            for (int i = 0; i < payloadButtons.Length; i++)
            {
                payloadButtons[i].Latched = i == kind;
                busButtons[i].Latched = i == (int)manifestBus;
                OpsKit.Enable(busButtons[i], !selected.Up && !queued);
                FleetCardView bird = Fleet(v, i);
                Readout(payloadStates[i], bird.State, FrontKit.Tone(bird.Tone));
            }

            FrontOfferView offer = selected.Launch;
            Readout(payloadReadback, selected.Callsign + " / " + PayloadJob[kind], Own);
            float footprint = GeoSpace.Footprint(launchPreviewKind, manifestBus) * v.TheatreKm;
            float transferSpeed = GeoBird.SpeedFor(manifestBus) * v.TheatreKm;
            string performance = v.TheatreKm > 0f ? " · " + Mathf.RoundToInt(footprint) + " KM RADIUS · " + FrontKit.Clock(10f / transferSpeed) + " PER 10 KM" : "";
            Readout(busReadback, BusNames[(int)manifestBus] + performance, Mc.Steel);

            string station = "INITIAL STATION · AUTO";
            if (selected.Up) station = "CURRENT FOOTPRINT SHOWN IN OPERATE";
            else if (queued)
            {
                foreach (FrontQueueRow programme in v.Row.Queue)
                    if (programme.Row == row.Row && programme.HasPoint)
                    {
                        station = "QUEUED STATION · GRID " + OpsKit.Grid(programme.X, programme.Z);
                        break;
                    }
            }
            else if (launchStation.HasValue)
            {
                MapFrame map = FrontController.Frame();
                station = "INITIAL STATION · GRID " + OpsKit.Grid(map.X(launchStation.Value.x), map.Z(launchStation.Value.y));
            }
            Readout(stationReadback, launchStationPick ? "PICK INITIAL STATION ON MAP" : station, launchStationPick ? RoomInk.Telemetry : Mc.Steel);
            stationPick.Latched = launchStationPick;
            OpsKit.Enable(autoStation, !selected.Up && !queued && (launchStationPick || launchStation.HasValue));
            OpsKit.Enable(stationPick, launchStationPick || !selected.Up && !queued);
            int chip = queued ? ChipAp(row) : 0;
            bool canQueue = !waiting && !selected.Up && !queued && string.IsNullOrEmpty(offer.Refusal);
            Color receiptInk = Mc.Steel;
            string receipt;
            if (queued)
            {
                receipt = row.Name + " · " + QueueStatus(row);
                receiptInk = RoomInk.Telemetry;
                launchProgress.Set(row.Progress, receiptInk);
            }
            else if (selected.Up) receipt = "ON STATION · SELECT OPERATE TO CONTROL " + selected.Callsign;
            else if (!canQueue)
            {
                receipt = offer.Refusal ?? "WAITING FOR HOST";
                receiptInk = FrontKit.Tone(AvState.Caution);
            }
            else receipt = Mathf.RoundToInt(offer.Cost) + " FRONT FUNDS · " + FrontKit.Clock(offer.BuildSeconds) + " BUILD · LIVE PAD REQUIRED";
            Readout(launchReceipt, receipt, receiptInk);
            launchProgress.Show(queued);
            Visible(queueSatellite.Rect, !queued);
            Visible(chipSatellite.Rect, chip > 0);
            OpsKit.Enable(queueSatellite, canQueue);
            OpsKit.Label(queueSatellite, selected.Up ? "PAYLOAD IN SERVICE" : "QUEUE " + selected.Callsign + " · " + BusNames[(int)launchPreviewBus]);
            queueSatellite.Help = canQueue ? "Submit the selected manifest for host approval. The programme uses front funds, then builds at a live pad." : receipt;
            OpsKit.Enable(chipSatellite, chip > 0 && v.Allocation >= chip);
            if (chip > 0) OpsKit.Label(chipSatellite, "CHIP IN · " + Pays(chip));

            FrontOfferView pad = OfferOf(v, ProgrammeId.UplinkSite);
            bool held = v.Airbases.Exists(a => a.Side == AirbaseSide.Own);
            padWhy = !string.IsNullOrEmpty(pad.Refusal) ? pad.Refusal : held ? "" : FrontWords.Refusal(FrontOutcome.NoSite, Front.Space);
            Readout(padHead, "LAUNCH PADS " + v.PadsUp + " / " + FrontRules.PadCap, RoomInk.Telemetry);
            for (int i = 0; i < padCards.Length; i++) padCards[i].Paint(v);
            buildPad.Latched = padPick;
            OpsKit.Enable(buildPad, padPick || padWhy.Length == 0);
            string padText = padPick ? (padMiss ? "OUTSIDE THE RING · PICK NEAR A HELD AIRBASE" : "PICK NEAR A HELD AIRBASE · ESC CANCELS")
                : padWhy.Length > 0 ? padWhy : Mathf.RoundToInt(pad.Cost) + " FRONT FUNDS · " + FrontKit.Clock(pad.BuildSeconds) + " BUILD · " + FrontRules.BirdsPerPad + " BIRDS PER PAD";
            Readout(padHint, padText, padPick || padWhy.Length > 0 ? RoomInk.Telemetry : Mc.Steel);
        }

        private static FleetCardView Fleet(FrontRoomView v, int kind) =>
            kind < v.Fleet.Count ? v.Fleet[kind] : new FleetCardView { Kind = kind, Callsign = FrontViews.BirdCallsign(kind), State = "AWAITING HOST", Launch = new FrontOfferView { Refusal = "WAITING FOR HOST" } };

        private static string QueueStatus(in FrontQueueView row) =>
            row.Waiting && row.Id == ProgrammeId.LaunchSatellite ? "WAITING FOR LIVE PAD" :
            row.Building ? Clocked("BUILDING · ", row.BuildLeft) : "FUNDED " + Mathf.RoundToInt(row.Progress * 100f) + "%";

        private void Donate(ushort row) => FrontPanels.Donate(actions, Front.Space, last, row);

        private void AvionicsSections(RectTransform root, float w, float h, params (float Y, float H, Color Ink)[] sections)
        {
            FrontVector drawing = FrontVector.Add(root, "Sections");
            AvLay.Place(drawing.rectTransform, 0f, 0f, w, h);
            skin.Add(() =>
            {
                drawing.Clear();
                foreach (var section in sections) Slab(drawing, ConsoleInset, section.Y, w - 2f * ConsoleInset, section.H, section.Ink);
                drawing.Flush();
            });
        }

        private static void Slab(FrontVector drawing, float x, float y, float w, float h, Color accent)
        {
            drawing.Rect(x, y, w, h, RoomInk.Plate.WithAlpha(0.92f));
            drawing.Frame(x, y, w, h, 1f, RoomInk.Bezel.WithAlpha(0.85f));
            drawing.Rect(x, y, 3f, h, accent.WithAlpha(0.9f));
        }

        private TMP_Text ConsoleHeading(RectTransform root, string id, float x, float y, float w, string word, Color ink)
        {
            TMP_Text line = ConsoleReadout(root, id, x, y, w, true);
            line.text = word;
            skin.Add(() => line.color = ink);
            return line;
        }

        private static TMP_Text ConsoleReadout(RectTransform root, string id, float x, float y, float w, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) =>
            FrontKit.Mono(root, id, x, y, w, 20f, bold ? 12f : 10.5f, align, bold);

        private static void Readout(TMP_Text line, string value, Color tone) =>
            FrontKit.Set(line, C2Kit.FitTo(line, value ?? "", line.rectTransform.rect.width), tone);

        private static void Visible(RectTransform rect, bool on)
        {
            if (rect.gameObject.activeSelf != on) rect.gameObject.SetActive(on);
        }

        private sealed class PadCard
        {
            private readonly OrbitRoom room;
            private readonly int slot;
            private readonly TMP_Text name, state, detail;
            private readonly FrontBar progress;
            private readonly AvControl action;
            private PadView current;

            public PadCard(OrbitRoom room, RectTransform parent, float x, float y, float w, int slot)
            {
                this.room = room;
                this.slot = slot;
                RectTransform root = AvLay.Child(parent, "Pad" + slot);
                AvLay.Place(root, x, y, w, 63f);
                AvFrame frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, 63f, 0f, 0f);
                room.skin.Add(() => frame.Paint(RoomInk.ConsoleBody, RoomInk.Bezel));
                name = ConsoleReadout(root, "Name", 10f, 5f, w - 202f, true);
                state = ConsoleReadout(root, "State", w - 182f, 5f, 172f, true, TextAlignmentOptions.MidlineRight);
                detail = ConsoleReadout(root, "Detail", 10f, 30f, w - 225f);
                progress = new FrontBar(root, room.skin, 10f, 54f, w - 224f, 4f);
                action = room.Small(root, "REBUILD", w - 208f, 30f, 198f, Press, "");
                AvHit.On(frame).Click = e =>
                {
                    if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left && current.U >= 0f)
                        room.ShowSite(new Vector2(current.U, current.V));
                };
            }

            private void Press()
            {
                room.actions.Touch();
                if (current.Building) room.Donate(current.Row);
                else if (current.Lost && current.U >= 0f && room.padWhy.Length == 0)
                    room.actions.Queue(Front.Space, ProgrammeId.UplinkSite, 0, true, current.U, current.V);
            }

            public void Paint(FrontRoomView v)
            {
                current = new PadView { Name = FrontViews.SiteName(slot), U = -1f };
                foreach (PadView pad in v.Pads)
                    if (pad.Slot == slot) { current = pad; break; }
                Readout(name, current.Name + (string.IsNullOrEmpty(current.Place) ? "" : " · " + current.Place), RoomInk.Engrave);
                FrontQueueView row = default;
                bool hasQueue = current.Building && QueueRow(v, current.Row, out row);
                string status = current.Up ? "LINK UP" : current.Lost ? "OFFLINE" : "NO PAD";
                string note = current.Up ? current.Links : current.Lost ? "REBUILD AT THE SAME SITE" : "BUILD A PAD TO ADD CAPACITY";
                Color ink = current.Up ? Mc.Go : current.Lost ? Mc.NoGo : Mc.Steel;
                int chip = 0;
                if (hasQueue)
                {
                    status = QueueStatus(row);
                    note = Mathf.RoundToInt(row.Cost) + " FRONT FUNDS · " + FrontKit.Clock(row.BuildTotal) + " BUILD";
                    ink = RoomInk.Telemetry;
                    chip = ChipAp(row);
                    progress.Set(row.Progress, ink);
                }
                Readout(state, status, ink);
                Readout(detail, note, Mc.Steel);
                progress.Show(hasQueue);
                Visible(action.Rect, current.Lost || chip > 0);
                OpsKit.Label(action, current.Lost ? "REBUILD" : "CHIP IN · " + Pays(chip));
                OpsKit.Enable(action, current.Lost ? current.U >= 0f && room.padWhy.Length == 0 : chip > 0 && v.Allocation >= chip);
                action.Help = current.Lost ? "Queue this pad at its previous position." : "Donate pilot allocation to the pad programme.";
            }
        }
    }
}
