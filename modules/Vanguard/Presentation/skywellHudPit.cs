using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// SKYWELL pit-stop words (SERVICE: FUEL n%, HANDOFF STA n, SERVICE COMPLETE, BOX LOST): the job line and pips while a receiver is served, commit and edge notices, and the
    /// ISkywellControl order / station-priority members the radial PIT STOP branch calls.
    /// </summary>
    internal sealed partial class SkywellHud
    {
        private const int MaxSlots = 8;

        private struct Slot
        {
            public int Index;
            public string Label, Status, StatusFirst; // strings are built when the servable set changes, never per read
        }

        private IHudLine pitLine;
        private bool pitAvailable;
        private byte order;                  // local standing order mirror (0 BOTH, 1 FUEL, 2 MUNITIONS)
        private int priority = -1;           // local station served first, -1 auto (one-shot)
        private readonly List<Slot> slots = new List<Slot>(MaxSlots);
        private int slotSignature;           // aircraft + servable stations + their ammo: the cache below is rebuilt only when it changes
        private Aircraft orderOwner;         // the aircraft the order / priority mirror belongs to
        private readonly Dictionary<uint, byte> lastFlags = new Dictionary<uint, byte>();
        private readonly Dictionary<int, int> runMissing = new Dictionary<int, int>(); // rounds missing when a station's run began

        // job in flight, as this HUD saw it begin
        private PitJobKind jobKind;
        private byte jobStation, jobIndex;
        private float jobStart, jobKg, fuelFrom, fuelTo;

        private string OrderWord() => order == 1 ? "FUEL" : order == 2 ? "MUNITIONS" : "BOTH";

        private void ResetStop()
        {
            jobKind = PitJobKind.None;
            jobIndex = 0;
            jobKg = 0f;
            runMissing.Clear();
        }

        /// <summary>The job title, detail, bar and the pips line; a commit notice when the previous job just finished.</summary>
        private void PitJobLines(Aircraft me, SkywellView v)
        {
            if (v.Job != jobKind || v.JobStation != jobStation || v.JobIndex != jobIndex)
            {
                if (v.JobIndex > jobIndex) CommitNotice(me);
                jobKind = v.Job;
                jobStation = v.JobStation;
                jobIndex = v.JobIndex;
                jobStart = Time.time;
                if (v.Job == PitJobKind.Fuel)
                {
                    jobKg = SkywellService.FuelNeedKg(me);
                    fuelFrom = me.GetFuelLevel();
                    fuelTo = Mathf.Max(fuelFrom, me.fuelLevel);
                }
                else if (v.Job != PitJobKind.None)
                {
                    int missing = MissingAt(me, v.JobStation);
                    if (!runMissing.TryGetValue(v.JobStation, out int began) || missing > began) runMissing[v.JobStation] = missing;
                }
            }
            float total = Mathf.Max(v.JobTenths / 10f, 0.1f), elapsed = Time.time - jobStart;
            float left = Mathf.Max(0f, total - elapsed), bar = Mathf.Clamp01(elapsed / total);
            if (v.Job == PitJobKind.Fuel)
            {
                // The gauge climbs through the flow (plug, flow, unplug); the real level takes over once the tanks are topped off.
                float flow = Mathf.Clamp01((elapsed - PitPlan.FuelPlug) / Mathf.Max(0.1f, total - PitPlan.FuelPlug - PitPlan.FuelUnplug));
                int percent = Mathf.RoundToInt(Mathf.Max(me.GetFuelLevel(), Mathf.Lerp(fuelFrom, fuelTo, flow)) * 100f);
                Line()?.Set(HudTone.Caution, $"SKYWELL  SERVICE: FUEL {percent}%", $"+{PitCopy.Kg(jobKg)} KG  ·  {left:0.0} s", bar);
            }
            else
            {
                int missing = MissingAt(me, v.JobStation);
                string title = $"SKYWELL  HANDOFF STA {v.JobStation + 1}";
                if (v.Job == PitJobKind.Cassette) Line()?.Set(HudTone.Caution, title, $"{StationName(me, v.JobStation)}  ·  CASSETTE SWAP  ·  {missing} RDS", bar);
                else
                {
                    runMissing.TryGetValue(v.JobStation, out int began);
                    int of = Mathf.Max(began, 1);
                    Line()?.Set(HudTone.Caution, title, $"{StationName(me, v.JobStation)}  {Mathf.Clamp(of - missing + 1, 1, of)}/{of}  ·  {left:0.0} s", bar);
                }
            }
            PitLine()?.Set(HudTone.Info, PitCopy.Pips(v.JobIndex, v.JobCount) + "  JOB " + Mathf.Min(v.JobIndex + 1, v.JobCount) + "/" + v.JobCount, null, 0f);
        }

        /// <summary>The last job finished with no successor (the stop is Served).</summary>
        private void FinishJob(Aircraft me)
        {
            if (jobKind == PitJobKind.None) return;
            CommitNotice(me);
            jobKind = PitJobKind.None;
        }

        private void CommitNotice(Aircraft me)
        {
            if (jobKind == PitJobKind.Fuel) Notice($"SKYWELL  FUEL +{PitCopy.Kg(jobKg)} KG", HudTone.Info);
            else if (jobKind == PitJobKind.Handoff || jobKind == PitJobKind.Cassette)
                Notice($"SKYWELL  STN {jobStation + 1}  {StationName(me, jobStation)}  LOADED", HudTone.Info);
        }

        /// <summary>Rising edges of the published flags become notices (hot break for the tanker; the receiver sees it on contact loss).</summary>
        private void Edges(SkywellView v, bool tanker)
        {
            lastFlags.TryGetValue(v.Tanker, out byte was);
            byte rose = (byte)(v.Flags & ~was);
            lastFlags[v.Tanker] = v.Flags;
            if (tanker && (rose & PitFlags.HotBreak) != 0) Notice("SKYWELL  HOT BREAK  MISSILE INBOUND", HudTone.Warning);
            if ((rose & PitFlags.Nothing) != 0) Notice("SKYWELL  NOTHING TO SERVE", HudTone.Caution);
            if ((rose & PitFlags.FuelStockOut) != 0) Notice("SKYWELL  FUEL STOCK EMPTY", HudTone.Caution);
            if ((rose & PitFlags.MunitionStockOut) != 0) Notice("SKYWELL  MUNITIONS STOCK EMPTY", HudTone.Caution);
            if ((rose & PitFlags.Capped) != 0) Notice("SKYWELL  PIT STOP CAPPED  FLY THE BOX AGAIN", HudTone.Caution);
            if ((rose & PitFlags.BoxLost) != 0 && tanker) Notice("SKYWELL  BOX LOST", HudTone.Warning);
        }

        private static float TankerJobProgress(SkywellView v) =>
            v.Job == PitJobKind.None ? 0f : Mathf.Clamp01((v.JobIndex + 0.5f) / Mathf.Max(v.JobCount, 1));

        private static int MissingAt(Aircraft a, int station) =>
            a.weaponStations != null && station >= 0 && station < a.weaponStations.Count && a.weaponStations[station] != null
                ? Mathf.Max(0, a.weaponStations[station].FullAmmo - a.weaponStations[station].GetAmmoTotal()) : 0;

        private static string StationName(Aircraft a, int station) =>
            a.weaponStations != null && station >= 0 && station < a.weaponStations.Count ? SkywellStations.Describe(a.weaponStations[station]) : "STATION";

        /// <summary>A different aircraft (respawn): the server's order for it starts at BOTH / auto, so the mirror must too.</summary>
        private void NewAircraft(Aircraft me)
        {
            orderOwner = me;
            order = 0;
            priority = -1;
            docking = 0;
            lastPhase.Clear();
            lastFlags.Clear();
            ResetStop();
            pitLine?.Release();
            pitLine = null;
            slots.Clear();
            slotSignature = 0;
        }

        /// <summary>
        /// 5 Hz look at the stations a handoff can still serve (missing rounds, passes the kit's skip rules). The signature pass
        /// allocates nothing; the list and its strings are rebuilt only when a station fills, fires or the aircraft changes.
        /// </summary>
        private void RefreshSlots(Aircraft me)
        {
            if (!pitAvailable || me.weaponStations == null)
            {
                slots.Clear();
                slotSignature = 0;
                return;
            }
            int signature = me.GetInstanceID();
            for (int i = 0; i < me.weaponStations.Count; i++)
            {
                WeaponStation s = me.weaponStations[i];
                if (SkywellStations.Skip(s) || MissingAt(me, i) <= 0) continue;
                signature = signature * 31 + i * 1009 + s.GetAmmoTotal() * 17 + s.FullAmmo;
            }
            if (signature == slotSignature) return;
            slotSignature = signature;
            slots.Clear();
            for (int i = 0; i < me.weaponStations.Count && slots.Count < MaxSlots; i++)
            {
                WeaponStation s = me.weaponStations[i];
                if (SkywellStations.Skip(s) || MissingAt(me, i) <= 0) continue;
                string status = "x" + s.GetAmmoTotal() + "/" + s.FullAmmo;
                slots.Add(new Slot { Index = i, Label = SkywellStations.Describe(s), Status = status, StatusFirst = status + " FIRST" });
            }
        }

        private int SlotOf(int station)
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].Index == station) return i;
            return -1;
        }

        // ---- ISkywellControl: the radial PIT STOP branch

        public bool PitAvailable => pitAvailable;

        public byte PitMode => order;

        public void SetPitMode(byte mode)
        {
            if (mode > (byte)PitOrder.Munitions) return;
            order = mode;
            Notice("SKYWELL  ORDER  " + PitCopy.OrderLabel((PitOrder)mode), HudTone.Info);
            Push();
        }

        public int PitStationCount => slots.Count;

        public int PitStationAt(int slot) => slot >= 0 && slot < slots.Count ? slots[slot].Index : -1;

        public string PitStationLabel(int station)
        {
            int at = SlotOf(station);
            if (at >= 0) return slots[at].Label;
            // Already full (or not cached yet): the name still belongs to the weapon station.
            return Local(out Aircraft a) && a.weaponStations != null && station >= 0 && station < a.weaponStations.Count
                ? SkywellStations.Describe(a.weaponStations[station]) : "";
        }

        public string PitStationStatus(int station)
        {
            int at = SlotOf(station);
            return at < 0 ? "" : station == priority ? slots[at].StatusFirst : slots[at].Status;
        }

        public void PrioritiseStation(int station)
        {
            priority = station >= 0 ? station : -1;
            Notice(priority >= 0 ? $"SKYWELL  HANDOFF FIRST  STN {priority + 1}" : "SKYWELL  ORDER  AUTO", HudTone.Info);
            Push();
        }

        private void Push()
        {
            if (Local(out Aircraft a)) SkywellNet.RequestOrder(a, order, priority);
        }
    }
}
