using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Patches;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The TAC MAP: the corner map of the HUD as a standing mini-hologram, and the HOLO MAP the map key HELD grows it into. One hologram in two sizes. While the pilot flies with the HUD up (and the TAC MAP
    /// is on HOLO) the bottom-left corner shows a wire relief of the real ground around the aircraft in a square of 300 units (the MINI: the aircraft in the middle, the view wedge, one range ring, the units the
    /// game's map shows, the selected target and the nearest airbase, at 10 Hz). Holding the map key past 0.2 s grows the same picture to 46 % of the screen height over 180 ms, at the held range and with the
    /// full marks (the BIG one, at 20 Hz); letting go shrinks it back and opens no MFD. A tap opens the game's maximised map (MFD) as it always did, and while that is up nothing of the holo draws; when it closes the
    /// MINI returns. On CLASSIC the MINI never draws and the game's own corner map is left alone, but the key still raises the big hologram. The picture is drawn by <see cref="HoloGrid"/> + <see cref="HoloView"/> (no game
    /// camera involved) into a texture of the mod's own and shown as projected light with the marks laid over it (HoloMap.Marks.cs, HoloMap.Ui.cs); the pilot keeps flying: nothing here captures the cursor, blocks an input or pauses.
    /// <para>Only the key-down Maximize is diverted. The game's key handler (<c>ExtraUiInput.Update</c>) calls <c>DynamicMap.Maximize</c> the frame the Rewired "Map" button goes down; two patches
    /// (<see cref="HoloMapKeyScopePatch"/>, <see cref="HoloMapKeyPatch"/>) tell this class that call is the key's, and it swallows it and waits (<see cref="KeyTakesMaximize"/>). Each frame it polls the
    /// same Rewired action for "held": released within 0.2 s it calls Maximize itself, outside the key path, so every Maximize listener runs as for the game's own call; held longer it shows the
    /// big holo and takes it down on release. Minimize is never touched. The mod's own callers of Maximize (menus, panels, automation) open the maximised map directly, as before.</para>
    /// <para>What is shown follows <see cref="HoloPresentation.Choose"/>; the machine for the key is <see cref="HoloMapState"/>. The game's corner map is hidden through a CanvasGroup of ours on its root while the MINI or
    /// the BIG is up and put back exactly the moment neither is: on Esc, a pause, death, eject, no living aircraft, the HUD or the corner map going away, CLASSIC, any maximised map and a scene change. It fails open:
    /// no baked terrain, no additive UI material, no HUD canvas or any exception while drawing leaves the game's corner map visible and untouched, and the key goes straight to the maximised map on key down.</para>
    /// </summary>
    // Runs after the native camera and map (order 2) have settled.
    [DefaultExecutionOrder(100)]
    internal sealed partial class HoloMap : MonoBehaviour, ISceneService
    {
        private const float ScanInterval = .25f;
        private const float FieldRetry = 2f;

        internal static HoloMap Live { get; private set; }

        /// <summary>The hologram (MINI or BIG) is up: the HUD's own corner-map dressing is to be hidden meanwhile.</summary>
        private static bool active;
        /// <summary>The hologram is up, in either size. Also tells the HUD (Core HudMapState) so its own corner-map dressing steps aside.</summary>
        public static bool Active { get => active; private set { active = value; BoscaliSummer.Core.Contracts.HudMapState.HoloActive = value; } }

        /// <summary>True while the game's own <c>ExtraUiInput.Update</c> runs: a Maximize call made then is the map key's.</summary>
        internal static bool InKeyUpdate { get; set; }

        private CommandSettings settings;
        private readonly HoloMapState state = new HoloMapState();
        // The player and action the game's own key handler reads (ExtraUiInput: ReInput.players.GetPlayer(0), "Map").
        private Rewired.Player keyPlayer;
        private TerrainHeightField field;
        private float nextField, nextPrepare, nextRender, nextScan;
        // What is up: the presentation asked for now, how far the one picture has grown (0 MINI .. 1 BIG, over 180 ms) and how far it has faded in (0..1, after it first shows).
        private HoloShape shape;
        private float size, appear;
        // An exception while drawing: nothing more is drawn until the scene changes, and the game's corner map is back.
        private bool broken;
        private Aircraft plane;
        private float planeX, planeY, planeZ, heading, radius;
        private string failure = "";

        /// <summary>The sim hook's settings in place of the saved ones; memory only, cleared with the scene.</summary>
        internal int? RangeOverride { get; set; }
        internal int? MiniRangeOverride { get; set; }
        internal bool? NorthOverride { get; set; }
        internal TacMapMode? TacOverride { get; set; }

        private int MiniKm => HoloPresentation.MiniRange(MiniRangeOverride ?? settings.TacMapRangeKm.Value);

        private int BigKm => HoloPresentation.BigRange(RangeOverride ?? settings.HoloRangeKm.Value, MiniKm);

        /// <summary>The radius of the picture being drawn: the MINI's, or the held map's.</summary>
        private int RangeKm => shape == HoloShape.Mini ? MiniKm : BigKm;

        private bool NorthUp => NorthOverride ?? settings.HoloNorthUp.Value;

        /// <summary>The TAC MAP is on HOLO: the corner map is the standing MINI. CLASSIC leaves the game's own corner map alone.</summary>
        private bool MiniOn => (TacOverride ?? settings.TacMap.Value) == TacMapMode.Holo;

        internal void Configure(CommandSettings value)
        {
            settings = value;
            Live = this;
            DynamicMapMaximizePatch.OnMaximized += NativeMaximized;
            DynamicMapMinimizePatch.OnMinimized += NativeMinimized;
        }

        // ------------------------------------------------------------------ the key

        /// <summary>
        /// The Maximize patch asks, before the original runs: true when the key-down press is swallowed (the original is then skipped) until the next frames tell a tap from a hold. False for every
        /// Maximize that is not the map key's, and for the key itself when the holo is off or unavailable (the map opens at once, as in the vanilla game) or the sim hook's holo is up.
        /// </summary>
        internal static bool KeyTakesMaximize()
        {
            HoloMap holo = Live;
            if (!InKeyUpdate || holo == null) return false;
            try { return holo.OnKey(); }
            catch (Exception error)
            {
                PatchGuard.Report("Command.HoloMap.Key", error);
                return false;
            }
        }

        private bool OnKey()
        {
            bool available = state.Stage == HoloStage.Mini && CanOpen();
            bool take = state.KeyDown(available, Time.unscaledTime);
            if (!take && state.Stage == HoloStage.Mfd) Hide();
            return take;
        }

        /// <summary>The map key is down now: the same Rewired player and action the game's key handler reads.</summary>
        private bool KeyHeld()
        {
            if (!Rewired.ReInput.isReady) return false;
            if (keyPlayer == null) keyPlayer = Rewired.ReInput.players.GetPlayer(0);
            return keyPlayer != null && keyPlayer.GetButton("Map");
        }

        /// <summary>A tap: the maximised map the game would have opened on key down, called here outside the key path so the Maximize patches and every listener run as for the game's own call.</summary>
        private static void OpenMfd()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
        }

        private void NativeMaximized()
        {
            state.Maximized();
            Hide();
        }

        private void NativeMinimized()
        {
            state.Minimized();
            Hide();
        }

        /// <summary>Everything the held holo needs, in one test: the setting, a map the game lets open, a living aircraft, the HUD up, and the pieces (terrain, textures, material, UI) built.</summary>
        private bool CanOpen()
        {
            if (Application.isBatchMode || broken || settings == null || !settings.HoloMap.Value || !DynamicMap.AllowedToOpen) return false;
            if (!FlightReady(out plane) || !SceneReady()) return false;
            return Prepare();
        }

        private bool Prepare()
        {
            if (field == null && !MfdTerrainRelief.TryGetHeightField(out field)) return Fail("no baked terrain for this map");
            if (!PrepareRender()) return Fail("no line shader");
            if (!PrepareAdditive()) return Fail("no additive UI material");
            if (!PrepareUi()) return Fail("no HUD canvas");
            failure = "";
            return true;
        }

        private bool Fail(string reason)
        {
            failure = reason;
            return false;
        }

        /// <summary>The pieces are all built, or one try to build them now (at most every two seconds: a map without baked terrain is asked again, not every frame).</summary>
        private bool Ready()
        {
            if (field != null && rt != null && root != null && additive != null) return true;
            if (Time.unscaledTime < nextPrepare) return false;
            nextPrepare = Time.unscaledTime + FieldRetry;
            return Prepare();
        }

        /// <summary>Opens or closes the big holo without the key (the sim hook); true when it is up afterwards. An opened holo stays up until it is closed, whatever the map key does.</summary>
        internal bool SimOpen(bool open)
        {
            if (open) state.Open(state.Stage == HoloStage.Mini && CanOpen());
            else state.Close();
            Step();
            return state.Holo;
        }

        // ------------------------------------------------------------------ the frame

        private void LateUpdate()
        {
            if (Application.isBatchMode || settings == null) return;
            Step();
        }

        /// <summary>One frame, guarded: an exception takes the holo down for good (until the scene changes) and gives the game's corner map back.</summary>
        private void Step()
        {
            if (broken || settings == null || Application.isBatchMode) return;
            try { Frame(); }
            catch (Exception error)
            {
                broken = true;
                failure = "exception: " + error.GetType().Name;
                PatchGuard.Report("Command.HoloMap.Frame", error);
                state.Close();
                Hide();
            }
        }

        private void Frame()
        {
            state.Sync(DynamicMap.mapMaximized);
            PreloadField();
            // The corner map the holo stands in for is showing: a living aircraft, the HUD up, no pause or cinematic mode, no maximised map.
            bool up = FlightReady(out plane) && SceneReady();
            // The held holo (and a tap waiting to be told from a hold) stays only while the setting is on, there is no Esc and the corner map is showing.
            bool keyed = up && settings.HoloMap.Value && !Input.GetKeyDown(KeyCode.Escape);
            if ((state.Holo || state.Stage == HoloStage.Pending) && !keyed) state.Close();
            if (state.Tick(Time.unscaledTime, state.KeyDriven && KeyHeld()) == HoloStep.OpenMfd) OpenMfd();

            HoloShape want = HoloPresentation.Choose(state.Stage, MiniOn, up);
            if (want != HoloShape.None && !Ready())
            {
                // The pieces are not there (no baked terrain, no material, no canvas): a hold that cannot be drawn is no hold, and the game's corner map stays.
                if (want == HoloShape.Big) state.Close();
                want = HoloShape.None;
            }
            if (want == HoloShape.None)
            {
                if (shape != HoloShape.None || Active || (root != null && root.activeSelf)) Hide();
                return;
            }
            Present(want);
        }

        /// <summary>
        /// The presentation for this frame: the one picture grows or shrinks toward its size, the layout follows, a picture is drawn when one is due (every frame while it changes size, then at the
        /// presentation's rate) and the game's corner map fades behind it.
        /// </summary>
        private void Present(HoloShape want)
        {
            bool fresh = shape == HoloShape.None, shifted = want != shape;
            shape = want;
            Active = true;
            if (fresh)
            {
                appear = size = 0f;
                nextScan = 0f;
                ShowRoot();
            }
            float seconds = Time.unscaledDeltaTime, before = size;
            appear = Mathf.MoveTowards(appear, 1f, seconds / HoloPresentation.GrowSeconds);
            size = HoloPresentation.Size(size, want, seconds);
            bool laid = Layout();
            float now = Time.unscaledTime;
            if (fresh || shifted || laid || size != before || now >= nextRender)
            {
                nextRender = now + HoloPresentation.RenderInterval(want, false);
                Refresh();
            }
            ApplyPresence();
        }

        /// <summary>The terrain is loaded while the aircraft sits on the ramp, not as a hitch at the first picture.</summary>
        private void PreloadField()
        {
            if (field != null || Time.unscaledTime < nextField || !(settings.HoloMap.Value || MiniOn)) return;
            nextField = Time.unscaledTime + FieldRetry;
            if (FlightReady(out _)) MfdTerrainRelief.TryGetHeightField(out field);
        }

        /// <summary>The local aircraft is flown by a living pilot.</summary>
        private static bool FlightReady(out Aircraft aircraft)
        {
            aircraft = null;
            if (!GameManager.GetLocalAircraft(out Aircraft local) || local == null || local.disabled) return false;
            Pilot[] pilots = local.pilots;
            if (pilots != null && pilots.Length > 0 && pilots[0] != null && (pilots[0].dead || pilots[0].ejected)) return false;
            aircraft = local;
            return true;
        }

        /// <summary>The native HUD is up, nothing pauses or hides it, and the corner map the holo stands in for is showing.</summary>
        private static bool SceneReady()
        {
            if (!CockpitOverlayGate.Open()) return false;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            return map != null && map.gameObject.activeInHierarchy && !DynamicMap.mapMaximized;
        }

        /// <summary>The holo goes away: the corner map comes back exactly as the game had it, and nothing renders until the next presentation.</summary>
        private void Hide()
        {
            shape = HoloShape.None;
            appear = size = 0f;
            Active = false;
            if (root != null && root.activeSelf) root.SetActive(false);
            RestoreMinimap();
        }

        /// <summary>The holo as the sim hook reports it.</summary>
        internal Dictionary<string, object> State()
        {
            return new Dictionary<string, object>
            {
                { "ok", true },
                { "active", Active },
                { "stage", state.Stage.ToString() },
                { "presentation", shape == HoloShape.Mini ? "mini" : shape == HoloShape.Big ? "big" : "none" },
                { "tacMode", settings != null ? (MiniOn ? "Holo" : "Classic") : "" },
                { "additiveMaterial", additive != null },
                { "renderTexture", rt != null && rt.IsCreated() },
                { "vertexCount", mesh != null ? mesh.vertexCount : 0 },
                { "litVertices", litVertices },
                { "marksDrawn", unitsDrawn + basesDrawn + (Active ? 1 : 0) },
                { "unitMarks", unitsDrawn },
                { "unitCap", HoloPresentation.UnitCap(shape) },
                { "airbaseMarks", basesDrawn },
                { "labelsUsed", labelsUsed },
                { "wedgeDegrees", wedgeDegrees },
                { "gridRef", gridRef },
                { "mapHidden", mapGroup != null && mapGroup.alpha < .01f },
                { "nativeMapAlpha", mapGroup != null ? mapGroup.alpha : 1f },
                { "plateAlpha", nativePlate != null ? nativePlate.canvasRenderer.GetAlpha() : 1f },
                { "plateFound", nativePlate != null },
                { "shadeAlpha", shade != null ? shade.color.a : 0f },
                { "grow", size },
                { "presence", appear },
                { "sidePixels", side },
                { "rangeKm", settings != null ? RangeKm : 0 },
                { "miniRangeKm", settings != null ? MiniKm : 0 },
                { "bigRangeKm", settings != null ? BigKm : 0 },
                { "northUp", settings != null && NorthUp },
                { "lastRenderMs", lastRenderMs },
                { "worstRenderMs", worstRenderMs },
                { "renders", renders },
                { "lastScanMs", lastScanMs },
                { "worstScanMs", worstScanMs },
                { "scans", scans },
                { "broken", broken },
                { "failure", failure },
            };
        }

        /// <summary>Clears the worst timings and the counts (the sim hook, after the warm-up).</summary>
        internal void ResetTimings()
        {
            lastRenderMs = worstRenderMs = lastScanMs = worstScanMs = 0f;
            renders = scans = 0;
        }

        // ------------------------------------------------------------------ the scene

        public void ResetForScene()
        {
            ReleaseAll();
            state.Reset();
            keyPlayer = null;
            field = null;
            plane = null;
            failure = "";
            RangeOverride = MiniRangeOverride = null;
            NorthOverride = null;
            TacOverride = null;
            broken = false;
            shape = HoloShape.None;
            nextField = nextPrepare = nextRender = nextScan = size = appear = 0f;
            Active = false;
            ResetTimings();
        }

        private void OnDestroy()
        {
            DynamicMapMaximizePatch.OnMaximized -= NativeMaximized;
            DynamicMapMinimizePatch.OnMinimized -= NativeMinimized;
            if (Live == this) Live = null;
            InKeyUpdate = false;
            ResetForScene();
        }
    }
}
