#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UnityCheckHarness;

// The real COMMS presenter beside the native-feed and cue stand-ins, at three viewport sizes.
public static partial class HudLogUnityCheck
{
    private static void ScopeLeft()
    {
        foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720), new Vector2(2560, 1080) })
            foreach (string scenario in new[] { "dialogue", "traffic", "long" }) LeftColumn(size, scenario);
    }

    private static object LeftAired(string net, string speaker, string text, string role = null, bool scripted = false, string priority = "Status")
    {
        object line = Activator.CreateInstance(Type("BoscaliSummer.Core.Chatter.ChatterAired"));
        Set(line, "Net", Enum.Parse(Type("BoscaliSummer.Core.Chatter.ChatterNet"), net));
        Set(line, "Priority", Enum.Parse(Type("BoscaliSummer.Core.Chatter.ChatterPriority"), priority));
        Set(line, "Speaker", speaker); Set(line, "SpeakerId", speaker); Set(line, "Text", text); Set(line, "Seconds", 20f);
        Set(line, "Role", role); Set(line, "Scripted", scripted);
        return line;
    }

    private static void LeftStripFrame(object strip, CueRig rig)
    {
        // Tick performs production visibility/font refresh. Paint then lays out the chosen viewport;
        // turn its overlay into a standalone world-space canvas so the existing capture camera sees it.
        Call(strip, "Tick", true);
        var host = (AvHudHost)Field(strip, "host");
        var rect = (RectTransform)host.Root.transform;
        rect.SetParent(null, false);
        host.Canvas.renderMode = RenderMode.WorldSpace;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = ((RectTransform)rig.C.Canvas.transform).pivot;
        rect.anchoredPosition = Vector2.zero;
        rect.position = rig.C.Canvas.transform.position;
        rect.sizeDelta = rig.Size / rig.U;
        rect.localScale = new Vector3(rig.U, rig.U, rig.U);
        rect.rotation = rig.C.Canvas.transform.rotation;
        foreach (RectTransform layer in new[] { host.Static, host.Data, host.Motion })
        {
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one; layer.pivot = new Vector2(0.5f, 0.5f);
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            layer.localScale = Vector3.one;
        }
        Canvas.ForceUpdateCanvases();
        Array rows = (Array)Field(strip, "rows");
        for (int i = 0; i < (int)Field(strip, "used"); i++)
        {
            object row = rows.GetValue(i);
            float clock = (bool)Field(row, "Scripted") ? Time.time : Time.unscaledTime;
            Set(row, "Shown", clock - 12f);
            Set(row, "Until", clock + 30f);
            rows.SetValue(row, i);
        }
        Call(strip, "Paint", rig.Size.y / rig.U);
        CueAt(rig, 2f);
        BackdropBack(rig.C.Canvas);
        Canvas.ForceUpdateCanvases();
    }

    private static List<TextMeshProUGUI> LeftStripLabels(object strip)
    {
        var host = (AvHudHost)Field(strip, "host");
        return host.Root.GetComponentsInChildren<TextMeshProUGUI>()
            .Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text) && t.alpha > 0f).ToList();
    }

    private static Rect LeftInk(CornerRig rig, IEnumerable<TextMeshProUGUI> labels)
    {
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        var canvas = (RectTransform)rig.Canvas.transform;
        foreach (TextMeshProUGUI label in labels)
        {
            label.ForceMeshUpdate();
            foreach (TMP_CharacterInfo c in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
            {
                if (!c.isVisible) continue;
                Vector3 lo = canvas.InverseTransformPoint(label.transform.TransformPoint(c.bottomLeft));
                Vector3 hi = canvas.InverseTransformPoint(label.transform.TransformPoint(c.topRight));
                left = Mathf.Min(left, lo.x - canvas.rect.xMin); right = Mathf.Max(right, hi.x - canvas.rect.xMin);
                top = Mathf.Min(top, canvas.rect.yMax - hi.y); bottom = Mathf.Max(bottom, canvas.rect.yMax - lo.y);
            }
        }
        Check(left <= right && top <= bottom, "The left-column block has visible ink.");
        return Rect.MinMaxRect(left, top, right, bottom);
    }

    private static void LeftColumn(Vector2 size, string scenario)
    {
        string tag = "left-" + scenario + "-" + (int)size.x + "x" + (int)size.y;
        CueRig rig = MakeCues(size, PilotOptions());
        object strip = Activator.CreateInstance(Type("BoscaliSummer.Modules.Chatter.Presentation.ChatterStrip"), All, null, new object[] { null }, null);
        try
        {
            LogSix(rig.C);
            CueAdd("Action", "Weapon safety lock <b>Enabled</b>", "Normal", CueNow, 20f);
            CueAdd("Action", "Stability Assist <b>Enabled</b>", "Normal", CueNow + 0.2f, 20f);
            CueAdd("Action", "Radar Armed", "Normal", CueNow + 0.4f, 20f);
            NativeSays(rig, "Action", true, "Radar Armed");
            if (scenario != "traffic")
            {
                string words = scenario == "long"
                    ? "Summer Lead, Broker. Easy money tonight: one freighter, Island Five by dark. The weather is filthy and the Vagrant is all the company could spare. Go and find her."
                    : "You have the flight. Hold C for the ring; your wing does what the ring says. Mostly.";
                Call(strip, "Show", LeftAired("Command", "BROKER", words, "Tradewind Air Services", true));
            }
            Call(strip, "Show", LeftAired("Tower", "OPAL AIRPORT TOWER", "Cleared to taxi, Runway 22. Mind the lead-in line."));
            Call(strip, "Show", LeftAired("Tower", "OPAL AIRPORT TOWER", "Incoming! Clear the runway and take cover!", priority: "Emergency"));
            if (scenario == "traffic")
                for (int i = 0; i < 6; i++) Call(strip, "Show", LeftAired("Wing", "VIPER " + (i + 1), "Holding formation. Fuel and stores check complete."));
            if (scenario == "long")
                for (int i = 0; i < 4; i++) Call(strip, "Show", LeftAired("Wing", "VIPER " + (i + 1), "Holding formation. Fuel and stores check complete."));
            LeftStripFrame(strip, rig);

            List<TextMeshProUGUI> comms = LeftStripLabels(strip);
            Rect log = LeftInk(rig.C, LogLabels(rig.C)), talk = LeftInk(rig.C, comms), cues = LeftInk(rig.C, CueLabels(rig));
            Check(log.yMax + 4f * rig.U <= talk.yMin, "The full event log clears COMMS: " + log + " / " + talk + " " + tag);
            Check(talk.yMax + 4f * rig.U <= cues.yMin, "Wrapped COMMS clears the info flashes: " + talk + " / " + cues + " " + tag);
            Check(cues.yMax < rig.Size.y - rig.C.MapPlate.rect.height, "Info flashes clear the minimap: " + cues + " " + tag);
            Check(talk.xMin >= 0f && talk.xMax <= size.x * 0.52f, "COMMS stays inside the left column: " + talk + " " + tag);
            Check(CueDrawn(rig) == 3, "All three simultaneous info flashes remain visible: " + tag);
            Check(comms.All(t => t.font == HudFont() && (t.fontStyle & FontStyles.Bold) == 0), "COMMS shares the native HUD font and regular weight: " + tag);
            Check(comms.All(t => !t.isTextTruncated), "Wrapped COMMS text stays readable in its measured rows: " + tag);
            if (scenario != "traffic")
            {
                string text = string.Join(" ", comms.Select(t => Plain(t)));
                Check(text.Contains("BROKER") && text.ToUpperInvariant().Contains("TRADEWIND AIR SERVICES"), "Scenario speaker and role stay in COMMS: " + text + " " + tag);
            }
            foreach (string root in new[] { "logRoot", "cueRoot", "cornerShade" })
                foreach (Graphic g in ((RectTransform)Field(rig.Status, root)).GetComponentsInChildren<Graphic>(true))
                    Check(!g.raycastTarget, "The left HUD yields pointer input: " + g.name + " " + tag);
            var host = (AvHudHost)Field(strip, "host");
            Check(!host.Group.blocksRaycasts && !host.Group.interactable, "COMMS yields flight controls: " + tag);
            foreach (Graphic g in host.Root.GetComponentsInChildren<Graphic>(true)) Check(!g.raycastTarget, "COMMS takes no pointer hit: " + g.name + " " + tag);
            var shadow = (Image)Field(rig.Status, "leftShadow");
            Check(shadow.transform.parent.GetComponentsInChildren<Graphic>(true).Length == 1,
                "The entire left column has exactly one shadow graphic: " + tag);
            Check(shadow.color.a > 0f && shadow.color.a <= 0.06f,
                "The shared shadow never exceeds six percent opacity: " + shadow.color.a + " " + tag);
            Rect shadowBox = CanvasRectOf((RectTransform)rig.C.Canvas.transform, shadow.rectTransform);
            Check(shadowBox.yMin <= log.yMin && shadowBox.yMax >= cues.yMax,
                "One continuous shadow covers log, COMMS and status: " + shadowBox + " " + tag);
            Check(host.Static.GetComponentsInChildren<Graphic>(true).All(g => g.name != "Shade"),
                "COMMS adds no separate shadow: " + tag);
            Capture(rig.C.Canvas, tag + ".png");

            float heldTop = Spot(rig, 0, "Y");
            if (scenario != "traffic")
            {
                Call(strip, "Tick", false);
                Check(host.Canvas.enabled, "Scripted dialogue remains visible outside flight: " + tag);
                Call(strip, "ClearScripted");
                Array remaining = (Array)Field(strip, "rows");
                int count = (int)Field(strip, "used");
                Check(count > 0 && Enumerable.Range(0, count).All(i => !(bool)Field(remaining.GetValue(i), "Scripted")), "Ending the mission drops scripted dialogue and preserves radio traffic: " + tag);
            }
            else
            {
                Call(strip, "Clear");
                int capacity = ((Array)Field(strip, "rows")).Length;
                for (int i = 0; i < capacity; i++) Call(strip, "Show", LeftAired("Tower", "RESCUE " + i, "Mayday.", priority: "Emergency"));
                int heldCount = (int)Field(strip, "used");
                string heldEmergencyText = string.Join(" ", LeftStripLabels(strip).Select(t => t.text));
                Call(strip, "Show", LeftAired("Wing", "ROUTINE ARRIVAL", "Radio check.", priority: "Ambient"));
                Check(heldCount > 0 && (int)Field(strip, "used") == heldCount &&
                    string.Join(" ", LeftStripLabels(strip).Select(t => t.text)) == heldEmergencyText,
                    "Routine traffic cannot displace the emergency calls that fit the bounded stack: " + tag);
            }
            // The maximised map's left host slot sits where COMMS draws (vg-dronelink capture, 2026-10-20): COMMS stands down under it and comes back after.
            var maximized = typeof(DynamicMap).GetProperty("mapMaximized", All);
            int kept = (int)Field(strip, "used");
            try
            {
                maximized.SetValue(null, true);
                Call(strip, "Tick", true);
                Check(!host.Canvas.enabled && (float)Field(Type("BoscaliSummer.Core.Ui.LeftHudStack"), "CommsBottom") == 0f,
                    "COMMS stands down and releases its layout under the maximised map: " + tag);
            }
            finally { maximized.SetValue(null, false); }
            Call(strip, "Tick", true);
            Check(host.Canvas.enabled && (int)Field(strip, "used") == kept, "COMMS comes back with its rows when the map closes: " + tag);
            Call(strip, "Clear");
            Check(!host.Canvas.enabled && (int)Field(strip, "used") == 0, "Clearing COMMS drops its messages and hides it: " + tag);
            Check((float)Field(Type("BoscaliSummer.Core.Ui.LeftHudStack"), "CommsBottom") == 0f, "Clearing COMMS releases its layout reservation: " + tag);
            CueAt(rig, 2.1f);
            Check(Spot(rig, 0, "Y") < heldTop - 1f, "Info flashes reclaim the cleared COMMS space on the next frame: " + tag);
        }
        finally
        {
            var host = (AvHudHost)Field(strip, "host");
            Call(strip, "Destroy");
            if (host.Root != null) UnityEngine.Object.DestroyImmediate(host.Root);
            CueEnd(rig);
        }
    }
}
#endif
