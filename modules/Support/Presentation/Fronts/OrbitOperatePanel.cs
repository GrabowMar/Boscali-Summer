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
        private const float FleetY = 64f, FleetH = 262f, FlightY = 338f, FlightH = 162f, HostileY = 512f, HostileH = 236f, PostureY = 760f, PostureH = 112f;
        private static readonly FrontDirective[] Orders = { FrontDirective.Recon, FrontDirective.Strike, FrontDirective.Defend };
        private RectTransform operateRoot;
        private FrontBox operate;
        private AvControl returnToOperate;
        private bool operateDownlink;
        private TMP_Text watchLine, flightHead, flightState, flightPlan, hostileHead, hostilePlan, orderLock, orderLine;
        private readonly AvControl[] flightSelect = new AvControl[FrontRules.BirdCap];
        private readonly TMP_Text[] flightTelemetry = new TMP_Text[FrontRules.BirdCap];
        private readonly FrontBar[] flightFuel = new FrontBar[FrontRules.BirdCap];
        private readonly AvControl[] hostileSelect = new AvControl[FrontRules.BirdCap];
        private readonly TMP_Text[] hostileNotes = new TMP_Text[FrontRules.BirdCap];
        private readonly AvControl[] orders = new AvControl[Orders.Length];
        private AvControl burn, downlinkOrCancel, intercept, inspectWarning;
        private int confirmTrackId;

        internal bool OperateDownlinkShown => operateDownlink;

        private void BuildOperate(RectTransform column, float w, float h)
        {
            operateRoot = AvLay.Child(column, "Operate");
            AvLay.Place(operateRoot, 0f, 0f, w, h);
            operate = new FrontBox(operateRoot, skin, "FlightControl", 0f, 0f, w, h, "FLIGHT CONTROL", AvIcon.Satellite, FrontLook.Console).Tagged("FDO");
            RectTransform root = operate.Rect;
            AvionicsSections(root, w, h, (FleetY, FleetH, Own), (FlightY, FlightH, RoomInk.Telemetry),
                (HostileY, HostileH, AvInk.Hostile), (PostureY, PostureH, Own));
            float x = ConsoleInset + 10f, width = w - 2f * x, half = (width - 8f) / 2f;
            watchLine = ConsoleReadout(root, "Watch", x, 32f, width - 154f, true);
            inspectWarning = Small(root, "FOCUS THREAT", x + width - 144f, 30f, 144f, WarningCommand,
                "Inspect the satellite being targeted.", RoomInk.Telemetry);
            ConsoleHeading(root, "Friendly", x, FleetY + 8f, width, "FRIENDLY FLEET", Own);
            for (int k = 0; k < flightSelect.Length; k++)
            {
                int slot = k;
                float y = FleetY + 36f + k * 73f;
                AvFrame row = FrontKit.Panel(root, "FriendlyRow" + k, x, y, width, 66f, 0f, 0f);
                skin.Add(() => row.Paint(RoomInk.ConsoleBody, RoomInk.Bezel));
                flightSelect[k] = Small(root, FrontViews.BirdCallsign(k), x + 8f, y + 7f, 154f, () => SelectFriendly(slot), "Inspect this satellite's footprint and commands.");
                flightTelemetry[k] = ConsoleReadout(root, "FriendlyTelemetry" + k, x + 174f, y + 8f, width - 185f);
                flightFuel[k] = new FrontBar(root, skin, x + 8f, y + 49f, width - 16f, 5f);
            }

            flightHead = ConsoleReadout(root, "FlightHead", x, FlightY + 8f, width, true);
            flightState = ConsoleReadout(root, "FlightState", x, FlightY + 36f, width);
            flightPlan = ConsoleReadout(root, "FlightPlan", x, FlightY + 61f, width);
            burn = Small(root, "PLAN BURN", x, FlightY + 103f, half, BurnCommand, "Preview a new station on the map before executing the burn.");
            burn.SetStyle(AvButtonStyle.Primary);
            downlinkOrCancel = Small(root, "DOWNLINK", x + half + 8f, FlightY + 103f, half, SecondFlightCommand, "Open the selected bird's sensor station.");

            hostileHead = ConsoleReadout(root, "HostileHead", x, HostileY + 8f, width, true);
            for (int k = 0; k < hostileSelect.Length; k++)
            {
                int slot = k;
                float y = HostileY + 38f + 34f * k;
                hostileSelect[k] = Small(root, "TRACK", x, y, 195f, () => SelectHostile(slot), "Inspect this hostile track.", AvInk.Hostile);
                hostileNotes[k] = ConsoleReadout(root, "HostileNote" + k, x + 205f, y + 2f, width - 205f);
            }
            hostilePlan = ConsoleReadout(root, "InterceptPlan", x, HostileY + 155f, width);
            intercept = Small(root, "PREVIEW ASAT", x, HostileY + 190f, width, InterceptCommand, "Review an interceptor programme for the selected track.", AvInk.Hostile);

            ConsoleHeading(root, "Posture", x, PostureY + 7f, width * 0.48f, "STANDING ORDERS", Own);
            orderLock = ConsoleReadout(root, "PostureLock", x + width * 0.48f, PostureY + 7f, width * 0.52f, false, TextAlignmentOptions.MidlineRight);
            float cell = (width - 12f) / 3f;
            for (int i = 0; i < orders.Length; i++)
            {
                FrontDirective posture = Orders[i];
                orders[i] = Small(root, FrontRules.Name(posture), x + i * (cell + 6f), PostureY + 38f, cell, () =>
                {
                    actions.Touch();
                    actions.SetDirective(Front.Space, posture);
                }, Describe(Front.Space, posture));
            }
            orderLine = ConsoleReadout(root, "PostureOrder", x, PostureY + 72f, width);
        }

        private void SelectFriendly(int slot)
        {
            confirmTrackId = 0;
            Select(slot);
        }

        private bool TryEvade(FrontRoomView v, out Vector2 station)
        {
            station = default;
            if (!v.IncomingLaunched || v.IncomingBird < 0 || !TryBird(v.IncomingBird, out OrbitTrackView bird) || !bird.Alive || bird.Relocating)
                return false;
            var current = new GeoBird(bird.U, bird.V, bird.Fuel, bird.Bus);
            if (!AsatRules.TryEvade(current, v.Now, v.Now + v.IncomingSeconds, v.IncomingAimU, v.IncomingAimV, out float u, out float vCoord))
                return false;
            station = new Vector2(u, vCoord);
            return true;
        }

        private void WarningCommand()
        {
            if (last == null || last.IncomingBird < 0) return;
            if (TryEvade(last, out Vector2 station))
            {
                StartRelocate(last.IncomingBird);
                if (relocating && selKind == last.IncomingBird)
                {
                    pending = station;
                    hasPending = true;
                    Repaint();
                }
            }
            else SelectFriendly(last.IncomingBird);
        }

        private void SelectHostile(int slot)
        {
            if (last == null || slot >= last.Hostile.Count) return;
            HostileBirdView hostile = last.Hostile[slot];
            confirmTrackId = 0;
            selectedTrackId = hostile.Id;
            Cancel();
        }

        private void BurnCommand()
        {
            if (selKind < 0) return;
            if (relocating) Confirm();
            else StartRelocate(selKind);
        }

        private void SecondFlightCommand()
        {
            if (relocating) Cancel();
            else if (selKind >= 0) ShowFeed(selKind);
        }

        private void InterceptCommand()
        {
            if (last == null) return;
            if (InterceptQueue(last, selectedTrackId, out _, out FrontQueueView queue))
            {
                Donate(queue.Row);
                return;
            }
            if (!TryHostile(last, selectedTrackId, out _) || !string.IsNullOrEmpty(last.Asat.Refusal)) return;
            actions.Touch();
            if (confirmTrackId == selectedTrackId)
            {
                actions.QueueAsat(selectedTrackId);
                confirmTrackId = 0;
            }
            else confirmTrackId = selectedTrackId;
            Repaint();
        }

        private static bool TryHostile(FrontRoomView v, int id, out HostileBirdView hostile)
        {
            foreach (HostileBirdView track in v.Hostile)
            {
                if (id == 0 || track.Id != id) continue;
                hostile = track;
                return true;
            }
            hostile = default;
            return false;
        }

        private static bool InterceptQueue(FrontRoomView v, int trackId, out int queuedTarget, out FrontQueueView queue)
        {
            foreach (FrontQueueRow row in v.Row.Queue)
                if (row.Id == ProgrammeId.Asat && (trackId == 0 || row.TargetTrack == trackId) && QueueRow(v, row.Row, out queue))
                {
                    queuedTarget = row.TargetTrack;
                    return true;
                }
            queuedTarget = 0;
            queue = default;
            return false;
        }

        private void PaintOperate(FrontRoomView v)
        {
            operate.SetMeta(v.BirdsUp + " ON STATION · " + v.Hostile.Count + " HOSTILE TRACKS", Mc.Steel);
            bool warningActive = v.IncomingBird >= 0;
            string warning = warningActive ? FrontViews.BirdCallsign(v.IncomingBird) + " · " +
                (v.IncomingLaunched ? "ASAT INBOUND · MANEUVER " : "INTERCEPTOR PREPARING · ") + FrontKit.Clock(v.IncomingSeconds)
                : string.IsNullOrEmpty(v.CoverageGap) ? "NO ACTIVE INTERCEPT WARNING" : v.CoverageGap;
            Readout(watchLine, warning, warningActive ? Mc.NoGo : Mc.Steel);
            Visible(inspectWarning.Rect, warningActive);
            bool canSuggestEvasion = warningActive && TryEvade(v, out _);
            OpsKit.Label(inspectWarning, canSuggestEvasion ? "PLAN EVASION" : "FOCUS THREAT");
            inspectWarning.Help = canSuggestEvasion ? "Preview a burn that clears the impact zone using the current fleet fix. Execute the burn to commit it."
                : "Inspect the satellite being targeted.";
            for (int i = 0; i < flightSelect.Length; i++)
            {
                FleetCardView bird = Fleet(v, i);
                AvControl control = flightSelect[i];
                control.Latched = i == selKind;
                OpsKit.Enable(control, bird.Up);
                string telemetry = bird.Up ? bird.State + " · " + BusNames[(int)bird.Bus] + " · FUEL " + Mathf.RoundToInt(bird.Fuel) + "%" : bird.State;
                Readout(flightTelemetry[i], telemetry, i == v.IncomingBird ? Mc.NoGo : FrontKit.Tone(bird.Tone));
                flightFuel[i].Set(bird.Fuel / 100f, bird.Fuel < 15f ? RoomInk.Telemetry : Own);
                flightFuel[i].Show(bird.Up);
            }

            OrbitTrackView friendly = default;
            bool haveFriendly = selKind >= 0 && TryBird(selKind, out friendly);
            FleetCardView selected = haveFriendly ? Fleet(v, selKind) : default;
            bool proposing = haveFriendly && relocating;
            bool canBurn = selected.CanRelocate;
            string plan = selected.Up ? selected.Covers : "LAUNCH A BIRD TO OPEN FLIGHT CONTROLS";
            Color planInk = proposing ? RoomInk.Telemetry : Mc.Steel;
            if (proposing)
            {
                canBurn = CanBurn(friendly);
                float cost = PendingCost(friendly);
                plan = hasPending
                    ? "TRANSFER " + FrontKit.Clock(PendingDistance(friendly) / GeoBird.SpeedFor(selected.Bus)) + " · FUEL −" + Mathf.CeilToInt(cost) + "% · RESERVE " + Mathf.FloorToInt(Mathf.Max(0, friendly.Fuel - cost)) + "%"
                    : "PICK A NEW STATION ON THE MAP";
                if (canBurn && v.IncomingLaunched && v.IncomingBird == selKind)
                {
                    var geo = new GeoBird(friendly.U, friendly.V, friendly.Fuel, friendly.Bus);
                    bool clears = AsatRules.Clears(geo.Relocate(v.Now, pending.x, pending.y), v.Now + v.IncomingSeconds, v.IncomingAimU, v.IncomingAimV);
                    plan += clears ? " · CLEARS IMPACT ZONE" : " · IN ZONE AT IMPACT";
                    planInk = clears ? Mc.Go : Mc.NoGo;
                }
            }
            Readout(flightHead, selected.Up ? selected.Callsign + " · COMMAND" : "SATELLITE COMMAND", Own);
            Readout(flightState, selected.Perk, Mc.Steel);
            Readout(flightPlan, plan, planInk);
            OpsKit.Label(burn, proposing ? "EXECUTE BURN" : "PLAN BURN");
            OpsKit.Enable(burn, canBurn);
            OpsKit.Label(downlinkOrCancel, proposing ? "CANCEL BURN" : "OPEN DOWNLINK");
            OpsKit.Enable(downlinkOrCancel, proposing || selected.Up && Feed != null);

            Readout(hostileHead, "HOSTILE PICTURE · " + v.Hostile.Count + " TRACKS", v.Hostile.Count > 0 ? AvInk.Hostile : Mc.Steel);
            for (int i = 0; i < hostileSelect.Length; i++)
            {
                bool hasTrack = i < v.Hostile.Count;
                Visible(hostileSelect[i].Rect, hasTrack);
                Visible(hostileNotes[i].rectTransform, hasTrack);
                if (!hasTrack) continue;
                HostileBirdView track = v.Hostile[i];
                OpsKit.Label(hostileSelect[i], TrackWord(track));
                hostileSelect[i].Latched = selectedTrackId == track.Id;
                Readout(hostileNotes[i], "COARSE SECTOR FIX", Mc.Steel);
            }
            bool hasTarget = TryHostile(v, selectedTrackId, out HostileBirdView target);
            bool queued = InterceptQueue(v, selectedTrackId, out int queuedTarget, out FrontQueueView queue);
            int chip = queued ? ChipAp(queue) : 0;
            bool engage = !queued && hasTarget && string.IsNullOrEmpty(v.Asat.Refusal);
            if (!engage || confirmTrackId != selectedTrackId) confirmTrackId = 0;
            bool confirming = confirmTrackId != 0;
            string threat = queued ? "INTERCEPT T" + queuedTarget + " · " + QueueStatus(queue)
                : engage ? TrackWord(target) + " · " + Mathf.RoundToInt(v.Asat.Cost) + " FRONT FUNDS · " + FrontKit.Clock(v.Asat.BuildSeconds) + " BUILD"
                : hasTarget ? v.Asat.Refusal : "SELECT A HOSTILE TRACK TO REVIEW AN INTERCEPT";
            Readout(hostilePlan, threat, confirming ? AvInk.Hostile : queued ? RoomInk.Telemetry : Mc.Steel);
            OpsKit.Label(intercept, queued ? chip > 0 ? "CHIP IN · " + Pays(chip) : "INTERCEPT QUEUED"
                : confirming ? "CONFIRM ASAT · " + TrackWord(target) : "PREVIEW ASAT");
            intercept.SetStyle(confirming ? AvButtonStyle.Danger : AvButtonStyle.Default);
            OpsKit.Enable(intercept, engage || chip > 0 && v.Allocation >= chip);

            FrontRow r = v.Row;
            bool waiting = FrontViews.IsWaiting(r);
            float left = r.DirectiveLockUntil - v.Now;
            bool locked = !waiting && left > 0f, mine = SameName(r.DirectiveBy, v.Callsign);
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].Latched = !waiting && r.Directive == Orders[i];
                OpsKit.Enable(orders[i], !waiting && (!locked || mine));
            }
            Readout(orderLock, waiting ? "WAITING FOR HOST" : locked ? LockWord(mine, r.DirectiveBy, left) : FrontRules.Name(r.Directive) + " ACTIVE", locked ? RoomInk.Telemetry : Mc.Steel);
            Readout(orderLine, waiting ? "" : Describe(Front.Space, r.Directive).ToUpperInvariant(), Mc.Steel);
        }

        private static string TrackWord(in HostileBirdView track) => "T" + track.Id + " · " + track.Word;
    }
}
