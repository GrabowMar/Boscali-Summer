using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Domain;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private const int PlaneFaultRows = 4;
        private const int PlaneStoreRows = 4;
        private readonly AvStatTile[] planeFlight = new AvStatTile[6];
        private readonly AvKeyValue[] planeSystems = new AvKeyValue[6];
        private readonly AvRow[] planeStores = new AvRow[PlaneStoreRows];
        private readonly AvRow[] planeFaults = new AvRow[PlaneFaultRows];
        private readonly UnitPart[] planeWorstParts = new UnitPart[PlaneFaultRows];
        private AvRow planeIdentity;
        private AvRow planeSelected;
        private AvTextBlock planeStoreOverflow;
        private AvControl planePreviousStores;
        private AvControl planeNextStores;
        private int planeStorePage;
        private PlaneDamagePart planeDamage;
        private AvTextBlock planeTuneName;
        private AvSegmented planeTuneSegmented;
        private AvTextBlock planeTuneState;
        private AvControl planeApplyTune;
        private Aircraft planeTuneAircraft;
        private int planeTuneMode = -1;
        private string planeStatus = "No aircraft assigned.";

        private void ResetPlanePage()
        {
            planeIdentity = null;
            planeSelected = null;
            planeStoreOverflow = null;
            planeDamage?.Clear();
            planeDamage = null;
            planeTuneName = null;
            planeTuneSegmented = null;
            planeTuneState = null;
            planeApplyTune = null;
            planeTuneAircraft = null;
            planeTuneMode = -1;
            planeStatus = "No aircraft assigned.";
            planePreviousStores = planeNextStores = null;
            planeStorePage = 0;
            Array.Clear(planeFlight, 0, planeFlight.Length);
            Array.Clear(planeSystems, 0, planeSystems.Length);
            Array.Clear(planeStores, 0, planeStores.Length);
            Array.Clear(planeFaults, 0, planeFaults.Length);
            Array.Clear(planeWorstParts, 0, planeWorstParts.Length);
        }

        private void BuildPlanePage(AvFlow p)
        {
            p.Section(AvIcon.Plane, "AIRCRAFT DOSSIER", "OWN AIRCRAFT · LIVE");
            planeIdentity = p.Add(new AvRow(p.Content));
            planeIdentity.Set("NO AIRCRAFT", null, "PILOT NOT IN AIRCRAFT", AvState.Inert);

            p.Section(AvIcon.Gauge, "AIRFRAME STATUS", "NATIVE HUD DAMAGE MODEL");
            planeDamage = new PlaneDamagePart(p.Content);
            float half = AvFlowMath.ColumnWidth(p.Inner, 2, AvGridTokens.Gap);
            AvCard engineCard = new AvCard(p.Content, console.Ticker, half, "ENGINE MAP");
            planeTuneName = engineCard.Flow.Add(new AvTextBlock(engineCard.Flow.Content, AvTextRole.DataStrong));
            planeTuneName.Set("STOCK");
            planeTuneSegmented = engineCard.Flow.Add(new AvSegmented(engineCard.Flow.Content, "MAP",
                new[] { "STOCK", "RANGE" }, () => planeTuneMode == PlaneEngineMap.Range ? 1 : 0,
                i => SelectPlaneTune(i == 1 ? PlaneEngineMap.Range : PlaneEngineMap.Stock)));
            AvButtons applyRow = engineCard.Flow.Buttons(
                new AvControl.Spec("APPLY MAP", ApplyPlaneTune, AvButtonStyle.Primary, AvIcon.CircleCheck));
            planeApplyTune = applyRow.Controls[0];
            planeTuneState = engineCard.Flow.Add(new AvTextBlock(engineCard.Flow.Content, AvTextRole.ProseSmall));
            planeTuneState.Set("Enter an aircraft to select an engine map.");
            p.Row(planeDamage, engineCard);

            p.Section(AvIcon.ChartLine, "FLIGHT DATA", "NATIVE UNITS");
            string[] flightKeys = { "TRUE AIRSPEED", "GROUND SPEED", "ALTITUDE MSL", "VERTICAL SPEED", "HEADING", "G LOAD" };
            for (int i = 0; i < planeFlight.Length; i++) planeFlight[i] = new AvStatTile(p.Content, flightKeys[i]);
            p.Row(planeFlight[0], planeFlight[1], planeFlight[2]);
            p.Row(planeFlight[3], planeFlight[4], planeFlight[5]);

            p.Section(AvIcon.Settings, "AIRCRAFT SYSTEMS", "READ ONLY");
            string[] systemKeys = { "FUEL", "THROTTLE", "LANDING GEAR", "FLIGHT ASSIST", "COUNTERMEASURE", "MISSILE WARNING" };
            for (int i = 0; i < planeSystems.Length; i++) planeSystems[i] = new AvKeyValue(p.Content, systemKeys[i]);
            AvCellGrid systemsGrid = p.Grid(2);
            for (int i = 0; i < planeSystems.Length; i++) systemsGrid.Add(planeSystems[i]);

            p.Section(AvIcon.Stack2, "STORES", "CURRENT LOADOUT");
            planeSelected = p.Add(new AvRow(p.Content));
            planeSelected.Set("NO STATION SELECTED", null, null, AvState.Inert);
            AvButtons storePager = p.Buttons(
                new AvControl.Spec("PREVIOUS", () => { planeStorePage = Math.Max(0, planeStorePage - 1); nextRefresh = 0f; },
                    AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("NEXT", () =>
                {
                    int count = CurrentPlaneStoreCount();
                    if ((planeStorePage + 1) * PlaneStoreRows < count) planeStorePage++;
                    nextRefresh = 0f;
                }, AvButtonStyle.Quiet, AvIcon.ChevronRight));
            planePreviousStores = storePager.Controls[0];
            planeNextStores = storePager.Controls[1];
            planeStoreOverflow = p.Add(new AvTextBlock(p.Content, AvTextRole.Label));
            planeStoreOverflow.Set("NO STATIONS");
            for (int i = 0; i < PlaneStoreRows; i++) planeStores[i] = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.AlertTriangle, "AIRFRAME INSPECTION", "WORST FOUR PARTS");
            for (int i = 0; i < PlaneFaultRows; i++) planeFaults[i] = p.Add(new AvRow(p.Content));

            AvTextBlock note = p.Add(new AvTextBlock(p.Content, AvTextRole.ProseSmall));
            note.Set("Damage reflects the aircraft's measured parts. Missing parts read as unavailable.");
        }

        private void RefreshPlanePage()
        {
            if (planeIdentity == null) return;
            Aircraft aircraft = null;
            if (GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
                aircraft = local.Aircraft;
            if (aircraft == null)
            {
                planeIdentity.Set("NO AIRCRAFT", null, "PILOT NOT IN AIRCRAFT", AvState.Inert);
                planeStatus = "Assign or enter an aircraft to see its live dossier.";
                for (int i = 0; i < planeFlight.Length; i++) planeFlight[i].Set(null);
                for (int i = 0; i < planeSystems.Length; i++) planeSystems[i].Set(null);
                for (int i = 0; i < planeStores.Length; i++) planeStores[i].Set(null, null, null, AvState.Inert);
                planeStoreOverflow.Set("");
                planeStorePage = 0;
                planePreviousStores.Interactable = false;
                planeNextStores.Interactable = false;
                planeSelected.Set("NO STATION SELECTED", null, null, AvState.Inert);
                planeDamage.Clear();
                planeTuneAircraft = null;
                planeTuneMode = -1;
                planeTuneName.Set("NO AIRCRAFT");
                planeTuneState.Set("Enter an aircraft to select an engine map.");
                planeApplyTune.Interactable = false;
                for (int i = 0; i < PlaneFaultRows; i++) PaintPlanePart(i, null);
                return;
            }

            string model = aircraft.definition != null ? aircraft.definition.unitName : aircraft.unitName;
            bool disabled = aircraft.disabled;
            planeIdentity.Set(string.IsNullOrEmpty(model) ? "AIRCRAFT" : model.ToUpperInvariant(), null,
                disabled ? "DISABLED" : aircraft.HasEjected() ? "PILOT EJECTED"
                    : aircraft.IsLanded() ? "ON GROUND" : "AIRBORNE",
                disabled ? AvState.Danger : AvState.Ready);
            planeDamage.Refresh(aircraft);
            RefreshPlaneTune(aircraft);

            Vector3 velocity = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            Vector3 airVelocity = velocity - aircraft.GetWindVelocity();
            planeFlight[0].Set(aircraft.rb != null ? UnitConverter.SpeedReading(airVelocity.magnitude) : null);
            planeFlight[1].Set(aircraft.rb != null
                ? UnitConverter.SpeedReading(new Vector2(velocity.x, velocity.z).magnitude) : null);
            planeFlight[2].Set(UnitConverter.AltitudeReading((float)aircraft.transform.GlobalPosition().y));
            planeFlight[3].Set(aircraft.rb != null ? UnitConverter.ClimbRateReading(velocity.y) : null);
            planeFlight[4].Set(AvNum.Thousands(Mathf.RoundToInt(Mathf.Repeat(aircraft.transform.eulerAngles.y, 360f))) + "°");
            planeFlight[5].Set(AvNum.Fixed(aircraft.gForce, 1) + " G");

            float fuel = Mathf.Clamp01(aircraft.fuelLevel);
            planeSystems[0].Set(AvNum.Percent(fuel));
            ControlInputs inputs = aircraft.GetInputs();
            planeSystems[1].Set(inputs != null ? AvNum.Percent(Mathf.Clamp01(inputs.throttle)) : null);
            planeSystems[2].Set(aircraft.gearDeployed ? "DOWN" : "UP");
            planeSystems[3].Set(aircraft.flightAssist ? "ON" : "OFF");
            Countermeasure countermeasure = aircraft.countermeasureManager?.GetActiveCountermeasure();
            planeSystems[4].Set(countermeasure != null ? AvNum.Thousands(countermeasure.ammo) + " READY" : "NONE");
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            int inbound = warning?.knownMissiles != null ? warning.knownMissiles.Count : 0;
            planeSystems[5].Set(inbound > 0 ? AvNum.Thousands(inbound) + " TRACKED" : "CLEAR",
                inbound > 0 ? AvState.Danger : AvState.Ready);

            WeaponManager manager = aircraft.weaponManager;
            WeaponStation selected = manager?.currentWeaponStation;
            WeaponInfo selectedInfo = selected?.WeaponInfo;
            planeSelected.Set(
                selectedInfo != null ? (string.IsNullOrEmpty(selectedInfo.shortName) ? selectedInfo.weaponName : selectedInfo.shortName) : "NO STATION SELECTED",
                null, selectedInfo != null ? selected.GetAmmoReadout() : null,
                selectedInfo != null ? AvState.Info : AvState.Inert);
            List<WeaponStation> stations = aircraft.weaponStations;
            int count = stations != null ? stations.Count : 0;
            planeStorePage = Mathf.Clamp(planeStorePage, 0, count == 0 ? 0 : (count - 1) / PlaneStoreRows);
            int first = planeStorePage * PlaneStoreRows;
            for (int i = 0; i < PlaneStoreRows; i++)
            {
                int index = first + i;
                WeaponStation station = index < count ? stations[index] : null;
                WeaponInfo info = station?.WeaponInfo;
                string name = info != null ? (string.IsNullOrEmpty(info.shortName) ? info.weaponName : info.shortName) : "EMPTY";
                planeStores[i].Set(index < count ? AvNum.Thousands(index + 1) + "  " + name : null,
                    null, index < count ? station.GetAmmoReadout() + (station == selected ? "  SELECTED" : "") : null,
                    station == selected ? AvState.Info : AvState.Inert);
            }
            planeStoreOverflow.Set(count == 0 ? "NO STATIONS"
                : AvNum.Thousands(first + 1) + "–" + AvNum.Thousands(Mathf.Min(first + PlaneStoreRows, count)) + " OF " + AvNum.Thousands(count));
            planePreviousStores.Interactable = planeStorePage > 0;
            planeNextStores.Interactable = first + PlaneStoreRows < count;

            Array.Clear(planeWorstParts, 0, planeWorstParts.Length);
            List<UnitPart> parts = aircraft.partLookup;
            if (parts != null)
            {
                int limit = Mathf.Min(parts.Count, 128);
                for (int i = 0; i < limit; i++)
                {
                    UnitPart part = parts[i];
                    if (part == null || part.gameObject == null) continue;
                    float condition = PartCondition(part);
                    if (float.IsNaN(condition)) continue;
                    for (int slot = 0; slot < PlaneFaultRows; slot++)
                    {
                        UnitPart prior = planeWorstParts[slot];
                        if (prior != null && PartCondition(prior) <= condition) continue;
                        for (int shift = PlaneFaultRows - 1; shift > slot; shift--)
                            planeWorstParts[shift] = planeWorstParts[shift - 1];
                        planeWorstParts[slot] = part;
                        break;
                    }
                }
            }
            bool damaged = false;
            for (int i = 0; i < PlaneFaultRows; i++)
            {
                PaintPlanePart(i, planeWorstParts[i]);
                damaged |= planeWorstParts[i] != null && PartCondition(planeWorstParts[i]) < .995f;
            }
            planeStatus = inbound > 0 ? "MISSILE WARNING · CHECK DEFENSIVE SYSTEMS"
                : fuel <= .15f ? "LOW FUEL · CHECK DIVERT OPTIONS"
                : damaged ? "AIRFRAME DAMAGE · INSPECT BEFORE NEXT SORTIE"
                : "AIRCRAFT SYSTEMS NORMAL · LIVE LOCAL READINGS";
        }

        private void PaintPlanePart(int row, UnitPart part)
        {
            if (part == null)
            {
                planeFaults[row].Set("NO MEASURED PART", null, null, AvState.Inert);
                return;
            }
            float condition = PartCondition(part);
            string name = part.gameObject.name.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
            if (name.Length > 28) name = name.Substring(0, 28);
            bool detached = part.IsDetached();
            AvState state = detached || condition < .25f ? AvState.Danger
                : condition < .995f ? AvState.Caution : AvState.Ready;
            planeFaults[row].Set(name, null, detached ? "LOST" : AvNum.Percent(condition), state);
        }

        private static float PartCondition(UnitPart part)
        {
            if (part == null) return float.NaN;
            if (part.IsDetached()) return 0f;
            float value = part.hitPoints / 100f;
            return float.IsNaN(value) || float.IsInfinity(value) ? float.NaN : Mathf.Clamp01(value);
        }

        private int CurrentPlaneStoreCount()
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player local) || local?.Aircraft == null) return 0;
            return local.Aircraft.weaponStations?.Count ?? 0;
        }

        private void RefreshPlaneTune(Aircraft aircraft)
        {
            if (aircraft != planeTuneAircraft)
            {
                planeTuneAircraft = aircraft;
                planeTuneMode = progression.TuneFor(aircraft);
            }
            planeTuneName.Set(planeTuneMode == PlaneEngineMap.Range ? "RANGE" : "STOCK");
            planeTuneSegmented.Refresh();
            bool landed = aircraft.IsLanded() && !aircraft.disabled;
            bool applied = progression.TuneFor(aircraft) == planeTuneMode;
            string effect = planeTuneMode == PlaneEngineMap.Range
                ? "10% less fuel; 85% throttle ceiling."
                : "Full throttle; normal fuel draw.";
            string state = (applied ? "ACTIVE · " : landed ? "SELECTED · " : "LAND TO APPLY · ") + effect;
            if (aircraft.persistentID.Id == progression.TuneFeedbackAircraft &&
                Time.unscaledTime < progression.TuneFeedbackUntil)
                state = progression.TuneFeedback;
            planeTuneState.Set(state);
            planeApplyTune.Interactable = landed && !applied;
        }

        private void SelectPlaneTune(byte mode)
        {
            if (planeTuneAircraft == null || !PlaneEngineMap.IsDefined(mode)) return;
            planeTuneMode = mode;
            RefreshPlaneTune(planeTuneAircraft);
        }

        private void ApplyPlaneTune()
        {
            Aircraft aircraft = planeTuneAircraft;
            if (aircraft == null || !aircraft.IsLanded() ||
                !PlaneEngineMap.IsDefined((byte)planeTuneMode)) return;
            planeTuneState.Set("ENGINE MAP REQUEST SENT TO HOST");
            progression.RequestTune(aircraft, planeTuneMode);
        }

        /// <summary>
        /// Hosts the aircraft's own HUD damage art (<see cref="PlaneNativeDamageView"/>, kept as
        /// data/art per the P2 brief) inside a kit v2 frame. The wrapped view needs its final
        /// pixel area at construction time, so it is built lazily on the first <see cref="Place"/>.
        /// </summary>
        private sealed class PlaneDamagePart : AvPart
        {
            private readonly AvFrame frame;
            private readonly RectTransform host;
            private readonly TMP_Text state;
            private PlaneNativeDamageView view;

            public PlaneDamagePart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "Damage");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                host = AvLay.Child(Rect, "Host");
                state = AvText.Make(Rect, "State", AvTextRole.Micro, "NO AIRCRAFT", TextAlignmentOptions.Center);
                Restyle();
            }

            public void Refresh(Aircraft aircraft)
            {
                if (view == null) return;
                view.Refresh(aircraft);
                state.text = view.Available ? "LIVE PART CONDITION" : "NATIVE HUD UNAVAILABLE";
            }

            public void Clear() { view?.Clear(); state.text = "NO AIRCRAFT"; }

            public override float Measure(float width) => 170f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float hostH = s.H - 24f;
                AvLay.Place(host, 4f, 4f, s.W - 8f, hostH);
                AvLay.Place(state.rectTransform, 4f, s.H - 18f, s.W - 8f, 16f);
                if (view == null) view = new PlaneNativeDamageView(host, new Rect(0f, 0f, s.W - 8f, hostH));
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card inert");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                state.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Dim);
            }
        }
    }
}
