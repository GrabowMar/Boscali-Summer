using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Vanguard's provider of the DOCK page's feed (<see cref="DockScope"/>, HUD v8) and the SKYWELL stock gauge. A receiver pilot's probe against the tanker's contact point in the
    /// tanker's own axes, the closing speed, the hook-up phase (SKYWELL's own: <see cref="SkywellBoard.Views"/> says it on every peer; before docking it is read from the range, as
    /// <see cref="SkywellHud"/> does) and the receiver's REAL fuel and rounds. Live while the local aircraft is a receiver docked to a tanker or within cue range of a deployed friendly
    /// one. Everything is read from the two synced aircraft and the board, so it works on clients too. The game moves the fuel and rounds in one step at the end of the transfer phase
    /// (<c>SkywellService.Transfer</c>), so the feed reports the real value, the level it will be filled to, and how much of the transfer time has passed.
    /// </summary>
    internal sealed class SkywellScopeFeed : MonoBehaviour, ISceneService
    {
        private const float CueRange = 800f;

        private ScopeProvider<DockScope> provider;
        private GaugeProvider gauge;
        private uint trackedTanker;
        private SkywellPhase lastPhase;
        private float transferStart, transferSeconds, fuelAtStart;
        private int roundsAtStart;
        private Aircraft namedTanker;
        private string tankerName;
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
            trackedTanker = 0;
            lastPhase = SkywellPhase.Idle;
            namedTanker = null;
        }

        private static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;

        private bool Read(out DockScope d)
        {
            d = default;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || SkywellBoard.KitStation(me) != null) return false;
            uint id = me.persistentID.Id;
            Aircraft tanker = null;
            SkywellView view = default;
            bool docked = false;
            foreach (SkywellView v in SkywellBoard.Views.Values)
                if (v.Receiver == id) { view = v; docked = true; break; }
            if (docked) tanker = Resolve(view.Tanker);
            else
            {
                float best = CueRange;
                Vector3 probe = SkywellBoard.ProbePoint(me);
                foreach (SkywellView v in SkywellBoard.Views.Values)
                {
                    if (!v.Active || v.Receiver != 0) continue;
                    Aircraft t = Resolve(v.Tanker);
                    if (t == null || t.disabled || t == me || t.NetworkHQ != me.NetworkHQ) continue;
                    float dist = Vector3.Distance(probe, SkywellBoard.ContactPoint(t));
                    if (dist < best) { best = dist; tanker = t; view = v; }
                }
            }
            if (tanker == null || tanker.disabled) { trackedTanker = 0; return false; }

            Vector3 probePoint = SkywellBoard.ProbePoint(me), contact = SkywellBoard.ContactPoint(tanker);
            Vector3 rel = tanker.transform.InverseTransformPoint(probePoint) - tanker.transform.InverseTransformPoint(contact);
            float range = Vector3.Distance(probePoint, contact);
            Vector3 relVelocity = (me.rb != null ? me.rb.velocity : Vector3.zero) - (tanker.rb != null ? tanker.rb.velocity : Vector3.zero);

            SkywellPhase phase = docked ? view.Phase : SkywellPhase.Idle;
            int rounds = 0, full = 0;
            if (me.weaponStations != null)
                foreach (WeaponStation s in me.weaponStations)
                    if (s?.WeaponInfo != null && !s.WeaponInfo.cargo && !s.WeaponInfo.nuclear && s.WeaponInfo.massPerRound > 0f)
                    {
                        rounds += s.GetAmmoTotal();
                        full += s.FullAmmo;
                    }

            // The transfer's clock and what it moved: recorded when the phase begins, read back when it is served.
            if (id != trackedTanker || !docked || phase != lastPhase)
            {
                if (docked && phase == SkywellPhase.Transfer)
                {
                    transferStart = Time.time;
                    transferSeconds = SkywellContact.TransferSeconds(SkywellService.FuelNeedKg(me), SkywellService.MissingRounds(me));
                    fuelAtStart = me.GetFuelQuantity();
                    roundsAtStart = rounds;
                }
                trackedTanker = docked ? id : 0;
                lastPhase = phase;
            }

            d.Docked = docked;
            d.Phase = docked ? (phase == SkywellPhase.Transfer ? DockPhase.Transfer : phase == SkywellPhase.Served ? DockPhase.Served : DockPhase.Engage)
                : range <= SkywellService.EngageRadius && rel.z < 5f ? DockPhase.Engage : range <= SkywellContact.CaptureRadius ? DockPhase.Capture : DockPhase.Cue;
            d.Tanker = TankerName(tanker);
            d.RelX = rel.x;
            d.RelY = rel.y;
            d.RelZ = rel.z;
            d.RangeM = range;
            d.ClosureMps = Vector3.Dot(relVelocity, tanker.transform.forward);
            d.CueM = CueRange;
            d.CaptureM = SkywellContact.CaptureRadius;
            d.EngageM = SkywellService.EngageRadius;
            d.MaxClosingMps = SkywellContact.MaxClosing;
            ControlInputs inputs = me.GetInputs();
            d.Brake = inputs != null && inputs.brake > 0.5f;
            d.Fuel = Mathf.Clamp01(me.GetFuelLevel());
            d.FuelTarget = Mathf.Clamp01(Mathf.Max(me.fuelLevel, d.Fuel));
            d.FuelNeedKg = SkywellService.FuelNeedKg(me);
            d.Ammo = rounds;
            d.AmmoFull = full;
            if (d.Phase == DockPhase.Transfer) d.TransferT = Mathf.Clamp01((Time.time - transferStart) / Mathf.Max(transferSeconds, 0.1f));
            if (d.Phase == DockPhase.Served)
            {
                d.TransferT = 1f;
                d.MovedFuelKg = Mathf.Max(0f, me.GetFuelQuantity() - fuelAtStart);
                d.MovedRounds = Mathf.Max(0, rounds - roundsAtStart);
            }
            d.HasStock = true;
            d.StockFuelKg = view.FuelKg;
            d.StockFuelMaxKg = SkywellService.FuelStockKg;
            d.StockMunKg = view.StockKg;
            return true;
        }

        /// <summary>The tanker's own name (upper case), cached by aircraft: <c>SKYWELL</c> when it has none.</summary>
        private string TankerName(Aircraft tanker)
        {
            if (!ReferenceEquals(tanker, namedTanker))
            {
                namedTanker = tanker;
                tankerName = string.IsNullOrWhiteSpace(tanker.unitName) ? "SKYWELL" : tanker.unitName.Trim().ToUpperInvariant();
            }
            return tankerName;
        }

        /// <summary>The stock gauge for a pilot flying the kit: fuel left on the bar, the munitions beside it (the DOCK page brings its own for a receiver).</summary>
        private bool ReadGauge(out GaugeSpec spec)
        {
            spec = default;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || SkywellBoard.KitStation(me) == null) return false;
            if (!SkywellBoard.Views.TryGetValue(me.persistentID.Id, out SkywellView v) || !v.Active) return false;
            bool imperial = PlayerSettings.unitSystem != PlayerSettings.UnitSystem.Metric;
            int key = (int)(v.FuelKg / 10f) * 1000 + (int)(v.StockKg / 10f);
            if (key != gaugeKey || imperial != gaugeImperial)
            {
                gaugeKey = key;
                gaugeImperial = imperial;
                gaugeText = Mass(v.FuelKg, imperial);
                gaugeSub = "MUN " + Mass(v.StockKg, imperial);
            }
            spec = new GaugeSpec { Id = "sky", Label = "SKYWELL", Kind = GaugeKind.Bar, Value = Mathf.Clamp01(v.FuelKg / SkywellService.FuelStockKg), Text = gaugeText, Sub = gaugeSub, Tone = ScopeTone.Ink, Rank = 5 };
            return true;
        }

        private static string Mass(float kg, bool imperial) => imperial ? (int)(Mathf.Round(kg * 2.20462f / 100f) * 100f) + " LB" : (int)(Mathf.Round(kg / 10f) * 10f) + " KG";
    }
}
