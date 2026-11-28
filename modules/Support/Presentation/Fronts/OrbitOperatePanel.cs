using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using static BoscaliSummer.Modules.Support.Presentation.Fronts.FrontPanels;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    internal sealed partial class OrbitRoom
    {
        private const float WatchY = 65f, ReturnY = 348f, ManoeuvreY = 570f, HostilesY = 713f;
        private RectTransform operateRoot;
        private FrontBox operate;
        private AvControl returnToOperate;
        private bool operateDownlink;
        private bool? retimeDraft;
        private int confirmTrackId;
        private TMP_Text watchLine, payloadLine, windowLine, clockKey, clockFace, bookingLine, nodeLine, retimeLine, interceptLine;
        private TMP_Text reportTitle, reportDetail, reportStatus;
        private FrontBar passProgress;
        private FrontVector reportFrame;
        private readonly AvControl[] flightSelect = new AvControl[FrontRules.BirdCap];
        private readonly TMP_Text[] flightTelemetry = new TMP_Text[FrontRules.BirdCap];
        private readonly AvControl[] hostileSelect = new AvControl[FrontRules.BirdCap];
        private AvControl bookPass, downlink, sendToMis, planNode, auxManoeuvre, inspectWarning, switchTiming, intercept;

        internal bool OperateDownlinkShown => operateDownlink;

        private void BuildOperate(RectTransform column, float width, float height)
        {
            operateRoot = AvLay.Child(column, "Operate");
            AvLay.Place(operateRoot, 0f, 0f, width, height);
            operate = new FrontBox(operateRoot, skin, "OrbitalWatch", 0f, 0f, width, height, "SPACE MISSION CONTROL", AvIcon.Satellite, FrontLook.Console);
            RectTransform root = operate.Rect;
            float x = ConsoleInset + 9f, w = width - 2f * x, half = (w - 8f) / 2f;
            AvionicsSections(root, width, height, (WatchY, 262f, Own));
            reportFrame = FrontVector.Add(root, "ReturnFrame");
            AvLay.Place(reportFrame.rectTransform, 0f, 0f, width, height);
            watchLine = ConsoleReadout(root, "Watch", x, 33f, w - 125f, true);
            inspectWarning = Small(root, "FOCUS THREAT", x + w - 114f, 31f, 114f, InspectThreat, "Select the threatened payload.", RoomInk.Telemetry);

            payloadLine = ConsoleReadout(root, "Objective", x + 3f, WatchY + 14f, w - 6f, true);
            clockKey = ConsoleReadout(root, "PassPhase", x + 3f, WatchY + 52f, half, true);
            clockFace = FrontKit.Cond(root, "PassClock", x, WatchY + 72f, half, 66f, 52f, TextAlignmentOptions.MidlineLeft, AvTextRole.Display);
            windowLine = ConsoleReadout(root, "Window", x + 3f, WatchY + 139f, w, true);
            bookingLine = ConsoleReadout(root, "Reservation", x + 3f, WatchY + 164f, w);
            passProgress = new FrontBar(root, skin, x + 3f, WatchY + 193f, w - 6f, 3f);
            float taskX = x + half + 8f;
            bookPass = Prominent(root, "BOOK PASS", taskX, WatchY + 62f, half, 43f,
                BookCommand, "Schedule collection over the selected grid for its next usable pass.", RoomInk.Telemetry);
            downlink = Small(root, "OPEN DOWNLINK", taskX, WatchY + 111f, half, OpenSelectedFeed, "Open the selected satellite's sensor station.");

            float cell = (w - 12f) / 3f;
            for (int i = 0; i < flightSelect.Length; i++)
            {
                int k = i;
                float slotX = x + i * (cell + 6f);
                flightSelect[i] = Small(root, FrontViews.BirdCallsign(i), slotX, WatchY + 208f, cell, () => Select(k),
                    "Bring this payload onto the mission console.", PayloadInk[i]);
                flightTelemetry[i] = ConsoleReadout(root, "Availability" + i, slotX, WatchY + 233f, cell, false, TextAlignmentOptions.Midline);
            }

            reportStatus = ConsoleReadout(root, "ReturnStatus", x + 5f, ReturnY + 12f, w - 10f, true);
            reportTitle = FrontKit.Cond(root, "ReturnTarget", x + 5f, ReturnY + 37f, w - 10f, 48f, 30f, TextAlignmentOptions.MidlineLeft, AvTextRole.Display);
            reportDetail = ConsoleReadout(root, "ReturnBrief", x + 5f, ReturnY + 91f, w - 10f);
            sendToMis = Prominent(root, "SEND TO MIS", x + 5f, ReturnY + 128f, w - 10f, 48f, SendReport,
                "Publish the returned target as a job for allied pilots.", RoomInk.Telemetry);

            ConsoleHeading(root, "TrackControl", x, ManoeuvreY, w * 0.5f, "TRACK CONTROL", Own);
            nodeLine = ConsoleReadout(root, "TrackSolution", x, ManoeuvreY + 29f, w, true);
            retimeLine = ConsoleReadout(root, "TimingSolution", x, ManoeuvreY + 56f, w);
            planNode = Small(root, "PLAN NEW TRACK", x, ManoeuvreY + 84f, half, BurnCommand, "Plan a ground-track transfer with a reserve quote.");
            float timingWidth = (half - 6f) * 0.5f;
            auxManoeuvre = Small(root, "ADVANCE PASS", taskX, ManoeuvreY + 84f, timingWidth, AuxiliaryCommand, "Preview a shift to an earlier pass.");
            switchTiming = Small(root, "DELAY PASS", x + w - timingWidth, ManoeuvreY + 84f, timingWidth, () => RetimeSelected(false),
                "Preview a shift to a later pass.", RoomInk.Telemetry);

            ConsoleHeading(root, "HostilePicture", x, HostilesY, w, "HOSTILE TRACKS", AvInk.Hostile);
            for (int i = 0; i < hostileSelect.Length; i++)
            {
                int k = i;
                hostileSelect[i] = Small(root, "TRACK", x + i * (cell + 6f), HostilesY + 27f, cell, () => SelectHostile(k), "Select a coarse hostile track.", AvInk.Hostile);
            }
            interceptLine = ConsoleReadout(root, "InterceptWindow", x, HostilesY + 55f, w, true);
            intercept = Small(root, "PREVIEW INTERCEPT", x, HostilesY + 85f, w, InterceptCommand, "Review the commitment for the selected track.", AvInk.Hostile);
        }

        private void InspectThreat()
        {
            if (last != null && last.IncomingBird >= 0) Select(last.IncomingBird);
        }

        private void BookCommand()
        {
            if (last == null || !TryBird(selKind, out OrbitTrackView bird) || !bird.Alive) return;
            if (IsThreatened(last, bird))
            {
                if (WatchEvasion(last, bird, out bool advance))
                {
                    relocating = hasPending = false;
                    RetimeSelected(advance);
                }
                return;
            }
            if (!CanBook(bird)) return;
            actions.Touch();
            if (bird.Booked) actions.CancelPass(selKind);
            else if (hasPass[selKind])
            {
                MapFrame map = FrontController.Frame();
                actions.BookPass(selKind, new Vector2(map.X(aim.x), map.Z(aim.y)));
            }
        }

        private static bool CanBook(in OrbitTrackView bird) => bird.Alive && bird.Kind != BirdKind.Kinetic;

        private void OpenSelectedFeed()
        {
            if (selKind >= 0) ShowFeed(selKind);
        }

        private void SendReport()
        {
            if (last == null || !last.CollectionReport.CanSend || last.CollectionReport.ContactId <= 0) return;
            actions.Touch();
            actions.SendOrbitalTarget(last.CollectionReport.ContactId);
        }

        private void BurnCommand()
        {
            if (selKind < 0) return;
            if (relocating) Confirm();
            else StartRelocate(selKind);
        }

        private void AuxiliaryCommand()
        {
            if (relocating) Cancel();
            else RetimeSelected(true);
        }

        private void RetimeSelected(bool advance)
        {
            if (last == null || !TryBird(selKind, out OrbitTrackView bird) || !bird.Alive || !PlanRetime(last, bird, advance, out _)) return;
            actions.Touch();
            if (retimeDraft == advance)
            {
                if (actions.Retime(selKind, advance)) retimeDraft = null;
            }
            else retimeDraft = advance;
            Repaint();
        }

        private static bool IsThreatened(FrontRoomView view, in OrbitTrackView bird) =>
            view.IncomingBird == (int)bird.Kind && !view.IncomingClear;

        private static bool PlanRetime(FrontRoomView view, in OrbitTrackView bird, bool advance, out GeoBird after)
        {
            if (GeoSpace.PlanRephase(bird.Geo, view.Now, AsatRules.RetimeShift(advance), out after) != GeoSpace.Refusal.None) return false;
            if (!IsThreatened(view, bird)) return true;
            float conjunction = view.Now + view.IncomingPassSeconds;
            return after.ArrivalAt < conjunction && !AsatRules.OpportunityRemains(after, conjunction);
        }

        private bool WatchEvasion(FrontRoomView view, in OrbitTrackView bird, out bool advance)
        {
            advance = retimeDraft.GetValueOrDefault();
            if (PlanRetime(view, bird, advance, out _)) return true;
            advance = !advance;
            return PlanRetime(view, bird, advance, out _);
        }

        private void SelectHostile(int slot)
        {
            if (last == null || slot >= last.Hostile.Count) return;
            actions.Touch();
            EndPicks();
            selectedTrackId = last.Hostile[slot].Id;
            Repaint();
        }

        private void InterceptCommand()
        {
            if (last == null) return;
            if (InterceptQueue(last, selectedTrackId, out _, out FrontQueueView queue)) { Donate(queue.Row); return; }
            if (last.OutgoingTrackId > 0 || !TryHostile(last, selectedTrackId, out _) || !string.IsNullOrEmpty(last.Asat.Refusal)) return;
            actions.Touch();
            if (confirmTrackId == selectedTrackId)
            {
                if (actions.QueueAsat(selectedTrackId)) confirmTrackId = 0;
            }
            else confirmTrackId = selectedTrackId;
            Repaint();
        }

        private static bool TryHostile(FrontRoomView view, int id, out HostileBirdView result)
        {
            if (id > 0)
                foreach (HostileBirdView track in view.Hostile)
                    if (track.Id == id) { result = track; return true; }
            result = default;
            return false;
        }

        private static bool InterceptQueue(FrontRoomView view, int trackId, out int queuedTarget, out FrontQueueView queue)
        {
            foreach (FrontQueueRow row in view.Row.Queue)
                if (row.Id == ProgrammeId.Asat && (trackId == 0 || row.TargetTrack == trackId) && QueueRow(view, row.Row, out queue))
                {
                    queuedTarget = row.TargetTrack;
                    return true;
                }
            queuedTarget = 0;
            queue = default;
            return false;
        }

        private void PaintOperate(FrontRoomView view)
        {
            bool warning = view.IncomingBird >= 0;
            string watch = warning ? AsatCountdown(view) : view.PadsUp > 0 ? view.PadsUp + " DOWNLINK" + (view.PadsUp == 1 ? "" : "S") + " AVAILABLE" : "DOWNLINK NETWORK OFFLINE";
            Readout(watchLine, watch, warning ? view.IncomingClear ? Mc.Go : Mc.NoGo : Mc.Steel);
            Visible(inspectWarning.Rect, warning);
            for (int i = 0; i < flightSelect.Length; i++)
            {
                FleetCardView bird = Fleet(view, i);
                flightSelect[i].Latched = i == selKind;
                OpsKit.Enable(flightSelect[i], bird.Up);
                string availability = bird.Up ? hasPass[i] ? PassClock(passes[i], view.Now) : "NO ACCESS TO SELECTED GRID" : bird.State;
                Readout(flightTelemetry[i], availability, i == view.IncomingBird && !view.IncomingClear ? Mc.NoGo : hasPass[i] ? PayloadInk[i] : Mc.Steel);
            }
            bool live = TryBird(selKind, out OrbitTrackView selected) && selected.Alive;
            PaintTasking(view, selected, live);
            PaintReport(view);
            PaintManoeuvre(view, selected, live);
            PaintIntercept(view);
            operate.SetMeta(view.BirdsUp + " PAYLOAD" + (view.BirdsUp == 1 ? "" : "S") + "  /  "
                + view.Hostile.Count + " TRACK" + (view.Hostile.Count == 1 ? "" : "S"), Mc.Steel);
        }

        private void PaintTasking(FrontRoomView view, in OrbitTrackView bird, bool live)
        {
            bool passKnown = live && hasPass[selKind];
            OrbitPass pass = passKnown ? passes[selKind] : default;
            bool inPass = passKnown && pass.Contains(view.Now);
            bool threatened = live && IsThreatened(view, bird);
            MapFrame frame = FrontController.Frame();
            string grid = OpsKit.Grid(frame.X(aim.x), frame.Z(aim.y));
            Color key = threatened ? Mc.NoGo : inPass ? Mc.Go : live ? PayloadInk[selKind] : Mc.Steel;
            Readout(payloadLine, "WATCH GRID " + grid + (live ? "  /  " + bird.Callsign + " · " + PayloadRole[(int)bird.Kind] : ""), key);
            string clockHeading = threatened ? view.IncomingLaunched ? "CONJUNCTION IN" : "INTERCEPTOR LAUNCH IN"
                : passKnown ? inPass ? "LOSS OF SIGNAL IN" : "ACQUISITION IN" : "NEXT PASS";
            string clock = threatened ? FrontKit.Clock(view.IncomingLaunched ? view.IncomingPassSeconds : view.IncomingSeconds)
                : passKnown ? FrontKit.Clock((inPass ? pass.EndAt : pass.StartAt) - view.Now) : "—:—";
            Readout(clockKey, clockHeading, key);
            FrontKit.Set(clockFace, clock, key);
            string window = passKnown ? inPass ? "LIVE OVER OBJECTIVE" : "LOS " + FrontKit.Clock(pass.EndAt - view.Now) + "  /  WINDOW " + FrontKit.Clock(pass.EndAt - pass.StartAt)
                : live ? "NO ACCESS · PICK A GRID OR PLAN A NEW TRACK" : "QUEUE A PAYLOAD IN LAUNCH";
            if (threatened) window = view.IncomingLaunched ? "RETIME THIS PAYLOAD TO BREAK CONJUNCTION"
                : "CONJUNCTION IN " + FrontKit.Clock(view.IncomingPassSeconds) + " · ACT BEFORE LAUNCH";
            Readout(windowLine, window, threatened ? Mc.NoGo : inPass ? Mc.Go : Mc.Steel);
            passProgress.Show(passKnown && !threatened);
            if (passKnown && !threatened) passProgress.Set(inPass
                ? 1f - (view.Now - pass.StartAt) / Mathf.Max(1f, pass.EndAt - pass.StartAt)
                : 1f - (pass.StartAt - view.Now) / Mathf.Max(1f, bird.Geo.PeriodSeconds), key);
            string booked = "";
            if (live && bird.Booked)
            {
                booked = "BOOKED " + OpsKit.Grid(frame.X(bird.BookedU), frame.Z(bird.BookedV)) + (bird.BookedAt < 0f
                    ? " · AWAITING ACCESS" : " · COLLECTION " + FrontKit.Clock(bird.BookedAt - view.Now));
            }
            else if (live && bird.CollectionStatus == OrbitCollection.Collected) booked = "COLLECTION RETURN RECEIVED";
            else if (live && bird.CollectionStatus == OrbitCollection.Missed) booked = "COLLECTION MISSED · BOOK AGAIN";
            else if (live && bird.Kind == BirdKind.Kinetic) booked = "STRIKE AVAILABILITY FOLLOWS THE LIVE PASS";
            Readout(bookingLine, booked, bird.Booked ? RoomInk.Telemetry : Mc.Steel);
            bool evasiveShift = false;
            bool canEvade = threatened && WatchEvasion(view, bird, out evasiveShift);
            string action = threatened ? !canEvade ? "NO CLEAR SHIFT" : retimeDraft == evasiveShift ? "CONFIRM EVASION" : "PREVIEW EVASION"
                : live && bird.Booked ? "CANCEL BOOKING" : "BOOK PASS";
            OpsKit.Label(bookPass, action);
            bookPass.SetStyle(threatened ? AvButtonStyle.Danger : AvButtonStyle.Primary);
            bookPass.Help = threatened ? "Preview a timing shift that moves this pass away from conjunction." : "Schedule collection over the selected grid.";
            Visible(bookPass.Rect, threatened || live && CanBook(bird));
            OpsKit.Enable(bookPass, threatened ? canEvade : live && (bird.Booked || passKnown && view.PadsUp > 0));
            OpsKit.Enable(downlink, live && Feed != null);
        }

        private void PaintReport(FrontRoomView view)
        {
            CollectionReportView report = view.CollectionReport;
            bool identified = report.ContactId > 0;
            Color ink = report.CanSend ? RoomInk.Telemetry : identified ? Own : Mc.Steel;
            string freshness = report.FreshUntil > view.Now ? "  /  FIX EXPIRES " + FrontKit.Clock(report.FreshUntil - view.Now) : "";
            Readout(reportStatus, "COLLECTION RETURN" + freshness, ink);
            string title = string.IsNullOrEmpty(report.Title) ? "AWAITING COLLECTION" : report.Title;
            Readout(reportTitle, title, identified ? RoomInk.Engrave : Mc.Steel);
            string grid = string.IsNullOrEmpty(report.Grid) ? "" : "GRID " + report.Grid + "  /  ";
            string brief = report.CanSend || string.IsNullOrEmpty(report.SendRefusal) ? report.Summary : report.SendRefusal;
            Readout(reportDetail, grid + (string.IsNullOrEmpty(brief) ? "BOOK A SENSOR PASS TO SEARCH THE WATCH GRID" : brief), ink);
            OpsKit.Enable(sendToMis, report.CanSend && identified);
            sendToMis.Armed = report.CanSend && identified;
            sendToMis.Help = report.CanSend ? "Send this confirmed target into MIS JOBS for allied pilots to claim."
                : report.SendRefusal ?? "A confirmed enemy ground target is required.";

            float width = operate.Rect.sizeDelta.x;
            reportFrame.Clear();
            reportFrame.Rect(ConsoleInset, ReturnY, width - ConsoleInset * 2f, 197f, RoomInk.ConsoleBody);
            reportFrame.Frame(ConsoleInset, ReturnY, width - ConsoleInset * 2f, 197f, 1f, ink.WithAlpha(report.CanSend ? 0.9f : 0.45f));
            reportFrame.Rect(ConsoleInset, ReturnY, 4f, 197f, ink.WithAlpha(0.95f));
            reportFrame.Line(new Vector2(ConsoleInset + 10f, ManoeuvreY - 12f), new Vector2(width - ConsoleInset - 10f, ManoeuvreY - 12f), 1f, RoomInk.Bezel.WithAlpha(0.55f));
            reportFrame.Line(new Vector2(ConsoleInset + 10f, HostilesY - 11f), new Vector2(width - ConsoleInset - 10f, HostilesY - 11f), 1f, RoomInk.Bezel.WithAlpha(0.55f));
            reportFrame.Flush();
        }

        private void PaintManoeuvre(FrontRoomView view, in OrbitTrackView bird, bool live)
        {
            bool proposesNode = live && relocating;
            bool haveNode = PreviewNode(bird, out GeoBird proposed);
            string node = live ? "RESERVE " + Mathf.RoundToInt(bird.Geo.DeltaV) + " M/S"
                + (bird.Relocating ? " · TRACK TRANSFER " + FrontKit.Clock(bird.EtaSeconds) : "") : "NO SATELLITE SELECTED";
            if (proposesNode) node = haveNode ? "TRANSFER " + FrontKit.Clock(proposed.ArrivalAt - view.Now)
                + " · " + Mathf.RoundToInt(bird.Geo.DeltaV - proposed.DeltaV) + " M/S"
                : hasPending ? "TRANSFER UNAVAILABLE AT THIS GRID" : "PICK THE NEW CROSSING ON THE MAP";
            Readout(nodeLine, node, haveNode ? RoomInk.Telemetry : Mc.Steel);
            bool threatened = live && IsThreatened(view, bird);
            GeoBird advanced = default, delayed = default;
            bool advance = live && PlanRetime(view, bird, true, out advanced), delay = live && PlanRetime(view, bird, false, out delayed);
            if (retimeDraft.HasValue && (retimeDraft.Value ? !advance : !delay)) retimeDraft = null;
            string timing = live ? "SHIFT WINDOW · " + Mathf.RoundToInt(GeoOrbit.PhaseDeltaV(bird.Kind, AsatRules.RetimeShift(false))) + " M/S" : "";
            if (threatened && !advance && !delay) timing = "NO TIMING SHIFT CAN CLEAR IN TIME";
            if (retimeDraft.HasValue)
            {
                GeoBird shifted = retimeDraft.Value ? advanced : delayed;
                timing = "TRANSFER " + FrontKit.Clock(shifted.ArrivalAt - view.Now) + " · "
                    + Mathf.RoundToInt(bird.Geo.DeltaV - shifted.DeltaV) + " M/S";
                if (threatened) timing += " · CLEARS CONJUNCTION";
                else if (shifted.NextPass(shifted.ArrivalAt, aim.x, aim.y, bird.Radius, out OrbitPass pass))
                    timing += " · " + PassClock(pass, view.Now);
            }
            Readout(retimeLine, timing, retimeDraft.HasValue ? RoomInk.Telemetry : threatened ? Mc.NoGo : Mc.Steel);
            OpsKit.Label(planNode, proposesNode ? "EXECUTE BURN" : "PLAN NEW TRACK");
            OpsKit.Enable(planNode, proposesNode ? haveNode : live && CanRelocate(selKind));
            Visible(auxManoeuvre.Rect, proposesNode || live);
            Visible(switchTiming.Rect, live && !proposesNode);
            float timingWidth = switchTiming.Rect.sizeDelta.x;
            float advanceWidth = proposesNode ? timingWidth * 2f + 6f : timingWidth;
            if (auxManoeuvre.Rect.sizeDelta.x != advanceWidth)
                AvLay.Place(auxManoeuvre.Rect, switchTiming.Rect.anchoredPosition.x - timingWidth - 6f, ManoeuvreY + 84f, advanceWidth, 24f);
            string step = FrontKit.Clock(AsatRules.RetimeShift(false));
            OpsKit.Label(auxManoeuvre, proposesNode ? "CANCEL BURN" : retimeDraft == true ? "CONFIRM ADVANCE" : "ADVANCE " + step);
            OpsKit.Label(switchTiming, retimeDraft == false ? "CONFIRM DELAY" : "DELAY " + step);
            OpsKit.Enable(auxManoeuvre, proposesNode || advance);
            OpsKit.Enable(switchTiming, delay);
        }

        private void PaintIntercept(FrontRoomView view)
        {
            for (int i = 0; i < hostileSelect.Length; i++)
            {
                bool shownTrack = i < view.Hostile.Count;
                Visible(hostileSelect[i].Rect, shownTrack);
                if (!shownTrack) continue;
                HostileBirdView track = view.Hostile[i];
                OpsKit.Label(hostileSelect[i], TrackWord(track));
                hostileSelect[i].Latched = selectedTrackId == track.Id;
            }
            bool hasTarget = TryHostile(view, selectedTrackId, out HostileBirdView target);
            bool queued = InterceptQueue(view, selectedTrackId, out int queuedTarget, out FrontQueueView queue);
            int chip = queued ? ChipAp(queue) : 0;
            bool outgoing = view.OutgoingTrackId > 0;
            bool engage = !queued && !outgoing && hasTarget && string.IsNullOrEmpty(view.Asat.Refusal);
            if (!engage || confirmTrackId != selectedTrackId) confirmTrackId = 0;
            bool confirming = confirmTrackId > 0;
            string state = outgoing ? "T" + view.OutgoingTrackId + (view.OutgoingLaunched ? " · CONJUNCTION " : " · LAUNCH ") + FrontKit.Clock(view.OutgoingSeconds)
                : queued ? "T" + queuedTarget + " · " + QueueStatus(queue)
                : engage ? TrackWord(target) + " · " + Mathf.RoundToInt(view.Asat.Cost) + " FUNDS  /  " + FrontKit.Clock(view.Asat.BuildSeconds) + " PREP"
                : hasTarget ? view.Asat.Refusal : view.Hostile.Count == 0 ? "NO HOSTILE TRACKS" : "SELECT A TRACK TO REVIEW AN INTERCEPT";
            Readout(interceptLine, state, outgoing || queued ? RoomInk.Telemetry : confirming ? AvInk.Hostile : Mc.Steel);
            OpsKit.Label(intercept, queued && chip > 0 ? "CHIP IN · " + Pays(chip) : confirming ? "CONFIRM INTERCEPT · T" + selectedTrackId : "PREVIEW INTERCEPT");
            intercept.SetStyle(confirming ? AvButtonStyle.Danger : AvButtonStyle.Default);
            Visible(intercept.Rect, hasTarget || chip > 0);
            OpsKit.Enable(intercept, engage || chip > 0 && view.Allocation >= chip);
        }

        private static string TrackWord(in HostileBirdView track) => "T" + track.Id + " · " + track.Word;
    }
}
