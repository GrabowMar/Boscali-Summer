using System.Text;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Opt-in weather debug window (<c>DebugControls</c>, Ctrl+F11 by default). Reads the field
    /// at the camera and, on the host, forces a regime through the key so every client follows.
    /// The rain preview is client-local and presentation-only: it overrides what the renderers
    /// are told about rain at the camera, never the field, the key or the network.
    /// </summary>
    internal sealed class WeatherDebugOverlay : MonoBehaviour, ISceneService
    {
        private static readonly float[] PreviewRates = { -1f, 0f, 1f, 5f, 15f, 40f, 100f };

        private WeatherSettings settings;
        private SynopticWeather manager;
        private bool open;
        private Rect window = new Rect(20f, 80f, 420f, 460f);
        private readonly StringBuilder text = new StringBuilder(512);

        public void Configure(WeatherSettings weatherSettings, SynopticWeather owner)
        {
            settings = weatherSettings;
            manager = owner;
            if (weatherSettings != null && weatherSettings.DebugControls.Value)
            {
                Plugin.Logger?.LogInfo("[Weather] Debug window: " +
                    (weatherSettings.DebugKeyRequiresCtrl.Value ? "Ctrl+" : "") +
                    weatherSettings.DebugKey.Value + " toggles the weather overlay.");
            }
        }

        public void ResetForScene()
        {
        }

        private void Update()
        {
            if (settings == null || !settings.DebugControls.Value) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetKeyDown(settings.DebugKey.Value) && (ctrl || !settings.DebugKeyRequiresCtrl.Value)) open = !open;
        }

        private void OnGUI()
        {
            if (!open || settings == null || !settings.DebugControls.Value || manager == null) return;
            window = GUILayout.Window(0x5eed_0f1, window, Draw, "Boscali weather");
        }

        private void Draw(int id)
        {
            if (!manager.Ready)
            {
                GUILayout.Label(manager.HostAuthority ? "No mission running." : "Waiting for the host's weather key.");
                GUI.DragWindow();
                return;
            }

            WeatherField field = manager.Field;
            WeatherPoint p = manager.Local;
            RegimeState regime = field.Regime;
            text.Length = 0;
            text.Append("Regime ").Append(RegimeTable.Name(regime.From));
            if (regime.To != regime.From) text.Append(" -> ").Append(RegimeTable.Name(regime.To)).Append(' ').Append((regime.Blend * 100f).ToString("0")).Append('%');
            text.Append("\nMission ").Append(manager.MissionTime.ToString("0")).Append(" s, cells ").Append(field.CellCount)
                .Append(", fronts ").Append(field.FrontCount).Append(", seed ").Append(manager.Key.Seed);
            text.Append("\nRain ").Append(p.RainRate.ToString("0.0")).Append(" mm/h  cover ").Append(p.Cover.ToString("0.00"))
                .Append("  vis ").Append(p.VisibilityKm.ToString("0.0")).Append(" km");
            text.Append("\nWind ").Append(WeatherWords.WindFrom(p.WindHeading).ToString("000")).Append('/').Append(p.WindSpeed.ToString("0.0"))
                .Append("  up ").Append(p.WindUp.ToString("0.0")).Append("  turb ").Append(p.Turbulence.ToString("0.00"))
                .Append("  core ").Append(p.CoreDepth.ToString("0.00"));
            text.Append("\n").Append(WeatherWords.Metar(p, 18, manager.HourOfDay));
            GUILayout.Label(text.ToString());

            GUILayout.Space(6f);
            GUILayout.Label("Rain preview (this client, render only)");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < PreviewRates.Length; i++)
            {
                string label = PreviewRates[i] < 0f ? "OFF" : PreviewRates[i].ToString("0");
                bool on = Mathf.Approximately(manager.RainPreview, PreviewRates[i]);
                if (GUILayout.Toggle(on, label, "Button")) manager.RainPreview = PreviewRates[i];
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            if (manager.HostAuthority)
            {
                GUILayout.Label("Force regime (host; every client follows)");
                GUILayout.BeginHorizontal();
                for (int i = 0; i < RegimeTable.Count; i++)
                {
                    if (i == 4)
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                    }
                    var r = (WeatherRegime)i;
                    if (GUILayout.Button(RegimeTable.Name(r))) manager.ForceRegime(r);
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Release overrides")) manager.ReleaseOverrides();
            }
            else
            {
                GUILayout.Label("Only the host can change the sky.");
            }
            GUI.DragWindow();
        }
    }
}
