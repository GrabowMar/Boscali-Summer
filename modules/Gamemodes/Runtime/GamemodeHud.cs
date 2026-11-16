using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Modules.Gamemodes.Networking;
using NuclearOption.Networking;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>Native-font, input-transparent match status. A separate shared-style overlay also reaches spectators and the spawn screen.</summary>
    internal sealed class GamemodeHud : MonoBehaviour, ISceneService
    {
        private const float Width = 560f, Height = 160f;
        private GamemodeRuntime runtime;
        private AvHudHost host;
        private TextMeshProUGUI title, scores, route, instruction;
        private RectTransform rule, backdrop;
        private IHudBoard board;
        private IHudLine scoreLine, taskLine;
        private float nextRefresh;
        private string lastNotice;
        private TMP_FontAsset face;
        private Material faceMaterial;

        public void Configure(GamemodeRuntime owner) => runtime = owner;

        public void ResetForScene()
        {
            LeftHudStack.MissionBottom = 0f;
            ReleaseLines();
            host?.Destroy();
            host = null;
            title = scores = route = instruction = null;
            rule = backdrop = null;
            face = null;
            faceMaterial = null;
            lastNotice = null;
            nextRefresh = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            if (runtime == null || GamemodeRuntime.Kind == GameModeKind.None || Application.isBatchMode)
            {
                LeftHudStack.MissionBottom = 0f;
                host?.SetVisible(false);
                ReleaseLines();
                lastNotice = null;
                return;
            }
            if (!Ensure()) return;
            ModuleServices.TryGet(out board);
            GamemodeStatus state = runtime.Current;
            bool ready = state.Mode == GamemodeRuntime.Kind;
            string name = Name(GamemodeRuntime.Kind);
            string heading = name + (ready ? PhaseText(state) : "  /  WAITING FOR HOST");
            string scoreText = !ready ? "SCORE UNAVAILABLE" : state.Mode == GameModeKind.WarBox ? "SUSTAINED WAR / DYNAMIC OPERATIONS" :
                "BDF  " + state.Score0 + "    :    " + state.Score1 + "  PALA";
            string routeText = ready ? state.Route ?? "" : "";
            if (ready && state.Mode == GameModeKind.Heavyweight && state.Phase == GamemodePhase.Running)
            {
                scoreText += "  /  " + (state.TransportingTeam == 0 ? "BDF" : "PALA") + " HAUL";
                routeText += "  /  " + state.Cargo + " CRATES LOADED";
            }
            string instructionText = ready ? state.Instruction ?? "" : "Waiting for a fresh authoritative mission report.";
            title.text = heading;
            scores.text = scoreText;
            route.text = routeText;
            instruction.text = instructionText;
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            Color ink = ready ? palette.AllClear : palette.Warning;
            title.color = scores.color = ink;
            route.color = instruction.color = palette.AllClear;
            rule.GetComponent<Image>().color = new Color(ink.r, ink.g, ink.b, 0.65f);
            if (!MissionPrompt.DashboardOpen) Layout();
            else LeftHudStack.MissionBottom = 0f;
            host.SetVisible(!MissionPrompt.DashboardOpen);

            // Held lines remain available to the common board; HUD v8 draws only its notices, hence the visible overlay above.
            scoreLine = board?.Acquire("gamemodes", "mission", "score");
            taskLine = board?.Acquire("gamemodes", "mission", "task");
            scoreLine?.Set(ready ? HudTone.Info : HudTone.Caution, heading, scoreText, 0f);
            taskLine?.Set(HudTone.Info, routeText, instructionText, 0f);
            string notice = ready ? name + " / " + state.Phase + " / " + state.Leg + " / " + scoreText + " / " + routeText : null;
            if (notice != null && notice != lastNotice)
            {
                board?.Notice("mission", state.Phase == GamemodePhase.Intermission ? HudTone.Caution : HudTone.Info,
                    heading, scoreText + (string.IsNullOrEmpty(routeText) ? "" : " / " + routeText));
                lastNotice = notice;
            }
        }

        private bool Ensure()
        {
            if (!VanillaHudStyle.TryHudFont(out TMP_FontAsset font, out Material material))
            {
                font = LeftHudStack.Font != null ? LeftHudStack.Font : AvType.Face(AvFace.Mono);
                material = LeftHudStack.FontMaterial;
                if (font == null) return false;
                if (material == null) material = VanillaHudStyle.Glow(font.material);
            }
            if (host == null)
            {
                host = AvHudHost.Create("Boscali / Gamemode status", transform, 40);
                var shade = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
                shade.transform.SetParent(host.Static, false);
                var shadeImage = shade.GetComponent<Image>();
                shadeImage.raycastTarget = false;
                shadeImage.color = new Color(0.015f, 0.025f, 0.03f, 0.78f);
                backdrop = (RectTransform)shade.transform;
                title = Label("Mode", font, material, 16f);
                scores = Label("Score", font, material, 22f);
                route = Label("Route", font, material, 16f);
                instruction = Label("Instruction", font, material, 16f);
                route.enableWordWrapping = true;
                instruction.enableWordWrapping = true;
                var line = new GameObject("Rule", typeof(RectTransform), typeof(Image));
                line.transform.SetParent(host.Static, false);
                line.GetComponent<Image>().raycastTarget = false;
                rule = (RectTransform)line.transform;
            }
            if (face != font || faceMaterial != material)
            {
                face = font;
                faceMaterial = material;
                foreach (TextMeshProUGUI text in new[] { title, scores, route, instruction })
                { text.font = font; text.fontSharedMaterial = material; }
            }
            return true;
        }

        private TextMeshProUGUI Label(string name, TMP_FontAsset font, Material material, float size)
        {
            TextMeshProUGUI text = HudText.Make(host.Data, name, font, material, TextAlignmentOptions.TopLeft, size, TextOverflowModes.Ellipsis);
            text.richText = false;
            text.characterSpacing = 0f;
            return text;
        }

        private void Layout()
        {
            float fit = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            host.Refresh(Mathf.Max(0.85f, Screen.height / 1080f) / Mathf.Max(0.1f, fit));
            float scale = Mathf.Max(0.1f, host.ScaleFactor);
            float canvasW = Screen.width / scale, canvasH = Screen.height / scale;
            float width = Mathf.Min(Width, canvasW - 32f);
            GameManager.GetLocalPlayer<Player>(out Player player);
            bool flying = player?.Aircraft != null && !player.Aircraft.disabled;
            instruction.gameObject.SetActive(!flying);
            if (flying)
            {
                // Join the shared left column, leaving cockpit instruments and warning/heading lanes clear.
                float top = Mathf.Max(16f, Mathf.Max(LeftHudStack.LogBottom, LeftHudStack.CommsBottom) + 16f);
                top = Mathf.Max(top, LeftHudStack.NativeFeedBottomPx / scale + 16f);
                float compactHeight = string.IsNullOrEmpty(route.text) ? 56f : 100f;
                float edge = canvasH - top - compactHeight;
                float column = LeftHudStack.Left;
                LeftHudStack.MissionBottom = top + compactHeight;
                AvHudHost.Place(title.rectTransform, column, edge + compactHeight - 22f, width, 22f);
                AvHudHost.Place(rule, column, edge + compactHeight - 26f, width, TopCentre.Stroke);
                AvHudHost.Place(scores.rectTransform, column, edge + compactHeight - 56f, width, 30f);
                AvHudHost.Place(route.rectTransform, column, edge, width, 42f);
                AvHudHost.Place(backdrop, column - 8f, edge - 6f, width + 16f, compactHeight + 12f);
                return;
            }
            LeftHudStack.MissionBottom = 0f;
            // The bottom band leaves the fixed TopCentre warning lane and the left log/radio/cue column intact.
            float left = Mathf.Clamp(Mathf.Max(LeftHudStack.Left + LeftHudStack.Width + TopCentre.Gap,
                (canvasW - width) * 0.5f), 16f, Mathf.Max(16f, canvasW - width - 16f));
            float bottom = 24f;
            if (board != null && board.TryGetScreenBox(out float bl, out float bt, out float br, out float bb))
            {
                float topPx = (canvasH - bottom - Height) * scale, bottomPx = (canvasH - bottom) * scale;
                if (left * scale < br && (left + width) * scale > bl && topPx < bb && bottomPx > bt)
                    left = Mathf.Clamp(bl / scale - width - TopCentre.Gap, 16f, Mathf.Max(16f, canvasW - width - 16f));
            }
            AvHudHost.Place(title.rectTransform, left, bottom + 136f, width, 22f);
            AvHudHost.Place(rule, left, bottom + 131f, width, TopCentre.Stroke);
            AvHudHost.Place(scores.rectTransform, left, bottom + 98f, width, 30f);
            AvHudHost.Place(route.rectTransform, left, bottom + 54f, width, 42f);
            AvHudHost.Place(instruction.rectTransform, left, bottom, width, 48f);
            AvHudHost.Place(backdrop, left - 12f, bottom - 10f, width + 24f, Height + 20f);
        }

        private static string Name(GameModeKind mode) => mode == GameModeKind.WarBox ? "WARBOX" :
            mode == GameModeKind.Fuzzball ? "FUZZBALL" : "HEAVYWEIGHT";

        private static string PhaseText(GamemodeStatus state)
        {
            if (state.Phase == GamemodePhase.Finished)
                return state.Winner < 0 ? "  /  DRAW" : "  /  " + (state.Winner == 0 ? "BDF" : "PALA") + " WINS";
            if (state.Mode == GameModeKind.WarBox) return "  /  CAMPAIGN";
            int seconds = Mathf.Max(0, Mathf.CeilToInt(state.Remaining));
            string clock = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            if (state.Phase == GamemodePhase.Intermission) return "  /  ROLE SWAP  " + clock;
            return state.Mode == GameModeKind.Heavyweight ? "  /  LEG " + state.Leg + "  /  " + clock : "  /  " + clock;
        }

        private void ReleaseLines()
        {
            scoreLine?.Release(); taskLine?.Release();
            scoreLine = taskLine = null;
        }
    }
}
