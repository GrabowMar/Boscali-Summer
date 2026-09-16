using System;
using System.Globalization;
using System.Text;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// The opt-in weather debug overlay: one IMGUI window that mirrors the schedule, the live
    /// sky and what the manager is driving toward, and — on the host only — aims the sky through
    /// <see cref="WeatherManager"/>'s debug controls.
    ///
    /// Presentation only: no patch, no reflection, no file or config writes. Styles are built
    /// lazily inside <c>OnGUI</c> because Unity forbids constructing a <c>GUIStyle</c> anywhere
    /// else, and any IMGUI failure latches the overlay off for the session — an IMGUI exception
    /// repeats every frame and would otherwise flood the log.
    /// </summary>
    internal sealed class WeatherDebugOverlay : MonoBehaviour, ISceneService
    {
        private const int WindowId = 0x57454154;
        private const float WindowWidth = 420f;
        private const float SliderCaptionWidth = 96f;
        private const float SliderValueWidth = 64f;
        private const string WindowTitle = "BOSCALI WEATHER — DEBUG";
        private const string ReadOnlyNotice = "READ-ONLY — THE HOST OWNS THE WEATHER";

        private static readonly Rect DefaultWindow = new Rect(48f, 48f, WindowWidth, 160f);

        /// <summary>One buffer for the whole panel: the read-only block and every slider value.</summary>
        private readonly StringBuilder text = new StringBuilder(768);

        private WeatherSettings settings;
        private WeatherManager manager;

        private bool open;
        private bool failed;
        private bool seeded;
        private bool dragging;

        private Rect window = DefaultWindow;

        // Slider backing fields. The manager is never read back into these while an override is
        // live, or the schedule's ramp would fight the handle mid-drag.
        private float sliderConditions;
        private float sliderCloudBase = WeatherModel.MinCloudBase;
        private float sliderWindSpeed;
        private float sliderTurbulence;
        private float sliderHeading;

        private Texture2D background;
        private Font monoFont;
        private GUIStyle panelStyle;
        private GUIStyle readStyle;
        private GUIStyle forcedStyle;

        private string hint;
        private KeyCode hintKey;
        private bool hintCtrl;

        public void Configure(WeatherSettings weatherSettings, WeatherManager weatherManager)
        {
            settings = weatherSettings;
            manager = weatherManager;
        }

        public void ResetForScene()
        {
            open = false;
            seeded = false;
            dragging = false;
            sliderConditions = 0f;
            sliderCloudBase = WeatherModel.MinCloudBase;
            sliderWindSpeed = 0f;
            sliderTurbulence = 0f;
            sliderHeading = 0f;
            window = DefaultWindow;
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (background != null) Destroy(background);
            if (monoFont != null) Destroy(monoFont);
        }

        private void Update()
        {
            // Input only: the slider state is seeded and applied from OnGUI, so a headless
            // process, where OnGUI never runs, never touches the manager.
            if (!CanShow || !Input.GetKeyDown(settings.DebugKey.Value)) return;
            if (settings.DebugKeyRequiresCtrl.Value && !CtrlHeld()) return;
            open = !open;
            if (open)
            {
                seeded = false;
                dragging = false;
            }
        }

        private void OnGUI()
        {
            if (!open || !CanShow) return;
            try
            {
                EnsureStyles();
                window = ClampToScreen(window);
                window = GUILayout.Window(WindowId, window, DrawWindow, WindowTitle, panelStyle,
                    GUILayout.Width(WindowWidth));
                window = ClampToScreen(window);
            }
            catch (Exception exception)
            {
                // Latch: an IMGUI failure repeats every frame, so log it once and stay closed.
                failed = true;
                open = false;
                Plugin.Logger?.LogWarning("Weather debug overlay disabled after an IMGUI failure: " + exception);
            }
        }

        private void DrawWindow(int id)
        {
            // The title bar sits above the client area; the top strip is the drag handle.
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 16f));

            GUILayout.BeginVertical(GUILayout.Width(WindowWidth));
            WeatherSnapshot snapshot = manager.Snapshot;
            GUILayout.Label(Readout(snapshot), readStyle);
            DrawControls(snapshot);
            GUILayout.EndVertical();
        }

        // ---- Read-only block -------------------------------------------------------------------

        private string Readout(WeatherSnapshot snapshot)
        {
            text.Length = 0;
            if (!snapshot.Available)
            {
                text.Append("NO MISSION");
                return text.ToString();
            }

            float now = snapshot.MissionTime;
            WeatherState schedule = snapshot.Model;
            WeatherState world = snapshot.Live;
            int front = WeatherModel.FrontIndex(now);

            text.Append("MISSION   ").Append(WeatherReadout.Clock(now));
            text.Append("   FRONT #").Append(front);
            text.Append("  PHASE ").Append((int)WeatherModel.PhaseIndex(front));
            text.Append("  BLEND ").Append(WeatherReadout.Percent01(WeatherModel.BlendWeight(now)));
            text.Append('\n');

            text.Append("SCHEDULE  ").Append(WeatherReadout.Regime(schedule.Regime));
            text.Append("  conditions ").Append(WeatherReadout.Percent01(schedule.Conditions));
            text.Append("  base ").Append(WeatherReadout.Meters(schedule.CloudBase));
            text.Append('\n');

            text.Append("WORLD     ").Append(WeatherReadout.Regime(world.Regime));
            text.Append("  conditions ").Append(WeatherReadout.Percent01(world.Conditions));
            text.Append("  base ").Append(WeatherReadout.Meters(world.CloudBase));
            text.Append('\n');

            text.Append("DRIVEN    cond ").Append(Signed(world.Conditions - schedule.Conditions));
            text.Append("  base ").Append(Signed(world.CloudBase - schedule.CloudBase));
            text.Append("  wind ").Append(Signed(world.WindSpeed - schedule.WindSpeed));
            text.Append("  turb ").Append(Signed(world.Turbulence - schedule.Turbulence));
            text.Append("  hdg ").Append(Signed(Mathf.DeltaAngle(schedule.WindHeading, world.WindHeading)));
            text.Append('\n');

            text.Append("WIND      mean ").Append(Wind(world.WindSpeed, world.WindHeading));
            text.Append("  local ").Append(Wind(snapshot.LocalWindSpeed, snapshot.LocalWindHeading));
            text.Append('\n');

            text.Append("SKY       occlusion ").Append(WeatherReadout.Percent01(snapshot.CloudOcclusion));
            text.Append("  daylight ").Append(WeatherReadout.Percent01(snapshot.DaylightFactor));
            text.Append('\n');

            // The manager exposes no remaining-hold reading; WeatherDrive.HoldSeconds caps a
            // foreign write's hold at 40 s but not when it started. Say so instead of guessing.
            text.Append("STATE     ").Append(StateWord(snapshot)).Append("  hold n/a");
            text.Append('\n');

            text.Append("NEXT      ");
            WeatherForecast forecast = manager.Forecast;
            if (forecast == null || forecast.Count == 0)
            {
                text.Append(WeatherReadout.Unknown);
            }
            else
            {
                text.Append(WeatherReadout.Regime(forecast[0].State.Regime));
                text.Append(" in ").Append(WeatherReadout.Clock(forecast[0].AtSeconds - now));
                if (forecast.Count > 1)
                {
                    text.Append("   then ").Append(WeatherReadout.Regime(forecast[1].State.Regime));
                    text.Append(" in ").Append(WeatherReadout.Clock(forecast[1].AtSeconds - now));
                }
            }
            text.Append('\n');

            AppendStorms(snapshot);

            return text.ToString();
        }

        /// <summary>
        /// The storm half of the readout: where the cells are, which one is chasing the reader,
        /// and how hard it is raining. This is the block that answers "are the supercells real".
        /// </summary>
        private void AppendStorms(WeatherSnapshot snapshot)
        {
            TryPlayerPosition(out float playerX, out float playerZ);
            text.Append("STORM     ").Append(snapshot.CellCount).Append(" cell(s)   rain ")
                .Append(WeatherReadout.Percent01(snapshot.RainIntensity)).Append("   tier ")
                .Append(StormReadout.Warning(snapshot.Warning));
            text.Append('\n');

            for (int i = 0; i < snapshot.CellCount && i < StormField.MaxCells; i++)
            {
                StormCell cell = snapshot.Cells[i];
                text.Append("   #").Append(cell.Slot).Append(' ')
                    .Append(StormReadout.Kind(cell.Kind)).Append("  int ")
                    .Append(WeatherReadout.Percent01(cell.Intensity)).Append("  top ")
                    .Append(WeatherReadout.Meters(cell.TopHeight)).Append("  ")
                    .Append(StormReadout.NauticalMiles(cell.DistanceTo(playerX, playerZ))).Append(' ')
                    .Append(StormReadout.BearingTo(playerX, playerZ, cell.X, cell.Z));
                text.Append('\n');
            }
        }

        private static bool TryPlayerPosition(out float x, out float z)
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null)
            {
                x = 0f;
                z = 0f;
                return false;
            }

            Vector3 position = cameras.transform.position;
            x = position.x;
            z = position.z;
            return true;
        }

        private string StateWord(WeatherSnapshot snapshot)        {
            if (manager.OverrideActive) return "OVERRIDE";
            return snapshot.Overridden ? "EXTERNAL" : "SCHEDULE";
        }

        // ---- Controls --------------------------------------------------------------------------

        private void DrawControls(WeatherSnapshot snapshot)
        {
            if (!manager.HostAuthority)
            {
                GUILayout.Label(ReadOnlyNotice, readStyle);
                return;
            }

            WeatherRegime current = manager.Snapshot.Model.Regime;
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < WeatherRegimes.Count; i++)
            {
                WeatherRegime regime = WeatherRegimes.FromIndex(i);
                bool active = manager.OverrideActive && regime == current;
                if (GUILayout.Button(WeatherRegimes.Label(regime), active ? forcedStyle : GUI.skin.button))
                    manager.ForceRegime(regime);
            }
            GUILayout.EndHorizontal();

            // The highlight above is styling; this line is the record of what is actually forced.
            GUILayout.Label(manager.OverrideActive
                ? "FORCED REGIME  " + WeatherReadout.Regime(current)
                : "NO FORCE  (SCHEDULE: " + WeatherReadout.Regime(current) + ")", readStyle);

            bool wasEnabled = GUI.enabled;
            try
            {
                GUI.enabled = wasEnabled && manager.OverrideActive;
                if (GUILayout.Button("RELEASE TO SCHEDULE")) manager.ReleaseOverride();
            }
            finally
            {
                GUI.enabled = wasEnabled;
            }

            // While the schedule owns the sky the sliders mirror the world; while an override is
            // live they are the handle's state alone, and never re-seeded mid-drag.
            if (snapshot.Available && !manager.OverrideActive && (!seeded || !dragging))
            {
                Seed(snapshot.Live);
                seeded = true;
            }

            bool moved = false;
            moved |= Slider("CONDITIONS", ref sliderConditions, 0f, 1f, 0.01f, "0.00", "");
            moved |= Slider("CLOUD BASE", ref sliderCloudBase, WeatherModel.MinCloudBase, WeatherModel.MaxCloudBase,
                25f, "0", " M");
            moved |= Slider("WIND SPEED", ref sliderWindSpeed, 0f, 30f, 0.5f, "0.0", " M/S");
            moved |= Slider("TURBULENCE", ref sliderTurbulence, 0f, 1f, 0.01f, "0.00", "");
            moved |= Slider("WIND HEADING", ref sliderHeading, 0f, 360f, 5f, "0", "°");
            dragging = moved;
            if (moved) manager.ApplyOverride(SliderState());

            GUILayout.Label(Hint(), readStyle);
        }

        /// <summary>
        /// Draw one absolute slider. Returns true when the handle moved this frame; the caller
        /// applies the whole set so a drag is a set, never a nudge.
        /// </summary>
        private bool Slider(string caption, ref float value, float min, float max, float step,
                            string format, string suffix)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(caption, readStyle, GUILayout.Width(SliderCaptionWidth));
            GUI.changed = false;
            float raw = GUILayout.HorizontalSlider(value, min, max);
            bool moved = GUI.changed;
            value = Mathf.Clamp(Mathf.Round(raw / step) * step, min, max);
            text.Length = 0;
            text.Append(value.ToString(format, CultureInfo.InvariantCulture)).Append(suffix);
            GUILayout.Label(text.ToString(), readStyle, GUILayout.Width(SliderValueWidth));
            GUILayout.EndHorizontal();
            return moved;
        }

        private void Seed(WeatherState live)
        {
            sliderConditions = WeatherRegimes.Clamp01(Quantize(live.Conditions, 0.01f));
            sliderCloudBase = WeatherModel.ClampCloudBase(Quantize(live.CloudBase, 25f));
            sliderWindSpeed = Mathf.Clamp(Quantize(live.WindSpeed, 0.5f), 0f, 30f);
            sliderTurbulence = WeatherRegimes.Clamp01(Quantize(live.Turbulence, 0.01f));
            sliderHeading = Mathf.Clamp(Quantize(live.WindHeading, 5f), 0f, 360f);
        }

        private WeatherState SliderState() => new WeatherState(
            WeatherRegimes.FromConditions(sliderConditions),
            sliderConditions,
            WeatherModel.ClampCloudBase(sliderCloudBase),
            Mathf.Max(0f, sliderWindSpeed),
            sliderHeading,
            WeatherRegimes.Clamp01(sliderTurbulence));

        // ---- Presentation ----------------------------------------------------------------------

        private string Hint()
        {
            if (hint == null || hintKey != settings.DebugKey.Value || hintCtrl != settings.DebugKeyRequiresCtrl.Value)
            {
                hintKey = settings.DebugKey.Value;
                hintCtrl = settings.DebugKeyRequiresCtrl.Value;
                hint = (hintCtrl ? "Ctrl+" : "") + hintKey +
                       " toggles this overlay (configure under [Weather] in the F1 config manager).";
            }
            return hint;
        }

        private void EnsureStyles()
        {
            // Unity forbids GUIStyle construction off the main thread; OnGUI is the safe point,
            // and every creation is guarded so each style is built exactly once.
            if (background == null)
            {
                background = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                background.SetPixel(0, 0, new Color(0.03f, 0.035f, 0.045f, 1f));
                background.Apply(false);
            }
            if (panelStyle == null)
            {
                // GUILayout.Window sizes to its content, so an opaque window background is the
                // only way to guarantee the whole panel stays readable in flight.
                panelStyle = new GUIStyle(GUI.skin.window)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { background = background, textColor = new Color(0.95f, 0.86f, 0.55f, 1f) },
                    onNormal = { background = background, textColor = new Color(0.95f, 0.86f, 0.55f, 1f) }
                };
            }
            if (readStyle == null)
            {
                readStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = false,
                    wordWrap = false,
                    fontSize = 10,
                    normal = { textColor = new Color(0.84f, 0.87f, 0.9f, 1f) }
                };
                monoFont = Font.CreateDynamicFontFromOSFont("Consolas", 10);
                if (monoFont != null) readStyle.font = monoFont;
            }
            if (forcedStyle == null)
            {
                forcedStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                forcedStyle.normal.textColor = new Color(1f, 0.83f, 0.25f, 1f);
                forcedStyle.hover.textColor = forcedStyle.normal.textColor;
            }
        }

        private static string Signed(float value) =>
            value.ToString("+0.00;-0.00;+0.00", CultureInfo.InvariantCulture);

        private static string Wind(float speed, float heading) =>
            speed.ToString("0.0", CultureInfo.InvariantCulture) + " M/S @ " +
            WeatherReadout.Compass16(heading) + " " +
            Mathf.RoundToInt(WeatherState.WrapHeading(heading)) + "°";

        private static float Quantize(float value, float step) => Mathf.Round(value / step) * step;

        private static Rect ClampToScreen(Rect rect)
        {
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
            return rect;
        }

        private bool CanShow => !failed && settings != null && manager != null &&
                                settings.Enabled.Value && settings.DebugControls.Value && !Application.isBatchMode;

        private static bool CtrlHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }
}
