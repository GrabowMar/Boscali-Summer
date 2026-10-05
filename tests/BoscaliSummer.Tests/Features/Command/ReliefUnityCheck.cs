#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BepInEx;
using NOAvionics;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class ReliefUnityCheck
{
    public static void Run()
    {
        try
        {
            // Kit v2 text needs the TMP essentials (default font + shaders); import them once and re-enter.
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            if (!AvBundle.Available || !AvIcons.Available) throw new Exception("Production fonts and icons must load.");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            if (Environment.GetEnvironmentVariable("BOSCALI_RELIEF_URP") == "1")
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                var pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.msaaSampleCount = 1;
                pipeline.renderScale = 1f;
                GraphicsSettings.renderPipelineAsset = pipeline;
                QualitySettings.renderPipeline = pipeline;
            }
            const int side = 900;
            string assetName = Environment.GetEnvironmentVariable("BOSCALI_ASSET_NAME") ?? "terrain2_map";
            float mapWidth = float.Parse(Environment.GetEnvironmentVariable("BOSCALI_MAP_WIDTH") ?? "81920",
                System.Globalization.CultureInfo.InvariantCulture);
            var screen = new GameObject("ScreenCamera", typeof(Camera)).GetComponent<Camera>();
            screen.orthographic = true;
            screen.orthographicSize = side * 0.5f;
            screen.transform.position = new Vector3(0f, 0f, -10f);
            screen.clearFlags = CameraClearFlags.SolidColor;
            screen.backgroundColor = new Color(0.015f, 0.035f, 0.055f);
            screen.cullingMask = 1;
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = screen;
            ((RectTransform)canvas.transform).sizeDelta = Vector2.one * side;
            var image = new GameObject("MapImage", typeof(RectTransform), typeof(Image));
            var background = new GameObject("MapBackground", typeof(RectTransform), typeof(Image));
            var nativeScale = new GameObject("NativeUiScale", typeof(RectTransform));
            nativeScale.transform.SetParent(canvas.transform, false);
            nativeScale.transform.localScale = Vector3.one * .7f;
            background.transform.SetParent(nativeScale.transform, false);
            ((RectTransform)background.transform).sizeDelta = new Vector2(1250f, side);
            background.AddComponent<RectMask2D>();
            var scaleCenter = new GameObject("MapScaleCenter", typeof(RectTransform));
            scaleCenter.transform.SetParent(background.transform, false);
            var scaleProxy = new GameObject("MapScaleProxy", typeof(RectTransform));
            scaleProxy.transform.SetParent(background.transform, false);
            image.transform.SetParent(background.transform, false);
            ((RectTransform)image.transform).sizeDelta = new Vector2(side * mapWidth / 81920f, side);
            image.GetComponent<Image>().sprite = MakeSprite();
            var control = new GameObject("ComSectorGridOverlay", typeof(RectTransform), typeof(RawImage))
                .GetComponent<RawImage>();
            control.transform.SetParent(image.transform, false);
            control.texture = MakeControl();
            var threat = new GameObject("BoscaliThreatHeat", typeof(RectTransform), typeof(RawImage))
                .GetComponent<RawImage>();
            threat.transform.SetParent(image.transform, false);
            threat.texture = MakeThreat();
            var map = new GameObject("DynamicMap", typeof(DynamicMap)).GetComponent<DynamicMap>();
            map.mapImage = image;
            map.iconLayer = image;
            map.viewIndicator = new GameObject("CameraIndicator", typeof(RectTransform));
            map.viewIndicator.transform.SetParent(image.transform, false);
            SceneSingleton<CameraStateManager>.i = new GameObject("ViewPose", typeof(CameraStateManager))
                .GetComponent<CameraStateManager>();
            SceneSingleton<CameraStateManager>.i.transform.position = new Vector3(5000f, 1500f, -9000f);
            map.mapBackground = background.GetComponent<Image>();
            map.mapScaleCenter = scaleCenter.transform;
            map.mapScaleProxy = scaleProxy.transform;
            map.maximizedMapCanvas = canvas;
            map.gridLabels = new GameObject("GridLabels", typeof(GridLabels)).GetComponent<GridLabels>();
            var flatLabels = new GameObject("MajorParent");
            flatLabels.transform.SetParent(map.gridLabels.transform, false);
            SceneSingleton<DynamicMap>.i = map;
            new GameObject("Settings", typeof(MapSettings)).GetComponent<MapSettings>().MapSize =
                new Vector2(mapWidth, 81920f);
            string bakedPath = Environment.GetEnvironmentVariable("BOSCALI_HEIGHT_PREVIEW");
            if (string.IsNullOrEmpty(bakedPath) || !File.Exists(bakedPath))
                throw new Exception("BOSCALI_HEIGHT_PREVIEW must point to the baked game heightfield.");
            string stylePath = Environment.GetEnvironmentVariable("BOSCALI_STYLE_PREVIEW");
            if (string.IsNullOrEmpty(stylePath) || !File.Exists(stylePath))
                throw new Exception("BOSCALI_STYLE_PREVIEW must point to the baked game imagery.");
            Paths.ConfigPath = Directory.GetCurrentDirectory();
            string maps = Path.Combine(Paths.ConfigPath, "BoscaliSummer", "Maps");
            Directory.CreateDirectory(maps);
            File.Copy(bakedPath, Path.Combine(maps, assetName + ".bmap"), true);
            File.Copy(stylePath, Path.Combine(maps, assetName + "_intel.png"), true);
            if (assetName == "terrain_naval_map")
                AssertNavalChartAlignment(image.GetComponent<Image>().sprite.texture, stylePath);
            Canvas.ForceUpdateCanvases();
            UnitMapIcon openingTrack = Track(image.transform, map, -12000f, 7000f);
            MfdTerrainRelief.Tick();
            if (!MfdTerrainRelief.IsDrawing) throw new Exception("Terrain model did not mount.");
            if (!MfdTerrainRelief.TryProject(-12000f, 7000f, ((RectTransform)image.transform).rect,
                    out Vector2 openingPoint) ||
                Vector2.Distance(openingTrack.iconImage.transform.localPosition, openingPoint) > .1f)
                throw new Exception("Opening relief left an existing native glyph flat until its next update batch.");
            Transform orbit = map.mapBackground.transform.Find("NOAvionics.MapOrbit");
            if (orbit == null || orbit.GetComponentsInChildren<AvControl>(true).Length != 8 ||
                orbit.Find("Footer") == null || orbit.GetComponent<AvHelpScope>() == null)
                throw new Exception("Orbit controls (8 kit buttons and the cursor line) did not mount on the map viewport.");
            AssertViewportTerrain(map, "initial open");
            float fittedZoom = map.GetZoomLevel();
            map.SetZoomLevel(1f);
            MfdTerrainRelief.Tick();
            AssertViewportTerrain(map, "minimum zoom");
            AssertWholeMap(map);
            if (Mathf.Abs(map.GetZoomLevel() - 1f) > .01f)
                throw new Exception("Viewport fit overrode the player's zoom choice.");
            map.SetZoomLevel(fittedZoom);
            MfdTerrainRelief.Tick();
            AssertViewportTerrain(map, "zoom restored");
            if (flatLabels.activeSelf) throw new Exception("Flat coordinates remained on oblique terrain.");
            if (control.enabled || threat.enabled)
                throw new Exception("A flat tactical field remained over the model.");
            if (!MfdTerrainRelief.TryProject(0f, 0f, ((RectTransform)image.transform).rect, out Vector2 center))
                throw new Exception("Terrain projection unavailable.");
            if (!MfdTerrainRelief.TryUnproject(center, ((RectTransform)image.transform).rect,
                    out GlobalPosition click) || Mathf.Abs(click.x) > 500f || Mathf.Abs(click.z) > 500f)
                throw new Exception("Rendered ground did not invert to the clicked world position.");
            MfdTerrainRelief.Rotate(45f, 0f);
            MfdTerrainRelief.Tick();
            AssertViewportTerrain(map, "45 degree orbit");
            Canvas.ForceUpdateCanvases();
            var rotatedTarget = new RenderTexture(1250, side, 24);
            screen.targetTexture = rotatedTarget;
            RenderCamera(screen);
            RenderTexture.active = rotatedTarget;
            var rotatedPreview = new Texture2D(1250, side, TextureFormat.RGB24, false);
            rotatedPreview.ReadPixels(new Rect(0, 0, 1250, side), 0, 0);
            rotatedPreview.Apply();
            File.WriteAllBytes("relief-rotated-preview.png", rotatedPreview.EncodeToPNG());
            screen.targetTexture = null;
            RenderTexture.active = null;
            foreach (float testYaw in new[] { 0f, 45f, 90f, 135f })
            foreach (float testPitch in new[] { 25f, 40f, 70f })
            {
                MfdTerrainRelief.Rotate(testYaw - MfdTerrainRelief.Yaw,
                    testPitch - MfdTerrainRelief.Pitch);
                MfdTerrainRelief.Tick();
                AssertViewportTerrain(map, $"yaw {testYaw}, pitch {testPitch}");
                AssertWholeMap(map);
            }
            MfdTerrainRelief.Rig.ZoomAt(2f, .5f, .5f, 0f);
            DrawTerrain();
            Vector2 beforePan = ProjectInViewport(map, 0f, 0f);
            image.transform.localPosition += new Vector3(90f, -60f, 0f);
            Canvas.ForceUpdateCanvases();
            MfdTerrainRelief.Tick();
            AssertViewportTerrain(map, "native pan");
            if (Vector2.Distance(beforePan, ProjectInViewport(map, 0f, 0f)) > 1f)
                throw new Exception("Native pan moved the relief view it no longer owns.");
            image.transform.localPosition -= new Vector3(90f, -60f, 0f);
            if (!MfdTerrainRelief.TryGround(.4f, .6f, out Vector3 grabbed))
                throw new Exception("No terrain under the grab point.");
            MfdTerrainRelief.Rig.Grab(grabbed.x, grabbed.y, grabbed.z, .6f, .4f);
            DrawTerrain();
            AssertViewportTerrain(map, "ground grab");
            if (!MfdTerrainRelief.TryGround(.6f, .4f, out Vector3 held) ||
                Vector3.Distance(held, grabbed) > 1f)
                throw new Exception("Grabbed terrain did not stay under the cursor.");
            MfdTerrainRelief.Rig.Reset();
            MfdTerrainRelief.ResetOrbit();
            MfdTerrainRelief.Tick();
            UnitMapIcon cachedTrack = Track(image.transform, map, -21000f, 13000f);
            Vector3 oldTrackPosition = cachedTrack.iconImage.transform.localPosition;
            int originalRevision = MfdTerrainRelief.ViewRevision;
            MfdTerrainRelief.Rotate(35f, 15f);
            if (Mathf.Abs(MfdTerrainRelief.Yaw - 35f) > .01f ||
                Mathf.Abs(MfdTerrainRelief.Pitch - 55f) > .01f)
                throw new Exception("Orbit did not change the intended view.");
            MfdTerrainRelief.Tick();
            if (MfdTerrainRelief.ViewRevision <= originalRevision)
                throw new Exception("Rendered orbit did not change the projection revision.");
            Rect mapRect = ((RectTransform)image.transform).rect;
            if (!MfdTerrainRelief.TryProject(-21000f, 13000f, mapRect, out Vector2 cachedPoint) ||
                Vector3.Distance(cachedTrack.iconImage.transform.localPosition,
                    new Vector3(cachedPoint.x, cachedPoint.y)) > 1f ||
                Vector3.Distance(oldTrackPosition, cachedTrack.iconImage.transform.localPosition) < 5f)
                throw new Exception("Cached contacts did not follow the orbit before native refresh.");
            if (!MfdTerrainRelief.TryProject(18000f, 12000f, mapRect, out Vector2 angled) ||
                !MfdTerrainRelief.TryUnproject(angled, mapRect, out GlobalPosition angledHit) ||
                Mathf.Abs(angledHit.x - 18000f) > 500f ||
                Mathf.Abs(angledHit.z - 12000f) > 500f)
                throw new Exception("Rotated view click missed its world position.");
            UnitMapIcon headingTrack = Track(image.transform, map, -12000f, 9000f);
            headingTrack.unit = new TestUnit { definition = new TestDefinition { mapOrient = true } };
            headingTrack.iconImage.transform.localPosition = new Vector3(-12000f * map.mapDisplayFactor,
                9000f * map.mapDisplayFactor);
            headingTrack.iconImage.transform.eulerAngles = new Vector3(0f, 0f, 30f);
            MfdTerrainRelief.ProjectIcon(headingTrack, map.mapDisplayFactor);
            float projectedHeading = headingTrack.iconImage.transform.eulerAngles.z;
            headingTrack.iconImage.transform.localPosition = new Vector3(-12000f * map.mapDisplayFactor,
                9000f * map.mapDisplayFactor);
            MfdTerrainRelief.ProjectIcon(headingTrack, map.mapDisplayFactor);
            if (Mathf.Abs(Mathf.DeltaAngle(projectedHeading,
                    headingTrack.iconImage.transform.eulerAngles.z)) > .1f)
                throw new Exception("A stale native heading drifted after reprojection.");
            MfdTerrainRelief.ResetOrbit();
            MfdTerrainRelief.Tick();
            if (!MfdTerrainRelief.TryProject(0f, 0f, ((RectTransform)image.transform).rect,
                    out center))
                throw new Exception("Terrain center projection unavailable after orbit reset.");
            var icon = new GameObject("Unit", typeof(UnitMapIcon)).GetComponent<UnitMapIcon>();
            icon.transform.SetParent(image.transform, false);
            icon.iconImage = new GameObject("Glyph", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.iconImage.transform.SetParent(icon.transform, false);
            MfdTerrainRelief.ProjectIcon(icon, 1f);
            if (Vector2.Distance(icon.iconImage.transform.localPosition, center) > 1f)
                throw new Exception("Marker and terrain use different projections.");
            FieldInfo nativeTrackPosition = typeof(MapIcon).GetField("globalPosition",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (nativeTrackPosition == null)
                throw new Exception("Native faction-known altitude is unavailable.");
            icon.unit = new Aircraft();
            nativeTrackPosition.SetValue(icon, new Vector3(0f, 5000f, 0f));
            icon.iconImage.transform.localPosition = Vector3.zero;
            MfdTerrainRelief.ProjectIcon(icon, 1f);
            if (Vector2.Distance(icon.iconImage.transform.localPosition, center) < 5f ||
                icon.transform.Find("NOAvionics.MapStem") == null)
                throw new Exception("Aircraft altitude did not raise its map symbol above the terrain.");
            icon.unit = new Missile();
            nativeTrackPosition.SetValue(icon, new Vector3(0f, 3000f, 0f));
            icon.iconImage.transform.localPosition = Vector3.zero;
            MfdTerrainRelief.ProjectIcon(icon, 1f);
            if (Vector2.Distance(icon.iconImage.transform.localPosition, center) < 5f)
                throw new Exception("Missile altitude did not raise its map symbol above the terrain.");
            var objective = new GameObject("Objective", typeof(RectTransform), typeof(ObjectiveMarker))
                .GetComponent<ObjectiveMarker>();
            objective.transform.SetParent(image.transform, false);
            objective.transform.localPosition = new Vector3(18000f * map.mapDisplayFactor,
                12000f * map.mapDisplayFactor);
            MfdTerrainRelief.ProjectMarker(objective.transform, map.mapDisplayFactor);
            if (!MfdTerrainRelief.TryProject(18000f, 12000f,
                    ((RectTransform)image.transform).rect, out Vector2 objectivePoint) ||
                Vector2.Distance(objective.transform.localPosition, objectivePoint) > 1f)
                throw new Exception("Native objective marker missed projected terrain.");
            var info = new GameObject("SelectedInfo", typeof(RectTransform), typeof(TargetMarker))
                .GetComponent<TargetMarker>();
            info.Icon = icon;
            MfdTerrainRelief.FollowIcon(info);
            if (Vector3.Distance(info.transform.position, icon.iconImage.transform.position) > .01f)
                throw new Exception("Selected-unit information did not follow its icon.");
            CheckRenderedPoseSynchronization(map, cachedTrack, objective, info);
            CheckLayoutBeforeSnapshot(map);
            UnitMapIcon friendly = Track(image.transform, map, -11000f, 4000f);
            UnitMapIcon hostile = Track(image.transform, map, 9000f, 4000f);
            friendly.unit = new TestUnit { definition = new TestDefinition(),
                NetworkHQ = new FactionHQ { Friendly = true } };
            hostile.unit = new TestUnit { definition = new TestDefinition(),
                NetworkHQ = new FactionHQ { Friendly = false } };
            UnitMapIcon playerTrack = Track(image.transform, map, 0f, 4000f);
            GameManager.LocalAircraft = new Aircraft { definition = new TestDefinition(),
                NetworkHQ = new FactionHQ { Friendly = true } };
            playerTrack.unit = GameManager.LocalAircraft;
            Vector2 friendlyScreen = RectTransformUtility.WorldToScreenPoint(screen,
                friendly.iconImage.transform.position);
            Vector2 hostileScreen = RectTransformUtility.WorldToScreenPoint(screen,
                hostile.iconImage.transform.position);
            Rect selection = Rect.MinMaxRect(Mathf.Min(friendlyScreen.x, hostileScreen.x) - 12f,
                Mathf.Min(friendlyScreen.y, hostileScreen.y) - 12f,
                Mathf.Max(friendlyScreen.x, hostileScreen.x) + 12f,
                Mathf.Max(friendlyScreen.y, hostileScreen.y) + 12f);
            typeof(MfdMapInteractions).GetMethod("SelectBox", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { map, selection, false });
            // The current native selection path includes observed contacts on either side.
            if (!map.selectedIcons.Contains(friendly) || !map.selectedIcons.Contains(hostile) ||
                map.selectedIcons.Contains(playerTrack))
                throw new Exception("Box selection missed an observed contact or included the player aircraft.");
            GameManager.LocalAircraft = null;
            typeof(MfdMapInteractions).GetMethod("ShowMenu", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { map, new GlobalPosition(0f, 0f, 0f) });
            if (canvas.transform.Find("NOAvionics.MapContext") == null)
                throw new Exception("Map context actions did not mount above the map.");
            CaptureContext(canvas, screen, side, friendlyScreen, hostileScreen);
            MfdMapInteractions.Restore();
            if (typeof(MfdMapInteractions).GetField("menu", BindingFlags.NonPublic | BindingFlags.Static)
                    .GetValue(null) != null)
                throw new Exception("Map context actions survived map cleanup.");
            map.selectedIcons.Clear();
            UnitMapIcon firstTrack = Track(image.transform, map, 17000f, 16000f);
            UnitMapIcon secondTrack = Track(image.transform, map, 17000f, 16000f);
            Track(image.transform, map, 17000f, 16000f);
            Track(image.transform, map, 17000f, 16000f);
            Track(image.transform, map, 17000f, 16000f);
            UnitMapIcon selectedTrack = Track(image.transform, map, 17000f, 16000f);
            map.selectedIcons.Add(selectedTrack);
            MfdTerrainRelief.Tick();
            if (!firstTrack.iconImage.enabled || secondTrack.iconImage.enabled ||
                !selectedTrack.iconImage.enabled ||
                background.transform.Find("NOAvionics.ContactCount") == null)
                throw new Exception("Dense contacts did not collapse to one selectable glyph and a count.");
            Airbase(image.transform, -18000f, 17000f);
            CheckSymbology(image.transform, map);
            Marker(image.transform, ((RectTransform)image.transform).rect, 22000f, 14000f,
                new Color(0.15f, 0.67f, 0.98f), 15f);
            Marker(image.transform, ((RectTransform)image.transform).rect, 15000f, -11000f,
                new Color(0.98f, 0.28f, 0.24f), 12f);
            Marker(image.transform, ((RectTransform)image.transform).rect, -24000f, -15000f,
                new Color(0.98f, 0.28f, 0.24f), 12f);
            Front(image.transform, ((RectTransform)image.transform).rect);
            Canvas.ForceUpdateCanvases();
            var target = new RenderTexture(side, side, 24);
            screen.targetTexture = target;
            RenderCamera(screen);
            RenderTexture.active = target;
            var output = new Texture2D(side, side, TextureFormat.RGB24, false);
            output.ReadPixels(new Rect(0, 0, side, side), 0, 0);
            output.Apply();
            File.WriteAllBytes("relief-preview.png", output.EncodeToPNG());
            var terrainCamera = (Camera)typeof(MfdTerrainRelief)
                .GetField("camera", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var fieldRenderers = (System.Collections.Generic.List<MeshRenderer>)typeof(MfdTerrainRelief)
                .GetField("controlRenderers", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (MeshRenderer field in fieldRenderers) field.enabled = false;
            terrainCamera.orthographicSize = 250f;
            RenderCamera(terrainCamera);
            RenderTexture.active = terrainCamera.targetTexture;
            var close = new Texture2D(terrainCamera.targetTexture.width,
                terrainCamera.targetTexture.height, TextureFormat.RGB24, false);
            close.ReadPixels(new Rect(0, 0, close.width, close.height), 0, 0);
            close.Apply();
            File.WriteAllBytes("terrain-close-preview.png", close.EncodeToPNG());
            MfdTerrainRelief.Restore();
            if (!control.enabled || !threat.enabled || !flatLabels.activeSelf ||
                !secondTrack.iconImage.enabled || MfdTerrainRelief.IsDrawing)
                throw new Exception("Closing the terrain model did not restore native overlay state.");
            if (Vector2.Distance(cachedTrack.iconImage.transform.localPosition,
                    new Vector2(-21000f, 13000f) * map.mapDisplayFactor) > .01f ||
                Vector2.Distance(objective.transform.localPosition,
                    new Vector2(18000f, 12000f) * map.mapDisplayFactor) > .01f)
                throw new Exception("Closing relief left projected coordinates in the native map.");
            MfdTerrainRelief.Reset();
            string heightSidecar = Path.Combine(maps, assetName + ".bmap");
            byte[] invalid = File.ReadAllBytes(heightSidecar);
            invalid[0] = 0;
            File.WriteAllBytes(heightSidecar, invalid);
            MfdTerrainRelief.Tick();
            if (MfdTerrainRelief.IsDrawing || image.GetComponent<Image>().color.a < .99f)
                throw new Exception("Invalid terrain data did not leave the native map visible.");
            File.WriteAllText("result.txt", "PASS: " + (GraphicsSettings.currentRenderPipeline != null ? "URP" : "built-in") + "; 4 captures; throttled terrain/icon/objective/selection/click synchronization, viewport resize, released render texture recovery, render-state restoration, native zoom hierarchy and full-viewport terrain coverage through pan/zoom/orbit, altitude projection, box selection, rendered context text and selection-box chrome, context lifecycle, native restoration, and invalid-asset fallback.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void RenderCamera(Camera camera)
    {
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else RenderPipeline.SubmitRenderRequest(camera,
            new UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture });
    }

    private static void DrawTerrain()
    {
        typeof(MfdTerrainRelief).GetField("nextRender", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, 0f);
        MfdTerrainRelief.Tick();
    }

    private static void CheckLayoutBeforeSnapshot(DynamicMap map)
    {
        var manager = new GameObject("MapUiOrder", typeof(MapUiManager)).GetComponent<MapUiManager>();
        MethodInfo update = typeof(MapUiManager).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        Vector2 size = map.mapBackground.rectTransform.sizeDelta;
        int layouts = 0;
        MfdRailPatch.BeforeSnapshot = () =>
        {
            layouts++;
            map.mapBackground.rectTransform.sizeDelta = size + new Vector2(125f, 0f);
        };
        update.Invoke(manager, null);
        var camera = (Camera)typeof(MfdTerrainRelief).GetField("camera", BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null);
        float aspect = map.mapBackground.rectTransform.rect.width / map.mapBackground.rectTransform.rect.height;
        if (layouts == 0 || Mathf.Abs(camera.aspect - aspect) > .001f)
            throw new Exception("Relief captured the viewport before rail reconciliation changed its layout.");
        int previousLayouts = layouts, revision = MfdTerrainRelief.ViewRevision;
        typeof(MapUiManager).GetField("nextRefresh", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(manager, Time.unscaledTime + 60f);
        MfdTerrainRelief.Rig.Yaw += 5f;
        typeof(MfdTerrainRelief).GetField("nextRender", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0f);
        update.Invoke(manager, null);
        if (layouts != previousLayouts || MfdTerrainRelief.ViewRevision <= revision)
            throw new Exception("The 10 Hz layout guard skipped this frame's relief update.");
        MfdRailPatch.BeforeSnapshot = null;
        map.mapBackground.rectTransform.sizeDelta = size;
        MfdTerrainRelief.Rig.Yaw -= 5f;
        DrawTerrain();
    }

    private static void CheckRenderedPoseSynchronization(DynamicMap map, UnitMapIcon track,
        ObjectiveMarker objective, TargetMarker info)
    {
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Static;
        var type = typeof(MfdTerrainRelief);
        var camera = (Camera)type.GetField("camera", fields).GetValue(null);
        var nextRender = type.GetField("nextRender", fields);
        var moving = type.GetField("cameraWasMoving", fields);
        RectTransform image = map.mapImage.GetComponent<RectTransform>();
        Rect rect = image.rect;
        Vector3 renderedCamera = camera.transform.position;
        Quaternion renderedRotation = camera.transform.rotation;
        Vector3 renderedTrack = track.iconImage.transform.position;
        Vector3 renderedObjective = objective.transform.position;
        int revision = MfdTerrainRelief.ViewRevision;
        if (!MfdTerrainRelief.TryProject(18000f, 12000f, rect, out Vector2 clickPoint) ||
            !MfdTerrainRelief.TryUnproject(clickPoint, rect, out GlobalPosition oldClick))
            throw new Exception("Displayed terrain click ray unavailable.");
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(map.maximizedMapCanvas.worldCamera,
            image.TransformPoint(clickPoint));
        if (!MfdTerrainRelief.ViewportPoint(screenPoint, out float vx, out float vy) ||
            !MfdTerrainRelief.TryGround(vx, vy, out Vector3 screenGround) ||
            Vector2.Distance(new Vector2(screenGround.x, screenGround.z),
                new Vector2(oldClick.x, oldClick.z) * (900f / 81920f)) > .1f)
            throw new Exception("Canvas-camera screen coordinates missed the displayed terrain.");

        MfdTerrainRelief.Rig.Yaw += 12f;
        SceneSingleton<CameraStateManager>.i.transform.position = new Vector3(-7500f, 1500f, 12000f);
        moving.SetValue(null, true);
        nextRender.SetValue(null, Time.unscaledTime + 60f);
        MfdTerrainRelief.Tick();
        if (MfdTerrainRelief.ViewRevision != revision ||
            Vector3.Distance(camera.transform.position, renderedCamera) > .001f ||
            Quaternion.Angle(camera.transform.rotation, renderedRotation) > .001f ||
            Vector3.Distance(track.iconImage.transform.position, renderedTrack) > .01f ||
            Vector3.Distance(objective.transform.position, renderedObjective) > .01f ||
            !MfdTerrainRelief.TryUnproject(clickPoint, rect, out GlobalPosition skippedClick) ||
            Mathf.Abs(skippedClick.x - oldClick.x) > .1f || Mathf.Abs(skippedClick.z - oldClick.z) > .1f)
            throw new Exception("A skipped terrain render advanced symbols or click rays ahead of the image.");
        if (!MfdTerrainRelief.TryProject(-7500f, 12000f, rect, out Vector2 cameraPoint) ||
            Vector2.Distance(map.viewIndicator.transform.localPosition, cameraPoint) > .1f)
            throw new Exception("Camera switch left the native view indicator on the flat or previous-pose map.");

        // Nuclear Option's FloatingOrigin translates every scene root, including the
        // private terrain. A throttled frame must keep the camera with its terrain root.
        var sceneRoot = (GameObject)type.GetField("sceneRoot", fields).GetValue(null);
        Vector3 shift = new Vector3(1024f, 64f, -2048f);
        sceneRoot.transform.position -= shift;
        Physics.SyncTransforms();
        MfdTerrainRelief.Tick();
        bool shiftedHit = MfdTerrainRelief.TryUnproject(clickPoint, rect, out GlobalPosition shiftedClick);
        Vector2 oldViewport = ProjectInViewport(map, oldClick.x, oldClick.z);
        Vector2 shiftedViewport = ProjectInViewport(map, shiftedClick.x, shiftedClick.z);
        Rect viewportBounds = map.mapBackground.rectTransform.rect;
        Vector2 pixelDelta = shiftedViewport - oldViewport;
        pixelDelta.x *= camera.targetTexture.width / viewportBounds.width;
        pixelDelta.y *= camera.targetTexture.height / viewportBounds.height;
        File.WriteAllText("sync-evidence.txt", $"Origin shift: camera drift {Vector3.Distance(camera.transform.position, renderedCamera - shift):R} m; click drift {shiftedClick.x - oldClick.x:R}/{shiftedClick.z - oldClick.z:R} world m; raster drift {pixelDelta.magnitude:R} px; hit {shiftedHit}.");
        // PhysX/ray arithmetic at the private root's -100000 m offset quantizes the
        // hit: observed z drift is 0.9423828 world m with exactly zero camera drift.
        // 1 world m is 900/81920 = 0.010986 model units (versus 160 m source cells).
        // Also bound the actual displayed raster drift to under 0.05 of one pixel.
        if (Vector3.Distance(camera.transform.position, renderedCamera - shift) > .01f ||
            !shiftedHit || Mathf.Abs(shiftedClick.x - oldClick.x) > 1f || Mathf.Abs(shiftedClick.z - oldClick.z) > 1f ||
            pixelDelta.magnitude > .05f)
            throw new Exception($"Floating origin separated relief: camera drift {Vector3.Distance(camera.transform.position, renderedCamera - shift)}, hit {shiftedHit}, click drift {shiftedClick.x - oldClick.x}/{shiftedClick.z - oldClick.z} m.");
        sceneRoot.transform.position += shift;
        Physics.SyncTransforms();

        // A committed camera move must refresh both sparse native objectives and selected
        // information after the native Update callbacks, without another native icon update.
        var sentinel = new RenderTexture(8, 8, 0);
        sentinel.Create();
        RenderTexture.active = sentinel;
        RenderSettings.fog = true;
        nextRender.SetValue(null, 0f);
        MfdTerrainRelief.Tick();
        if (RenderTexture.active != sentinel || !RenderSettings.fog)
            throw new Exception("Relief rendering leaked active render target or fog state.");
        RenderTexture.active = null;
        RenderSettings.fog = false;
        UnityEngine.Object.DestroyImmediate(sentinel);
        if (MfdTerrainRelief.ViewRevision <= revision ||
            Vector3.Distance(track.iconImage.transform.position, renderedTrack) < 1f ||
            !MfdTerrainRelief.TryProject(18000f, 12000f, rect, out Vector2 objectivePoint) ||
            Vector2.Distance(objective.transform.localPosition, objectivePoint) > .1f ||
            Vector3.Distance(info.transform.position, info.Icon.iconImage.transform.position) > .01f)
            throw new Exception("Committed terrain render did not refresh cached contacts, objectives and selection together.");

        // A native hierarchy change happens during camera/map transitions, independently
        // of the relief rig. It must refresh all icon batches on this same frame.
        image.localPosition += new Vector3(50f, -35f, 0f);
        nextRender.SetValue(null, Time.unscaledTime + 60f);
        type.GetField("nextIconRefresh", fields).SetValue(null, Time.unscaledTime + 60f);
        MfdTerrainRelief.Tick();
        if (!MfdTerrainRelief.TryProject(18000f, 12000f, rect, out objectivePoint) ||
            Vector2.Distance(objective.transform.localPosition, objectivePoint) > .1f ||
            Vector3.Distance(info.transform.position, info.Icon.iconImage.transform.position) > .01f)
            throw new Exception("Native map hierarchy transition left cached markers behind.");
        image.localPosition -= new Vector3(50f, -35f, 0f);
        Quaternion imageRotation = image.localRotation;
        image.localRotation = Quaternion.Euler(0f, 0f, 27f);
        MfdTerrainRelief.Tick();
        if (!MfdTerrainRelief.TryProject(-7500f, 12000f, rect, out cameraPoint) ||
            !MfdTerrainRelief.TryProject(-7500f, 13000f, rect, out Vector2 cameraAhead) ||
            Vector3.Dot(map.viewIndicator.transform.up,
                image.TransformDirection((Vector3)(cameraAhead - cameraPoint)).normalized) < .999f)
            throw new Exception("Rotated native hierarchy changed camera-indicator heading away from the projected direction.");
        image.localRotation = imageRotation;

        Vector2 oldSize = map.mapBackground.rectTransform.sizeDelta;
        map.mapBackground.rectTransform.sizeDelta += new Vector2(125f, 0f);
        nextRender.SetValue(null, Time.unscaledTime + 60f);
        MfdTerrainRelief.Tick();
        float aspect = map.mapBackground.rectTransform.rect.width / map.mapBackground.rectTransform.rect.height;
        if (Mathf.Abs(camera.aspect - aspect) > .001f ||
            (float)nextRender.GetValue(null) > Time.unscaledTime + 1f)
            throw new Exception("Viewport resize did not draw the matching terrain aspect immediately.");
        map.mapBackground.rectTransform.sizeDelta = oldSize;
        MfdTerrainRelief.Tick();
        camera.targetTexture.Release();
        nextRender.SetValue(null, Time.unscaledTime + 60f);
        MfdTerrainRelief.Tick();
        if (!camera.targetTexture.IsCreated())
            throw new Exception("Relief did not recover a released render texture.");

        MfdTerrainRelief.Rig.Yaw -= 12f;
        nextRender.SetValue(null, 0f);
        MfdTerrainRelief.Tick();
    }

    private static void CaptureContext(Canvas canvas, Camera screen, int side,
        Vector2 friendlyScreen, Vector2 hostileScreen)
    {
        typeof(MfdMapInteractions).GetMethod("DrawBox", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { canvas, friendlyScreen - Vector2.one * 15f,
                hostileScreen + Vector2.one * 15f });
        Transform menu = canvas.transform.Find("NOAvionics.MapContext");
        Canvas.ForceUpdateCanvases();
        TMP_Text[] labels = menu.GetComponentsInChildren<TMP_Text>(true);
        if (labels.Length < 5) throw new Exception("Map context is missing its title, fix or terrain actions.");
        foreach (TMP_Text label in labels)
        {
            label.ForceMeshUpdate();
            Vector2 needed = label.GetPreferredValues(label.text);
            if (label.fontSize < AvTypeScale.Floor || label.isTextTruncated ||
                needed.x > label.rectTransform.rect.width + 2f ||
                needed.y > label.rectTransform.rect.height + 2f)
                throw new Exception("Map context text does not fit: " + label.text);
        }
        foreach (Selectable control in menu.GetComponentsInChildren<Selectable>(true))
            if (control.navigation.mode != Navigation.Mode.None)
                throw new Exception("Map context participates in flight-key navigation.");
        Transform box = canvas.transform.Find("NOAvionics.SelectionBox");
        if (box == null || box.GetComponent<Graphic>().raycastTarget)
            throw new Exception("Selection-box chrome must be rendered and pointer-transparent.");
        var target = new RenderTexture(side, side, 24);
        screen.targetTexture = target;
        RenderCamera(screen);
        RenderTexture.active = target;
        var picture = new Texture2D(side, side, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0f, 0f, side, side), 0, 0);
        picture.Apply();
        File.WriteAllBytes("relief-context-preview.png", picture.EncodeToPNG());
        RenderTexture.active = null;
        screen.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(picture);
        UnityEngine.Object.DestroyImmediate(target);
    }

    private static void AssertViewportTerrain(DynamicMap map, string state)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform viewport = map.mapBackground.rectTransform;
        Transform terrain = viewport.Find("NOAvionics.IntelligenceTerrain");
        if (terrain == null || terrain.parent != viewport ||
            !(terrain is RectTransform terrainRect))
            throw new Exception($"Terrain is still confined to the native map-image box at {state}.");
        var viewportCorners = new Vector3[4];
        viewport.GetWorldCorners(viewportCorners);
        Rect visible = terrainRect.rect;
        for (int i = 0; i < viewportCorners.Length; i++)
        {
            Vector3 localCorner = terrainRect.InverseTransformPoint(viewportCorners[i]);
            if (localCorner.x < visible.xMin - 2f || localCorner.x > visible.xMax + 2f ||
                localCorner.y < visible.yMin - 2f || localCorner.y > visible.yMax + 2f)
                throw new Exception($"Terrain leaves an uncovered viewport corner {i} at {state}.");

        }
    }

    private static Vector2 ProjectInViewport(DynamicMap map, float x, float z)
    {
        RectTransform image = map.mapImage.GetComponent<RectTransform>();
        if (!MfdTerrainRelief.TryProject(x, z, image.rect, out Vector2 local))
            throw new Exception("Terrain projection unavailable.");
        Vector3 point = map.mapBackground.rectTransform.InverseTransformPoint(
            image.TransformPoint(local));
        return new Vector2(point.x, point.y);
    }

    private static void AssertWholeMap(DynamicMap map)
    {
        Rect bounds = map.mapBackground.rectTransform.rect;
        float halfX = (Environment.GetEnvironmentVariable("BOSCALI_ASSET_NAME") ==
            "terrain_naval_map" ? 163840f : 81920f) * .5f;
        const float halfZ = 40960f;
        foreach (float x in new[] { -halfX, halfX })
        foreach (float z in new[] { -halfZ, halfZ })
        {
            Vector2 at = ProjectInViewport(map, x, z);
            if (at.x < bounds.xMin || at.x > bounds.xMax ||
                at.y < bounds.yMin || at.y > bounds.yMax)
                throw new Exception($"Minimum zoom cannot show the entire theater: {x}, {z} -> {at}.");
        }
    }

    private static void AssertNavalChartAlignment(Texture2D chart, string stylePath)
    {
        var style = new Texture2D(2, 2);
        style.LoadImage(File.ReadAllBytes(stylePath));
        int ocean = 0, polluted = 0;
        for (int y = 1; y < 12; y++)
        for (int x = 1; x < 24; x++)
        {
            float u = x / 24f, v = y / 12f;
            if (chart.GetPixelBilinear(u, v).grayscale > .01f) continue;
            ocean++;
            if (style.GetPixelBilinear(u, v).g > .09f) polluted++;
        }
        if (ocean < 100 || polluted > 3)
            throw new Exception($"Naval style uses island-atlas detail over {polluted} chart ocean samples.");
        UnityEngine.Object.DestroyImmediate(style);
    }

    private static void Airbase(Transform parent, float x, float z)
    {
        var root = new GameObject("Airbase", typeof(RectTransform), typeof(AirbaseMapIcon))
            .GetComponent<AirbaseMapIcon>();
        root.transform.SetParent(parent, false);
        var glyph = new GameObject("Glyph", typeof(RectTransform), typeof(Image))
            .GetComponent<Image>();
        glyph.transform.SetParent(root.transform, false);
        glyph.color = new Color(.15f, .67f, .98f);
        glyph.raycastTarget = false;
        glyph.rectTransform.sizeDelta = Vector2.one;
        glyph.transform.localPosition = new Vector3(x * 900f / 81920f, z * 900f / 81920f);
        glyph.transform.localScale = Vector3.one * 50f;
        root.iconImage = glyph;
        MfdTerrainRelief.ProjectIcon(root, 900f / 81920f);
        if (root.transform.Find("NOAvionics.MapStem") == null ||
            root.transform.Find("NOAvionics.MapFoot") == null)
            throw new Exception("Airbase did not receive a terrain anchor.");
        MfdMapInteractions.HandleAirbaseClick(root);
        if (root.NativeClicks != 1)
            throw new Exception("Plain runway click did not delegate exactly once to the native airbase control.");
    }

    private static UnitMapIcon Plated(Transform parent, DynamicMap map, float x, float z, Unit unit, bool friendly)
    {
        UnitMapIcon icon = Track(parent, map, x, z);
        unit.NetworkHQ = new FactionHQ { Friendly = friendly };
        unit.definition = unit.definition ?? new TestDefinition();
        icon.unit = unit;
        icon.iconImage.color = friendly ? new Color(.20f, .60f, 1f) : new Color(1f, .28f, .24f);
        MapSymbology.Apply(icon, true);
        MfdTerrainRelief.ProjectIcon(icon, map.mapDisplayFactor);
        return icon;
    }

    /// <summary>Framed contacts: glyph per ground class, plate under every glyph, shape by allegiance, sized on the grid.</summary>
    private static void CheckSymbology(Transform parent, DynamicMap map)
    {
        var tank = new GroundVehicle { definition = new VehicleDefinition { vehicleType = VehicleType.MBT } };
        var sam = new GroundVehicle { definition = new VehicleDefinition { vehicleType = VehicleType.R_SAM } };
        var truck = new GroundVehicle { definition = new VehicleDefinition { vehicleType = VehicleType.TRUCK } };
        UnitMapIcon tankIcon = Plated(parent, map, -3000f, -6000f, tank, true);
        UnitMapIcon samIcon = Plated(parent, map, 3000f, -6000f, sam, false);
        UnitMapIcon truckIcon = Plated(parent, map, 9000f, -6000f, truck, false);
        UnitMapIcon shipIcon = Plated(parent, map, -9000f, -6000f, new Ship { definition = new TestDefinition { mapOrient = true } }, true);
        UnitMapIcon jetIcon = Plated(parent, map, -15000f, -6000f, new Aircraft { definition = new TestDefinition { mapOrient = true } }, false);
        foreach (UnitMapIcon icon in new[] { tankIcon, samIcon, truckIcon, shipIcon, jetIcon })
        {
            Image plate = parent.Find("NOAvionics.MapSymbols")?.GetComponentInChildren<Image>();
            if (plate == null) throw new Exception("Framed contacts got no plate layer.");
        }
        if (tankIcon.iconImage.sprite == null || tankIcon.iconImage.sprite == samIcon.iconImage.sprite ||
            samIcon.iconImage.sprite == truckIcon.iconImage.sprite)
            throw new Exception("Ground classes did not get distinct glyphs.");
        Transform layer = parent.Find("NOAvionics.MapSymbols");
        if (layer == null || layer.childCount < 5) throw new Exception("Expected one plate per framed contact.");
        for (int i = 0; i < layer.childCount; i++)
        {
            Image plate = layer.GetChild(i).GetComponent<Image>();
            if (!plate.enabled || plate.sprite == null) throw new Exception("A framed contact has no visible plate.");
        }
        Transform glyph = tankIcon.iconImage.transform;
        float glyphSize = Mathf.Max(glyph.lossyScale.x, glyph.lossyScale.y) * glyph.GetComponent<RectTransform>().rect.width;
        if (glyphSize < 9f || glyphSize > 22f) throw new Exception("Glyph size off the symbol grid: " + glyphSize);
        MapSymbology.Sync(tankIcon);
        Image tankPlate = null;
        foreach (Image candidate in layer.GetComponentsInChildren<Image>())
            if (Vector3.Distance(candidate.transform.position, glyph.position) < .05f) tankPlate = candidate;
        if (tankPlate == null) throw new Exception("The plate does not sit under its glyph.");
        if (tankPlate.sprite == samIcon.iconImage.sprite) throw new Exception("Plate and glyph share a sprite.");
        Plugin.Settings.Command.MapSymbology.Value = false;
        MapSymbology.Apply(tankIcon, true);
        if (tankPlate.enabled) throw new Exception("Turning symbology off left the plate up.");
        Plugin.Settings.Command.MapSymbology.Value = true;
        MapSymbology.Apply(tankIcon, true);
        MfdTerrainRelief.ProjectIcon(tankIcon, map.mapDisplayFactor);
        if (!tankPlate.enabled) throw new Exception("Turning symbology back on did not restore the plate.");
    }

    private static UnitMapIcon Track(Transform parent, DynamicMap map, float x, float z)
    {
        var root = new GameObject("Track", typeof(RectTransform), typeof(UnitMapIcon))
            .GetComponent<UnitMapIcon>();
        root.transform.SetParent(parent, false);
        var glyph = new GameObject("Glyph", typeof(RectTransform), typeof(Image))
            .GetComponent<Image>();
        glyph.transform.SetParent(root.transform, false);
        glyph.rectTransform.sizeDelta = Vector2.one;
        glyph.transform.localPosition = new Vector3(x * map.mapDisplayFactor,
            z * map.mapDisplayFactor);
        glyph.transform.localScale = Vector3.one * 15f;
        glyph.color = new Color(.15f, .67f, .98f);
        root.iconImage = glyph;
        map.mapIcons.Add(root);
        MfdTerrainRelief.ProjectIcon(root, map.mapDisplayFactor);
        return root;
    }

    private static void Marker(Transform parent, Rect rect, float x, float z, Color ink, float size)
    {
        if (!MfdTerrainRelief.TryProject(x, z, rect, out Vector2 point)) return;
        var image = new GameObject("Unit", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.color = ink;
        image.raycastTarget = false;
        var rt = image.rectTransform;
        rt.sizeDelta = Vector2.one * size;
        rt.anchoredPosition = point;
    }

    private static void Front(Transform parent, Rect rect)
    {
        Vector2 previous = default;
        for (int i = 0; i <= 32; i++)
        {
            float u = i / 32f;
            float x = (u - 0.5f) * 81920f;
            float z = -11000f + Mathf.Sin(u * 8f) * 4500f;
            if (!MfdTerrainRelief.TryProject(x, z, rect, out Vector2 next)) continue;
            if (i > 0)
            {
                var line = new GameObject("Frontline", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                line.transform.SetParent(parent, false);
                line.color = new Color(0.87f, 0.92f, 0.94f, 0.88f);
                line.raycastTarget = false;
                var rt = line.rectTransform;
                Vector2 delta = next - previous;
                rt.sizeDelta = new Vector2(delta.magnitude + 1f, 2.5f);
                rt.anchoredPosition = (previous + next) * 0.5f;
                rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            }
            previous = next;
        }
    }

    private static float Height(float u, float v)
    {
        float a = 1800f * Mathf.Exp(-((u - 0.31f) * (u - 0.31f) / 0.035f +
            (v - 0.68f) * (v - 0.68f) / 0.065f));
        float b = 1550f * Mathf.Exp(-((u - 0.73f) * (u - 0.73f) / 0.05f +
            (v - 0.60f) * (v - 0.60f) / 0.06f));
        float c = 1100f * Mathf.Exp(-((u - 0.48f) * (u - 0.48f) / 0.14f +
            (v - 0.28f) * (v - 0.28f) / 0.025f));
        float channel = 950f * Mathf.Exp(-((u - 0.51f) * (u - 0.51f) / 0.004f +
            (v - 0.65f) * (v - 0.65f) / 0.055f));
        float terrain = Mathf.Max(0f, a + b + c - channel - 90f);
        if (terrain < 15f) return 0f;
        float ridges = 130f * Mathf.Sin(u * 51f + Mathf.Sin(v * 31f) * 1.6f) *
            Mathf.Cos(v * 43f + Mathf.Sin(u * 27f));
        ridges += 65f * Mathf.Sin(u * 107f + v * 37f) * Mathf.Cos(v * 91f - u * 17f);
        return Mathf.Max(0f, terrain + ridges * Mathf.Clamp01(terrain / 450f));
    }

    private static Sprite MakeSprite()
    {
        string reference = Environment.GetEnvironmentVariable("BOSCALI_MAP_PREVIEW");
        if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
        {
            var captured = new Texture2D(2, 2);
            captured.LoadImage(File.ReadAllBytes(reference));
            captured.name = Environment.GetEnvironmentVariable("BOSCALI_ASSET_NAME") ?? "terrain2_map";
            return Sprite.Create(captured, new Rect(0, 0, captured.width, captured.height), Vector2.one * 0.5f);
        }
        const int side = 512;
        var texture = new Texture2D(side, side, TextureFormat.RGBA32, false);
        var pixels = new Color32[side * side];
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            float h = Height(x / (float)(side - 1), z / (float)(side - 1));
            byte shade = (byte)Mathf.Clamp(44f + h * 0.04f, 20f, 155f);
            pixels[z * side + x] = h > 15f
                ? new Color32(shade, shade, shade, 255)
                : new Color32(8, 20, 28, 255);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, side, side), Vector2.one * 0.5f);
    }

    private static Texture2D MakeControl()
    {
        const int side = 128;
        var texture = new Texture2D(side, side, TextureFormat.RGBA32, false);
        var pixels = new Color32[side * side];
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            pixels[z * side + x] = z > 73
                ? new Color32(20, 110, 170, 55)
                : z < 55 ? new Color32(165, 35, 30, 65) : new Color32(0, 0, 0, 0);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    private static Texture2D MakeThreat()
    {
        const int side = 128;
        var texture = new Texture2D(side, side, TextureFormat.RGBA32, false);
        var pixels = new Color32[side * side];
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            float dx = (x - 86f) / 18f, dz = (z - 57f) / 18f;
            float ring = Mathf.Abs(dx * dx + dz * dz - 1f);
            byte alpha = (byte)Mathf.Clamp((0.18f - ring) * 140f, 0f, 35f);
            pixels[z * side + x] = new Color32(240, 138, 55, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
#endif
