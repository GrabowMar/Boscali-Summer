using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Domain;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private const int PlaneFaultRows = 4;
        private const int PlaneStoreRows = 4;
        private readonly TMP_Text[] planeFlight = new TMP_Text[6];
        private readonly TMP_Text[] planeSystems = new TMP_Text[6];
        private readonly TMP_Text[] planeStores = new TMP_Text[PlaneStoreRows];
        private readonly TMP_Text[] planeFaultNames = new TMP_Text[PlaneFaultRows];
        private readonly TMP_Text[] planeFaultValues = new TMP_Text[PlaneFaultRows];
        private readonly Image[] planeFaultBars = new Image[PlaneFaultRows];
        private readonly UnitPart[] planeWorstParts = new UnitPart[PlaneFaultRows];
        private TMP_Text planeName;
        private TMP_Text planeState;
        private TMP_Text planeSelected;
        private TMP_Text planeStoreOverflow;
        private AvButton planePreviousStores;
        private AvButton planeNextStores;
        private int planeStorePage;
        private Image planeWeaponIcon;
        private PlaneNativeDamageView planeDamage;
        private TMP_Text planePlotState;
        private SqdGlyph planeReferenceOutline;
        private TMP_Text planeReferenceNote;
        private Image planeMapIcon;
        private TMP_Text planeMapFallback;
        private TMP_Text planeTuneName;
        private TMP_Text planeTuneState;
        private AvButton planeStockButton;
        private AvButton planeRangeButton;
        private AvButton planeApplyTune;
        private Aircraft planeTuneAircraft;
        private int planeTuneMode = -1;
        private string planeStatus = "No aircraft assigned.";

        private void ResetPlanePage()
        {
            planeName = planeState = planeSelected = planeStoreOverflow = null;
            planeWeaponIcon = null;
            planeDamage?.Clear();
            planeDamage = null;
            planePlotState = null;
            planeReferenceOutline = null;
            planeReferenceNote = null;
            planeMapIcon = null;
            planeMapFallback = planeTuneName = planeTuneState = null;
            planeStockButton = planeRangeButton = planeApplyTune = null;
            planeTuneAircraft = null;
            planeTuneMode = -1;
            planeStatus = "No aircraft assigned.";
            planePreviousStores = planeNextStores = null;
            planeStorePage = 0;
            Array.Clear(planeFlight, 0, planeFlight.Length);
            Array.Clear(planeSystems, 0, planeSystems.Length);
            Array.Clear(planeStores, 0, planeStores.Length);
            Array.Clear(planeFaultNames, 0, planeFaultNames.Length);
            Array.Clear(planeFaultValues, 0, planeFaultValues.Length);
            Array.Clear(planeFaultBars, 0, planeFaultBars.Length);
            Array.Clear(planeWorstParts, 0, planeWorstParts.Length);
        }

        private void BuildPlanePage(RectTransform page, Rect body)
        {
            const float contentHeight = 1096f;
            RectTransform parent = AvScreen.Scroll(page, body, contentHeight, out body);
            PageRail(parent, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = DrawPageHeader(parent, x, body.y, width,
                "AIRCRAFT DOSSIER", "OWN AIRCRAFT · LIVE", SqdMark.Aircraft);

            AvStyled.Box(parent, new Rect(x, y, width, 66f), "card");
            AvKit.Rule(parent, new Rect(x + 5f, y - 7f, 3f, 52f), AvTheme.RailInfo);
            planeName = Fitted(PlainLabel(parent, new Rect(x + 16f, y - 8f, width - 32f, 25f),
                "NO AIRCRAFT", "page-title"));
            planeState = PlainLabel(parent, new Rect(x + 16f, y - 35f, width - 32f, 19f),
                "PILOT NOT IN AIRCRAFT", "row-sub");
            y -= 77f;

            y = DrawSectionTitle(parent, x, y, width, "AIRFRAME STATUS", "NATIVE HUD DAMAGE MODEL", false);
            AvStyled.Box(parent, new Rect(x, y, width, 194f), "card");
            float plotWidth = Mathf.Min(218f, width * .52f);
            planeDamage = new PlaneNativeDamageView(parent, new Rect(x + 8f, y - 8f, plotWidth, 158f));
            planeReferenceOutline = SqdGlyph.Create(parent,
                new Rect(x + (plotWidth - 106f) * .5f, y - 34f, 106f, 106f), SqdMark.Aircraft);
            planeReferenceOutline.color = AvTheme.RailInfo.WithAlpha(.72f);
            planeReferenceNote = PlainLabel(parent,
                new Rect(x + 12f, y - 145f, plotWidth - 8f, 16f),
                "REFERENCE ONLY · NO PART DATA", "section-title-note");
            planeReferenceNote.alignment = TextAlignmentOptions.Center;
            PlainLabel(parent, new Rect(x + 12f, y - 169f, plotWidth - 8f, 15f),
                "AIRCRAFT HUD SCHEMATIC", "section-title-note");
            planePlotState = PlainLabel(parent, new Rect(x + 12f, y - 8f, plotWidth - 8f, 18f),
                "NO AIRCRAFT", "section-title-note");
            float rightX = x + plotWidth + 19f;
            float rightWidth = width - plotWidth - 27f;
            AvKit.Rule(parent, new Rect(rightX - 7f, y - 8f, 1f, 177f), AvTheme.Hairline);
            AvStyled.Box(parent, new Rect(rightX + 2f, y - 11f, 55f, 55f), "card inert");
            planeMapFallback = PlainLabel(parent, new Rect(rightX + 2f, y - 11f, 55f, 55f),
                "AIR", "section-title-note");
            planeMapFallback.alignment = TextAlignmentOptions.Center;
            planeMapIcon = AvKit.Panel(parent, new Rect(rightX + 2f, y - 11f, 55f, 55f), Color.white);
            planeMapIcon.preserveAspect = true;
            planeMapIcon.raycastTarget = false;
            planeMapIcon.enabled = false;
            PlainLabel(parent, new Rect(rightX + 66f, y - 13f, rightWidth - 66f, 18f),
                "ENGINE MAP", "section-title-note");
            planeTuneName = Fitted(PlainLabel(parent, new Rect(rightX + 66f, y - 35f,
                rightWidth - 66f, 25f), "STOCK", "kv-value"));
            float tuneButtonWidth = (rightWidth - 8f) * .5f;
            planeStockButton = AvStyled.Button(parent, new Rect(rightX, y - 73f, tuneButtonWidth, 25f),
                "STOCK", "btn", () => SelectPlaneTune(PlaneEngineMap.Stock), AvButtonStyle.Quiet);
            planeRangeButton = AvStyled.Button(parent, new Rect(rightX + tuneButtonWidth + 8f,
                y - 73f, tuneButtonWidth, 25f),
                "RANGE", "btn", () => SelectPlaneTune(PlaneEngineMap.Range), AvButtonStyle.Quiet);
            planeApplyTune = AvStyled.Button(parent, new Rect(rightX, y - 104f,
                rightWidth, 27f), "APPLY MAP", "btn", ApplyPlaneTune, AvButtonStyle.Primary);
            planeTuneState = PlainLabel(parent, new Rect(rightX, y - 142f, rightWidth, 40f),
                "Stock: full throttle and normal fuel draw.", "row-sub");
            y -= 207f;

            y = DrawSectionTitle(parent, x, y, width, "FLIGHT DATA", "NATIVE UNITS", false);
            string[] flightKeys = { "TRUE AIRSPEED", "GROUND SPEED", "ALTITUDE MSL", "VERTICAL SPEED", "HEADING", "G LOAD" };
            float tileWidth = (width - 16f) / 3f;
            for (int i = 0; i < planeFlight.Length; i++)
            {
                float tx = x + (i % 3) * (tileWidth + 8f);
                float ty = y - (i / 3) * 57f;
                AvStyled.Box(parent, new Rect(tx, ty, tileWidth, 51f), "card inert");
                PlainLabel(parent, new Rect(tx + 7f, ty - 5f, tileWidth - 14f, 15f),
                    flightKeys[i], "section-title-note");
                planeFlight[i] = Fitted(PlainLabel(parent,
                    new Rect(tx + 7f, ty - 23f, tileWidth - 14f, 22f), "—", "kv-value"));
            }
            y -= 125f;

            y = DrawSectionTitle(parent, x, y, width, "AIRCRAFT SYSTEMS", "READ ONLY", false);
            string[] systemKeys = { "FUEL", "THROTTLE", "LANDING GEAR", "FLIGHT ASSIST", "COUNTERMEASURE", "MISSILE WARNING" };
            float columnWidth = (width - 12f) / 2f;
            for (int i = 0; i < planeSystems.Length; i++)
            {
                float sx = x + (i % 2) * (columnWidth + 12f);
                float sy = y - (i / 2) * 32f;
                AvStyled.Box(parent, new Rect(sx, sy, columnWidth, 28f), "row");
                PlainLabel(parent, new Rect(sx + 7f, sy - 3f, columnWidth * .55f, 20f),
                    systemKeys[i], "form-key");
                planeSystems[i] = Fitted(PlainLabel(parent,
                    new Rect(sx + columnWidth * .53f, sy - 3f, columnWidth * .43f, 20f),
                    "—", "form-value"));
                planeSystems[i].alignment = TextAlignmentOptions.MidlineRight;
            }
            y -= 109f;

            y = DrawSectionTitle(parent, x, y, width, "STORES", "CURRENT LOADOUT", false);
            AvStyled.Box(parent, new Rect(x, y, width, 57f), "card");
            planeWeaponIcon = AvKit.Panel(parent, new Rect(x + 8f, y - 7f, 42f, 42f), Color.white);
            planeWeaponIcon.preserveAspect = true;
            planeWeaponIcon.enabled = false;
            planeWeaponIcon.raycastTarget = false;
            PlainLabel(parent, new Rect(x + 58f, y - 7f, width - 70f, 15f),
                "SELECTED STATION", "section-title-note");
            planeSelected = Fitted(PlainLabel(parent,
                new Rect(x + 58f, y - 24f, width - 70f, 25f), "NO STATION", "kv-value"));
            y -= 65f;
            planePreviousStores = AvStyled.Button(parent, new Rect(x, y, 85f, 25f),
                "PREVIOUS", "btn", () =>
                {
                    planeStorePage = Math.Max(0, planeStorePage - 1);
                    nextRefresh = 0f;
                }, AvButtonStyle.Quiet);
            planePreviousStores.WithTooltip("Show earlier weapon stations.");
            planeStoreOverflow = PlainLabel(parent, new Rect(x + 90f, y, width - 180f, 25f),
                "NO STATIONS", "section-title-note");
            planeStoreOverflow.alignment = TextAlignmentOptions.Center;
            planeNextStores = AvStyled.Button(parent, new Rect(x + width - 85f, y, 85f, 25f),
                "NEXT", "btn", () =>
                {
                    int count = CurrentPlaneStoreCount();
                    if ((planeStorePage + 1) * PlaneStoreRows < count) planeStorePage++;
                    nextRefresh = 0f;
                }, AvButtonStyle.Quiet);
            planeNextStores.WithTooltip("Show later weapon stations.");
            y -= 31f;
            for (int i = 0; i < PlaneStoreRows; i++)
            {
                float rowY = y - i * 25f;
                AvStyled.Box(parent, new Rect(x, rowY, width, 22f), "row");
                planeStores[i] = Fitted(PlainLabel(parent,
                    new Rect(x + 8f, rowY - 2f, width - 16f, 18f), "—", "row-sub"));
            }
            y -= 104f;
            y -= 19f;

            y = DrawSectionTitle(parent, x, y, width, "AIRFRAME INSPECTION", "WORST FOUR PARTS", false);
            for (int i = 0; i < PlaneFaultRows; i++)
            {
                float rowY = y - i * 34f;
                AvStyled.Box(parent, new Rect(x, rowY, width, 30f), "row");
                planeFaultNames[i] = Fitted(PlainLabel(parent,
                    new Rect(x + 8f, rowY - 2f, width * .62f, 19f), "NO READING", "row-sub"));
                planeFaultValues[i] = PlainLabel(parent,
                    new Rect(x + width - 73f, rowY - 2f, 65f, 19f), "—", "form-value");
                planeFaultValues[i].alignment = TextAlignmentOptions.MidlineRight;
                Image track = AvKit.Panel(parent,
                    new Rect(x + 8f, rowY - 24f, width - 16f, 3f), AvTheme.RailInert);
                track.raycastTarget = false;
                planeFaultBars[i] = AvKit.Panel(parent,
                    new Rect(x + 8f, rowY - 24f, width - 16f, 3f), AvTheme.RailReady, AvSprites.White);
                planeFaultBars[i].type = Image.Type.Filled;
                planeFaultBars[i].fillMethod = Image.FillMethod.Horizontal;
                planeFaultBars[i].raycastTarget = false;
                planeFaultBars[i].fillAmount = 0f;
            }
            y -= 146f;
            PlainLabel(parent, new Rect(x + 2f, y, width - 4f, 26f),
                "Damage reflects the aircraft's measured parts. Missing parts read as unavailable.", "row-sub");
        }

        private void RefreshPlanePage()
        {
            if (planeName == null) return;
            Aircraft aircraft = null;
            if (GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
                aircraft = local.Aircraft;
            if (aircraft == null)
            {
                planeName.text = "NO AIRCRAFT";
                planeState.text = "PILOT NOT IN AIRCRAFT";
                planeState.color = AvTheme.Dim;
                planeStatus = "Assign or enter an aircraft to see its live dossier.";
                dataBar.State.text = "AIRCRAFT NOT ASSIGNED";
                dataBar.State.color = AvTheme.RailInert;
                dataBar.SetChip(0, "NO AIRCRAFT", false);
                dataBar.SetChip(1, "NO FLIGHT", false);
                dataBar.SetChip(2, "NO STORES", false);
                for (int i = 0; i < planeFlight.Length; i++) planeFlight[i].text = "—";
                for (int i = 0; i < planeSystems.Length; i++) planeSystems[i].text = "—";
                for (int i = 0; i < planeStores.Length; i++) planeStores[i].text = "—";
                planeStoreOverflow.text = "";
                planeStorePage = 0;
                planePreviousStores.SetEnabled(false);
                planeNextStores.SetEnabled(false);
                planeSelected.text = "NO STATION";
                planeWeaponIcon.enabled = false;
                planeSystems[5].color = AvTheme.Dim;
                planeDamage.Clear();
                planePlotState.text = "NO AIRCRAFT";
                planeReferenceOutline.gameObject.SetActive(false);
                planeReferenceNote.gameObject.SetActive(true);
                planeReferenceNote.text = "AWAITING AIRFRAME";
                planeMapIcon.enabled = false;
                planeMapFallback.gameObject.SetActive(true);
                planeTuneAircraft = null;
                planeTuneMode = -1;
                planeTuneName.text = "NO AIRCRAFT";
                planeTuneState.text = "Enter an aircraft to select an engine map.";
                planeStockButton.SetEnabled(false);
                planeRangeButton.SetEnabled(false);
                planeApplyTune.SetEnabled(false);
                for (int i = 0; i < PlaneFaultRows; i++) PaintPlanePart(i, null);
                return;
            }

            string model = aircraft.definition != null ? aircraft.definition.unitName : aircraft.unitName;
            planeName.text = string.IsNullOrEmpty(model) ? "AIRCRAFT" : model.ToUpperInvariant();
            planeState.text = aircraft.disabled ? "DISABLED" : aircraft.HasEjected() ? "PILOT EJECTED"
                : aircraft.IsLanded() ? "ON GROUND" : "AIRBORNE";
            planeState.color = aircraft.disabled ? AvTheme.RailDanger : AvTheme.RailReady;
            planeDamage.Refresh(aircraft);
            planePlotState.text = planeDamage.Available ? "LIVE PART CONDITION" : "NATIVE HUD UNAVAILABLE";
            planeReferenceOutline.gameObject.SetActive(!planeDamage.Available);
            planeReferenceNote.gameObject.SetActive(!planeDamage.Available);
            if (!planeDamage.Available) planeReferenceNote.text = "REFERENCE ONLY · NO PART DATA";
            planeMapIcon.sprite = aircraft.definition != null ? aircraft.definition.mapIcon : null;
            planeMapIcon.enabled = planeMapIcon.sprite != null;
            planeMapFallback.gameObject.SetActive(planeMapIcon.sprite == null);
            RefreshPlaneTune(aircraft);

            Vector3 velocity = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            Vector3 airVelocity = velocity - aircraft.GetWindVelocity();
            planeFlight[0].text = aircraft.rb != null ? UnitConverter.SpeedReading(airVelocity.magnitude) : "—";
            planeFlight[1].text = aircraft.rb != null
                ? UnitConverter.SpeedReading(new Vector2(velocity.x, velocity.z).magnitude) : "—";
            planeFlight[2].text = UnitConverter.AltitudeReading((float)aircraft.transform.GlobalPosition().y);
            planeFlight[3].text = aircraft.rb != null ? UnitConverter.ClimbRateReading(velocity.y) : "—";
            planeFlight[4].text = Mathf.RoundToInt(Mathf.Repeat(aircraft.transform.eulerAngles.y, 360f)) + "°";
            planeFlight[5].text = aircraft.gForce.ToString("0.0") + " G";

            float fuel = Mathf.Clamp01(aircraft.fuelLevel);
            planeSystems[0].text = Mathf.RoundToInt(fuel * 100f) + "%";
            ControlInputs inputs = aircraft.GetInputs();
            planeSystems[1].text = inputs != null ? Mathf.RoundToInt(Mathf.Clamp01(inputs.throttle) * 100f) + "%" : "—";
            planeSystems[2].text = aircraft.gearDeployed ? "DOWN" : "UP";
            planeSystems[3].text = aircraft.flightAssist ? "ON" : "OFF";
            Countermeasure countermeasure = aircraft.countermeasureManager?.GetActiveCountermeasure();
            planeSystems[4].text = countermeasure != null ? countermeasure.ammo.ToString() + " READY" : "NONE";
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            int inbound = warning?.knownMissiles != null ? warning.knownMissiles.Count : 0;
            planeSystems[5].text = inbound > 0 ? inbound + " TRACKED" : "CLEAR";
            planeSystems[5].color = inbound > 0 ? AvTheme.RailDanger : AvTheme.RailReady;
            dataBar.State.text = inbound > 0 ? "MISSILE WARNING" : "AIRCRAFT TELEMETRY";
            dataBar.State.color = inbound > 0 ? AvTheme.RailDanger : AvTheme.RailReady;
            dataBar.SetChip(0, planeState.text, !aircraft.disabled);
            dataBar.SetChip(1, "FUEL " + Mathf.RoundToInt(fuel * 100f) + "%", fuel > .15f);
            dataBar.SetChip(2, inbound > 0 ? inbound + " INBOUND" : "RWR CLEAR", inbound == 0);

            WeaponManager manager = aircraft.weaponManager;
            WeaponStation selected = manager?.currentWeaponStation;
            WeaponInfo selectedInfo = selected?.WeaponInfo;
            planeSelected.text = selectedInfo != null
                ? (string.IsNullOrEmpty(selectedInfo.shortName) ? selectedInfo.weaponName : selectedInfo.shortName)
                    + "  ·  " + selected.GetAmmoReadout()
                : "NO STATION SELECTED";
            planeWeaponIcon.sprite = selectedInfo != null ? selectedInfo.weaponIcon : null;
            planeWeaponIcon.enabled = planeWeaponIcon.sprite != null;
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
                planeStores[i].text = index < count ? (index + 1).ToString("00") + "  " + name +
                    "  ·  " + station.GetAmmoReadout() + (station == selected ? "  SELECTED" : "") : "—";
            }
            planeStoreOverflow.text = count == 0 ? "NO STATIONS"
                : (first + 1) + "–" + Mathf.Min(first + PlaneStoreRows, count) + " OF " + count;
            planePreviousStores.SetEnabled(planeStorePage > 0);
            planeNextStores.SetEnabled(first + PlaneStoreRows < count);

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
                planeFaultNames[row].text = "NO MEASURED PART";
                planeFaultValues[row].text = "—";
                planeFaultBars[row].fillAmount = 0f;
                return;
            }
            float condition = PartCondition(part);
            string name = part.gameObject.name.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
            if (name.Length > 28) name = name.Substring(0, 28);
            bool detached = part.IsDetached();
            planeFaultNames[row].text = name;
            planeFaultValues[row].text = detached ? "LOST" : Mathf.RoundToInt(condition * 100f) + "%";
            Color ink = detached || condition < .25f ? AvTheme.RailDanger
                : condition < .995f ? AvTheme.RailCaution : AvTheme.RailReady;
            planeFaultValues[row].color = ink;
            planeFaultBars[row].color = ink;
            planeFaultBars[row].fillAmount = condition;
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
            planeTuneName.text = planeTuneMode == PlaneEngineMap.Range ? "RANGE" : "STOCK";
            bool landed = aircraft.IsLanded() && !aircraft.disabled;
            bool applied = progression.TuneFor(aircraft) == planeTuneMode;
            string effect = planeTuneMode == PlaneEngineMap.Range
                ? "10% less fuel; 85% throttle ceiling."
                : "Full throttle; normal fuel draw.";
            planeTuneState.text = (applied ? "ACTIVE · " : landed ? "SELECTED · " : "LAND TO APPLY · ") + effect;
            if (aircraft.persistentID.Id == progression.TuneFeedbackAircraft &&
                Time.unscaledTime < progression.TuneFeedbackUntil)
                planeTuneState.text = progression.TuneFeedback;
            planeStockButton.SetEnabled(true);
            planeRangeButton.SetEnabled(true);
            planeApplyTune.SetEnabled(landed && !applied);
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
            planeTuneState.text = "ENGINE MAP REQUEST SENT TO HOST";
            progression.RequestTune(aircraft, planeTuneMode);
        }
    }
}
