using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>Read-only TAC BOARD feed for the service zone, tender jobs and the tanker's fuel/magazine.</summary>
    internal sealed class SkywellScopeFeed : MonoBehaviour, ISceneService
    {
        private const float CueRange = 800f;
        private ScopeProvider<DockScope> provider;
        private GaugeProvider gauge;
        private Aircraft trackedReceiver, namedTanker;
        private uint trackedTanker;
        private bool transferSeen;
        private float fuelAtStart, jobStart;
        private int roundsAtStart, jobIndex = -1, jobStation = -1;
        private PitJobKind jobKind;
        private string tankerName, jobName = "";
        private int gaugeKey = int.MinValue;
        private string gaugeText = "", gaugeSub = "";
        private bool gaugeImperial;

        private void Awake()
        {
            provider = Read;
            gauge = ReadGauge;
            ScopeFeeds.Register(provider);
            ScopeGauges.Register("sky", gauge);
        }

        private void OnDestroy()
        {
            ScopeFeeds.Unregister(provider);
            ScopeGauges.Unregister("sky", gauge);
        }

        public void ResetForScene()
        {
            trackedReceiver = namedTanker = null;
            trackedTanker = 0;
            transferSeen = false;
            jobIndex = jobStation = -1;
            jobKind = PitJobKind.None;
            jobName = "";
            gaugeKey = int.MinValue;
        }

        private static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;

        private bool Read(out DockScope d)
        {
            d = default;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || SkywellBoard.KitStation(me) != null) return false;
            uint id = me.persistentID.Id;
            SkywellView view = default;
            Aircraft tanker = null;
            bool serving = false, served = false;
            foreach (SkywellView v in SkywellBoard.Views.Values)
                if (v.Receiver == id) { view = v; tanker = Resolve(v.Tanker); serving = true; break; }
            if (!serving)
                foreach (SkywellView v in SkywellBoard.Views.Values)
                    if (v.Released == id && v.ReleaseKind == PitRelease.Served && v.StateAge < 8f)
                    { view = v; tanker = Resolve(v.Tanker); served = true; break; }
            if (tanker == null)
            {
                float best = CueRange;
                foreach (SkywellView v in SkywellBoard.Views.Values)
                {
                    if (!v.Active || v.Receiver != 0) continue;
                    Aircraft t = Resolve(v.Tanker);
                    if (t == null || t.disabled || t == me || t.NetworkHQ != me.NetworkHQ) continue;
                    float distance = Vector3.Distance(me.transform.position, SkywellBoard.Anchor(t));
                    if (distance < best) { best = distance; tanker = t; view = v; }
                }
            }
            if (tanker == null || tanker.disabled) { trackedTanker = 0; return false; }

            SkywellBoard.Rel(tanker, me, out Vector3 rel, out float speed, out float forward, out float roll);
            Vector3 error = rel - SkywellZone.HoldSlot;
            bool inside = SkywellZone.Contains(rel, speed, roll);
            int rounds = 0, full = 0;
            if (me.weaponStations != null)
                foreach (WeaponStation station in me.weaponStations)
                    if (!SkywellStations.Skip(station)) { rounds += station.GetAmmoTotal(); full += station.FullAmmo; }

            if (trackedReceiver != me || trackedTanker != view.Tanker || (!serving && !served))
            {
                trackedReceiver = me;
                trackedTanker = view.Tanker;
                transferSeen = false;
                jobIndex = -1;
            }
            if (serving && view.State == TenderState.Service)
            {
                if (!transferSeen)
                {
                    fuelAtStart = me.GetFuelQuantity();
                    roundsAtStart = rounds;
                    transferSeen = true;
                }
                if (jobIndex != view.JobIndex || jobKind != view.Job || jobStation != view.JobStation)
                {
                    jobIndex = view.JobIndex;
                    jobKind = view.Job;
                    jobStation = view.JobStation;
                    jobStart = Time.timeSinceLevelLoad;
                    jobName = view.Job == PitJobKind.Fuel ? "FUEL"
                        : view.Job == PitJobKind.None ? "SERVICE" : "HANDOFF STA " + (view.JobStation + 1);
                }
            }
            else if (serving) { transferSeen = false; jobIndex = -1; }

            d.ServiceZone = true;
            d.ZoneReady = inside;
            d.Docked = serving;
            d.Phase = served ? DockPhase.Served : serving && view.State == TenderState.Service ? DockPhase.Transfer
                : serving || inside ? DockPhase.Engage : error.magnitude <= SkywellZone.Radius ? DockPhase.Capture : DockPhase.Cue;
            d.Tanker = TankerName(tanker);
            d.RelX = error.x; d.RelY = error.y; d.RelZ = error.z;
            d.RangeM = error.magnitude;
            d.ClosureMps = forward;
            d.CueM = CueRange;
            d.CaptureM = SkywellZone.Radius;
            d.EngageM = SkywellZone.Radius;
            d.MaxClosingMps = SkywellZone.MaxRelSpeed;
            d.Fuel = Mathf.Clamp01(me.GetFuelLevel());
            d.FuelTarget = Mathf.Clamp01(Mathf.Max(me.fuelLevel, d.Fuel));
            d.FuelNeedKg = SkywellService.FuelNeedKg(me);
            d.Ammo = rounds; d.AmmoFull = full;
            d.JobName = jobName;
            if (d.Phase == DockPhase.Transfer)
                d.TransferT = Mathf.Clamp01((Time.timeSinceLevelLoad - jobStart) / Mathf.Max(view.JobTenths / 10f, 0.1f));
            if (served)
            {
                d.TransferT = 1f;
                d.HasMoved = transferSeen;
                d.MovedFuelKg = transferSeen ? Mathf.Max(0f, me.GetFuelQuantity() - fuelAtStart) : 0f;
                d.MovedRounds = transferSeen ? Mathf.Max(0, rounds - roundsAtStart) : 0;
            }
            d.HasStock = true;
            d.StockFuelKg = view.FuelKg;
            d.StockFuelMaxKg = SkywellService.FuelStockKg;
            d.StockRounds = view.Rounds;
            return true;
        }

        private string TankerName(Aircraft tanker)
        {
            if (!ReferenceEquals(tanker, namedTanker))
            {
                namedTanker = tanker;
                tankerName = string.IsNullOrWhiteSpace(tanker.unitName) ? "SKYWELL" : tanker.unitName.Trim().ToUpperInvariant();
            }
            return tankerName;
        }

        private bool ReadGauge(out GaugeSpec spec)
        {
            spec = default;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || SkywellBoard.KitStation(me) == null) return false;
            if (!SkywellBoard.Views.TryGetValue(me.persistentID.Id, out SkywellView v) || !v.Active) return false;
            bool imperial = PlayerSettings.unitSystem != PlayerSettings.UnitSystem.Metric;
            int key = (int)(v.FuelKg / 10f) * 10 + v.Rounds;
            if (key != gaugeKey || imperial != gaugeImperial)
            {
                gaugeKey = key; gaugeImperial = imperial;
                gaugeText = Mass(v.FuelKg, imperial);
                gaugeSub = PitCopy.Rounds(v.Rounds);
            }
            spec = new GaugeSpec { Id = "sky", Label = "SKYWELL", Kind = GaugeKind.Bar, Value = Mathf.Clamp01(v.FuelKg / SkywellService.FuelStockKg), Text = gaugeText, Sub = gaugeSub, Tone = ScopeTone.Ink, Rank = 5 };
            return true;
        }

        private static string Mass(float kg, bool imperial) => imperial ? (int)(Mathf.Round(kg * 2.20462f / 100f) * 100f) + " LB" : (int)(Mathf.Round(kg / 10f) * 10f) + " KG";
    }
}
