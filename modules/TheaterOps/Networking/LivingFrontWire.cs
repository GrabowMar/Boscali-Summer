using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.TheaterOps.Domain;
using Mirage.Serialization;

namespace BoscaliSummer.Modules.TheaterOps.Networking
{
    /// <summary>
    /// Body of the Living Front snapshot, protocol 3 (spec 6.1). Writers clip strings and counts and clamp the informational
    /// numbers; a reader rejects a structural violation (a count over its cap, an enum or flag out of range, a string over its
    /// length, a non-finite float where a finite one is required, a contradictory combination) by returning false.
    /// </summary>
    internal static class LivingFrontWire
    {
        internal const int MaximumName = 32, MaximumCallsign = 24, MaximumDetail = 24, MaximumKey = 96, MaximumFaction = 64;
        private const int MaximumMarks = 8, MaximumTitle = 40, MaximumPilot = 32, MaximumGroup = 32;
        private const float MaximumCoordinate = 1e6f;

        // ---------------------------------------------------------------- write

        internal static void WriteBody(NetworkWriter w, TheaterWarSnapshot b)
        {
            w.WriteByte((byte)b.Posture); w.WriteSingle(b.SensedAgeSeconds); w.WritePackedUInt32((uint)b.Caps);
            w.WriteByte((byte)b.Reason); w.WriteSingle(b.ReasonSeconds); w.WriteSingle(b.StaffPickSeconds);
            w.WriteByte((byte)b.GroundReason);

            int count = Math.Min(b.Fronts?.Length ?? 0, LivingWarRules.MaximumFronts);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                TheaterFrontView f = b.Fronts[i];
                w.WriteString(NetText.Clip(f.Key, MaximumKey)); w.WriteString(NetText.Clip(f.Name, MaximumName));
                w.WriteSingle(f.X); w.WriteSingle(f.Z); w.WriteByte((byte)f.Status);
                w.WriteSingle(f.Pressure); w.WriteSingle(f.Trend); w.WriteSingle(f.ObservedAgeSeconds);
                w.WriteByte((byte)((f.Observed ? 1 : 0) | (f.Objective ? 2 : 0) | (f.Held ? 4 : 0)));
                w.WriteByte(f.NearFriendly); w.WriteByte(f.NearHostile); w.WriteByte(f.Carriers);
            }

            count = Math.Min(b.Proposals?.Length ?? 0, LivingWarRules.MaximumOffers);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                TheaterProposalView p = b.Proposals[i];
                w.WriteInt32(p.Id); w.WriteInt32(p.Revision); w.WriteByte((byte)p.Kind);
                w.WriteString(NetText.Clip(p.Name, MaximumName)); w.WriteString(NetText.Clip(p.TargetKey, MaximumKey));
                w.WriteSingle(p.X); w.WriteSingle(p.Z); w.WriteSingle(p.Radius);
                w.WriteByte((byte)p.Risk); w.WriteByte((byte)p.Carrier);
                w.WriteByte(p.FriendlyGround); w.WriteByte(p.FriendlyOther); w.WriteByte(p.HostileTracked);
                w.WriteSingle(p.Pressure); w.WriteByte(p.Recommended ? (byte)1 : (byte)0);
            }

            TheaterOperationView op = b.Operation;
            w.WriteByte(op == null ? (byte)0 : (byte)1);
            if (op != null) WriteOperation(w, op);

            TheaterWaveView wave = b.Wave;
            w.WriteByte(wave == null ? (byte)0 : (byte)1);
            if (wave != null)
            {
                w.WriteByte((byte)wave.State); w.WriteByte((byte)wave.Block);
                w.WriteByte((byte)wave.Credits); w.WriteByte((byte)wave.CreditsPerWave);
                w.WriteString(NetText.Clip(wave.Group, MaximumGroup));
                w.WriteInt32(Math.Clamp(wave.Cost, -1, 1000000)); w.WriteInt32(Math.Clamp(wave.Pool, -1, 1000000));
                w.WriteInt32(Math.Clamp(wave.CooldownSeconds, -1, 1000000)); w.WriteInt32(Math.Clamp(wave.Bought, 0, 10000));
                w.WriteSingle(float.IsNaN(wave.LastBoughtAgo) ? -1f : Math.Clamp(wave.LastBoughtAgo, -1f, 86400f));
                w.WriteByte(wave.DepotKnown ? (byte)1 : (byte)0);
                if (wave.DepotKnown) { w.WriteSingle(wave.DepotX); w.WriteSingle(wave.DepotZ); }
            }

