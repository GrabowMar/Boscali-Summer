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
using UnityEngine.UI;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Local presentation of the selected zone: HUD bearing, green smoke, and centre-box
    /// release-window calls (the big map's fix is COMMS ink, read through <see cref="ILandingZone"/>).
    /// No presentation object changes a drop or sends network traffic. One instance of each
    /// visual follows the zone until it is cleared.
    /// </summary>
    [DefaultExecutionOrder(1001)]
    internal sealed class LandingZoneCue : MonoBehaviour, ISceneService
    {
        private const float DisplaySeconds = 5f;
        private const float RemindSeconds = 15f;

        private static readonly FieldInfo WeaponMountField = HarmonyLib.AccessTools.Field(typeof(Weapon), "mount");

        private float nextRead;
        private bool? haloIn, gliderIn;
        private float haloInsideAt, gliderInsideAt;
        private Aircraft seenAircraft;
        private float seenX, seenY, seenZ;
        private bool seen;
        private GlobalPosition landing;
        private GameObject hudRoot, smoke;
        private RectTransform hudCanvas;
        private LandingZoneMarker hudMarker;
        private string hudCaption = "LZ";
        // Layer on: the fix sits on a holder inside the HUD layer's Motion root and is placed at its late point (OnPaint); off: the old own canvas, placed in LateUpdate.
        private bool hudInLayer;
        private float hudUnit = 1f, lzRange;      // device pixels per canvas unit of the 1080-high grid; the zone's range (m) for the ink ramp

        public void ResetForScene()
        {
            nextRead = 0f;
            Forget();
            if (hudRoot != null) Destroy(hudRoot);
            hudRoot = null;
            hudCanvas = null;
            hudMarker = null;
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
        }

        private void OnPaint(HudView view)
        {
            if (HudLayer.Enabled && view.Valid) Cue(view, true);
        }

        private void Cue(HudView view, bool layer)
        {
            if (!seen) return;
            CombatHUD nativeHud = SceneSingleton<CombatHUD>.i;
            if (!CockpitOverlayGate.Open() || nativeHud == null || nativeHud.aircraft != seenAircraft)
            { hudMarker?.SetVisible(false); return; }
            Camera camera = layer ? view.Cam : SceneSingleton<CameraStateManager>.i != null
                ? SceneSingleton<CameraStateManager>.i.mainCamera : Camera.main;
            if (camera == null) { hudMarker?.SetVisible(false); return; }
            if (hudRoot != null && hudInLayer != layer)
            {
                Destroy(hudRoot);      // the A/B switch moved: rebuild on the other parent
                hudRoot = null; hudCanvas = null; hudMarker = null;
            }
            if (hudRoot == null) BuildHud(layer);
            if (layer)
            {
                // The holder keeps the 1080-high grid the marker is written in, whatever the HUD canvas scale is.
                hudUnit = Mathf.Max(0.1f, Screen.height / 1080f);
                hudCanvas.localScale = Vector3.one * (hudUnit / Mathf.Max(0.01f, view.Scale));
                hudCanvas.sizeDelta = new Vector2(Screen.width / hudUnit, 1080f);
            }

            Vector3 world = landing.ToLocalPosition();
            Vector2 point = default;
            bool projected;
            if (layer)
            {
                projected = view.Project(world, out point);
                point *= view.Scale / hudUnit;      // HUD canvas units to this block's grid
            }
            else
            {
                Vector3 screen = camera.WorldToScreenPoint(world);
                projected = screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(hudCanvas, screen, null, out point);
            }
            Vector3 direction = camera.transform.InverseTransformPoint(world);
            if (!LandingZoneMarkerMath.Place(point.x, point.y, projected, direction.x, direction.y,
                hudCanvas.rect.width * .5f - 150f, hudCanvas.rect.height * .5f - 110f,
                out float x, out float y, out float bearing, out bool pinned))
            { hudMarker.SetVisible(false); return; }
            Rgba ink = HudInk.Range(VanillaHudStyle.Colours.AllClear.ToRgba(), lzRange);      // desaturates and dims with the zone's distance
            hudMarker.Show(new Vector2(x, y), hudCaption, new Color(ink.R, ink.G, ink.B, ink.A), pinned, bearing);
        }

        private void BuildHud(bool layer)
        {
            hudInLayer = layer;
            if (layer)
            {
                hudRoot = new GameObject("Boscali Landing Zone HUD", typeof(RectTransform));
                var holder = (RectTransform)hudRoot.transform;
                holder.SetParent(HudLayer.Motion, false);
                holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(0.5f, 0.5f);
                holder.anchoredPosition = Vector2.zero;
            }
            else
            {
                hudRoot = new GameObject("Boscali Landing Zone HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                hudRoot.transform.SetParent(transform, false);
                Canvas canvas = hudRoot.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1;
                CanvasScaler scaler = hudRoot.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;
                CanvasGroup group = hudRoot.AddComponent<CanvasGroup>();
                group.blocksRaycasts = group.interactable = false;
            }
            hudCanvas = (RectTransform)hudRoot.transform;
            hudMarker = new LandingZoneMarker(hudCanvas, "Landing zone HUD fix", true);
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
