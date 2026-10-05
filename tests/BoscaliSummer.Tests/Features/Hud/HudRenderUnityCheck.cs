#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BoscaliSummer.Modules.Hud.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class HudRenderUnityCheck
{
    private static int assertions;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        try
        {
            var cameraObject = new GameObject("Main camera", typeof(Camera), typeof(CameraStateManager));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 60f;
            var view = cameraObject.GetComponent<CameraStateManager>();
            view.enabled = false;
            view.mainCamera = camera;
            view.currentState = view.orbitState;
            CameraStateManager.i = view;

            var aircraftObject = new GameObject("Ownship", typeof(Aircraft), typeof(Rigidbody));
            var aircraft = aircraftObject.GetComponent<Aircraft>();
            aircraft.rb = aircraftObject.GetComponent<Rigidbody>();
            aircraft.rb.useGravity = false;
            aircraft.rb.velocity = new Vector3(10f, 2f, 130f);
            aircraft.cockpit = new GameObject("Cockpit", typeof(Cockpit)).GetComponent<Cockpit>();
            aircraft.cockpit.transform.SetParent(aircraft.transform, false);
            aircraft.transform.position = new Vector3(3f, 8f, 30f);
            view.followingUnit = aircraft;

            var canvas = new GameObject("HUD canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var flight = new GameObject("Flight HUD", typeof(FlightHud)).GetComponent<FlightHud>();
            flight.Initialize(canvas);
            flight.center = Graphic("HUD center", canvas.transform);
            flight.velocityVector = Graphic("Velocity", canvas.transform).gameObject.AddComponent<Image>();
            FlightHud.i = flight;
            var combat = new GameObject("Combat HUD", typeof(CombatHUD)).GetComponent<CombatHUD>();
            combat.enabled = false;
            combat.aircraft = aircraft;
            CombatHUD.i = combat;
            var target = new GameObject("Target", typeof(Unit)).GetComponent<Unit>();
            target.transform.position = new Vector3(12f, 9f, 150f);
            var marker = Graphic("Target icon", canvas.transform).gameObject.AddComponent<HUDUnitMarker>();
            marker.image = marker.gameObject.AddComponent<Image>();
            marker.unit = target;
            combat.Add(marker);
            var ally = new GameObject("Unselected ally", typeof(Unit)).GetComponent<Unit>();
            ally.transform.position = target.transform.position + Vector3.right * 3f;
            var unselectedMarker = Graphic("Unselected ally icon", canvas.transform).gameObject.AddComponent<HUDUnitMarker>();
            unselectedMarker.image = unselectedMarker.gameObject.AddComponent<Image>();
            unselectedMarker.unit = ally;
            combat.Add(unselectedMarker);
            var allyInfo = new GameObject("Hovered ally information", typeof(AllyInfo)).GetComponent<AllyInfo>();
            allyInfo.transform.SetParent(canvas.transform, false);
            var allyText = Text("Ally caption", canvas.transform);
            allyInfo.Configure(allyText, unselectedMarker);
            combat.hitUnit = target;
            combat.hitMarker = Graphic("Hit marker", canvas.transform);
            combat.Jam(.5f);
            var objectiveManager = new GameObject("Objectives", typeof(ObjectiveOverlayManager)).GetComponent<ObjectiveOverlayManager>();
            var objective = new GameObject("Capture Airstrip", typeof(ObjectiveOverlay)).GetComponent<ObjectiveOverlay>();
            objective.Pointer = Graphic("Objective pointer", canvas.transform);
            objective.TextNoOverlap = new TextNoOverlap { Text = Text("Objective label", canvas.transform), NudgeOffset = new Vector2(6f, 9f) };
            var objectivePosition = new GlobalPosition(target.transform.position);
            objectiveManager.Configure(aircraft, objective, objectivePosition);
            combat.Objectives(objectiveManager);
            objective.UpdateOverlay(new MissionPosition.PositionResult { Position = objectivePosition });
            objective.TextNoOverlap.PreviousPosition = objective.TextNoOverlap.TargetPosition + new Vector2(20f, -10f);
            objective.TextNoOverlap.Text.transform.position = objective.TextNoOverlap.PreviousPosition;
            var targetInfo = Text("Target information", canvas.transform);
            var targetText = Text("Target arrow caption", canvas.transform);
            var targetTail = Graphic("Target arrow tail", canvas.transform);
            targetTail.position = new Vector3(40f, 80f, 0f);
            combat.Captions(targetInfo, targetText, targetTail);
            var initialGun = new GameObject("Initial gun state", typeof(HUDBoresightState)).GetComponent<HUDBoresightState>();
            var initialLead = Image("Initial lead cue", canvas.transform);
            initialGun.Configure(Vector3.forward, Image("Initial boresight", canvas.transform), initialLead,
                Image("Initial target cue", canvas.transform), Image("Initial lead line", canvas.transform), objectivePosition);
            combat.Weapon(initialGun, target);
            var airbase = new GameObject("Cached airbase", typeof(Airbase)).GetComponent<Airbase>();
            airbase.center = target.transform;
            var runway = new Airbase.Runway { Start = new GameObject("Cached runway start").transform };
            runway.Start.position = target.transform.position + Vector3.right * 10f;
            var airbaseOverlay = new GameObject("Native airbase overlay", typeof(AirbaseOverlay)).GetComponent<AirbaseOverlay>();
            airbaseOverlay.transform.SetParent(canvas.transform, false);
            var airbaseMarker = Image("Airbase marker", canvas.transform);
            var airbaseLabel = Text("Airbase label", canvas.transform);
            airbaseLabel.text = "Native cached airbase text";
            var glide = Image("Glideslope", canvas.transform);
            var glideAim = Image("Glideslope aim", canvas.transform);
            airbaseOverlay.Border = Image("Runway border", canvas.transform);
            airbaseOverlay.Configure(airbase, new Airbase.Runway.RunwayUsage(runway), airbaseMarker, airbaseLabel, glide, glideAim);
            DynamicMap.i = new GameObject("Minimap", typeof(DynamicMap)).GetComponent<DynamicMap>();
            marker.UpdatePosition(aircraft.NetworkHQ, view.transform.GlobalPosition(), view.transform.forward);
            typeof(CombatHUD).GetMethod("UpdateHitMarkers", Private).Invoke(combat, null);

            bool active = true;
            var projection = new ThirdPersonHudCenter();
            projection.Subscribe(() => active, () => aircraft);
            Canvas.ForceUpdateCanvases();
            marker.Positions = combat.HitRefreshes = 0;
            Vector3 stale = Flat(camera.WorldToScreenPoint(target.transform.position));

            // A final camera turn/FOV change and the same-frame floating-origin shift happen
            // after native Update/LateUpdate projected the icon against the earlier pose.
            view.transform.SetPositionAndRotation(new Vector3(4f, 2f, -2f), Quaternion.Euler(1f, 8f, 0f));
            camera.fieldOfView = 73f;
            HudCoordinates.Origin = new Vector3(-1000f, 0f, 1000f);
            view.transform.position += HudCoordinates.Origin;
            aircraft.transform.position += HudCoordinates.Origin;
            target.transform.position += HudCoordinates.Origin;
            ally.transform.position += HudCoordinates.Origin;
            runway.Start.position += HudCoordinates.Origin;
            var auxiliary = new GameObject("Relief/target/overlay camera", typeof(Camera)).GetComponent<Camera>();
            auxiliary.enabled = false;
            Begin(projection, auxiliary);
            Assert(marker.Positions == 0 && combat.HitRefreshes == 0, "An auxiliary camera cannot consume the final main-view refresh.");
            Vector3 expected = Flat(camera.WorldToScreenPoint(target.transform.position));
            Assert((expected - stale).magnitude > 10f, "The scenario changes the screen-space projection materially.");
            Vector3 batchedMarker = Vector3.zero, batchedHit = Vector3.zero, batchedObjective = Vector3.zero, batchedObjectiveText = Vector3.zero, batchedLead = Vector3.zero, batchedTargetInfo = Vector3.zero, batchedAirbase = Vector3.zero;
            Canvas.WillRenderCanvases snapshot = () =>
            {
                batchedMarker = marker.transform.position; batchedHit = combat.hitMarker.position;
                batchedObjective = objective.Pointer.position; batchedObjectiveText = objective.TextNoOverlap.Text.transform.position;
                batchedLead = initialLead.transform.position; batchedTargetInfo = targetInfo.transform.position;
                batchedAirbase = airbaseMarker.transform.position;
            };
            Canvas.willRenderCanvases += snapshot;
            try { Canvas.ForceUpdateCanvases(); }
            finally { Canvas.willRenderCanvases -= snapshot; }
            // preWillRender must refresh every cue before the UGUI will-render rebuild/batch
            // boundary. A later begin-camera callback can make Transform assertions pass
            // while the canvas preparation already saw last frame's projected positions.
            Near(batchedMarker, expected + new Vector3(.5f, 1f, 0f), "Canvas prepares target geometry using the final camera pose.");
            Near(batchedHit, expected, "Canvas prepares hit geometry using the same final camera pose.");
            Near(batchedLead, expected, "Canvas prepares weapon cue geometry from the cached native aim solution.");
            Near(batchedTargetInfo, batchedMarker, "Selected-target information follows the refreshed marker at the canvas boundary.");
            Near(targetText.transform.position, targetTail.position, "Offscreen target caption follows the final arrow tail.");
            Near(allyText.transform.localPosition, unselectedMarker.image.transform.localPosition, "A visible ally caption follows the final marker without replaying hover selection.");
            Assert(initialGun.SolverUpdates == 0, "Canvas preparation never advances the weapon aim solver.");
            Near(batchedAirbase, expected, "Canvas prepares the airbase marker using native cached base state and final camera.");
            Near(airbaseLabel.transform.position, expected - Vector3.up * 20f, "Airbase label follows its final marker projection.");
            Assert(airbaseLabel.text == "Native cached airbase text", "Airbase projection retains native label content.");
            Near(batchedObjective, expected, "Canvas prepares objective geometry from the cached global mission result.");
            Near(batchedObjectiveText, expected + new Vector3(20f, -35f, 0f), "Objective text follows its camera displacement while retaining the native declutter offset.");
            Near(objective.TextNoOverlap.PreviousPosition, objective.TextNoOverlap.Text.transform.position, "Objective smoothing position follows the same displacement.");
            Near(objective.TextNoOverlap.NudgeOffset, new Vector2(6f, 9f), "Objective reprojection leaves native nudge state unchanged.");
            Assert(objectiveManager.MissionQueries == 0 && objectiveManager.SmoothingUpdates == 0,
                "Objective reprojection does not query missions or advance declutter smoothing.");
            Begin(projection, camera);
            Near(marker.transform.position, expected + new Vector3(.5f, 1f, 0f), "Target marker uses final pose/FOV/origin and preserves native jamming.");
            Near(combat.hitMarker.position, expected, "Hit marker uses the same final projection.");
            Near(marker.View.Position, view.transform.position - HudCoordinates.Origin, "Marker receives final global viewer position.");
            Near(marker.Forward, view.transform.forward, "Marker receives final camera facing.");
            Near(flight.center.position, Flat(camera.WorldToScreenPoint(aircraft.cockpit.transform.position + aircraft.cockpit.transform.forward * 4000f)), "HUD center uses final pose.");
            Near(flight.velocityVector.transform.position, Flat(camera.WorldToScreenPoint(aircraft.cockpit.transform.position + aircraft.rb.velocity * 1000f)), "Velocity vector uses final pose.");
            Begin(projection, camera);
            Assert(marker.Positions == 1 && marker.Distortions == 1 && combat.HitRefreshes == 1, "A repeated main render does not replay combat work or random jamming.");
            Assert(combat.NativeUpdates == 0, "Refresh never replays the native LateUpdate input path.");
            // A script may force an early canvas preparation before the normal native update.
            // That native update can replace prepared coordinates without changing the camera.
            marker.UpdatePosition(aircraft.NetworkHQ, view.transform.GlobalPosition(), view.transform.forward);
            combat.hitMarker.position = stale;
            Canvas.willRenderCanvases += snapshot;
            try { Canvas.ForceUpdateCanvases(); }
            finally { Canvas.willRenderCanvases -= snapshot; }
            Near(batchedMarker, expected + new Vector3(.5f, 1f, 0f), "A later canvas preparation repairs native writes even when its camera stamp is unchanged.");
            Near(batchedHit, expected, "Same-view canvas preparation repairs an overwritten hit marker.");
            Assert(marker.Positions == 3 && marker.Distortions == 1 && combat.HitRefreshes == 2,
                "Canvas preparation can repeat projection without drawing another random jamming sample.");
            Assert(unselectedMarker.image.color.a == .625f, "Repeated projection preserves the native jamming alpha sample after normal unselected-marker colour writes.");
            // A later main render may use a different projection in the same frame. The view
            // stamp must allow it and the fallback must rebuild the canvas, while keeping the
            // original random jamming sample and native solver/input state.
            Matrix4x4 originalProjection = camera.projectionMatrix;
            Matrix4x4 changedProjection = originalProjection;
            changedProjection.m02 += .2f;
            camera.projectionMatrix = changedProjection;
            Canvas.willRenderCanvases += snapshot;
            try { Begin(projection, camera); }
            finally { Canvas.willRenderCanvases -= snapshot; }
            Near(batchedMarker, Flat(camera.WorldToScreenPoint(target.transform.position)) + new Vector3(.5f, 1f, 0f), "A changed projection at main begin rebuilds the canvas before drawing.");
            Assert(marker.Positions == 4 && marker.Distortions == 1 && combat.NativeUpdates == 0,
                "Changed-view fallback reprojects once without rerolling jamming or replaying input.");
            camera.projectionMatrix = originalProjection;
            Begin(projection, camera);
            var replacementFlight = new GameObject("Replacement Flight HUD", typeof(FlightHud)).GetComponent<FlightHud>();
            replacementFlight.Initialize(canvas);
            replacementFlight.center = Graphic("Replacement HUD center", canvas.transform);
            FlightHud.i = replacementFlight;
            Begin(projection, camera);
            Near(replacementFlight.center.position, flight.center.position, "A same-frame native HUD replacement is prepared even when the camera stamp is unchanged.");
            FlightHud.i = flight;
            Begin(projection, camera);
            Aircraft originalAircraft = aircraft;
            aircraft = new GameObject("Replacement ownship", typeof(Aircraft)).GetComponent<Aircraft>();
            aircraft.transform.position = originalAircraft.transform.position + Vector3.right * 200f;
            aircraft.cockpit = new GameObject("Replacement cockpit", typeof(Cockpit)).GetComponent<Cockpit>();
            aircraft.cockpit.transform.SetParent(aircraft.transform, false);
            int previousMarkerUpdates = marker.Positions;
            Begin(projection, camera);
            Near(flight.center.position, Flat(camera.WorldToScreenPoint(aircraft.cockpit.transform.position + aircraft.cockpit.transform.forward * 4000f)),
                "A same-frame ownship replacement is prepared even when its camera stamp is unchanged.");
            Assert(marker.Positions == previousMarkerUpdates, "A native combat HUD for another aircraft is not reprojected.");
            aircraft = originalAircraft;
            Begin(projection, camera);
            TargetCaptions(combat, aircraft, camera, target, ally, targetInfo, targetText, targetTail, unselectedMarker);
            Weapons(combat, aircraft, camera, target, canvas);
            AirbaseCues(airbaseOverlay, aircraft, camera, runway, airbaseMarker, glide, glideAim);
            active = false;
            Vector3 retained = flight.center.position;
            view.transform.rotation = Quaternion.identity;
            Begin(projection, camera);
            Near(flight.center.position, retained, "Closed third-person gate leaves native HUD transforms untouched.");
            projection.Unsubscribe();
            flight.center.position = new Vector3(1f, 2f, 3f);
            Canvas.ForceUpdateCanvases();
            Near(flight.center.position, new Vector3(1f, 2f, 3f), "Unsubscribe releases the canvas callback.");

            // Batch mode deliberately prevents presentation. Test ownership restoration directly
            // by recording the exact objects/state this owner can have changed in a live player.
            var enabler = new ExternalHudEnabler();
            canvas.gameObject.SetActive(true);
            DynamicMap.i.gameObject.SetActive(true);
            Own(enabler, view, canvas.gameObject, DynamicMap.i.gameObject);
            enabler.Release();
            Assert(!canvas.gameObject.activeSelf && !DynamicMap.i.gameObject.activeSelf, "Same-view release restores only objects we activated.");
            canvas.gameObject.SetActive(true);
            DynamicMap.i.gameObject.SetActive(true);
            Own(enabler, view, canvas.gameObject, DynamicMap.i.gameObject);
            view.currentState = view.cockpitState;
            enabler.Release();
            Assert(canvas.gameObject.activeSelf && DynamicMap.i.gameObject.activeSelf, "A native cockpit transition keeps the visibility chosen by EnterState.");
            enabler.Release();
            Assert(canvas.gameObject.activeSelf, "Release is idempotent and cannot hide an unowned native object.");
            view.currentState = view.orbitState;
            Own(enabler, view, canvas.gameObject, DynamicMap.i.gameObject);
            DynamicMap.mapMaximized = true;
            enabler.Release();
            Assert(!canvas.gameObject.activeSelf && DynamicMap.i.gameObject.activeSelf, "Maximized-map visibility remains owned by the native map.");
            DynamicMap.mapMaximized = false;
            Assert(ExternalHudEnabler.IsOwnAircraftExternalView(out var eligible) && eligible == aircraft, "Own live aircraft is eligible in orbit.");
            aircraft.cockpit.detached = true;
            Assert(!ExternalHudEnabler.IsOwnAircraftExternalView(out _), "Detached cockpit closes the external HUD gate.");
            Assert(!ExternalHudEnabler.CanShowExternalHud(false, true) && !ExternalHudEnabler.CanShowExternalHud(true, false), "Disabled setting and foreign view close the presentation gate.");
            File.WriteAllText("result.txt", "PASS: " + assertions + " production HUD render/transition assertions using real Unity camera/canvas and synthetic native data seams; no live game/input or native weapon acceptance.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static Transform Graphic(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }
    private static Image Image(string name, Transform parent) => Graphic(name, parent).gameObject.AddComponent<Image>();
    private static TextMeshProUGUI Text(string name, Transform parent) => Graphic(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
    private static void TargetCaptions(CombatHUD combat, Aircraft aircraft, Camera camera, Unit target, Unit replacement, TextMeshProUGUI info, TextMeshProUGUI arrowText, Transform tail, HUDUnitMarker otherMarker)
    {
        Quaternion rotation = camera.transform.rotation;
        camera.transform.rotation = rotation * Quaternion.Euler(0f, 180f, 0f);
        Canvas.ForceUpdateCanvases();
        Assert(!info.enabled && arrowText.enabled, "Final-camera offscreen selection hides its onscreen caption and shows the native arrow caption.");
        Near(arrowText.transform.position, tail.position, "Offscreen caption uses the final moved arrow tail.");
        camera.transform.rotation = rotation;
        Canvas.ForceUpdateCanvases();
        Assert(info.enabled && !arrowText.enabled, "A target returning to the final view recovers its native onscreen caption immediately.");
        Assert(info.text == target.name, "Re-entering target caption contains the current native target information.");
        aircraft.NetworkHQ.Known = false;
        Canvas.ForceUpdateCanvases();
        Assert(!info.enabled, "Native unknown target position cannot resurrect target information.");
        aircraft.NetworkHQ.Known = true;
        combat.Target(null);
        Canvas.ForceUpdateCanvases();
        Assert(!info.enabled, "No selected target keeps its native information hidden.");
        combat.Target(replacement);
        Canvas.ForceUpdateCanvases();
        Assert(info.enabled && info.text == replacement.name, "A new native target replaces stale caption content during preparation.");
        Near(info.transform.position, otherMarker.image.transform.position, "New-target information follows its refreshed marker.");
        Assert(otherMarker.image.color.a == 1f && combat.NativeUpdates == 0,
            "Native selected-theme colour is restored without replaying the input/LateUpdate path.");
        combat.Target(target);
        Canvas.ForceUpdateCanvases();
    }
    private static void AirbaseCues(AirbaseOverlay overlay, Aircraft aircraft, Camera camera, Airbase.Runway runway, Image marker, Image glide, Image aim)
    {
        Quaternion originalCameraRotation = camera.transform.rotation;
        camera.transform.rotation = originalCameraRotation * Quaternion.Euler(0f, 180f, 0f);
        ThirdPersonAirbaseProjection.Refresh(overlay, aircraft, camera);
        Assert(!marker.enabled, "An airbase behind the final camera is hidden after camera movement.");
        camera.transform.rotation = originalCameraRotation;
        Vector3 originalOwnshipPosition = aircraft.transform.position;
        aircraft.pilots[0].flightInfo.HasTakenOff = false;
        aircraft.radarAlt = .5f;
        aircraft.transform.position = runway.Start.position;
        overlay.Flags(false, true, false);
        ThirdPersonAirbaseProjection.Refresh(overlay, aircraft, camera);
        Near(marker.transform.position, Flat(camera.WorldToScreenPoint(runway.Start.position)), "Taxi marker projects the cached runway start.");
        Assert(!overlay.Reached && !aircraft.pilots[0].flightInfo.HasTakenOff && overlay.RegistrationUpdates == 0,
            "Taxi projection at the runway does not advance reached, takeoff or registration state.");
        overlay.Flags(false, true, true);
        ThirdPersonAirbaseProjection.Refresh(overlay, aircraft, camera);
        Assert(!marker.enabled && overlay.Reached, "Native reached-runway state keeps the taxi marker hidden.");
        aircraft.transform.position = originalOwnshipPosition;
        aircraft.pilots[0].flightInfo.HasTakenOff = true;
        aircraft.radarAlt = 200f;
        aircraft.gearDeployed = true;
        overlay.Flags(true, false, false);
        ThirdPersonAirbaseProjection.Refresh(overlay, aircraft, camera);
        Near(overlay.Border.transform.position, Flat(camera.WorldToScreenPoint(runway.Start.position)), "Landing border uses the native geometry-only draw seam.");
        Near(glide.transform.position, Flat(camera.WorldToScreenPoint(runway.Start.position)), "Glideslope uses the native draw seam with the final camera.");
        Near(aim.transform.position, Flat(camera.WorldToScreenPoint(runway.Start.position + Vector3.up * 10f)), "Glideslope aim follows the final camera.");
        Assert(overlay.BorderRefreshes == 1 && overlay.GlideRefreshes == 1 && glide.enabled && aim.enabled && overlay.RegistrationUpdates == 0,
            "Landing reprojection redraws presentation without replaying registration.");
        aircraft.gearDeployed = false;
        ThirdPersonAirbaseProjection.Refresh(overlay, aircraft, camera);
        Assert(!glide.enabled && !aim.enabled && overlay.GlideRefreshes == 1, "Retracted gear releases native glideslope visibility without calling the geometry draw.");
        overlay.Flags(false, false, false);
    }
    private static void Weapons(CombatHUD combat, Aircraft aircraft, Camera camera, Unit target, Canvas canvas)
    {
        var gun = new GameObject("Gun state", typeof(HUDBoresightState)).GetComponent<HUDBoresightState>();
        var sight = Image("Gun boresight", canvas.transform);
        var lead = Image("Gun lead", canvas.transform);
        var leadTarget = Image("Lead target", canvas.transform);
        var leadLine = Image("Lead line", canvas.transform);
        var aim = new GlobalPosition(target.transform.position - HudCoordinates.Origin + new Vector3(5f, 3f, 0f));
        gun.Configure(Vector3.forward, sight, lead, leadTarget, leadLine, aim);
        combat.Weapon(gun, target);
        ThirdPersonWeaponProjection.Refresh(combat, aircraft, camera);
        Near(sight.transform.position, Flat(camera.WorldToScreenPoint(aircraft.transform.position + aircraft.transform.forward * 3000f)), "Gun boresight uses final camera.");
        Near(lead.transform.position, Flat(camera.WorldToScreenPoint(aim.ToLocalPosition())), "Gun lead reprojects the cached native aim solution.");
        Near(leadTarget.transform.position, Flat(camera.WorldToScreenPoint(target.transform.position)), "Gun lead target uses final camera.");
        PlayerSettings.lagPip = true;
        ThirdPersonWeaponProjection.Refresh(combat, aircraft, camera);
        Near(lead.transform.position, sight.transform.position - Flat(camera.WorldToScreenPoint(aim.ToLocalPosition())) + leadTarget.transform.position, "Native lag-pip geometry is retained.");
        Assert(gun.SolverUpdates == 0 && camera.fieldOfView == 73f, "Gun reprojection does not replay aim solvers or alter FOV.");
        PlayerSettings.lagPip = false;

        var turret = new GameObject("Turret state", typeof(HUDTurretState)).GetComponent<HUDTurretState>();
        var crosshair = Graphic("Turret crosshair", canvas.transform).gameObject.AddComponent<HUDTurretCrosshair>();
        crosshair.WorldPoint = target.transform.position;
        turret.Configure(crosshair);
        combat.Weapon(turret, target);
        ThirdPersonWeaponProjection.Refresh(combat, aircraft, camera);
        Near(crosshair.transform.position, Flat(camera.WorldToScreenPoint(crosshair.WorldPoint)), "Turret native projection receives final camera.");
        Assert(crosshair.Refreshes == 1 && turret.SolverUpdates == 0, "Turret refresh invokes only the presentation seam.");

        var bomb = new GameObject("Bomb state", typeof(HUDBombingState)).GetComponent<HUDBombingState>();
        var pipper = Image("CCIP pipper", canvas.transform);
        var line = Image("CCIP line", canvas.transform);
        var alignment = Image("CCRP alignment", canvas.transform);
        alignment.gameObject.SetActive(false);
        var fallTime = Text("CCIP time of flight", canvas.transform);
        var impact = target.transform.position - HudCoordinates.Origin;
        var average = new GlobalPosition(impact);
        bomb.Configure(impact, average, pipper, line, alignment, fallTime);
        Datum.origin = new GameObject("Datum origin").transform;
        Datum.origin.position = HudCoordinates.Origin;
        combat.Weapon(bomb, target);
        ThirdPersonWeaponProjection.Refresh(combat, aircraft, camera);
        Near(pipper.transform.position, Flat(camera.WorldToScreenPoint(target.transform.position)), "CCIP uses cached global impact with current floating origin.");
        Near(bomb.CachedImpact, impact, "CCIP smoothing cache is unchanged.");
        Assert(bomb.SolverUpdates == 0, "Bomb projection does not advance native trajectory/smoothing.");
        Quaternion cameraRotation = camera.transform.rotation;
        camera.transform.rotation = cameraRotation * Quaternion.Euler(0f, 180f, 0f);
        ThirdPersonWeaponProjection.Refresh(combat, aircraft, camera);
        Assert(!pipper.enabled && !line.enabled && fallTime.enabled,
            "Camera-facing visibility can hide a valid cached CCIP cue without invalidating its native trajectory.");
        camera.transform.rotation = cameraRotation;
        ThirdPersonWeaponProjection.Refresh(combat, aircraft, camera);
        Assert(pipper.enabled, "A valid CCIP cue hidden by the earlier camera is shown again when the final camera faces its impact.");
        Near(pipper.transform.position, Flat(camera.WorldToScreenPoint(target.transform.position)), "A re-entering CCIP cue uses its cached impact and final camera.");
        fallTime.enabled = false;
        pipper.enabled = line.enabled = false;
        ThirdPersonWeaponProjection.Refresh(combat, aircraft, camera);
        Assert(!pipper.enabled && !line.enabled && !fallTime.enabled,
            "Native invalid/disabled CCIP trajectory is never resurrected by final-camera preparation.");
        Near(bomb.CachedImpact, impact, "CCIP visibility re-entry leaves cached impact smoothing unchanged.");
        Assert(bomb.SolverUpdates == 0, "CCIP visibility re-entry never advances the trajectory solver.");
        alignment.gameObject.SetActive(true);
        ThirdPersonWeaponProjection.Refresh(combat, aircraft, camera);
        Vector3 delta = average - aircraft.GlobalPosition();
        Vector3 horizontal = delta; horizontal.y = 0f;
        float height = -delta.y + Vector3.Project(horizontal, aircraft.transform.forward).y;
        Near(alignment.transform.position, Flat(camera.WorldToScreenPoint(average.ToLocalPosition() + Vector3.up * height)), "CCRP reads cached target with final camera.");
    }
    private static Vector3 Flat(Vector3 point) => new Vector3(point.x, point.y, 0f);
    private static void Begin(ThirdPersonHudCenter projection, Camera camera) => typeof(ThirdPersonHudCenter)
        .GetMethod("OnBeginCameraRendering", Private).Invoke(projection, new object[] { default(ScriptableRenderContext), camera });
    private static void Own(ExternalHudEnabler owner, CameraStateManager camera, GameObject hud, GameObject map)
    {
        Set(owner, "forcedHud", hud);
        Set(owner, "forcedMap", map);
        Set(owner, "cameraOwner", camera);
        Set(owner, "cameraState", camera.currentState);
        Set(owner, "followingUnit", camera.followingUnit);
    }
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    private static void Near(Vector3 actual, Vector3 expected, string message) => Assert((actual - expected).magnitude < .01f, message + " actual=" + actual + " expected=" + expected);
    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
