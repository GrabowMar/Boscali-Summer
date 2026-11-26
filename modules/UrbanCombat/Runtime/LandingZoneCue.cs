using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Local presentation of the selected zone: HUD bearing, green smoke, and centre-box
    /// release-window calls (the big map's fix is COMMS ink, read through <see cref="ILandingZone"/>),
    /// plus live HUD & tactical map group icons for active friendly HALO paratrooper sticks.
    /// No presentation object changes a drop or sends network traffic.
    /// </summary>
    [DefaultExecutionOrder(1001)]
    internal sealed class LandingZoneCue : MonoBehaviour, ISceneService
    {
        private const float DisplaySeconds = 5f;
        private const float RemindSeconds = 15f;
        private const int MaxStickMarkers = 4;

        private static readonly FieldInfo WeaponMountField = HarmonyLib.AccessTools.Field(typeof(Weapon), "mount");

        private float nextRead;
        private bool? haloIn, gliderIn;
        private float haloInsideAt, gliderInsideAt;
        private Aircraft seenAircraft;
        private float seenX, seenY, seenZ;
        private bool seen;
        private GlobalPosition landing;
        private readonly HudMarkerCanvas canvas = new HudMarkerCanvas();
        private GameObject smoke;
        private LandingZoneMarker hudMarker;
        private string hudCaption = "LZ";
        private float lzRange;
        private readonly List<AirAssaultVisuals.HaloStickSnapshot> activeSticks = new List<AirAssaultVisuals.HaloStickSnapshot>(MaxStickMarkers);
        private readonly List<LandingZoneMarker> stickHudMarkers = new List<LandingZoneMarker>(MaxStickMarkers);
        private RectTransform stickMapRoot;
        private AvVector stickTracks;
        private readonly List<LandingZoneMarker> stickMapMarkers = new List<LandingZoneMarker>(MaxStickMarkers);

        public void ResetForScene()
        {
            nextRead = 0f;
            Forget();
            canvas.Clear();
            hudMarker = null;
            activeSticks.Clear();
            stickHudMarkers.Clear();
            if (stickMapRoot != null) Destroy(stickMapRoot.gameObject);
            stickMapRoot = null;
            stickTracks = null;
            stickMapMarkers.Clear();
        }

        private void Forget()
        {
            haloIn = gliderIn = null;
            seenAircraft = null;
            seen = false;
            hudMarker?.SetVisible(false);
            if (smoke != null) Destroy(smoke);
            smoke = null;
        }

        private void OnEnable() => HudLayer.Paint += OnPaint;

        private void OnDisable()
        {
            HudLayer.Paint -= OnPaint;
            ResetForScene();
        }
        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + 0.25f;

            if (Application.isBatchMode) { Forget(); return; }
            if (!ModuleServices.TryGet(out ILandingZone zoneSvc) || !zoneSvc.TryGetLocal(out GlobalPosition zone)) { Forget(); return; }
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || me.HasEjected()) { Forget(); return; }
            // A new aircraft or a moved zone starts the cue again, so the pilot hears the
            // standing state once instead of waiting for the next crossing.
            if (!seen || seenAircraft != me || seenX != zone.x || seenY != zone.y || seenZ != zone.z)
            {
                seen = true;
                seenAircraft = me;
                seenX = zone.x;
                seenY = zone.y;
                seenZ = zone.z;
                haloIn = gliderIn = null;
                if (smoke != null) Destroy(smoke);
                smoke = null;
            }
            landing = zone;
            if (smoke == null) smoke = LandingSmoke.Mark(zone.AsVector3());
            Vector3 self = me.GlobalPosition().AsVector3();
            float dist = new Vector2(zone.x - self.x, zone.z - self.z).magnitude;
            lzRange = dist;
            hudCaption = "LZ  " + (VanillaHudStyle.Metric ? Km(dist) :
                (dist / 1852f).ToString("0.0", CultureInfo.InvariantCulture) + " NM");

            bool halo = false, glider = false;
            if (me.weaponStations != null) foreach (WeaponStation station in me.weaponStations)
            {
                if (station == null) continue;
                foreach (Weapon weapon in station.Weapons)
                {
                    if (weapon == null || !(WeaponMountField?.GetValue(weapon) is WeaponMount mount) || mount.jsonKey == null) continue;
                    if (weapon is MountedTroops && mount.jsonKey == AirAssaultController.HaloMountKey && weapon.ammo > 0) halo = true;
                    else if (mount.jsonKey.StartsWith("VG_Hamilcar", System.StringComparison.Ordinal) && weapon.ammo > 0) glider = true;
                }
            }
            if (!halo) haloIn = null;
            if (!glider) gliderIn = null;
            if (!halo && !glider) return;

            float agl = self.y - zone.y;
            if (halo)
            {
                float reach = LandingZoneReach.Halo(agl);
                bool inside = LandingZoneReach.InWindow(dist, reach);
                Announce(ref haloIn, ref haloInsideAt, inside, inside
                    ? $"HALO ▸ LZ {Km(dist)} — DROP NOW"
                    : $"HALO ▸ LZ {Km(dist)} — OUT OF GLIDE (MAX {Km(reach)})");
            }
            if (glider)
            {
                float reach = LandingZoneReach.Glider(agl);
                bool inside = LandingZoneReach.InWindow(dist, reach);
                Announce(ref gliderIn, ref gliderInsideAt, inside, inside
                    ? $"HAMILCAR ▸ LZ {Km(dist)} — RELEASE NOW"
                    : $"HAMILCAR ▸ LZ {Km(dist)} — OUT OF GLIDE (MAX {Km(reach)})");
            }
        }

        private void LateUpdate()
        {
            if (!HudLayer.Enabled) Cue(default, false);      // A/B switch off: the old LateUpdate placement
            UpdateMapSticks();
        }

        private void OnPaint(HudView view)
        {
            if (HudLayer.Enabled && view.Valid) Cue(view, true);
        }

        private void Cue(HudView view, bool layer)
        {
            AirAssaultVisuals.CopyActiveHaloSticks(activeSticks);
            CombatHUD nativeHud = SceneSingleton<CombatHUD>.i;
            Aircraft me = nativeHud != null ? nativeHud.aircraft : null;
            if ((!seen && activeSticks.Count == 0) || !CockpitOverlayGate.Open() || nativeHud == null || me == null || me.disabled)
            {
                HideHudMarkers();
                return;
            }
            Camera camera = layer ? view.Cam : SceneSingleton<CameraStateManager>.i != null
                ? SceneSingleton<CameraStateManager>.i.mainCamera : Camera.main;
            if (camera == null) { HideHudMarkers(); return; }
            if (canvas.Root != null && canvas.InLayer != layer)
            {
                canvas.Clear();
                hudMarker = null;
                stickHudMarkers.Clear();
            }
            if (canvas.Root == null)
            {
                canvas.Create("Boscali Landing Zone HUD", transform, layer);
                hudMarker = new LandingZoneMarker(canvas.Root, "Landing zone HUD fix", true);
            }
            if (layer) canvas.FitToLayer(view);

            float halfW = canvas.Root.rect.width * .5f - 150f;
            float halfH = canvas.Root.rect.height * .5f - 110f;

            if (seen && me == seenAircraft)
            {
                Vector3 world = landing.ToLocalPosition();
                bool projected = canvas.Project(camera, view, world, out Vector2 point);
                Vector3 direction = camera.transform.InverseTransformPoint(world);
                if (LandingZoneMarkerMath.Place(point.x, point.y, projected, direction.x, direction.y,
                    halfW, halfH, out float x, out float y, out float bearing, out bool pinned))
                {
                    Rgba ink = HudInk.Range(VanillaHudStyle.Colours.AllClear.ToRgba(), lzRange);      // desaturates and dims with the zone's distance
                    hudMarker.Show(new Vector2(x, y), hudCaption, new Color(ink.R, ink.G, ink.B, ink.A), pinned, bearing);
                }
                else hudMarker.SetVisible(false);
            }
            else hudMarker?.SetVisible(false);

            int shown = 0;
            GlobalPosition myPos = me.GlobalPosition();
            FactionHQ myHq = me.NetworkHQ;
            for (int i = 0; i < activeSticks.Count && shown < MaxStickMarkers; i++)
            {
                AirAssaultVisuals.HaloStickSnapshot snap = activeSticks[i];
                if (myHq != null && snap.Faction != null && snap.Faction != myHq) continue;
                Vector3 world = snap.Centroid.ToLocalPosition();
                bool projected = canvas.Project(camera, view, world, out Vector2 point);
                Vector3 direction = camera.transform.InverseTransformPoint(world);
                if (!LandingZoneMarkerMath.Place(point.x, point.y, projected, direction.x, direction.y,
                    halfW, halfH, out float x, out float y, out float bearing, out bool pinned)) continue;

                while (stickHudMarkers.Count <= shown)
                    stickHudMarkers.Add(new LandingZoneMarker(canvas.Root, "Paratrooper stick HUD " + stickHudMarkers.Count, true));
                float range = FastMath.Distance(snap.Centroid, myPos);
                Rgba baseInk = snap.Phase == HaloPhase.Deploy ? VanillaHudStyle.Caution.ToRgba()
                    : snap.Phase == HaloPhase.Landed ? VanillaHudStyle.Colours.AllClear.ToRgba()
                    : VanillaHudStyle.InkHi.ToRgba();
                Rgba ink = HudInk.Range(baseInk, range);
                string caption = LandingZoneMarkerMath.StickCaption(snap.Count, snap.Phase, range, VanillaHudStyle.Metric, true);
                stickHudMarkers[shown].Show(new Vector2(x, y), caption, new Color(ink.R, ink.G, ink.B, ink.A), pinned, bearing);
                shown++;
            }
            for (int i = shown; i < stickHudMarkers.Count; i++) stickHudMarkers[i].SetVisible(false);
        }

        private void HideHudMarkers()
        {
            hudMarker?.SetVisible(false);
            for (int i = 0; i < stickHudMarkers.Count; i++) stickHudMarkers[i].SetVisible(false);
        }

        private void UpdateMapSticks()
        {
            if (Application.isBatchMode)
            {
                if (stickMapRoot != null) stickMapRoot.gameObject.SetActive(false);
                return;
            }
            AirAssaultVisuals.CopyActiveHaloSticks(activeSticks);
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (activeSticks.Count == 0 || map == null || map.mapImage == null || !DynamicMap.mapMaximized)
            {
                if (stickMapRoot != null) stickMapRoot.gameObject.SetActive(false);
                return;
            }

            if (stickMapRoot == null)
            {
                var rootGo = new GameObject("Boscali Paratroopers Map Layer", typeof(RectTransform));
                stickMapRoot = (RectTransform)rootGo.transform;
                stickMapRoot.sizeDelta = Vector2.zero;
                stickMapRoot.SetParent(map.mapImage.transform, false);
                stickMapRoot.anchorMin = stickMapRoot.anchorMax = stickMapRoot.pivot = new Vector2(.5f, .5f);
                stickMapRoot.anchoredPosition = Vector2.zero;
                CanvasGroup group = rootGo.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;
                stickTracks = AvVector.Create(stickMapRoot, "Paratrooper glide tracks", 64);
            }

            stickMapRoot.gameObject.SetActive(true);
            if (stickMapRoot.parent != map.mapImage.transform)
            {
                stickMapRoot.SetParent(map.mapImage.transform, false);
                stickMapRoot.anchorMin = stickMapRoot.anchorMax = stickMapRoot.pivot = new Vector2(.5f, .5f);
                stickMapRoot.anchoredPosition = Vector2.zero;
            }
            stickMapRoot.SetAsLastSibling();

            float factor = map.mapDisplayFactor;
            float screenScale = Mathf.Abs(stickMapRoot.lossyScale.x);
            if (!float.IsFinite(factor) || factor <= 0f || !float.IsFinite(screenScale) || screenScale < .001f)
            {
                stickMapRoot.gameObject.SetActive(false);
                return;
            }

            bool projected = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
            FactionHQ mapHq = map.HQ;
            AvQuadBuffer buf = stickTracks.Buffer;
            buf.Clear();

            int shown = 0;
            float invScale = 1f / screenScale;
            for (int i = 0; i < activeSticks.Count && shown < MaxStickMarkers; i++)
            {
                AirAssaultVisuals.HaloStickSnapshot snap = activeSticks[i];
                if (mapHq != null && snap.Faction != null && snap.Faction != mapHq) continue;
                float cx = snap.Centroid.x * factor, cy = snap.Centroid.z * factor;
                if (projected && !projection.TryProject(snap.Centroid.x, snap.Centroid.z, out cx, out cy)) continue;

                while (stickMapMarkers.Count <= shown)
                    stickMapMarkers.Add(new LandingZoneMarker(stickMapRoot, "Paratrooper stick map " + stickMapMarkers.Count, false));

                LandingZoneMarker marker = stickMapMarkers[shown];
                marker.Root.localScale = Vector3.one * invScale;
                bool underCanopyOrDown = snap.Phase >= HaloPhase.Deploy;
                Color tint = underCanopyOrDown ? new Color(0.45f, 1f, 0.62f, 0.95f) : new Color(0.35f, 0.88f, 1f, 0.95f);
                marker.Show(new Vector2(cx, cy), LandingZoneMarkerMath.StickCaption(snap.Count, snap.Phase, 0f, true, false), tint, false, 0f);

                if (snap.Phase < HaloPhase.Landed)
                {
                    float lx = snap.Landing.x * factor, ly = snap.Landing.z * factor;
                    if (!projected || projection.TryProject(snap.Landing.x, snap.Landing.z, out lx, out ly))
                    {
                        var trackInk = new Rgba(tint.r, tint.g, tint.b, 0.55f);
                        AvStrokes.Line(buf, cx, cy, lx, ly, 1.5f * invScale, trackInk);
                        float tick = 5f * invScale;
                        AvStrokes.Line(buf, lx - tick, ly - tick, lx + tick, ly + tick, 1.4f * invScale, trackInk);
                        AvStrokes.Line(buf, lx - tick, ly + tick, lx + tick, ly - tick, 1.4f * invScale, trackInk);
                    }
                }
                shown++;
            }

            for (int i = shown; i < stickMapMarkers.Count; i++) stickMapMarkers[i].SetVisible(false);
            stickTracks.Commit();
        }

        /// <summary>One line on the centre box when the window opens or closes, and a repeat of the release line while it stays open.</summary>
        private static void Announce(ref bool? state, ref float insideAt, bool inside, string text)
        {
            float now = Time.unscaledTime;
            if (state != inside)
            {
                state = inside;
                if (inside) insideAt = now;
                Report(text);
            }
            else if (inside && now - insideAt >= RemindSeconds)
            {
                insideAt = now;
                Report(text);
            }
        }

        private static void Report(string text)
        {
            AircraftActionsReport report = SceneSingleton<AircraftActionsReport>.i;
            if (report != null) report.ReportText(text, DisplaySeconds);
        }

        private static string Km(float metres) => (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " KM";
    }
}
