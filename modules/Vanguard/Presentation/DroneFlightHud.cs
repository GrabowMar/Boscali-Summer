using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Configuration;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    [DefaultExecutionOrder(51)]
    internal sealed class DroneFlightHud : MonoBehaviour, ISceneService
    {
        // CursorManager uses flags as a bitmask. A dedicated bit avoids releasing another panel's cursor.
        private const CursorFlags HudPointer = (CursorFlags)(1 << 16);
        private static readonly Color ButtonFill = new Color32(12, 23, 22, 235);

        private RectTransform surface, attitude, ladder, instruments;
        private TMP_Text speed, altitude, heading, throttle, verticalSpeed, stores, target, viewMode, sight, lockMark, returnHint;
        private DroneAttitudeLadder attitudeInk;
        private Image returnButton;
        private DroneControlSettings bindings;
        private TMP_FontAsset font;
        private Material material;
        private bool pointer, returnPressed;
        private float nextReadout;
        private bool? shownMetric;
        private int shownSpeed, shownAltitude, shownHeading, shownThrottle, shownVerticalSpeed;
        private DroneWeapon shownWeapon;
        private int shownAmmo = -1;
        private Unit shownLock;
        private bool shownSearching;
        private KeyCode shownReturnKey;

        internal void Configure(DroneControlSettings settings) => bindings = settings;

        internal bool Visible => surface != null && surface.gameObject.activeInHierarchy;

        public void ResetForScene()
        {
            SetPointer(false);
            if (surface != null) Destroy(surface.gameObject);
            surface = null;
            font = null;
            material = null;
            shownLock = null;
            shownMetric = null;
        }

        private void OnDisable() => SetPointer(false);
        private void OnDestroy() => ResetForScene();

        private void LateUpdate()
        {
            DronePilot pilot = DronePilot.Active;
            DroneFlightView presentation = DroneFlightView.Active;
            Camera camera = presentation != null ? presentation.Camera : null;
            Missile drone = pilot != null ? pilot.Current : null;
            if (drone == null || drone.disabled || camera == null || !DronePilotInput.CanReturn || PlayerSettings.cinematicMode)
            {
                SetPointer(false);
                if (Visible) surface.gameObject.SetActive(false);
                return;
            }

            if (surface == null) Build();
            if (!Visible)
            {
                surface.gameObject.SetActive(true);
                nextReadout = 0f;
                shownAmmo = -1;
            }

            bool focused = Application.isFocused;
#if !PUBLIC_RELEASE
            focused |= DronePilotInput.TestPointer;
#endif
            SetPointer(focused && DronePilotInput.PointerHeld && DronePilotInput.CanUseHudPointer);
            if (pointer && UpdatePointer(Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(0))) return;
            Transform body = drone.transform;
            Transform mount = pilot.WeaponMount;
            Vector3 aim = pilot.Weapon == DroneWeapon.Gun && mount != null
                ? mount.position + pilot.GunDirection * 800f
                : body.position + body.forward * 800f;
            Project(sight, camera, aim);
            Unit locked = pilot.Weapon == DroneWeapon.Guided && pilot.Lock != null && !pilot.Lock.disabled ? pilot.Lock : null;
            if (locked != null) Project(lockMark, camera, pilot.LockPoint.ToLocalPosition());
            else SetActive(lockMark.gameObject, false);

            bool horizon = !pilot.ThirdPerson && !pilot.GunView;
            SetActive(attitude.gameObject, horizon);
            if (horizon)
            {
                Vector3 up = body.InverseTransformDirection(Vector3.up);
                float pitch = Mathf.Asin(Mathf.Clamp(up.z, -1f, 1f)) * Mathf.Rad2Deg;
                float bank = Mathf.Atan2(up.x, up.y) * Mathf.Rad2Deg;
                attitude.localRotation = Quaternion.Euler(0f, 0f, -bank);
                ladder.anchoredPosition = new Vector2(0f, -pitch * DroneAttitudeLadder.PixelsPerDegree);
            }

            if (Time.unscaledTime < nextReadout) return;
            nextReadout = Time.unscaledTime + 0.1f;
            UpdateReadouts(pilot, drone, locked);
        }

        private void UpdateReadouts(DronePilot pilot, Missile drone, Unit locked)
        {
            var palette = VanillaHudStyle.Colours;
            speed.color = altitude.color = heading.color = throttle.color = verticalSpeed.color = stores.color =
                viewMode.color = sight.color = attitudeInk.color = palette.AllClear;
            lockMark.color = palette.Warning;
            target.color = locked != null ? palette.Warning : palette.AllClear;

            bool metric = VanillaHudStyle.Metric;
            Vector3 velocity = drone.rb != null ? drone.rb.velocity : Vector3.zero;
            float speedMs = drone.LocalSim ? drone.speed : velocity.magnitude;
            bool unitsChanged = shownMetric != metric;
            shownMetric = metric;
            SetNumber(speed, ref shownSpeed, speedMs * (metric ? 3.6f : 1.944f), metric ? "{0:0} KM/H" : "{0:0} KT", unitsChanged);
            SetNumber(altitude, ref shownAltitude, drone.GlobalPosition().y * (metric ? 1f : 3.281f), metric ? "{0:0} M" : "{0:0} FT", unitsChanged);
            int bearing = Mathf.RoundToInt(DroneLink.Bearing(Vector3.zero, drone.transform.forward)) % 360;
            if (unitsChanged || shownHeading != bearing)
            {
                shownHeading = bearing;
                AvText.Set(heading, bearing.ToString("000"));
            }
            SetNumber(throttle, ref shownThrottle, pilot.Throttle * 100f, "THR {0:0}%", unitsChanged);
            float vertical = velocity.y * (metric ? 10f : 196.85f);
            int roundedVertical = Mathf.RoundToInt(vertical);
            if (unitsChanged || shownVerticalSpeed != roundedVertical)
            {
                shownVerticalSpeed = roundedVertical;
                verticalSpeed.SetText(metric ? "{0:0.0} M/S" : "{0:0} FT/MIN", roundedVertical * (metric ? 0.1f : 1f));
            }
            AvText.Set(viewMode, pilot.ThirdPerson ? "CHASE" : pilot.GunView ? "GUN VIEW" : string.Empty);

            int ammo = pilot.Ammunition();
            if (shownWeapon != pilot.Weapon || shownAmmo != ammo)
            {
                shownWeapon = pilot.Weapon;
                shownAmmo = ammo;
                string weapon = pilot.Weapon switch
                {
                    DroneWeapon.Gun => pilot.HasPortGun ? "PORT CANNON" : "CANNON",
                    DroneWeapon.Guided => RemoraBoard.IsManta(drone) ? "AGM" : "AAM",
                    _ => string.Empty
                };
                AvText.Set(stores, weapon.Length > 0 ? weapon + "  " + ammo : "UNARMED");
            }
            bool searching = pilot.Weapon == DroneWeapon.Guided;
            // Reference equality also clears a lock when its Unity object has been destroyed.
            if (!ReferenceEquals(shownLock, locked) || shownSearching != searching)
            {
                shownLock = locked;
                shownSearching = searching;
                AvText.Set(target, locked != null ? "LOCK  " + locked.unitName.ToUpperInvariant() : searching ? "SEARCHING" : string.Empty);
            }
            KeyCode key = bindings != null ? bindings.Release.Value : KeyCode.Backspace;
            if (shownReturnKey != key)
            {
                shownReturnKey = key;
                AvText.Set(returnHint, key == KeyCode.None ? "HOLD LEFT ALT TO CLICK" :
                    "HOLD LEFT ALT TO CLICK   /   " + key.ToString().ToUpperInvariant());
            }
        }

        private static void SetNumber(TMP_Text label, ref int previous, float reading, string format, bool force)
        {
            int rounded = Mathf.RoundToInt(reading);
            if (!force && previous == rounded) return;
            previous = rounded;
            label.SetText(format, rounded);
        }

        private void SetPointer(bool enable)
        {
            if (pointer == enable) return;
            pointer = enable;
            CursorManager.SetFlag(HudPointer, enable);
            returnPressed = false;
            if (returnButton != null) returnButton.color = ButtonFill;
        }

        private bool UpdatePointer(Vector2 position, bool down, bool up)
        {
            bool overButton = RectTransformUtility.RectangleContainsScreenPoint(returnButton.rectTransform, position);
            if (down) returnPressed = overButton;
            bool click = returnPressed && overButton && up;
            if (up) returnPressed = false;
            returnButton.color = overButton ? (returnPressed ? new Color32(44, 94, 65, 249) : new Color32(25, 59, 45, 244)) : ButtonFill;
            DronePilot pilot = DronePilot.Active;
            if (!click || pilot == null || !pilot.RequestReturn()) return false;
            SetPointer(false);
            surface.gameObject.SetActive(false);
            return true;
        }

        private void Build()
        {
            var root = new GameObject("BoscaliDroneFlightHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            surface = (RectTransform)root.transform;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 112;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            VanillaHudStyle.TryHudFont(out font, out material);

            instruments = Rect(surface, "Flight instruments", new Vector2(1400f, 700f), Vector2.zero);
            RectTransform attitudeMask = Rect(instruments, "Pitch limits", new Vector2(480f, 335f), Vector2.zero);
            attitudeMask.gameObject.AddComponent<RectMask2D>();
            attitude = Rect(attitudeMask, "Bank", Vector2.zero, Vector2.zero);
            ladder = Rect(attitude, "Pitch ladder", new Vector2(420f, 490f), Vector2.zero);
            attitudeInk = ladder.gameObject.AddComponent<DroneAttitudeLadder>();
            attitudeInk.raycastTarget = false;

            heading = Label(instruments, "Heading", string.Empty, 25f, 95f, 0f, 204f);
            Label(instruments, "Heading index", "-   |   -   |   V   |   -   |   -", 16f, 370f, 0f, 177f);
            viewMode = Label(instruments, "View", string.Empty, 14f, 190f, 0f, 238f);
            speed = Label(instruments, "Speed", string.Empty, 23f, 150f, -278f, 7f);
            altitude = Label(instruments, "Altitude", string.Empty, 23f, 150f, 278f, 7f);
            throttle = Label(instruments, "Throttle", string.Empty, 17f, 155f, -278f, -32f);
            verticalSpeed = Label(instruments, "Vertical speed", string.Empty, 17f, 160f, 278f, -32f);
            Label(instruments, "Speed index", "--", 22f, 30f, -187f, 7f);
            Label(instruments, "Altitude index", "--", 22f, 30f, 187f, 7f);
            stores = Label(instruments, "Selected weapon", string.Empty, 20f, 330f, 465f, 234f);
            target = Label(instruments, "Target", string.Empty, 16f, 450f, 465f, 205f);
            sight = Label(instruments, "Sight", "+", 27f, 90f, 0f, 0f);
            lockMark = Label(instruments, "Lock", "<   >", 27f, 90f, 0f, 0f);

            // Keep the button's hover updates separate from the instrument batch.
            RectTransform menu = Rect(surface, "Aircraft handback", new Vector2(344f, 61f), new Vector2(0f, 33f));
            menu.anchorMin = menu.anchorMax = menu.pivot = new Vector2(0.5f, 0f);
            var menuCanvas = menu.gameObject.AddComponent<Canvas>();
            menuCanvas.overrideSorting = true;
            menuCanvas.sortingOrder = canvas.sortingOrder + 1;
            returnButton = AvLay.Solid(menu, "Return to Aircraft", ButtonFill);
            RectTransform button = returnButton.rectTransform;
            button.anchorMin = Vector2.zero;
            button.anchorMax = Vector2.one;
            button.offsetMin = button.offsetMax = Vector2.zero;
            Label(button, "Label", "RETURN TO AIRCRAFT", 21f, 330f, 0f, 10f);
            returnHint = Label(button, "Shortcut", "HOLD LEFT ALT TO CLICK   /   BACKSPACE", 15f, 340f, 0f, -15f);
            shownReturnKey = KeyCode.Backspace;
            shownAmmo = -1;
            nextReadout = 0f;
        }

        private static RectTransform Rect(RectTransform parent, string name, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private TMP_Text Label(RectTransform parent, string name, string value, float size, float width, float x, float y)
        {
            TMP_Text label = AvText.Make(parent, name, AvTextRole.DataSmall, value, TextAlignmentOptions.Center);
            if (font != null) label.font = font;
            if (material != null) label.fontSharedMaterial = material;
            label.raycastTarget = false;
            label.color = VanillaHudStyle.Colours.AllClear;
            AvText.Size(label, size);
            label.rectTransform.sizeDelta = new Vector2(width, 33f);
            label.rectTransform.anchoredPosition = new Vector2(x, y);
            return label;
        }

        private void Project(TMP_Text marker, Camera camera, Vector3 world)
        {
            Vector3 point = camera.WorldToScreenPoint(world);
            Vector2 local = default;
            bool visible = point.z > 0f && point.x >= 0f && point.x <= Screen.width && point.y >= 0f && point.y <= Screen.height &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(instruments, point, null, out local);
            SetActive(marker.gameObject, visible);
            if (visible) marker.rectTransform.anchoredPosition = local;
        }

        private static void SetActive(GameObject item, bool visible)
        {
            if (item.activeSelf != visible) item.SetActive(visible);
        }

#if !PUBLIC_RELEASE
        internal bool PointerActive => pointer;

        internal bool ClickReturn()
        {
            if (!pointer || returnButton == null) return false;
            Vector2 position = RectTransformUtility.WorldToScreenPoint(null, returnButton.transform.position);
            UpdatePointer(position, true, false);
            return UpdatePointer(position, false, true);
        }
#endif
    }

    // The entire pitch ladder is one mesh. Moving its transform leaves the geometry alone.
    internal sealed class DroneAttitudeLadder : MaskableGraphic
    {
        internal const float PixelsPerDegree = 7f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            for (int degrees = -30; degrees <= 30; degrees += 5)
            {
                float y = degrees * PixelsPerDegree;
                float inner = degrees == 0 ? 58f : 79f;
                float width = degrees == 0 ? 120f : degrees % 10 == 0 ? 55f : 25f;
                Bar(mesh, -inner - width, -inner, y);
                Bar(mesh, inner, inner + width, y);
            }
        }

        private void Bar(VertexHelper mesh, float left, float right, float y)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(left, y - 0.75f), color, Vector2.zero);
            mesh.AddVert(new Vector3(left, y + 0.75f), color, Vector2.zero);
            mesh.AddVert(new Vector3(right, y + 0.75f), color, Vector2.zero);
            mesh.AddVert(new Vector3(right, y - 0.75f), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
