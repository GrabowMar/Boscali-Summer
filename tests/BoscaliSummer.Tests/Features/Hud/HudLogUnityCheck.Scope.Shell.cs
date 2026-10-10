#if UNITY_EDITOR
using System;
using UnityEngine;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// The TAC BOARD shell on its own (no page fixture): the full-height scope with a stub page (the shell is what is checked: header band, grid,
// frame, the 160 ms wipe, the gauge row, the ticker, the lamp strip with threat lamps and the lamp test, the compact fold and the empty boards).
// Renders shell-<state>-<W>x<H>.png. A page group never edits this file.
public static partial class HudLogUnityCheck
{
    /// <summary>Forces the scope onto <paramref name="page"/> (a stub page, never live by itself) at <paramref name="t"/>: the selector is told it is live once.</summary>
    private static void ShellForce(object status, string page, float t)
    {
        object selector = Field(status, "selector");
        ushort mask = (ushort)(1 << Convert.ToInt32(Enum.Parse(Type(HudDomain + "ScopePage"), page)));
        Call(selector, "Update", mask, t, false);
    }

    private static object ShellLamps(bool lock_, int missiles)
    {
        object threat = Type(HudDomain + "ThreatPicture").GetProperty("Clear").GetValue(null);
        Set(threat, "Missiles", missiles); Set(threat, "SinceLock", lock_ ? 0.4f : 99f); Set(threat, "SinceSpike", 99f);
        return threat;
    }

    private static void ShellScene(object frame, float t, bool full)
    {
        Set(frame, "Systems", Fill(Boxed("SystemStates"), "Gear", EnumOf("GearLamp", "Down"), "FlightAssist", true, "AutoHover", false, "RadarEmitting", true,
            "NightVision", false, "WeaponSafe", false, "EngineRunning", true));
        if (!full) return;
        Set(frame, "Threat", ShellLamps(true, 2));
        Set(frame, "HasTicker", true);
        Set(frame, "Ticker", Fill(Boxed("TickerLine"), "Tone", Tone("Warning"), "Channel", "events", "Text", "SAM LAUNCH - TRACKING YOU", "Count", 3, "Age", 0.5f));
        object lance = Fill(Activator.CreateInstance(Type("BoscaliSummer.Core.Contracts.GaugeSpec")), "Id", "lance", "Label", "LANCE", "Kind", Enum.Parse(Type("BoscaliSummer.Core.Contracts.GaugeKind"), "Arc"),
            "Value", 0.66f, "Text", "66%", "Sub", "RG-12");
        object bar = Fill(Activator.CreateInstance(Type("BoscaliSummer.Core.Contracts.GaugeSpec")), "Id", "rail", "Label", "RAILS", "Kind", Enum.Parse(Type("BoscaliSummer.Core.Contracts.GaugeKind"), "Bar"),
            "Value", 0.4f, "Text", "9 / 24", "Sub", "AMBER 1/3", "Ticks", new[] { 0.33f }, "TickCount", 1, "TickTone", Enum.Parse(Type("BoscaliSummer.Core.Contracts.ScopeTone"), "Dim"));
        object needle = Fill(Activator.CreateInstance(Type("BoscaliSummer.Core.Contracts.GaugeSpec")), "Id", "ace", "Label", "ACE HUNT", "Kind", Enum.Parse(Type("BoscaliSummer.Core.Contracts.GaugeKind"), "Needle"),
            "Angle", 40f, "Text", "VIPER", "Sub", "12 KM", "Tone", Enum.Parse(Type("BoscaliSummer.Core.Contracts.ScopeTone"), "Amber"));
        object ring = Fill(Activator.CreateInstance(Type("BoscaliSummer.Core.Contracts.GaugeSpec")), "Id", "perk", "Label", "PERK", "Kind", Enum.Parse(Type("BoscaliSummer.Core.Contracts.GaugeKind"), "Ring"),
            "Value", 0.6f, "Inner", "18", "Text", "KINETIC", "Sub", "SPACE", "Tone", Enum.Parse(Type("BoscaliSummer.Core.Contracts.ScopeTone"), "Dim"));
        Type gauges = Type("BoscaliSummer.Core.Contracts.ScopeGauges");
        foreach (var g in new[] { ("lance", lance), ("rail", bar), ("ace", needle), ("perk", ring) }) gauges.GetMethod("Publish", All).Invoke(null, new[] { g.Item1, g.Item2 });
    }

