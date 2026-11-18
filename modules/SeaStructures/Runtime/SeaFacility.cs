using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.SeaStructures.Domain;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>The native identity and one working vertical flight facility of each static platform.</summary>
    internal static class SeaFacility
    {
        // The native faction list matches ShipType.ToString() to UnitType.typeName. This reserved value is local definition data,
        // not a new network message; the matching row receives its human label in SeaFacilityCategoryPatch below.
        internal const ShipType PlatformType = (ShipType)0x4253;
        internal const string CategoryLabel = "SEA PLATFORMS";
        private static readonly FieldInfo AirbaseSettings = AccessTools.Field(typeof(Airbase), "airbaseSettings");
        private static readonly FieldInfo AvailableAircraft = AccessTools.Field(typeof(Hangar), "availableAircraft");
        private static readonly FieldInfo SpawnTransform = AccessTools.Field(typeof(Hangar), "spawnTransform");
        private static readonly FieldInfo CriticalPart = AccessTools.Field(typeof(Hangar), "criticalPart");
        private static readonly FieldInfo WaitForOpen = AccessTools.Field(typeof(Hangar), "waitForOpenBeforeSpawn");
        private static readonly FieldInfo Doors = AccessTools.Field(typeof(Hangar), "doors");
        private static readonly FieldInfo Lights = AccessTools.Field(typeof(Hangar), "lights");
        private static readonly FieldInfo SelfDisabled = AccessTools.Field(typeof(Hangar), "selfDisabled");

        internal static float LandingRadius(SeaKind kind) => kind == SeaKind.SeaFort ? 10f : 12f;

        internal static bool Fits(SeaKind kind, AircraftDefinition definition)
        {
            if (definition == null || definition.aircraftParameters == null || !definition.aircraftParameters.verticalLanding) return false;
            float length = definition.length, width = definition.width, height = definition.height;
            if (!float.IsFinite(length) || !float.IsFinite(width) || !float.IsFinite(height) || length <= 0f || width <= 0f || height <= 0f) return false;
            // Match the native vertical landing query (aircraft.maxRadius), and keep a metre inside the authored deck edges.
            if (Mathf.Max(length, width, height) * .5f >= LandingRadius(kind)) return false;
            return kind == SeaKind.SeaFort ? length <= 44f && width <= 20f : length * length + width * width <= 25f * 25f;
        }

        internal static void Configure(Encyclopedia encyclopedia, GameObject template, SeaLayout layout, ShipDefinition donor, Transform pad, UnitPart deckPart)
        {
            Ship ship = template.GetComponent<Ship>();
            Airbase airbase = template.GetComponent<Airbase>();
            Hangar[] hangars = template.GetComponentsInChildren<Hangar>(true);
            if (airbase == null || hangars.Length != 1 || deckPart == null)
                throw new InvalidOperationException(layout.Key + " needs one native attached airbase, hangar and flight-deck part.");

            ShipDefinition definition = Object.Instantiate(donor);
            definition.name = definition.jsonKey = layout.Key;
            definition.unitName = layout.UnitName;
            definition.bogeyName = "PLATFORM";
            definition.description = layout.Kind == SeaKind.SeaFort
                ? "A fixed offshore defensive fortress with native gun and missile batteries and a compact helicopter/VTOL landing deck."
                : "A fixed offshore oil platform with defensive weapons and a helicopter/VTOL landing deck.";
            definition.shipType = PlatformType;
            // Native attached-airbase hangar readouts recognize SHP. The specific identity comes from this definition, not its donor.
            definition.code = "SHP";
            definition.shipInfo = new ShipInfo { mass = definition.mass, topSpeed = 0f };
            definition.mapOrient = false;
            definition.unitPrefab = template;
            ship.definition = definition;

            SeaSection padSection = Array.Find(layout.Sections, s => s.Pad);
            Transform point = new GameObject("Sea platform vertical landing point").transform;
            point.SetParent(pad, false);
            point.position = padSection.At + Vector3.up * .05f;
            // The fort approaches outboard to the west; the rig's deck approaches away from its south-west quarters.
            point.rotation = Quaternion.Euler(0f, layout.Kind == SeaKind.SeaFort ? 270f : 225f, 0f);
            airbase.center = point;
            airbase.aircraftSelectionTransform = point;
            // The donor's fixed preview camera sits in its old hull. Native selection gives this transform priority over
            // the aircraft's floor anchor, so author an independent view above and outboard of the new flight deck.
            Transform previewCamera = new GameObject("Sea platform aircraft preview camera").transform;
            previewCamera.SetParent(pad, false);
            previewCamera.position = padSection.At + (layout.Kind == SeaKind.SeaFort
                ? new Vector3(-30f, 16f, -24f) : new Vector3(-26f, 16f, -26f));
            previewCamera.rotation = Quaternion.LookRotation(point.position + Vector3.up * 3f - previewCamera.position, Vector3.up);
            airbase.fixedCameraTransform = previewCamera;
            airbase.runways = Array.Empty<Airbase.Runway>();
            airbase.verticalLandingPoints = new[] { new Airbase.VerticalLandingPoint
            {
                point = point, unitPart = deckPart, approachAngleRange = 80f, size = LandingRadius(layout.Kind)
            } };
            var settings = (NuclearOption.SavedMission.SavedAirbase)AirbaseSettings.GetValue(airbase);
            settings.DisplayName = layout.UnitName;
            settings.Disabled = false;

            var aircraft = new List<AircraftDefinition>();
            foreach (AircraftDefinition candidate in encyclopedia.aircraft)
                if (Fits(layout.Kind, candidate)) aircraft.Add(candidate);
            aircraft.Sort((a, b) => string.CompareOrdinal(a.jsonKey, b.jsonKey));
            if (aircraft.Count == 0) throw new InvalidOperationException(layout.Key + " has no compatible vertical aircraft.");
            Hangar hangar = hangars[0];
            hangar.attachedUnit = ship;
            AvailableAircraft.SetValue(hangar, aircraft.ToArray());
            SpawnTransform.SetValue(hangar, point);
            CriticalPart.SetValue(hangar, deckPart);
            WaitForOpen.SetValue(hangar, false);
            Doors.SetValue(hangar, Array.Empty<HangarDoor>());
            Lights.SetValue(hangar, Array.Empty<HangarLighting>());
        }

        internal static void RegisterDefinitions(Encyclopedia encyclopedia, GameObject[] templates)
        {
            if (Encyclopedia.Lookup == null || encyclopedia.IndexLookup == null) return;
            string categoryKey = PlatformType.ToString();
            Encyclopedia.UnitType category = encyclopedia.shipTypes.Find(t => t.typeName == categoryKey);
            if (category == null)
            {
                category = new Encyclopedia.UnitType { typeName = categoryKey };
                encyclopedia.shipTypes.Add(category);
            }
            GameAssets assets = GameAccess.GameAssetsOrNull;
            if (assets != null) category.typeSprite = assets.hangarSprite_helipad;
            // Same fixed order on every peer, following the existing Skylark/runtime-store network-definition registry pattern.
            foreach (GameObject template in templates)
            {
                if (template == null) continue;
                var definition = (ShipDefinition)template.GetComponent<Ship>().definition;
                if (category.typeSprite != null) definition.mapIcon = category.typeSprite;
                if (Encyclopedia.Lookup.TryGetValue(definition.jsonKey, out var existing))
                {
                    if (existing != definition) throw new InvalidOperationException("Duplicate platform definition " + definition.jsonKey);
                    continue;
                }
                if (!encyclopedia.ships.Contains(definition)) encyclopedia.ships.Add(definition);
                ((INetworkDefinition)definition).LookupIndex = encyclopedia.IndexLookup.Count;
                encyclopedia.IndexLookup.Add(definition);
                Encyclopedia.Lookup.Add(definition.jsonKey, definition);
            }
        }

        internal static void Disable(GameObject structure)
        {
            foreach (Hangar hangar in structure.GetComponentsInChildren<Hangar>(true)) SelfDisabled.SetValue(hangar, true);
            if (GameAccess.IsServer() && structure.TryGetComponent(out Airbase airbase)) airbase.SetDisabled(true);
        }
    }

    [HarmonyPatch(typeof(InfoPanel_Faction), "SetupList")]
    internal static class SeaFacilityCategoryPatch
    {
        private static void Postfix(InfoPanel_Faction __instance, string typeName)
        {
            if (typeName != "Ships") return;
            string row = "Item_" + SeaFacility.PlatformType;
            foreach (InfoPanel_ItemPrefab entry in __instance.GetComponentsInChildren<InfoPanel_ItemPrefab>(true))
                if (entry.name == row) entry.SetTextIcon(SeaFacility.CategoryLabel,
                    GameAssets.i != null ? GameAssets.i.hangarSprite_helipad : null, fit: true);
        }
    }
}
