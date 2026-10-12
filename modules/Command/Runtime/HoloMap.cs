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
    /// The HOLO MAP: the map key's middle stop. MINI (the game's small corner map) -> key -> HOLO -> key -> MFD (the game's maximised map) -> key -> MINI. The HOLO is a wire
    /// relief of the real ground around the player's aircraft, seen from a fixed tilt with the aircraft in the middle (heading up, or north up), drawn into a texture of the mod's
    /// own (<see cref="HoloGrid"/> + <see cref="HoloView"/>, no game camera involved) and shown over the bottom-left corner of the HUD as projected light, with the aircraft, range
    /// rings, airbases, the units the game's map shows and the selected target drawn over it. The pilot keeps flying: nothing here captures the cursor, blocks an input or pauses.
    /// <para>Only the key path is diverted. The game's key handler (<c>ExtraUiInput.Update</c>) calls <c>DynamicMap.Maximize</c> on the Rewired "Map" button; two patches
    /// (<see cref="HoloMapKeyScopePatch"/>, <see cref="HoloMapKeyPatch"/>) tell this class that call is the key's, and it may take the press instead (<see cref="KeyTakesMaximize"/>). The mod's
    /// own callers of Maximize (menus, panels, automation) open the maximised map directly, as before, and close the holo.</para>
    /// <para>It closes on Esc, a pause, death, eject, no living aircraft, the HUD or the corner map going away, the setting turned off, any maximised map and a scene change, and fails
    /// open: no baked terrain, no additive UI material or no HUD canvas, and the key goes straight to the maximised map. The state machine is <see cref="HoloMapState"/>.</para>
    /// </summary>
    // Runs after the native camera and map (order 2) have settled.
    [DefaultExecutionOrder(100)]
    internal sealed partial class HoloMap : MonoBehaviour, ISceneService
    {
        private const float GrowSeconds = .18f;
        private const float RenderInterval = .05f;
        private const float ScanInterval = .25f;
        private const float FieldRetry = 2f;
        private const float GrownScale = 1f, StartScale = .6f;

        internal static HoloMap Live { get; private set; }

        /// <summary>The hologram is up (the pilot is at HOLO): the HUD's own corner-map dressing is to be hidden meanwhile.</summary>
        private static bool active;
        /// <summary>The hologram is up. Also tells the HUD (Core HudMapState) so its own corner-map dressing steps aside.</summary>
        public static bool Active { get => active; private set { active = value; BoscaliSummer.Core.Contracts.HudMapState.HoloActive = value; } }

        /// <summary>True while the game's own <c>ExtraUiInput.Update</c> runs: a Maximize call made then is the map key's.</summary>
        internal static bool InKeyUpdate { get; set; }

        private CommandSettings settings;
        private readonly HoloMapState state = new HoloMapState();
        private TerrainHeightField field;
        private float nextField, nextRender, nextScan;
        private float grow;
        private Aircraft plane;
        private float planeX, planeY, planeZ, heading, radius;
        private string failure = "";

        /// <summary>The sim hook's range and orientation in place of the saved settings; memory only, cleared with the scene.</summary>
        internal int? RangeOverride { get; set; }
        internal bool? NorthOverride { get; set; }

        private int RangeKm
        {
            get
            {
                int km = RangeOverride ?? settings.HoloRangeKm.Value;
                return km == 10 || km == 40 ? km : 20;
            }
        }

        private bool NorthUp => NorthOverride ?? settings.HoloNorthUp.Value;

        internal void Configure(CommandSettings value)
        {
            settings = value;
            Live = this;
            DynamicMapMaximizePatch.OnMaximized += NativeMaximized;
            DynamicMapMinimizePatch.OnMinimized += NativeMinimized;
        }

        // ------------------------------------------------------------------ the key

        /// <summary>
        /// The Maximize patch asks, before the original runs: true when the holo takes this press instead (the original is then skipped). False for every Maximize that is not the map
        /// key's, and for the key itself when the holo is off, unavailable, or already up (that press is the way on to the maximised map).
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
            bool take = state.KeyMaximize(available);
            if (take) Opened();
            else if (state.Stage == HoloStage.Mfd) Hide();
            return take;
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

        /// <summary>Everything the holo needs, in one test: the setting, a map the game lets open, a living aircraft, the HUD up, and the pieces (terrain, textures, material, UI) built.</summary>
        private bool CanOpen()
        {
            if (Application.isBatchMode || settings == null || !settings.HoloMap.Value || !DynamicMap.AllowedToOpen) return false;
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

        private void Opened()
        {
            nextRender = nextScan = 0f;
            grow = 0f;
            Active = true;
            ShowRoot();
            Refresh();
            ApplyGrow();
        }

        /// <summary>Opens or closes the holo without the key (the sim hook); true when it is up afterwards.</summary>
        internal bool SimOpen(bool open)
        {
            if (!open)
            {
                state.Close();
                Hide();
                return false;
            }
            if (state.Open(state.Stage == HoloStage.Mini && CanOpen())) Opened();
            return state.Holo;
        }

        // ------------------------------------------------------------------ the frame

        private void LateUpdate()
        {
            if (Application.isBatchMode || settings == null) return;
            state.Sync(DynamicMap.mapMaximized);
            PreloadField();
            if (state.Holo && !Wanted()) state.Close();
            if (!state.Holo)
            {
                if (Active || (root != null && root.activeSelf)) Hide();
                return;
            }
            if (rt == null || root == null)
            {
                state.Close();
                Hide();
                return;
            }
            Active = true;
            grow = Mathf.MoveTowards(grow, 1f, Time.unscaledDeltaTime / GrowSeconds);
            if (Time.unscaledTime >= nextRender)
            {
                nextRender = Time.unscaledTime + RenderInterval;
                Refresh();
            }
            ApplyGrow();
        }

        /// <summary>The terrain is loaded while the aircraft sits on the ramp, not as a hitch at the first key press.</summary>
        private void PreloadField()
        {
            if (field != null || Time.unscaledTime < nextField || !settings.HoloMap.Value) return;
            nextField = Time.unscaledTime + FieldRetry;
            if (FlightReady(out _)) MfdTerrainRelief.TryGetHeightField(out field);
        }

        /// <summary>The holo stays up only while all of this holds: the setting, no Esc, a living aircraft, the HUD and the corner map showing, no pause or cinematic mode.</summary>
        private bool Wanted()
        {
            return settings.HoloMap.Value && !Input.GetKeyDown(KeyCode.Escape) && FlightReady(out plane) && SceneReady();
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

        /// <summary>The holo goes away: the corner map comes back exactly as the game had it, and nothing renders until the next open.</summary>
        private void Hide()
        {
            grow = 0f;
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
                { "additiveMaterial", additive != null },
                { "renderTexture", rt != null && rt.IsCreated() },
                { "vertexCount", mesh != null ? mesh.vertexCount : 0 },
                { "litVertices", litVertices },
                { "marksDrawn", unitsDrawn + basesDrawn + (Active ? 1 : 0) },
                { "unitMarks", unitsDrawn },
                { "airbaseMarks", basesDrawn },
                { "labelsUsed", labelsUsed },
                { "lastRenderMs", lastRenderMs },
                { "mapHidden", mapGroup != null && mapGroup.alpha < .01f },
                { "grow", grow },
                { "rangeKm", settings != null ? RangeKm : 0 },
                { "northUp", settings != null && NorthUp },
                { "failure", failure },
            };
        }

        // ------------------------------------------------------------------ the scene

        public void ResetForScene()
        {
            ReleaseAll();
            state.Reset();
            field = null;
            plane = null;
            failure = "";
            RangeOverride = null;
            NorthOverride = null;
            nextField = nextRender = nextScan = grow = 0f;
            Active = false;
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