    private static object Fill(object box, params object[] nameValue)
    {
        for (int i = 0; i + 1 < nameValue.Length; i += 2) Set(box, (string)nameValue[i], nameValue[i + 1]);
        return box;
    }

    private static float BoardHeight(object status) => ((RectTransform)Field(status, "root")).sizeDelta.y;

    private static void ScopeShell()
    {
        foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720) })
        {
            string res = "-" + (int)size.x + "x" + (int)size.y;
            float u = Mathf.Max(size.y / 1080f, 0.85f);      // the board's scale never goes under 0.85 (a 12 unit word is 10 px at 720p)

            // Full page: the stub THREAT page snapped in (the first page of the board never wipes), every row present.
            MakeBoard(size, 5f, out Canvas canvas, out object status, out object frame);
            ShellScene(frame, 5f, true);
            ShellForce(status, "Threat", 5f);
            Call(status, "Present", frame);
            Canvas.ForceUpdateCanvases();
            Check(Mathf.Abs(BoardHeight(status) - 434f * u) < 1f, "A full board is the prototype's 434 units tall: " + BoardHeight(status) / u);
            object current = Field(status, "page");
            Check((string)current.GetType().GetProperty("Title").GetValue(current) == "THREAT", "The forced stub page is the current page");
            ScopeChecks(status, "shell-full" + res);
            Capture(canvas, "shell-full" + res + ".png");

            // Mid-wipe: the board was cruising (compact SYSTEMS), a page goes live 80 ms ago.
            Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
            MakeBoard(size, 10f, out canvas, out status, out frame);
            ShellScene(frame, 10f, true);
            Call(status, "Present", frame);                       // cruise: compact, snapped (SYSTEMS is not live here: its feed is empty, so the scope has no row)
            Check(BoardHeight(status) > 0f, "A board with lamps, gauges and a ticker shows without a page");
            ShellForce(status, "Threat", 10.04f);
            Set(frame, "Now", 10.04f);
            Call(status, "Present", frame);
            Call(status, "Animate", 10.08f);
            Canvas.ForceUpdateCanvases();
            ScopeChecks(status, "shell-wipe" + res);
            Capture(canvas, "shell-wipe" + res + ".png");
            Call(status, "Animate", 10.3f);
            Check(Mathf.Abs(BoardHeight(status) - 434f * u) < 1f, "After the wipe the board is full height: " + BoardHeight(status) / u);

            // Lamp test sweep (spawn): lamps light one by one.
            Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
            MakeBoard(size, 20f, out canvas, out status, out frame);
            ShellScene(frame, 20f, false);
            Set(frame, "BootAge", 0.5f); Set(frame, "Threat", ShellLamps(false, 0));
            Call(status, "Present", frame);
            Canvas.ForceUpdateCanvases();
            ScopeChecks(status, "shell-boot" + res);
            Capture(canvas, "shell-boot" + res + ".png");

            // Lamps only: the smallest board.
            Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
            MakeBoard(size, 30f, out canvas, out status, out frame);
            ShellScene(frame, 30f, false);
            Call(status, "Present", frame);
            Canvas.ForceUpdateCanvases();
            Check(Mathf.Abs(BoardHeight(status) - 34f * u) < 1f, "Only the lamp strip: one pad, the strip, one pad: " + BoardHeight(status) / u);
            ScopeChecks(status, "shell-lamps" + res);
            Capture(canvas, "shell-lamps" + res + ".png");
            Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
        }
    }
}
#endif