            TheaterResultView result = b.LastResult;
            w.WriteByte(result == null ? (byte)0 : (byte)1);
            if (result != null)
            {
                w.WriteInt32(result.OperationId); w.WriteByte((byte)result.Kind); w.WriteString(NetText.Clip(result.Name, MaximumName));
                w.WriteByte((byte)result.Outcome); w.WriteByte((byte)result.Why); w.WriteSingle(result.AgeSeconds);
                w.WriteInt32(result.Funds); w.WriteByte((byte)(sbyte)Math.Clamp(result.Morale, -128, 127));
            }

            TheaterOutcomeView outcome = b.LastOutcome;
            w.WriteByte(outcome == null ? (byte)0 : (byte)1);
            if (outcome != null)
            {
                w.WriteInt32(outcome.Seq); w.WriteString(NetText.Clip(outcome.Title, MaximumTitle));
                w.WriteString(NetText.Clip(outcome.Pilot, MaximumPilot)); w.WriteSingle(outcome.X); w.WriteSingle(outcome.Z);
                w.WriteByte(outcome.Counted ? (byte)1 : (byte)0); w.WriteByte(outcome.ByWing ? (byte)1 : (byte)0);
                w.WriteByte((byte)outcome.Credits);
            }

            // A struct is never null; the census is always sent and Known says whether it was measured.
            TheaterCensus c = b.Census;
            w.WriteByte(1);
            w.WriteByte(c.Known ? (byte)1 : (byte)0);
            w.WritePackedInt32(c.AirFriendly); w.WritePackedInt32(c.AirParked); w.WritePackedInt32(c.AirHostileTracked);
            w.WritePackedInt32(c.GroundFriendly); w.WritePackedInt32(c.GroundHostileTracked);
            w.WritePackedInt32(c.ShipsFriendly); w.WritePackedInt32(c.ShipsHostileTracked);
            w.WritePackedInt32(c.BasesFriendly); w.WritePackedInt32(c.BasesHostile); w.WritePackedInt32(c.BasesNeutral);
            w.WritePackedInt32(c.BasesContested); w.WritePackedInt32(c.Radars);
            w.WritePackedInt32(c.SectorsFriendly); w.WritePackedInt32(c.SectorsHostile);
            w.WritePackedInt32(c.SectorsContested); w.WritePackedInt32(c.SectorsNeutral);
            w.WritePackedInt32(c.SortieCap); w.WritePackedInt32(c.SortieSead); w.WritePackedInt32(c.SortieCas);
            w.WritePackedInt32(c.SortieStrike); w.WritePackedInt32(c.SortieTransit);
            w.WriteSingle(c.FrontMetres); w.WriteByte(c.Threat); w.WriteByte((byte)c.ThreatWhy);

