using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Progression.Domain;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private const int PlaneFaultRows = 4;
        private const int PlaneStoreRows = 4;
        private const int SystemGear = 0, SystemAssist = 1, SystemCountermeasure = 2, SystemWarning = 3;
        private readonly AvStatTile[] planeFlight = new AvStatTile[6];
        private readonly SqdRosterRow[] planeStores = new SqdRosterRow[PlaneStoreRows];
        private readonly SqdRosterRow[] planeFaults = new SqdRosterRow[PlaneFaultRows];
        private readonly UnitPart[] planeWorstParts = new UnitPart[PlaneFaultRows];
        private readonly List<AvPart> planeLiveParts = new List<AvPart>(32);
        private SqdStatGrid planeSystems;
        private PlaneHero planeIdentity;
        private SqdEmptyCard planeEmpty;
        private AvSection planeStoresSection;
        private AvButtons planeStorePager;
        private SqdRosterRow planeSelected;
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
        private bool planeLive;
        private string planeStatus = "No aircraft assigned.";

        private void ResetPlanePage()
        {
            planeIdentity = null;
            planeSelected = null;
            planeEmpty = null;
            planeSystems = null;
            planeStoresSection = null;
            planeStorePager = null;
            planeDamage?.Clear();
            planeDamage = null;
            planeTuneName = null;
            planeTuneSegmented = null;
            planeTuneState = null;
            planeApplyTune = null;
            planeTuneAircraft = null;
            planeTuneMode = -1;
            planeLive = false;
            planeStatus = "No aircraft assigned.";
            planePreviousStores = planeNextStores = null;
            planeStorePage = 0;
            planeLiveParts.Clear();
            Array.Clear(planeFlight, 0, planeFlight.Length);
            Array.Clear(planeStores, 0, planeStores.Length);
            Array.Clear(planeFaults, 0, planeFaults.Length);
            Array.Clear(planeWorstParts, 0, planeWorstParts.Length);
        }

        private T Live<T>(T part) where T : AvPart { planeLiveParts.Add(part); return part; }

        /// <summary>
        /// Without an aircraft the page is one compact card, not a wall of empty readouts: every live part
        /// collapses out of the flow and the empty card takes its place, and back again on entering one.
        /// </summary>
        private void SetPlaneLive(bool live)
        {
            if (planeEmpty == null || (live == planeLive && planeEmpty.Shown == !live)) return;
            planeLive = live;
            planeEmpty.SetShown(!live);
            for (int i = 0; i < planeLiveParts.Count; i++) planeLiveParts[i].SetShown(live);
        }

        private void BuildPlanePage(AvFlow p)
        {
            planeEmpty = p.Add(new SqdEmptyCard(p.Content, AvIcon.Plane, "NO AIRCRAFT",
                "Enter an aircraft to open its dossier."), 1f);
            planeIdentity = p.Add(Live(new PlaneHero(p.Content)));

            planeDamage = Live(new PlaneDamagePart(p.Content));
            float half = AvFlowMath.ColumnWidth(p.Inner, 2, AvGridTokens.Gap);
            AvCard engineCard = Live(new AvCard(p.Content, console.Ticker, half, "ENGINE MAP"));
            planeTuneName = engineCard.Flow.Add(new AvTextBlock(engineCard.Flow.Content, AvTextRole.DataStrong));
            planeTuneName.Set("STOCK");
            planeTuneSegmented = engineCard.Flow.Add(new AvSegmented(engineCard.Flow.Content, "MAP",
                new[] { "STOCK", "RANGE" }, () => planeTuneMode == PlaneEngineMap.Range ? 1 : 0,
                i => SelectPlaneTune(i == 1 ? PlaneEngineMap.Range : PlaneEngineMap.Stock)));
            planeTuneSegmented.Options[0].Help = "STOCK: full throttle and normal fuel draw.";
            planeTuneSegmented.Options[1].Help = "RANGE: 10% less fuel draw, throttle capped at 85%. Choose it, then land to apply it.";
            AvButtons applyRow = engineCard.Flow.Buttons(
                new AvControl.Spec("APPLY MAP", ApplyPlaneTune, AvButtonStyle.Primary, AvIcon.CircleCheck));
            planeApplyTune = applyRow.Controls[0];
            planeApplyTune.Help = "Send the selected engine map to the host. It can only be applied while landed.";
            planeTuneState = engineCard.Flow.Add(new AvTextBlock(engineCard.Flow.Content, AvTextRole.ProseSmall));
            planeTuneState.Set("Enter an aircraft to select an engine map.");
            p.Row(planeDamage, engineCard);

            // Flight data and systems share one block: two rows of three readouts, then the four systems in a line.
            string[] flightKeys = { "TRUE AIRSPEED", "GROUND SPEED", "ALTITUDE MSL", "VERTICAL SPEED", "HEADING", "G LOAD" };
            for (int i = 0; i < planeFlight.Length; i++) planeFlight[i] = Live(new AvStatTile(p.Content, flightKeys[i]));
            p.Row(planeFlight[0], planeFlight[1], planeFlight[2]);
            p.Row(planeFlight[3], planeFlight[4], planeFlight[5]);

            planeSystems = p.Add(Live(new SqdStatGrid(p.Content, 4)));
            planeSystems.Add("GEAR");
            planeSystems.Add("ASSIST");
            planeSystems.Add("CMS");
            planeSystems.Add("WARNING");
            planeSystems.Help = "SYSTEMS: GEAR is the landing gear, ASSIST the flight-assist mode, CMS the active countermeasure and its stock, WARNING the missiles the warning receiver is tracking.";

            planeStoresSection = Live(p.Section(AvIcon.Stack2, "STORES", null));
            planeSelected = p.Add(Live(new SqdRosterRow(p.Content)));
            planeSelected.Set("NO STATION SELECTED", null, null, null, AvState.Inert);
            planeSelected.Help = "The weapon station your trigger is armed on, with its ammunition.";
            for (int i = 0; i < PlaneStoreRows; i++) planeStores[i] = Live(new SqdRosterRow(p.Content));
            p.Row(planeStores[0], planeStores[1]);
            p.Row(planeStores[2], planeStores[3]);
            planeStorePager = p.Add(Live(new AvButtons(p.Content, new[]
            {
                new AvControl.Spec("PREVIOUS", () => { planeStorePage = Math.Max(0, planeStorePage - 1); nextRefresh = 0f; },
                    AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("NEXT", () =>
                {
                    int count = CurrentPlaneStoreCount();
                    if ((planeStorePage + 1) * PlaneStoreRows < count) planeStorePage++;
                    nextRefresh = 0f;
                }, AvButtonStyle.Quiet, AvIcon.ChevronRight, true)
            })));
            planePreviousStores = planeStorePager.Controls[0];
            planeNextStores = planeStorePager.Controls[1];
            planePreviousStores.Help = "Show earlier weapon stations.";
            planeNextStores.Help = "Show later weapon stations.";

            Live(p.Section(AvIcon.AlertTriangle, "WORST PARTS", null));
            for (int i = 0; i < PlaneFaultRows; i++) planeFaults[i] = Live(new SqdRosterRow(p.Content));
            p.Row(planeFaults[0], planeFaults[1]);
            p.Row(planeFaults[2], planeFaults[3]);

            // Start on the empty card: there is no aircraft until the first refresh proves otherwise.
            planeLive = true;
            SetPlaneLive(false);
        }

        private void RefreshPlanePage()
        {
            if (planeIdentity == null) return;
            Aircraft aircraft = null;
            if (GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
                aircraft = local.Aircraft;
            if (aircraft == null)
            {
                planeStatus = "Assign or enter an aircraft to see its live dossier.";
                planeStorePage = 0;
                planePreviousStores.Interactable = false;
                planeNextStores.Interactable = false;
                planeDamage.Clear();
                planeTuneAircraft = null;
                planeTuneMode = -1;
                planeTuneName.Set("NO AIRCRAFT");
                planeTuneState.Set("Enter an aircraft to select an engine map.");
                planeApplyTune.Interactable = false;
                SetPlaneLive(false);
                return;
            }
            SetPlaneLive(true);

            string model = aircraft.definition != null ? aircraft.definition.unitName : aircraft.unitName;
            bool disabled = aircraft.disabled;
            float fuel = Mathf.Clamp01(aircraft.fuelLevel);
            ControlInputs inputs = aircraft.GetInputs();
            planeIdentity.Set(string.IsNullOrEmpty(model) ? "AIRCRAFT" : model.ToUpperInvariant(),
                disabled ? "DISABLED" : aircraft.HasEjected() ? "PILOT EJECTED"
                    : aircraft.IsLanded() ? "ON GROUND" : "AIRBORNE",
                disabled ? AvState.Danger : aircraft.IsLanded() ? AvState.Info : AvState.Ready,
                fuel, inputs != null ? Mathf.Clamp01(inputs.throttle) : (float?)null);
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

            planeSystems.Set(SystemGear, aircraft.gearDeployed ? "DOWN" : "UP");
            planeSystems.Set(SystemAssist, aircraft.flightAssist ? "ON" : "OFF");
            Countermeasure countermeasure = aircraft.countermeasureManager?.GetActiveCountermeasure();
            planeSystems.Set(SystemCountermeasure, countermeasure != null ? AvNum.Thousands(countermeasure.ammo) + " READY" : "NONE");
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            int inbound = warning?.knownMissiles != null ? warning.knownMissiles.Count : 0;
            planeSystems.Set(SystemWarning, inbound > 0 ? AvNum.Thousands(inbound) + " TRACKED" : "CLEAR",
                inbound > 0 ? AvState.Danger : AvState.Ready);

            WeaponManager manager = aircraft.weaponManager;
            WeaponStation selected = manager?.currentWeaponStation;
            WeaponInfo selectedInfo = selected?.WeaponInfo;
            planeSelected.Set(
                selectedInfo != null ? (string.IsNullOrEmpty(selectedInfo.shortName) ? selectedInfo.weaponName : selectedInfo.shortName) : "NO STATION SELECTED",
                selectedInfo != null ? "SELECTED STATION" : null, selectedInfo != null ? selected.GetAmmoReadout() : null, null,
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
                planeStores[i].SetShown(index < count);
                if (index < count)
                    planeStores[i].Set(AvNum.Thousands(index + 1) + "  " + name, null, station.GetAmmoReadout(),
                        station == selected ? "SELECTED" : null, station == selected ? AvState.Info : AvState.Inert);
            }
            planeStoresSection.SetCaption(count == 0 ? "NO STATIONS"
                : AvNum.Thousands(first + 1) + "–" + AvNum.Thousands(Mathf.Min(first + PlaneStoreRows, count)) + " OF " + AvNum.Thousands(count));
            planeStorePager.SetShown(count > PlaneStoreRows);
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
            planeFaults[row].SetShown(part != null);
            if (part == null) return;
            float condition = PartCondition(part);
            string name = part.gameObject.name.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
            if (name.Length > 18) name = name.Substring(0, 18);
            bool detached = part.IsDetached();
            AvState state = detached || condition < .25f ? AvState.Danger
                : condition < .995f ? AvState.Caution : AvState.Ready;
            planeFaults[row].Set(name, null, detached ? "LOST" : AvNum.Percent(condition), null, state);
            planeFaults[row].SetMeter(condition, SqdTone.Rail(state));
            planeFaults[row].Help = (part.gameObject.name.Replace('_', ' ').Replace('-', ' ')) + ": " +
                (detached ? "torn off the airframe." : AvNum.Percent(condition) + " condition. The four lowest parts are listed; the damage art above shows where.");
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
        /// The aircraft dossier hero: airframe name in title type, a state chip, and fuel / throttle
        /// bars with their mono readouts. Every text has a fixed slot.
        /// </summary>
        private sealed class PlaneHero : AvPart
        {
            private const float CardH = 92f, Pad = 12f;
            private readonly AvFrame frame;
            private readonly TMP_Text name, fuelKey, fuelValue, throttleKey, throttleValue;
            private readonly SqdBar fuelBar, throttleBar;
            private readonly AvChip stateChip;
            private float fuel, throttle;
            private AvState state = AvState.Ready;

            public PlaneHero(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "PlaneHero");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                frame.Bracket = 6f;
                name = AvText.Make(Rect, "Name", AvTextRole.Title);
                AvText.Fit(name, false);
                stateChip = new AvChip(Rect);
                fuelKey = AvText.Make(Rect, "FuelKey", AvTextRole.Micro, "FUEL");
                fuelValue = AvText.Make(Rect, "FuelValue", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(fuelValue, false);
                throttleKey = AvText.Make(Rect, "ThrottleKey", AvTextRole.Micro, "THROTTLE");
                throttleValue = AvText.Make(Rect, "ThrottleValue", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(throttleValue, false);
                fuelBar = new SqdBar(Rect, "Fuel");
                throttleBar = new SqdBar(Rect, "Throttle");
                Restyle();
            }

            public void Set(string airframe, string stateWord, AvState st, float fuelFraction, float? throttleFraction)
            {
                name.text = airframe ?? "";
                stateChip.Set(stateWord, st);
                state = st;
                fuel = fuelFraction;
                throttle = throttleFraction ?? 0f;
                fuelValue.text = AvNum.Percent(fuelFraction);
                throttleValue.text = throttleFraction.HasValue ? AvNum.Percent(throttleFraction.Value) : "—";
                Restyle();
            }

            public override float Measure(float width) => CardH;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(name.rectTransform, Pad, 8f, s.W - 2f * Pad - 118f, 26f);
                stateChip.Place(new AvSlot(s.W - Pad - 108f, 10f, 108f, AvGridTokens.ChipStrip));
                const float keyW = 70f, valueW = 56f;
                float barX = Pad + keyW, barW = s.W - 2f * Pad - keyW - valueW - 8f;
                AvLay.Place(fuelKey.rectTransform, Pad, 44f, keyW, 16f);
                fuelBar.Place(barX, 50f, barW, 5f);
                AvLay.Place(fuelValue.rectTransform, s.W - Pad - valueW, 42f, valueW, 20f);
                AvLay.Place(throttleKey.rectTransform, Pad, 66f, keyW, 16f);
                throttleBar.Place(barX, 72f, barW, 5f);
                AvLay.Place(throttleValue.rectTransform, s.W - Pad - valueW, 64f, valueW, 20f);
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card raised");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceRaised), AvStyleHost.Resolve(c.Border, AvTheme.Frame));
                frame.BracketColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
                name.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("title").Color, AvTheme.TextPrimary);
                fuelKey.color = throttleKey.color = SqdTone.Caption;
                fuelValue.color = throttleValue.color = SqdTone.Ink;
                stateChip.Restyle();
                fuelBar.Restyle();
                throttleBar.Restyle();
                fuelBar.Set(fuel, SqdTone.Rail(fuel <= .15f ? AvState.Caution : AvState.Info));
                throttleBar.Set(throttle, SqdTone.Rail(AvState.Info));
            }
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
            private bool lastAvailable;

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
                if (view.Available != lastAvailable) { lastAvailable = view.Available; Changed(); }
            }

            public void Clear() { view?.Clear(); state.text = "NO AIRCRAFT"; if (lastAvailable) { lastAvailable = false; Changed(); } }

            public override float Measure(float width) => 170f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float hostH = s.H - 24f;
                AvLay.Place(host, 4f, 4f, s.W - 8f, hostH);
                AvLay.Place(state.rectTransform, 4f, lastAvailable ? s.H - 18f : (s.H - 16f) * 0.5f, s.W - 8f, 16f);
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
