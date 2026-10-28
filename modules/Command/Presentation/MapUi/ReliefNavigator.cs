using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Grab-the-ground navigation for the relief map. Left drag holds the pressed terrain
    /// under the cursor, the zoom axis eases toward the ground under the cursor, middle
    /// drag orbits about the ground where the drag began (Ctrl + wheel turns and
    /// Shift + wheel tilts in steps, for a wheel button that will not hold), and the native map move
    /// axes pan relative to the current heading. Drags start only past the drag threshold,
    /// so clicks, the context menu, armed pickers and pings still see a click.
    /// Client-local presentation: nothing here reaches game state or the network.
    /// </summary>
    internal static class ReliefNavigator
    {
        private const float MinimumSlop = 6f;
        private const float ZoomPerUnit = .2f;
        private const float ZoomTau = .06f;
        private const float AngleTau = .08f;
        private const float FocusTau = .12f;
        private const float CoastTau = .16f;
        private const float DoubleClickSeconds = .3f;
        private const float KeyPanViewsPerSecond = .6f;
        private const float OrbitYawPerPixel = .25f;
        private const float OrbitPitchPerPixel = .2f;
        private const float WheelTurnDegrees = 15f;
        private const float WheelTiltDegrees = 5f;

        private enum Drag { None, PendingGrab, Grab, PendingOrbit, Orbit }

        private static Drag drag;
        private static bool middleClaimed;
        private static int dragButton;
        private static Vector2 pressScreen;
        private static Vector2 lastMouse;
        private static Vector3 anchor;
        private static int releaseFrame = -1;
        private static Vector2 velocity;
        private static Vector2 coast;
        private static bool zoomEasing;
        private static float targetZoom = ReliefRig.MinZoom;
        private static float zoomVx = .5f, zoomVy = .5f, zoomHeight;
        private static bool angleEasing;
        private static float targetYaw, targetPitch = ReliefRig.DefaultPitch;
        private static bool focusEasing;
        private static float targetFocusX, targetFocusZ;
        private static bool follow;
        private static bool fly;
        private static float lastClickTime = -1f;
        private static int doubleClickFrame = -1;
        private static bool doubleClickPending;
        private static Vector2 lastClickScreen;
        private static Player player;

        internal static bool Following => follow;
        internal static bool Flying => fly;
        /// <summary>An orbit drag (held wheel, or Alt + left) is under way.</summary>
        internal static bool Turning => drag == Drag.Orbit;

        /// <summary>A grab drag, or its release frame, must not also click the icon beneath it.</summary>
        internal static bool BlockIconClick() => drag == Drag.Grab || drag == Drag.Orbit ||
            releaseFrame == Time.frameCount;
        internal static bool BlockContextClick() => drag == Drag.Grab || drag == Drag.Orbit ||
            releaseFrame == Time.frameCount ||
            doubleClickFrame == Time.frameCount;

        internal static void Tick(ReliefRig rig)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            Vector2 mouse = Input.mousePosition;
            bool inside = MfdTerrainRelief.ViewportPoint(mouse, out float vx, out float vy);
            bool free = inside && !MapUiPointer.OverControls() && !MfdMapInteractions.Typing();

            // The wheel button is read as a held state, not as its one-frame press: on a live session
            // Windows, Unity and Rewired all showed the button held over the map while the press frame
            // never reached this code, so the orbit never began. One hold starts one drag.
            bool middle = MiddleHeld();
            if (!middle) middleClaimed = false;

            if (drag == Drag.None && free)
            {
                // Alt + left drag orbits like the held wheel does: a mouse or driver that never
                // reports a held wheel button still gets the drag.
                if (Input.GetMouseButtonDown(0) && AltHeld() && !LeftDragClaimed())
                    Press(rig, 0, true, mouse, vx, vy);
                else if (Input.GetMouseButtonDown(0) && !BoxHeld() && !LeftDragClaimed())
                    Press(rig, 0, false, mouse, vx, vy);
                else if (middle && !middleClaimed)
                {
                    middleClaimed = true;
                    Press(rig, 2, true, mouse, vx, vy);
                }
                // The right button orbits again, as it did before: it is the one held-button drag every
                // mouse reports. A right click without travel still opens the map menu, and an armed
                // call-in keeps the button to itself.
                else if (Input.GetMouseButtonDown(1) && !NOAvionics.MapPicker.IsBusy &&
                    Plugin.Settings?.Command?.MapRightDragTurns?.Value != false)
                    Press(rig, 1, true, mouse, vx, vy);
            }
            if (drag != Drag.None) Hold(rig, mouse, vx, vy, dt);
            else if (coast.sqrMagnitude > 1e-4f)
            {
                rig.FocusX += coast.x * dt;
                rig.FocusZ += coast.y * dt;
                rig.ClampFocus();
                coast *= Mathf.Exp(-dt / CoastTau);
            }
            else coast = Vector2.zero;

            ReadAxes(rig, free, vx, vy, dt);
            Ease(rig, dt);
            lastMouse = mouse;
        }

        private static void Press(ReliefRig rig, int button, bool orbit, Vector2 mouse, float vx, float vy)
        {
            drag = orbit ? Drag.PendingOrbit : Drag.PendingGrab;
            dragButton = button;
            pressScreen = lastMouse = mouse;
            anchor = Ground(rig, vx, vy);
            velocity = coast = Vector2.zero;
            if (orbit) return;
            if (Plugin.Settings?.Command?.MapDoubleClickZoom?.Value != false &&
                Time.unscaledTime - lastClickTime < DoubleClickSeconds &&
                (mouse - lastClickScreen).sqrMagnitude < Slop * Slop)
            {
                lastClickTime = -1f;
                doubleClickFrame = Time.frameCount;
                doubleClickPending = true;
                follow = false;
                FocusOn(anchor.x, anchor.z);
                ZoomTo(rig, (zoomEasing ? targetZoom : rig.Zoom) * 2f, .5f, .5f, 0f);
                return;
            }
            lastClickTime = Time.unscaledTime;
            lastClickScreen = mouse;
        }

        private static void Hold(ReliefRig rig, Vector2 mouse, float vx, float vy, float dt)
        {
            bool held = dragButton == 2 ? MiddleHeld() : Input.GetMouseButton(dragButton);
            if (!held || (drag == Drag.PendingGrab && BoxHeld()))
            {
                // An orbit that ends on a left or right release must not also click or open the menu under it.
                if (drag == Drag.Orbit && dragButton != 2) releaseFrame = Time.frameCount;
                if (dragButton == 0 && doubleClickPending)
                {
                    doubleClickFrame = Time.frameCount;
                    doubleClickPending = false;
                }
                if (drag == Drag.Grab)
                {
                    releaseFrame = Time.frameCount;
                    coast = velocity;
                }
                drag = Drag.None;
                return;
            }
            if (drag == Drag.PendingGrab || drag == Drag.PendingOrbit)
            {
                if ((mouse - pressScreen).sqrMagnitude < Slop * Slop) return;
                drag = drag == Drag.PendingGrab ? Drag.Grab : Drag.Orbit;
                follow = focusEasing = false;
                if (drag == Drag.Orbit) angleEasing = false;
            }
            if (drag == Drag.Grab)
            {
                float fromX = rig.FocusX, fromZ = rig.FocusZ;
                rig.Grab(anchor.x, anchor.y, anchor.z, vx, vy);
                if (dt > 0f)
                {
                    Vector2 step = new Vector2(rig.FocusX - fromX, rig.FocusZ - fromZ) / dt;
                    velocity = Vector2.Lerp(velocity, step, 1f - Mathf.Exp(-dt / .03f));
                }
                return;
            }
            Vector2 delta = mouse - lastMouse;
            if (delta.sqrMagnitude > 0f)
            {
                float speed = Mathf.Clamp(Plugin.Settings?.Command?.MapTurnSensitivity?.Value ?? 1f, .25f, 4f);
                rig.OrbitAbout(delta.x * OrbitYawPerPixel * speed, -delta.y * OrbitPitchPerPixel * speed,
                    anchor.x, anchor.y, anchor.z);
            }
        }

        private static void ReadAxes(ReliefRig rig, bool free, float vx, float vy, float dt)
        {
            if (MfdMapInteractions.Typing()) return;
            if (player == null && ReInput.isReady) player = ReInput.players.GetPlayer(0);

            float zoom = player != null ? player.GetAxis("Zoom View") : 0f;
            // Ctrl + wheel turns and Shift + wheel tilts, a notch at a time: the orbit without holding
            // the wheel button down, which some mice and drivers never report as a held button.
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f && free && (MfdMapInteractions.ControlHeld() || MfdMapInteractions.ShiftHeld()))
            {
                if (MfdMapInteractions.ControlHeld()) Turn(Mathf.Sign(wheel) * WheelTurnDegrees);
                else Tilt(Mathf.Sign(wheel) * WheelTiltDegrees);
                zoom = 0f;
            }
            // Wheel over a panel scrolls the panel; a stick or key zoom off the map uses the centre.
            if (zoom != 0f && !MapUiPointer.OverControls())
            {
                Vector3 at = free ? Ground(rig, vx, vy) : Vector3.zero;
                float from = zoomEasing ? targetZoom : rig.Zoom;
                ZoomTo(rig, from * Mathf.Exp(zoom * ZoomPerUnit *
                        Mathf.Clamp(Plugin.Settings?.Command?.MapZoomStep?.Value ?? 1f, .25f, 3f)),
                    free ? vx : .5f, free ? vy : .5f, at.y);
            }

            float right = player != null ? player.GetAxis("Move Map Horizontal") : 0f;
            float up = player != null ? player.GetAxis("Move Map Vertical") : 0f;
            if (fly)
            {
                float keysRight = (Input.GetKey(KeyCode.D) ? 1f : 0f) -
                    (Input.GetKey(KeyCode.A) ? 1f : 0f);
                float keysUp = (Input.GetKey(KeyCode.W) ? 1f : 0f) -
                    (Input.GetKey(KeyCode.S) ? 1f : 0f);
                if (keysRight != 0f || keysUp != 0f)
                {
                    right = keysRight;
                    up = keysUp;
                }
            }
            if (right == 0f && up == 0f) return;
            follow = focusEasing = false;
            float yaw = rig.Yaw * Mathf.Deg2Rad;
            float step = KeyPanViewsPerSecond * 2f * rig.Size * dt;
            rig.FocusX += (Mathf.Cos(yaw) * right + Mathf.Sin(yaw) * up) * step;
            rig.FocusZ += (-Mathf.Sin(yaw) * right + Mathf.Cos(yaw) * up) * step;
            rig.ClampFocus();
        }

        private static void Ease(ReliefRig rig, float dt)
        {
            if (follow)
            {
                if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null &&
                    MfdTerrainRelief.WorldToModel(aircraft.GlobalPosition(), out float x, out float z))
                    FocusOn(x, z);
                else follow = false;
            }
            if (angleEasing)
            {
                float yaw = ReliefRig.DampAngle(rig.Yaw, targetYaw, dt, AngleTau);
                float pitch = ReliefRig.Damp(rig.Pitch, targetPitch, dt, AngleTau);
                bool done = Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw)) < .05f &&
                    Mathf.Abs(pitch - targetPitch) < .05f;
                if (done) { yaw = targetYaw; pitch = targetPitch; angleEasing = false; }
                Vector3 pivot = Ground(rig, .5f, .5f);
                rig.OrbitAbout(Mathf.DeltaAngle(rig.Yaw, yaw), pitch - rig.Pitch, pivot.x, pivot.y, pivot.z);
            }
            if (zoomEasing)
            {
                float zoom = ReliefRig.Damp(rig.Zoom, targetZoom, dt, ZoomTau);
                if (Mathf.Abs(zoom - targetZoom) < targetZoom * .002f)
                {
                    zoom = targetZoom;
                    zoomEasing = false;
                }
                rig.ZoomAt(zoom, zoomVx, zoomVy, zoomHeight);
            }
            if (focusEasing)
            {
                rig.FocusX = ReliefRig.Damp(rig.FocusX, targetFocusX, dt, FocusTau);
                rig.FocusZ = ReliefRig.Damp(rig.FocusZ, targetFocusZ, dt, FocusTau);
                rig.ClampFocus();
                if (!follow && Mathf.Abs(rig.FocusX - targetFocusX) < .01f &&
                    Mathf.Abs(rig.FocusZ - targetFocusZ) < .01f) focusEasing = false;
            }
        }

        private static void ZoomTo(ReliefRig rig, float zoom, float vx, float vy, float height)
        {
            targetZoom = Mathf.Clamp(zoom, ReliefRig.MinZoom, rig.ZoomLimit);
            zoomVx = vx;
            zoomVy = vy;
            zoomHeight = height;
            zoomEasing = true;
        }

        private static void FocusOn(float x, float z)
        {
            targetFocusX = x;
            targetFocusZ = z;
            focusEasing = true;
        }

        private static void AngleTo(float yaw, float pitch)
        {
            targetYaw = ReliefRig.WrapAngle(yaw);
            targetPitch = Mathf.Clamp(pitch, ReliefRig.MinPitch, ReliefRig.MaxPitch);
            angleEasing = true;
        }

        internal static void Turn(float degrees)
        {
            ReliefRig rig = MfdTerrainRelief.Rig;
            AngleTo((angleEasing ? targetYaw : rig.Yaw) + degrees, angleEasing ? targetPitch : rig.Pitch);
        }

        internal static void Tilt(float degrees)
        {
            ReliefRig rig = MfdTerrainRelief.Rig;
            AngleTo(angleEasing ? targetYaw : rig.Yaw, (angleEasing ? targetPitch : rig.Pitch) + degrees);
        }

        internal static void North() => AngleTo(0f, ReliefRig.DefaultPitch);

        internal static void Fit()
        {
            follow = false;
            FocusOn(0f, 0f);
            ZoomTo(MfdTerrainRelief.Rig, ReliefRig.MinZoom, .5f, .5f, 0f);
        }

        internal static void Focus(GlobalPosition point, bool zoom)
        {
            if (!MfdTerrainRelief.WorldToModel(point, out float x, out float z)) return;
            follow = false;
            FocusOn(x, z);
            if (zoom) ZoomTo(MfdTerrainRelief.Rig,
                Mathf.Max(3f, MfdTerrainRelief.Rig.Zoom), .5f, .5f, 0f);
        }

        internal static void ToggleFollow()
        {
            follow = !follow && GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null;
            if (follow) fly = false;
            focusEasing = follow;
        }

        internal static void ToggleFly()
        {
            fly = !fly;
            if (fly) follow = focusEasing = false;
            coast = Vector2.zero;
        }

        /// <summary>The terrain under a viewport point, or the sea-level plane off the terrain.</summary>
        private static Vector3 Ground(ReliefRig rig, float vx, float vy)
        {
            if (MfdTerrainRelief.TryGround(vx, vy, out Vector3 ground)) return ground;
            rig.Unproject(vx, vy, 0f, out float x, out float z);
            return new Vector3(x, 0f, z);
        }

        private static float Slop => Mathf.Max(MinimumSlop, EventSystem.current != null
            ? EventSystem.current.pixelDragThreshold : 0f);

        // The wheel button is read on two channels: Unity's input and the game's Rewired mouse.
        private static bool MiddleHeld() => Input.GetMouseButton(2) ||
            (ReInput.isReady && ReInput.controllers.Mouse != null && ReInput.controllers.Mouse.GetButton(2));
        private static bool AltHeld() => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        /// <summary>Shift or Ctrl hands a left drag to the selection box.</summary>
        private static bool BoxHeld() => MfdMapInteractions.BoxDrag();

        private static bool LeftDragClaimed() => StrMapOverlay.DrawArmed ||
            ModuleServices.TryGet(out IMapBoxInput input) && input.BlocksBoxSelection;

        /// <summary>Map closed: drop gestures and easing, keep the view.</summary>
        internal static void Release()
        {
            fly = false;
            drag = Drag.None;
            middleClaimed = false;
            releaseFrame = -1;
            doubleClickFrame = -1;
            doubleClickPending = false;
            velocity = coast = Vector2.zero;
            zoomEasing = angleEasing = focusEasing = false;
            lastClickTime = -1f;
        }

        /// <summary>Scene reset: forget everything, including FOLLOW.</summary>
        internal static void Reset()
        {
            Release();
            follow = false;
            targetZoom = ReliefRig.MinZoom;
            player = null;
        }
    }
}
