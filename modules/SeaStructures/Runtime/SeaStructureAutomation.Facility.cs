#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Modules.SeaStructures.Runtime;
using HarmonyLib;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.SeaStructures
{
    public static partial class SeaStructureAutomation
    {
        private static AircraftSelectionMenu facilityMenu;
        private static bool facilityFlyPressed;

        private static Dictionary<string, object> FacilityAction(string action, SeaStructure target)
        {
            if (target == null || !target.TryGetComponent(out Airbase airbase) ||
                !GameManager.GetLocalPlayer<Player>(out Player player) || player?.HQ == null || player.HQ != airbase.CurrentHQ)
                return Failure(Source, action, "requires a local player and a friendly platform airbase");
            if (action == "facility-menu")
            {
                if (facilityMenu != null) AccessTools.Method(typeof(AircraftSelectionMenu), "HideSelection").Invoke(facilityMenu, null);
                Aircraft old = player.Aircraft;
                if (old != null)
                {
                    player.RemoveAircraft(old);
                    NetworkSceneSingleton<Spawner>.i.ServerObjectManager.Destroy(old.gameObject);
                }
                GameplayUI ui = SceneSingleton<GameplayUI>.i;
                if (ui == null) return Failure(Source, action, "native gameplay UI is not ready");
                ui.SelectAirbase(airbase);
                var select = (Button)AccessTools.Field(typeof(GameplayUI), "selectAircraftButton").GetValue(ui);
                if (select == null || !select.interactable) return Failure(Source, action, "native select-aircraft button is unavailable");
                select.onClick.Invoke();
                facilityMenu = UnityEngine.Object.FindObjectOfType<AircraftSelectionMenu>();
                AircraftDefinition selected = facilityMenu != null ? facilityMenu.GetSelectedType() : null;
                if (selected == null) return Failure(Source, action, "native menu did not create a compatible aircraft preview");
                // Fixture ownership only. The actual menu button still uses RequestSpawnAtAirbase and the normal host/HQ/owned-airframe checks.
                if (!player.OwnsAirframe(selected, true)) player.CreditAirframe(selected, 1, false);
                facilityFlyPressed = false;
            }
            else if (action == "facility-fly")
            {
                Button fly = facilityMenu != null ? (Button)AccessTools.Field(typeof(AircraftSelectionMenu), "flyButton").GetValue(facilityMenu) : null;
                if (fly == null || !fly.gameObject.activeInHierarchy || !fly.interactable)
                    return Failure(Source, action, "the rendered native FLY button is unavailable");
                fly.onClick.Invoke();
                facilityFlyPressed = true;
            }
            return Status(action, target);
        }

        private static void ReadFacility(SeaStructure target, Dictionary<string, object> result)
        {
            var definition = target.Ship.definition as ShipDefinition;
            Airbase airbase = target.GetComponent<Airbase>();
            Hangar[] hangars = target.GetComponentsInChildren<Hangar>(true);
            bool registered = definition != null && Encyclopedia.Lookup.TryGetValue(definition.jsonKey, out var found) && found == definition;
            result["platformIdentity"] = definition != null && definition.jsonKey == target.Layout.Key &&
                definition.shipType == SeaFacility.PlatformType && definition.bogeyName == "PLATFORM" && definition.unitName == target.Layout.UnitName;
            result["definitionKey"] = definition != null ? definition.jsonKey : "";
            result["definitionRegistered"] = registered;
            result["definitionIndex"] = definition != null ? ((INetworkDefinition)definition).LookupIndex ?? -1 : -1;
            result["shipType"] = definition != null ? definition.shipType.ToString() : "";
            result["categoryLabel"] = SeaFacility.CategoryLabel;
            string categoryRow = "Item_" + SeaFacility.PlatformType;
            result["nativeCategoryHumanLabel"] = Resources.FindObjectsOfTypeAll<InfoPanel_ItemPrefab>()
                .Where(row => row.name == categoryRow).Any(row => row.GetComponentsInChildren<TMP_Text>(true)
                    .Any(text => text.text.Contains(SeaFacility.CategoryLabel, StringComparison.Ordinal)));
            result["bogeyName"] = definition != null ? definition.bogeyName : "";
            result["nativeAirbase"] = airbase != null && airbase.AttachedAirbase && airbase.CurrentHQ == target.Ship.NetworkHQ;
            result["nativeHangars"] = hangars.Length;
            result["nativeVerticalPoints"] = airbase != null ? airbase.verticalLandingPoints.Length : 0;
            result["airbaseName"] = airbase?.SavedAirbase?.DisplayName ?? "";
            result["airbaseDisabled"] = airbase == null || airbase.disabled;
            List<AircraftDefinition> roster = airbase != null ? airbase.GetAvailableAircraft() : new List<AircraftDefinition>();
            result["compatibleAircraft"] = roster.Count;
            result["compatibleOnly"] = roster.Count > 0 && roster.All(d => SeaFacility.Fits(target.Layout.Kind, d));
            result["fixedWingRejected"] = Encyclopedia.i.aircraft.Where(d => d != null && d.aircraftParameters != null && !d.aircraftParameters.verticalLanding)
                .All(d => !roster.Contains(d) && !airbase.CanSpawnAircraft(d));
            result["aircraftRoster"] = string.Join(",", roster.Select(d => d.jsonKey));
            result["spawnAvailable"] = roster.Any(d => airbase.CanSpawnAircraft(d));
            result["noFixedWingRunway"] = airbase != null && airbase.runways.Length == 0;
            result["flyPressed"] = facilityFlyPressed;
            result["menuVisible"] = facilityMenu != null && facilityMenu.gameObject.activeInHierarchy;
            string menuText = facilityMenu != null ? string.Join("|", facilityMenu.GetComponentsInChildren<TMP_Text>(false).Select(t => t.GetParsedText())) : "";
            result["menuHasPlatformName"] = menuText.Contains(target.Layout.UnitName, StringComparison.Ordinal);
            result["menuHasNoAircraft"] = menuText.Contains("No aircraft available", StringComparison.OrdinalIgnoreCase);
            result["menuSelectedAircraft"] = facilityMenu != null ? facilityMenu.GetSelectedType()?.jsonKey ?? "" : "";
            Transform previewCamera = airbase != null ? airbase.fixedCameraTransform : null;
            Transform deckPoint = airbase != null && airbase.verticalLandingPoints.Length == 1 ? airbase.verticalLandingPoints[0].point : null;
            result["previewCameraAboveDeck"] = previewCamera != null && deckPoint != null && previewCamera.position.y > deckPoint.position.y + 8f;
            result["previewCameraRayClear"] = previewCamera != null && deckPoint != null && !Physics.Linecast(previewCamera.position,
                deckPoint.position + Vector3.up * 3f, (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask, QueryTriggerInteraction.Ignore);
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            result["previewCameraActive"] = facilityMenu != null && camera != null && CameraStateManager.cameraMode == CameraMode.selection &&
                previewCamera != null && (camera.transform.position - previewCamera.position).sqrMagnitude < .01f;
            Aircraft previewAircraft = facilityMenu != null ? (Aircraft)AccessTools.Field(typeof(AircraftSelectionMenu), "previewAircraft").GetValue(facilityMenu) : null;
            Vector3 previewAt = previewAircraft != null && camera?.mainCamera != null ? camera.mainCamera.WorldToViewportPoint(previewAircraft.transform.position) : Vector3.zero;
            result["previewAircraftInFrame"] = previewAircraft != null && previewAt.z > 0f && previewAt.x > .1f && previewAt.x < .9f && previewAt.y > .1f && previewAt.y < .9f;
            ShipDefinition donor = Encyclopedia.i.ships.FirstOrDefault(d => d.jsonKey == SeaTemplates.Donor(target.Layout.Kind));
            result["nativeNetworkBehaviourCount"] = target.GetComponentsInChildren<Mirage.NetworkBehaviour>(true).Length;
            result["donorNetworkBehaviourCount"] = donor != null ? donor.unitPrefab.GetComponentsInChildren<Mirage.NetworkBehaviour>(true).Length : -1;
            result["sameNativeNetworkLayout"] = donor != null && target.GetComponentsInChildren<Mirage.NetworkBehaviour>(true).Select(b => b.GetType().FullName).OrderBy(n => n)
                .SequenceEqual(donor.unitPrefab.GetComponentsInChildren<Mirage.NetworkBehaviour>(true).Select(b => b.GetType().FullName).OrderBy(n => n));

            GameManager.GetLocalPlayer<Player>(out Player player);
            Aircraft aircraft = player?.Aircraft;
            Hangar origin = aircraft != null ? aircraft.spawningHangar.GetAs<Hangar>() : null;
            bool spawned = aircraft != null && origin != null && origin.attachedUnit == target.Ship;
            result["playerSpawnedHere"] = spawned;
            result["playerAircraft"] = aircraft != null ? aircraft.definition.jsonKey : "";
            result["occupiedBlocksAnotherSpawn"] = spawned && !airbase.CanSpawnAircraft((AircraftDefinition)aircraft.definition);
            result["playerAircraftHealthy"] = spawned && !aircraft.disabled;
            result["playerSpawnHeightError"] = -1f;
            result["playerSpawnPlanarError"] = -1f;
            result["playerOnDeck"] = false;
            result["nativeLandingAccepted"] = false;
            if (spawned && airbase.verticalLandingPoints.Length == 1)
            {
                Transform point = airbase.verticalLandingPoints[0].point;
                Vector3 wanted = point.position + point.up * aircraft.definition.spawnOffset.y + point.forward * aircraft.definition.spawnOffset.z;
                Vector3 error = aircraft.transform.position - wanted;
                result["playerSpawnHeightError"] = Mathf.Abs(error.y);
                result["playerSpawnPlanarError"] = new Vector2(error.x, error.z).magnitude;
                result["playerOnDeck"] = Mathf.Abs(error.y) < 1f && new Vector2(error.x, error.z).magnitude < 3f && aircraft.radarAlt < 2f;
                var query = new RunwayQuery { RunwayType = RunwayQueryType.Vertical, MinSize = aircraft.maxRadius };
                result["nativeLandingAccepted"] = airbase.IsSuitable(query) && airbase.TryRequestVerticalLanding(aircraft, query, out _);
            }
        }
    }
}
#endif
