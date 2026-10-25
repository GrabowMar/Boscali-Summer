#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using BoscaliSummer.Modules.Spooktober.Domain;
using BoscaliSummer.Modules.Spooktober.Presentation;
using BoscaliSummer.Modules.Spooktober.Runtime;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static UnityCheckHarness;

public static class SpookUnityCheck
{
    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvFxDriver.Configure(AvFxTier.Off, false);
            var canvas = new GameObject("Canvas", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)canvas.transform;
            root.sizeDelta = new Vector2(480f, 680f);
            var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 340f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f, .035f, .025f);
            var con = AvConsole.Build(root, "SET", "PERFORMANCE", 6, 480f, 680f);
            var perf = con.Page(4);
            perf.Section(AvIcon.Activity, "FRAME TIME", "FIXTURE");
            var fps = new AvGauge(perf.Content, "FPS");
            var frame = new AvGauge(perf.Content, "FRAME");
            var worst = new AvGauge(perf.Content, "WORST");
            fps.Set(.5f, "60", AvState.Ready);
            frame.Set(.5f, "16.7", AvState.Ready);
            worst.Set(.5f, "17.6", AvState.Ready);
            perf.Row(fps, frame, worst);
            var chart = perf.Add(new AvLineChart(perf.Content, 96f));
            chart.SetSeries(new[] { 16.7f, 16.8f, 17.6f, 16.7f }, 4, "16.7", "17.6", "16.7 MS");
            float now = 0f;
            SpookConsole.Clock = () => now;
            var extras = new SpookSetExtras(() => false);
            for (int p = 0; p < 5; p++) extras.BuildPilotPage(p, con.Page(p), con);
            extras.BuildExtraPage(5, con.Page(5), con);
            con.Finish();
            con.SetPage(4);
            con.Footer.Set("FUNCTIONAL STATUS");
            con.Ticker.TickNow();
            RectTransform indicator = con.Root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Echo RX Indicator");
            TMP_Text key = indicator.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "Value");
            AvFrame bulb = indicator.GetComponentsInChildren<AvFrame>(true).Single(f => f.name == "Bulb");
            Check(key.text == "IDLE", "Lamp waits before the first letter");
            string gauges = Text(fps.Rect) + Text(frame.Rect) + Text(worst.Rect);
            for (int i = 0; i < 230; i++)
            {
                now = i * .1f;
                con.Ticker.TickNow();
                Check(gauges == Text(fps.Rect) + Text(frame.Rect) + Text(worst.Rect), "Morse must never change telemetry");
                Check(Text(con.Footer.Rect).Contains("FUNCTIONAL STATUS"), "Morse must never overwrite functional status");
            }
            now = 24f;
            con.SetPage(0);
            con.Ticker.TickNow();
            now = 25f;
            con.SetPage(4);
            con.Ticker.TickNow();
            Check(key.text == "IDLE", "Returning starts a fresh word after a lead-in");
            for (int i = 251; i <= 263; i++) { now = i * .1f; con.Ticker.TickNow(); }
            Check(key.text == "RX", "First dash lights the dedicated bulb");
            Render(camera, canvas, "ECHO-lamp-on.png");
            for (int i = 264; i <= 275; i++) { now = i * .1f; con.Ticker.TickNow(); }
            Check(key.text == "IDLE", "Inter-element gap darkens the bulb");
            Render(camera, canvas, "ECHO-lamp-off.png");
            TMP_InputField input = perf.Content.GetComponentInChildren<TMP_InputField>();
            input.text = "ghost";
            input.onSubmit.Invoke(input.text);
            Check(SpookStore.Current.Has(SpookPuzzle.Echo) && SpookStore.Saves == 1 && key.text == "GHOST" && bulb.FillColor.r < .1f,
                "Solve persists once and stops the lamp");
            Check(Text(con.Footer.Rect).Contains("FUNCTIONAL STATUS"), "Solve feedback must stay out of the footer");
            Render(camera, canvas, "ECHO-solved.png");
            con.SetPage(5);
            con.Ticker.TickNow();
            Check(Text(con.Footer.Rect).Contains("FUNCTIONAL STATUS"), "Boss menu must leave functional status alone");
            SpookStore.Current.Solve(SpookPuzzle.Signal);
            SpookStore.Current.Solve(SpookPuzzle.Cipher);
            SpookStore.Current.Solve(SpookPuzzle.Breaker);
            SpookStore.Current.RecordDefeat(SpookBoss.Kaiju);
            con.Ticker.TickNow();
            con.Ticker.TickNow(); // the cards' buttons were hidden: their flow re-lays on the tick after they show
            string menu = Text(con.Page(5).Content);
            Check(menu.Contains("4/4") && menu.Contains("1/2") && menu.Contains("OFFLINE") && menu.Contains("READY"),
                "Boss menu strip and cards read the chain");
            Render(camera, canvas, "MENU.png");
            now = 506.5f; // a slot where FIELD reads BEHIND YOU, the caption turns and the ace's name flickers
            con.Ticker.TickNow();
            con.Ticker.TickNow();
            menu = Text(con.Page(5).Content);
            Check(menu.Contains("BEHIND YOU") && !menu.Contains("ONE AT A TIME") && !menu.Contains("FALLEN ACE"), "The page haunts itself on the clock");
            Render(camera, canvas, "MENU-haunted.png");
            File.WriteAllText("result.txt", "PASS: isolated Morse lamp on/off and repeat; page return; solve/save/stop; untouched telemetry and functional footer. Production presentation, fixture telemetry/config/boss seams; offline only.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static string Text(RectTransform rect) => string.Join("|", rect.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text));

    private static void Render(Camera camera, Canvas canvas, string file)
    {
        foreach (Image image in canvas.GetComponentsInChildren<Image>(true))
            if (image.name == "ScanCover") image.enabled = false;
        foreach (AvReveal reveal in canvas.GetComponentsInChildren<AvReveal>(true)) reveal.Finish();
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        CapturePng(camera, 480, 680, file);
    }
}
#endif