            count = Math.Min(b.Log?.Length ?? 0, StaffLog.MaximumEntries);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++) w.WriteString(NetText.Clip(b.Log[i], StaffLog.MaximumTextLength));
        }

        private static void WriteOperation(NetworkWriter w, TheaterOperationView op)
        {
            w.WriteInt32(op.Id); w.WriteInt32(op.Revision); w.WriteByte((byte)op.Kind);
            w.WriteString(NetText.Clip(op.TargetKey, MaximumKey)); w.WriteString(NetText.Clip(op.Name, MaximumName));
            w.WriteSingle(op.X); w.WriteSingle(op.Z); w.WriteSingle(op.OriginX); w.WriteSingle(op.OriginZ); w.WriteSingle(op.Radius);
            w.WriteByte((byte)op.Phase); w.WriteByte((byte)op.PhaseNote);
            w.WriteSingle(op.ElapsedSeconds); w.WriteSingle(op.LimitSeconds); w.WriteSingle(op.Progress);
            TheaterForcesView f = op.Forces ?? new TheaterForcesView();
            w.WriteByte(f.Groups); w.WriteByte(f.Rally); w.WriteByte(f.Line); w.WriteByte(f.Assault); w.WriteByte(f.Contact); w.WriteByte(f.Withdraw);
            w.WriteByte(f.Vehicles); w.WriteByte(f.Formed); w.WriteByte(f.Moving); w.WriteByte(f.Steered); w.WriteByte(f.OnSlot); w.WriteByte(f.Stalled);
            w.WritePackedUInt32(f.EtaSeconds); w.WriteSingle(f.NearestToFixMeters);
            int marks = Math.Min(f.Marks?.Length ?? 0, MaximumMarks);
            w.WriteByte((byte)marks);
            for (int i = 0; i < marks; i++)
            {
                TheaterForceMark m = f.Marks[i];
                w.WriteSingle(m.X); w.WriteSingle(m.Z); w.WriteByte(m.Stage); w.WriteByte(m.Alive);
            }
            w.WriteString(NetText.Clip(op.PickedBy, MaximumCallsign)); w.WriteUInt64(op.PickedById);
            w.WriteByte(op.PickedByStaff ? (byte)1 : (byte)0); w.WriteSingle(op.LockSeconds);
        }

        // ---------------------------------------------------------------- read

        internal static bool ReadBody(NetworkReader r, string faction, out TheaterWarSnapshot body)
        {
            body = null;
            var s = new TheaterWarSnapshot { Faction = faction };
            byte posture = r.ReadByte(); float sensed = r.ReadSingle(); uint caps = r.ReadPackedUInt32(); byte reason = r.ReadByte();
            float reasonSeconds = r.ReadSingle(), pick = r.ReadSingle(); byte ground = r.ReadByte();
            if (posture > 2 || !Within(sensed, 0f, 3600f) || caps > 0x1FF || reason > 16 || !Within(reasonSeconds, 0f, 600f) ||
                !Within(pick, -1f, 120f) || !(ground <= 7 || ground == 255)) return false;
            s.Posture = (TheaterWarPosture)posture; s.SensedAgeSeconds = sensed; s.Caps = (TheaterCaps)caps;
            s.Reason = (TheaterEmptyReason)reason; s.ReasonSeconds = reasonSeconds; s.StaffPickSeconds = pick;
            s.GroundReason = (GroundReason)ground;

            int count = r.ReadByte();
            if (count > LivingWarRules.MaximumFronts) return false;
            s.Fronts = new TheaterFrontView[count];
            for (int i = 0; i < count; i++) if (!ReadFront(r, out s.Fronts[i])) return false;

            count = r.ReadByte();
            if (count > LivingWarRules.MaximumOffers) return false;
            s.Proposals = new TheaterProposalView[count];
            for (int i = 0; i < count; i++) if (!ReadProposal(r, out s.Proposals[i])) return false;

            byte present = r.ReadByte();
            if (present > 1) return false;
            if (present == 1 && !ReadOperation(r, out s.Operation)) return false;

            present = r.ReadByte();
            if (present > 1) return false;
            if (present == 1 && !ReadWave(r, out s.Wave)) return false;

            present = r.ReadByte();
            if (present > 1) return false;
            if (present == 1 && !ReadResult(r, out s.LastResult)) return false;

            present = r.ReadByte();
            if (present > 1) return false;
            if (present == 1 && !ReadOutcome(r, out s.LastOutcome)) return false;

            present = r.ReadByte();
            if (present > 1) return false;
            if (present == 1 && !ReadCensus(r, out s.Census)) return false;

            count = r.ReadByte();
            if (count > StaffLog.MaximumEntries) return false;
            s.Log = new string[count];
            for (int i = 0; i < count; i++)
                if ((s.Log[i] = Text(r, StaffLog.MaximumTextLength)) == null) return false;
            body = s;
            return true;
        }

        private static bool ReadFront(NetworkReader r, out TheaterFrontView front)
        {
            front = null;
            string key = Text(r, MaximumKey), name = Text(r, MaximumName);
            float x = r.ReadSingle(), z = r.ReadSingle(); byte status = r.ReadByte();
            float pressure = r.ReadSingle(), trend = r.ReadSingle(), age = r.ReadSingle();
            byte flags = r.ReadByte(), nearFriendly = r.ReadByte(), nearHostile = r.ReadByte(), carriers = r.ReadByte();
            if (string.IsNullOrEmpty(key) || name == null || !Coordinate(x) || !Coordinate(z) || status > 3 ||
                !Within(pressure, 0f, 1f) || !Within(trend, -1f, 1f) || !Within(age, -1f, 86400f) || flags > 7) return false;
            front = new TheaterFrontView
            {
                Key = key, Name = name, X = x, Z = z, Status = (TheaterFrontStatus)status, Pressure = pressure, Trend = trend,
                ObservedAgeSeconds = age, Observed = (flags & 1) != 0, Objective = (flags & 2) != 0, Held = (flags & 4) != 0,
                NearFriendly = nearFriendly, NearHostile = nearHostile, Carriers = carriers,
            };
            return true;
        }

        private static bool ReadProposal(NetworkReader r, out TheaterProposalView proposal)
        {
            proposal = null;
            int id = r.ReadInt32(), revision = r.ReadInt32(); byte kind = r.ReadByte();
            string name = Text(r, MaximumName), key = Text(r, MaximumKey);
            float x = r.ReadSingle(), z = r.ReadSingle(), radius = r.ReadSingle();
            byte risk = r.ReadByte(), carrier = r.ReadByte(), friendlyGround = r.ReadByte(), friendlyOther = r.ReadByte(), hostile = r.ReadByte();
            float pressure = r.ReadSingle(); byte flags = r.ReadByte();
            if (id <= 0 || revision <= 0 || kind > 2 || name == null || string.IsNullOrEmpty(key) || !float.IsFinite(x) || !float.IsFinite(z) ||
                !Within(radius, 0f, 30000f) || risk > 2 || carrier > 2 || !Within(pressure, 0f, 1f) || flags > 1) return false;
            proposal = new TheaterProposalView
            {
                Id = id, Revision = revision, Kind = (TheaterOpKind)kind, Name = name, TargetKey = key, X = x, Z = z, Radius = radius,
                Risk = (TheaterRisk)risk, Carrier = (TheaterCarrier)carrier, FriendlyGround = friendlyGround, FriendlyOther = friendlyOther,
                HostileTracked = hostile, Pressure = pressure, Recommended = flags == 1,
            };
            return true;
        }

        private static bool ReadOperation(NetworkReader r, out TheaterOperationView op)
        {
            op = null;
            int id = r.ReadInt32(), revision = r.ReadInt32(); byte kind = r.ReadByte();
            string key = Text(r, MaximumKey), name = Text(r, MaximumName);
            float x = r.ReadSingle(), z = r.ReadSingle(), originX = r.ReadSingle(), originZ = r.ReadSingle(), radius = r.ReadSingle();
            byte phase = r.ReadByte(), note = r.ReadByte();
            float elapsed = r.ReadSingle(), limit = r.ReadSingle(), progress = r.ReadSingle();
            if (id <= 0 || revision <= 0 || kind > 2 || string.IsNullOrEmpty(key) || name == null || !float.IsFinite(x) || !float.IsFinite(z) ||
                !float.IsFinite(originX) || !float.IsFinite(originZ) || !Within(radius, 0f, 30000f) || phase > 6 || note > 2 ||
                !Within(elapsed, 0f, 3600f) || !Within(limit, 0f, 3600f) || !Within(progress, -1f, 1f)) return false;
            var forces = new TheaterForcesView();
            byte groups = r.ReadByte(), rally = r.ReadByte(), line = r.ReadByte(), assault = r.ReadByte(), contact = r.ReadByte(), withdraw = r.ReadByte();
            byte vehicles = r.ReadByte(), formed = r.ReadByte(), moving = r.ReadByte(), steered = r.ReadByte(), onSlot = r.ReadByte(), stalled = r.ReadByte();
            uint eta = r.ReadPackedUInt32(); float nearest = r.ReadSingle();
            if (!Unknown(groups, 32) || !Unknown(rally, 32) || !Unknown(line, 32) || !Unknown(assault, 32) || !Unknown(contact, 32) ||
                !Unknown(withdraw, 32) || !Unknown(vehicles, 192) || !Unknown(formed, 192) || !Unknown(moving, 192) || !Unknown(steered, 192) ||
                !Unknown(onSlot, 192) || !Unknown(stalled, 192) || !(eta <= 3600u || eta == 65535u) || !Within(nearest, -1f, 1e7f)) return false;
            forces.Groups = groups; forces.Rally = rally; forces.Line = line; forces.Assault = assault; forces.Contact = contact;
            forces.Withdraw = withdraw; forces.Vehicles = vehicles; forces.Formed = formed; forces.Moving = moving; forces.Steered = steered;
            forces.OnSlot = onSlot; forces.Stalled = stalled; forces.EtaSeconds = (ushort)eta; forces.NearestToFixMeters = nearest;
            int marks = r.ReadByte();
            if (marks > MaximumMarks) return false;
            forces.Marks = new TheaterForceMark[marks];
            for (int i = 0; i < marks; i++)
            {
                float mx = r.ReadSingle(), mz = r.ReadSingle(); byte stage = r.ReadByte(), alive = r.ReadByte();
                if (!Coordinate(mx) || !Coordinate(mz) || stage > 5 || alive > 6) return false;
                forces.Marks[i] = new TheaterForceMark { X = mx, Z = mz, Stage = stage, Alive = alive };
            }
            string pickedBy = Text(r, MaximumCallsign); ulong pickedById = r.ReadUInt64();
            byte byStaff = r.ReadByte(); float lockSeconds = r.ReadSingle();
            if (pickedBy == null || byStaff > 1 || !Within(lockSeconds, 0f, 120f)) return false;
            op = new TheaterOperationView
            {
                Id = id, Revision = revision, Kind = (TheaterOpKind)kind, TargetKey = key, Name = name, X = x, Z = z, OriginX = originX,
                OriginZ = originZ, Radius = radius, Phase = (TheaterPhase)phase, PhaseNote = (TheaterPhaseNote)note, ElapsedSeconds = elapsed,
                LimitSeconds = limit, Progress = progress, Forces = forces, PickedBy = pickedBy, PickedById = pickedById,
                PickedByStaff = byStaff == 1, LockSeconds = lockSeconds,
            };
            return true;
        }

        private static bool ReadWave(NetworkReader r, out TheaterWaveView wave)
        {
            wave = null;
            byte state = r.ReadByte(), block = r.ReadByte(), credits = r.ReadByte(), perWave = r.ReadByte();
            string group = Text(r, MaximumGroup);
            int cost = r.ReadInt32(), pool = r.ReadInt32(), cooldown = r.ReadInt32(), bought = r.ReadInt32();
            float lastAgo = r.ReadSingle(); byte depotKnown = r.ReadByte();
            if (state > 2 || block > 8 || credits > 6 || perWave < 1 || perWave > 9 || group == null || depotKnown > 1 ||
                state == (byte)WaveState.Off && (block < (byte)WaveBlock.SwitchedOff || block > (byte)WaveBlock.MissionForbids) ||
                state == (byte)WaveState.Blocked && block == (byte)WaveBlock.None) return false;
            float depotX = 0f, depotZ = 0f;
            if (depotKnown == 1)
            {
                depotX = r.ReadSingle(); depotZ = r.ReadSingle();
                if (!float.IsFinite(depotX) || !float.IsFinite(depotZ)) return false;
            }
            wave = new TheaterWaveView((WaveState)state, (WaveBlock)block, credits, perWave, group, Math.Clamp(cost, -1, 1000000), Math.Clamp(pool, -1, 1000000),
                Math.Clamp(cooldown, -1, 1000000), Math.Clamp(bought, 0, 10000), float.IsNaN(lastAgo) ? -1f : Math.Clamp(lastAgo, -1f, 86400f),
                depotKnown == 1, depotX, depotZ);
            return true;
        }

        private static bool ReadResult(NetworkReader r, out TheaterResultView result)
        {
            result = null;
            int id = r.ReadInt32(); byte kind = r.ReadByte(); string name = Text(r, MaximumName);
            byte outcome = r.ReadByte(), why = r.ReadByte(); float age = r.ReadSingle();
            int funds = r.ReadInt32(); sbyte morale = (sbyte)r.ReadByte();
            if (id <= 0 || kind > 2 || name == null || outcome < 1 || outcome > 5 || why > 11 || !Within(age, 0f, 86400f) ||
                funds < 0 || funds > 100000 || morale < -10 || morale > 10) return false;
            result = new TheaterResultView
            {
                OperationId = id, Kind = (TheaterOpKind)kind, Name = name, Outcome = (TheaterOutcome)outcome, Why = (TheaterWhy)why,
                AgeSeconds = age, Funds = funds, Morale = morale,
            };
            return true;
        }

        private static bool ReadOutcome(NetworkReader r, out TheaterOutcomeView outcome)
        {
            outcome = null;
            int seq = r.ReadInt32(); string title = Text(r, MaximumTitle), pilot = Text(r, MaximumPilot);
            float x = r.ReadSingle(), z = r.ReadSingle(); byte counted = r.ReadByte(), byWing = r.ReadByte(), credits = r.ReadByte();
            if (seq < 1 || title == null || pilot == null || !float.IsFinite(x) || !float.IsFinite(z) || counted > 1 || byWing > 1 || credits > 6)
                return false;
            outcome = new TheaterOutcomeView(seq, title, pilot, x, z, counted == 1, byWing == 1, credits);
            return true;
        }

        private static bool ReadCensus(NetworkReader r, out TheaterCensus census)
        {
            census = TheaterCensus.Unmeasured;
            byte known = r.ReadByte();
            if (known > 1) return false;
            census.Known = known == 1;
            if (!Count(r, 16384, out census.AirFriendly) || !Count(r, 16384, out census.AirParked) || !Count(r, 16384, out census.AirHostileTracked) ||
                !Count(r, 16384, out census.GroundFriendly) || !Count(r, 16384, out census.GroundHostileTracked) ||
                !Count(r, 16384, out census.ShipsFriendly) || !Count(r, 16384, out census.ShipsHostileTracked) ||
                !Count(r, 255, out census.BasesFriendly) || !Count(r, 255, out census.BasesHostile) || !Count(r, 255, out census.BasesNeutral) ||
                !Count(r, 255, out census.BasesContested) || !Count(r, 16384, out census.Radars) ||
                !Count(r, 16384, out census.SectorsFriendly) || !Count(r, 16384, out census.SectorsHostile) ||
                !Count(r, 16384, out census.SectorsContested) || !Count(r, 16384, out census.SectorsNeutral) ||
                !Count(r, 16384, out census.SortieCap) || !Count(r, 16384, out census.SortieSead) || !Count(r, 16384, out census.SortieCas) ||
                !Count(r, 16384, out census.SortieStrike) || !Count(r, 16384, out census.SortieTransit)) return false;
            float front = r.ReadSingle(); byte threat = r.ReadByte(), why = r.ReadByte();
            if (!Within(front, -1f, 1e7f) || threat > 5 || why > 6) return false;
            census.FrontMetres = front; census.Threat = threat; census.ThreatWhy = (TheaterThreatWhy)why;
            return true;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>null when the string is missing or longer than max: the caller rejects the message.</summary>
        internal static string Text(NetworkReader r, int max)
        {
            string value = r.ReadString();
            return value == null || value.Length > max ? null : value;
        }

        private static bool Within(float value, float low, float high) => value >= low && value <= high;   // NaN fails both
        private static bool Coordinate(float value) => float.IsFinite(value) && Math.Abs(value) <= MaximumCoordinate;
        private static bool Unknown(byte value, int high) => value <= high || value == 255;

        private static bool Count(NetworkReader r, int high, out short value)
        {
            int read = r.ReadPackedInt32();
            value = (short)read;
            return read >= -1 && read <= high;
        }
    }
}
