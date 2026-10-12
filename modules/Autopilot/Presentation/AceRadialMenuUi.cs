using System;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Autopilot.Configuration;
using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Modules.Autopilot.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// The interaction menu (v7 "Ring + Dock"). By default it takes the game's own Radial Menu binding (held past the game's press delay, like the
    /// native wheel it replaces; a tap stays the game's) plus an optional extra key. Hold and point, release to run; release at the hub to latch and click;
    /// digits, arrows, the wheel, RMB, Backspace and Esc work too (see <see cref="MenuController"/>). This component only hosts the pieces:
    /// <see cref="AceRadialInputSource"/> samples input, the pure <see cref="MenuController"/> decides, <see cref="MenuGame"/> builds the model
    /// from the live game, <see cref="MenuPainter"/> draws it on one AvionicsUi vector surface plus a TMP label pool, and
    /// <see cref="AceRadialInputGuard"/> / <see cref="AceRadialPauseGate"/> keep the game from reacting while it is open. The model is built before
    /// anything is taken from the game, so a provider that throws can never leave the guard held.
    /// </summary>
    internal sealed class AceRadialMenuUi : MonoBehaviour, ISceneService
    {
        private const float RefreshSec = 0.25f;
        private const int QuadCapacity = 7000;
        private const CursorFlags ForeignCursorOwners = CursorFlags.GameMenu | CursorFlags.Dialogue | CursorFlags.Chat;

        private readonly AceRadialInputSource input = new AceRadialInputSource();
        private readonly MenuRecall recall = new MenuRecall(); // this session only: the controller is rebuilt on every open

        private AutopilotSettings settings;
        private AvHudHost host;
        private AvVector surface;
        private MenuTextPool text;
        private MenuPainter painter;
        private MenuModel model;
        private MenuLayout layout;
        private MenuController controller;
        private int layoutW, layoutH;
        private float openedAt, lastRefresh;
        private float pressedAt, radialDownAt;
        private bool wasOpen, releasePending, overflowLogged, narrowLogged, radialArmed;
        private KeyCode openKey = KeyCode.None;
        private int soundMark;

        /// <summary>The Harmony patch on the native wheel's Update reads this: the wheel stays frozen while this menu is open.</summary>
        public static bool IsOpenNow { get; private set; }

        /// <summary>The menu has taken the Radial Menu binding: the patch keeps the native wheel from opening on it.</summary>
        public static bool OwnsRadialButton { get; private set; }

        public bool IsOpen => controller != null && controller.Open;

        public void Configure(AutopilotSettings autopilotSettings) => settings = autopilotSettings;

        public void ResetForScene()
        {
            CloseMenu();
            CockpitStateProbe.Reset();
            MenuGame.Reset();
        }

        private void Awake() => BuildCanvas();

        private void OnDisable() => CloseMenu();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) CloseMenu();
        }

        private void OnDestroy()
        {
            CloseMenu();
            OwnsRadialButton = false;
            host?.Destroy();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            KeyCode key = settings?.AceRadialKey?.Value ?? KeyCode.None;
            bool radial = settings?.ReplaceNativeWheel?.Value ?? true;
            OwnsRadialButton = radial;
            if (key != openKey) { openKey = key; if (controller != null) controller.OpenKeyName = KeyName(key); }
            AceRadialPauseGate.Tick(now);
            if (releasePending && !input.AnyMenuInputHeld(key, radial)) FinishClose();
            if (key == KeyCode.None && !radial)
            {
                if (IsOpen) CloseMenu(); // both ways in were switched off while the menu was open
                return;
            }

            if (!IsOpen)
            {
                bool opened = WantsOpen(key, radial, now) && CanOpen() && TryOpen(key, radial, now);
                if (!opened)
                {
                    if (controller != null && controller.Showing) { controller.Step(input.Sample(key, radial, false, now)); Present(now); }
                    else if (host.Canvas.enabled) host.SetVisible(false);
                    return;
                }
            }
            else
            {
                if (!StillValid()) controller.Cancel();
                else
                {
                    if (!input.OpenHeld(key, radial) || now - pressedAt >= PlayerSettings.clickDelay) AceRadialInputGuard.TakeKeyboard();
                    controller.Step(input.Sample(key, radial, true, now));
                    RunCommands(now);
                }
                if (!controller.Open) { OnClosed(now); Present(now); return; }
                if (now - lastRefresh >= RefreshSec) Refresh(now);
            }
            Present(now);
        }

        // ------------------------------------------------------------------ open / close

        private static string KeyName(KeyCode key) => key == KeyCode.None ? "" : key.ToString().ToUpperInvariant();

        /// <summary>The extra key opens on its press; the Radial Menu binding once it has been held for the game's press delay, exactly when the native wheel would.</summary>
        private bool WantsOpen(KeyCode key, bool radial, float now)
        {
            if (key != KeyCode.None && Input.GetKeyDown(key)) { pressedAt = now; return true; }
            Rewired.Player pad = radial ? GameManager.playerInput : null;
            if (pad == null) return false;
            if (pad.GetButtonDown(AceRadialInputSource.PadPress)) { radialDownAt = now; radialArmed = true; input.ResolveRadialBinding(); }
            if (!radialArmed) return false;
            if (!input.RadialHeld()) { radialArmed = false; return false; }
            if (now - radialDownAt < PlayerSettings.pressDelay) return false;
            radialArmed = false; pressedAt = radialDownAt;
            return true;
        }

        private bool CanOpen()
        {
            if (GameplayUI.GameIsPaused || DynamicMap.mapMaximized) return false;
            if ((CursorManager.GetFlags() & ForeignCursorOwners) != 0) return false;
            // another window (OPS, weather, cinematic, contracts) owns the cursor and has already taken the Rewired mouse: leave it alone
            if (!AceRadialInputGuard.Held && CursorManager.GetFlag(CursorFlags.SelectionMenu)) return false;
            if (NativeWheelOpen() || Typing()) return false;
            return LiveAircraft();
        }

        private static bool StillValid() =>
            Application.isFocused && !GameplayUI.GameIsPaused && !DynamicMap.mapMaximized && LiveAircraft();

        /// <summary>A wrecked jet stays the local aircraft for a while, disabled: the menu would show a fabricated default world for it.</summary>
        private static bool LiveAircraft() => GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled;

        private static bool NativeWheelOpen()
        {
            try { return RadialMenuMain.IsInUse(); }
            catch (Exception) { return false; }
        }

        private static bool Typing()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        private bool TryOpen(KeyCode key, bool radial, float now)
        {
            MenuModel built;
            try { built = MenuGame.Build(); }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Autopilot: interaction menu could not build its catalog: " + e.Message);
                return false;
            }
            model = built;
            EnsureLayout();
            controller = new MenuController(model, layout, recall) { OpenKeyName = KeyName(key) };
            input.Prime();
            controller.Step(input.Sample(key, radial, true, now));
            if (!controller.Open) return false;
            try
            {
                AceRadialInputGuard.Acquire();
                AceRadialPauseGate.Hold();
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Autopilot: interaction menu could not take the input: " + e.Message);
                CloseMenu();
                return false;
            }
            wasOpen = true; IsOpenNow = true; releasePending = false;
            soundMark = -4096 + 2 * 16; AvUiSound.Play(AvUiCue.Engage);
            openedAt = lastRefresh = now;
            return true;
        }

        private void OnClosed(float now)
        {
            if (!wasOpen) return;
            wasOpen = false; IsOpenNow = false;
            releasePending = true;
            AceRadialPauseGate.Release(now);
        }

        /// <summary>The Rewired mouse and keyboard go back only once every key and button the menu used is up, so a click or key that closed it cannot also fire.</summary>
        private void FinishClose()
        {
            releasePending = false;
            AceRadialInputGuard.Release();
        }

        /// <summary>Hard close (scene change, focus loss, key unbound, disable): the input comes back at once.</summary>
        private void CloseMenu()
        {
            controller?.Cancel();
            wasOpen = false; IsOpenNow = false;
            releasePending = false;
            AceRadialInputGuard.Release();
            AceRadialPauseGate.ForceRelease();
            if (host != null) host.SetVisible(false);
        }

        private void RunCommands(float now)
        {
            bool ran = false;
            AvUiCue cue = AvUiCue.Hover; // Hover is the silent cue
            foreach (MenuCommand cmd in controller.Commands)
            {
                if (cmd.Kind == MenuCommandKind.Pulse) cue = AvUiCue.Caution;
                else if (cmd.Kind == MenuCommandKind.Back || cmd.Kind == MenuCommandKind.Close) { if (cue == AvUiCue.Hover) cue = AvUiCue.Release; }
                if (cmd.Kind != MenuCommandKind.Execute) continue;
                cue = AvUiCue.Confirm;
                try { cmd.Row.Run?.Invoke(); }
                catch (Exception e) { Plugin.Logger?.LogWarning("Autopilot: menu action '" + cmd.Row.Id + "' failed: " + e.Message); }
                ran = true;
            }
            // every change of aim ticks, so a stick or hat user hears the wheel move without looking for the highlight
            int mark = controller.Cat * 4096 + (controller.Focus + 3) * 16 + (controller.WheelAim + 1) + (controller.Sub != null ? 1 << 20 : 0);
            if (cue == AvUiCue.Hover && mark != soundMark) cue = AvUiCue.Navigate;
            soundMark = mark;
            AvUiSound.Play(cue);
            if (ran && controller.Open) Refresh(now);
        }

        /// <summary>Reads the live game again and lets the model take the new state without moving a row.</summary>
        private void Refresh(float now)
        {
            lastRefresh = now;
            try { model.Reconcile(MenuGame.Build()); controller.Resync(); }
            catch (Exception e) { Plugin.Logger?.LogWarning("Autopilot: interaction menu refresh failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ present

        private void EnsureLayout()
        {
            if (layout != null && layoutW == Screen.width && layoutH == Screen.height) return;
            layoutW = Screen.width; layoutH = Screen.height;
            if (!narrowLogged && layoutW < layoutH * 1.5f)
            {
                narrowLogged = true;
                Plugin.Logger?.LogWarning("Autopilot: interaction menu at " + layoutW + "x" + layoutH + " (narrower than 3:2): the dock may cover ring plates; 16:9 and wider are laid out.");
            }
            layout = MenuLayout.Compute(layoutW, layoutH, model.Categories, text);
            controller?.SetLayout(layout);
            Vector2 pivot = new Vector2(layout.Cx / layoutW, 1f - layout.Cy / layoutH);
            host.Static.pivot = pivot; host.Data.pivot = pivot;
        }

        private void Present(float now)
        {
            bool show = controller != null && (controller.Open || controller.Showing);
            if (!show) { if (host.Canvas.enabled) host.SetVisible(false); return; }
            EnsureLayout();
            host.SetVisible(true);
            float open = controller.Open ? MenuPainter.OpenProgress(openedAt, now) : 1f;
            host.Group.alpha = open;
            if (controller.Open) AceRadialInputGuard.SetCursorVisible(controller.Device != MenuDevice.Pad);
            painter.Paint(controller, layout, now);
            surface.Commit();
            if (surface.Buffer.Overflowed && !overflowLogged)
            {
                overflowLogged = true;
                Plugin.Logger?.LogWarning("Autopilot: interaction menu vector buffer overflowed (" + surface.Buffer.Capacity + " quads); raise QuadCapacity.");
            }
        }

        // ------------------------------------------------------------------ build

        private void BuildCanvas()
        {
            host = AvHudHost.Create("Boscali / Interaction", transform, 4);
            host.Canvas.scaleFactor = 1f; // canvas units are screen pixels: the layout is in pixels
            host.SetVisible(false);
            surface = AvVector.Create(host.Static, "Menu", QuadCapacity);
            text = new MenuTextPool(host.Data);
            text.Prewarm(80); // the ring and a full dock: no label is built on the first open
            painter = new MenuPainter(surface.Buffer, text);
        }
    }
}
