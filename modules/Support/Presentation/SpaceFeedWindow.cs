using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The full-screen SPACE tasking station: the C2 chrome (banner, header, session line), the warning bar, the sensor frame on the
    /// left and the control column on the right (constellation, track file, CONFIRM / TRANSMIT, the top TASKED posts, the host
    /// console), then the C2 footer. It stays open on RWR, MAWS, bandit and terrain warnings (the strip says so and the vanilla
    /// receiver keeps playing its own alert); it closes only on an explicit exit, eight seconds without real input, loss of the
    /// ownship an airborne-open window needs, a lost operator or faction, or lost focus.
    ///
    /// <para>Input. It owns the pointer only: the game's cursor flag frees the cursor and the Rewired mouse is disabled so a click
    /// cannot fire a weapon; the window then hit-tests its own controls by their <see cref="AvHit"/>. Unlike the weather console it
    /// never touches the keyboard or a joystick: an airborne pilot keeps every flight, weapon and escape binding. The mouse is
    /// handed back exactly as found (a mouse that was already disabled stays disabled), only once its buttons are up so the
    /// closing click cannot fire, and immediately on destroy, focus loss or a fault. No hold is automatic.</para>
    /// </summary>
    internal sealed class SpaceFeedWindow : MonoBehaviour
    {
        private const float WindowWidth = 1880f, WindowHeight = 1040f;   // AvWindow caps at 1880 x 1040 (1080p reference)
        private const int SortOrder = 30006;
        private const float TitleHeight = 30f;

        private SpaceFeedController owner;
        private AvWindow window;
        private SpaceFeedPanel panel;
        private SpaceStation station;
        private readonly FeedInputLease lease = new FeedInputLease();
        private readonly List<AvHit> hits = new List<AvHit>(96);
        private AvHit hovered;
        private bool ownsCursorFlag, mouseDisabledByUs, pauseTouched, pauseWas, requireOwnship, closing;
        private FeedCloseReason closeReason = FeedCloseReason.UserExit;
        private float nextHits;
        private Vector2 lastMouse;

        public bool IsOpen => window != null && window.Visible;
        public SpaceFeedPanel Panel => panel;

        internal static SpaceFeedWindow Create(Transform parent, SpaceFeedController owner)
        {
            if (Application.isBatchMode) return null;
            var go = new GameObject("BoscaliSpaceFeed", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<SpaceFeedWindow>();
            view.owner = owner;
            view.Build(go.transform);
            return view;
        }

        private void Build(Transform uiRoot)
        {
            window = AvWindow.Build(uiRoot, "SpaceFeed", "SPACE FEED · OPERATOR VIEW", WindowWidth, WindowHeight, SortOrder);
            window.Closed += OnWindowClosed;
            window.CloseControl.Help = "Close the feed (Esc). Keyboard and joystick stay live; the mouse drives the feed.";
            float boardWidth = Mathf.Min(WindowWidth, 1880f) - 2f * AvGridTokens.Pad - AvGridTokens.Gutter;
            float boardHeight = Mathf.Min(WindowHeight, 1040f) - TitleHeight - AvGridTokens.Footer - OpsPage.FlowInset;
            AvFlow body = window.Body;
            body.ViewportHeight = boardHeight + OpsPage.FlowInset;
            station = new SpaceStation(body.Content, owner, boardWidth, boardHeight, window.Ticker.Register);
            panel = station.Panel;
            body.Add(station);
            window.Footer.Set("Keyboard and joystick stay live; the mouse drives the feed. The feed closes after 8 s without input.", AvState.Inert);
        }

        /// <summary>Paints the station: the shared chrome, the panel and the C2 footer (the footer carries the intent and the words).</summary>
        internal void Paint(SpaceFeedView view, C2ChromeView chrome) => station.Paint(view, chrome);

        /// <summary>Opens the window if an operator exists. False (and nothing taken) when there is none.</summary>
        internal bool Open(SpaceFeedView view, C2ChromeView chrome)
        {
            if (window == null) return false;
            if (IsOpen) return true;
            if (!SpaceCockpitThreatProbe.TryRead(out CockpitThreatSnapshot snap) || !snap.ValidOperator) return false;
            requireOwnship = snap.ValidOwnship; // the entry context: a ground operator never needs an aircraft, an airborne one always does
            closing = false;
            closeReason = FeedCloseReason.UserExit; // the X button closes without Close(reason): never reuse the last session's reason
            try
            {
                window.Show();
                TakeInput();
                station.Paint(view, chrome); // the chrome and footer too: no empty header for the first frames
                nextHits = 0f;
                lastMouse = Input.mousePosition;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError("SPACE feed open failed: " + e);
                Abort();
                return false;
            }
        }

        internal void Close(FeedCloseReason reason)
        {
            if (!IsOpen || closing) return;
            closing = true;
            closeReason = reason;
            window.Hide(); // raises Closed -> OnWindowClosed hands the input back
        }

        private void OnWindowClosed()
        {
            SetHovered(null);
            GiveBackInput();
            closing = false;
            owner?.OnWindowClosed(closeReason);
        }

        private void Abort()
        {
            try { if (window != null && window.Visible) window.Hide(); }
            catch (Exception) { /* the guard below must run whatever the window did */ }
            GiveBackInput();
            if (lease.ForceRelease()) RestoreMouse();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && IsOpen) Close(FeedCloseReason.FocusLost);
        }

        private void OnDisable()
        {
            if (lease.ForceRelease()) RestoreMouse();
            ReleaseFlags();
        }

        private void OnDestroy()
        {
            if (window != null && window.Visible) { try { window.Hide(); } catch (Exception) { } }
            if (lease.ForceRelease()) RestoreMouse();
            ReleaseFlags();
        }

        private void Update()
        {
            // A mouse still held at close is handed back the moment its buttons are up.
            if (lease.Tick(ButtonsDown())) RestoreMouse();
            if (!IsOpen) return;
            try
            {
                float now = SupportManager.MissionNow();
                if (Input.GetKeyDown(KeyCode.Escape)) { Close(FeedCloseReason.UserExit); return; }

                Vector2 mouse = Input.mousePosition;
                if (Input.anyKeyDown || (mouse - lastMouse).sqrMagnitude > 4f || Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) owner.Touch();
                lastMouse = mouse;

                SpaceCockpitThreatProbe.TryRead(out CockpitThreatSnapshot snap);
                float idle = owner.Draft.IsIdle(now) ? SpaceFeedRules.IdleSeconds : 0f;
                FeedCloseReason reason = SpaceFeedRules.CloseReason(snap, Application.isFocused, idle, requireOwnship);
                if (reason != FeedCloseReason.None) { Close(reason); return; }

                // With the Rewired mouse off (by us, or already) the event system hears nothing: hit-test our own controls.
                Mouse device = TryMouse();
                if (mouseDisabledByUs || (device != null && !device.enabled)) Pointer(mouse);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError("SPACE feed fault, closing: " + e);
                Abort();
            }
        }

        // ---- Pointer (the Rewired mouse is off, so the window hit-tests its own controls) ------------------------------

        private void Pointer(Vector2 mouse)
        {
            if (Time.unscaledTime >= nextHits)
            {
                nextHits = Time.unscaledTime + 0.1f;
                hits.Clear();
                window.Root.GetComponentsInChildren(false, hits);
            }
            AvHit under = null;
            for (int i = hits.Count - 1; i >= 0; i--) // later children draw on top
            {
                AvHit h = hits[i];
                if (h != null && h.isActiveAndEnabled && h.Interactable &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)h.transform, mouse, null)) { under = h; break; }
            }
            SetHovered(under);
            if (under != null && Input.GetMouseButtonDown(0)) ((IPointerClickHandler)under).OnPointerClick(PointerData());
        }

        private void SetHovered(AvHit next)
        {
            if (hovered == next) return;
            if (hovered != null)
            {
                ((IPointerExitHandler)hovered).OnPointerExit(PointerData());
                hovered.GetComponent<AvHelpTip>()?.OnPointerExit(PointerData());
            }
            hovered = next;
            if (hovered != null)
            {
                ((IPointerEnterHandler)hovered).OnPointerEnter(PointerData());
                hovered.GetComponent<AvHelpTip>()?.OnPointerEnter(PointerData());
            }
        }

        private static PointerEventData PointerData() =>
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = Input.mousePosition };

        // ---- Input ownership -------------------------------------------------------------------------------------------

        private static bool ButtonsDown() => Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);

        private static Mouse TryMouse() => ReInput.isReady && ReInput.controllers != null ? ReInput.controllers.Mouse : null;

        private void TakeInput()
        {
            ownsCursorFlag = !CursorManager.GetFlag(CursorFlags.SelectionMenu);
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            if (!pauseTouched)
            {
                pauseTouched = true;
                pauseWas = GameplayUI.AllowPauseKeybind;
                GameplayUI.AllowPauseKeybind = false; // Esc closes the feed instead of opening the pause menu
            }
            Mouse mouse = TryMouse();
            if (lease.Acquire(mouse != null, mouse != null && mouse.enabled))
            {
                mouse.enabled = false;
                mouseDisabledByUs = true;
            }
        }

        private void GiveBackInput()
        {
            ReleaseFlags();
            if (lease.Release(ButtonsDown())) RestoreMouse();
        }

        private void ReleaseFlags()
        {
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            ownsCursorFlag = false;
            if (pauseTouched) GameplayUI.AllowPauseKeybind = pauseWas;
            pauseTouched = false;
        }

        private void RestoreMouse()
        {
            Mouse mouse = TryMouse();
            if (mouse != null) mouse.enabled = lease.RestoreMouseTo;
            mouseDisabledByUs = false;
        }
    }
}
