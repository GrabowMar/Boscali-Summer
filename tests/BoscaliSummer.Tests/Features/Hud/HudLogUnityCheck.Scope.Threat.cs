#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// THREAT fixture: pages/threat.js sim(t) of the takeover prototype, the 16 s loop.
//   0.0  SA-19 spikes us (dashed amber spoke)        0.8  LOCK + SARH launch (M1, 14 s out), BEAM cue starts
//   2.6-5.8  turn left 60 deg: M1 walks round the scope into the hatched 3/9 notch wedge (BEAMED)
//   6.6-7.8  CHAFF x5                                 7.0  IR launch from behind (M2, converging track)
//   8.4  M1 defeated: hollow chevron + TRASHED, lock drops   9.2-10.4 FLARE x5     11.2 M2 defeated     12.6 ZSU search spike
// The page reads ThreatScope (every missile, every radar, the dispensers); the header's DISPENSE call comes from the page's own cue, as in the game. The page remembers the rounds when
// the nearest missile first appeared, so RenderScope rehearses t = 0.9 and 8.5 first (PreRoll).
public static partial class HudLogUnityCheck
{
    private static float EaseOut01(float u) { u = Mathf.Clamp01(u); u = 1f - u; return 1f - u * u * u; }

    private readonly struct SimMissile
    {
        public readonly int Number;
        public readonly string Kind;
        public readonly bool Live;
        public readonly float Tti, Km, Bearing, Age, Since;
        public SimMissile(int number, string kind, bool live, float tti, float km, float bearing, float age, float since) { Number = number; Kind = kind; Live = live; Tti = tti; Km = km; Bearing = bearing; Age = age; Since = since; }
    }

    /// <summary>One of the prototype's missiles at <paramref name="t"/>; false before it is launched or once its ghost is gone.</summary>
    private static bool ThreatMissileAt(int index, float t, out SimMissile m)
    {
        float t0 = index == 0 ? 0.8f : 7.0f, td = index == 0 ? 8.4f : 11.2f, tti0 = index == 0 ? 14.8f : 16.7f, v = index == 0 ? 0.9f : 0.7f, defl = index == 0 ? 30f : -26f;
        m = default;
        if (t < t0) return false;
        bool live = t < td;
        float ad = t - td;
        if (!live && ad > 2.6f) return false;
        float tti = live ? tti0 - t : (tti0 - td) - 0.45f * ad, tb = Mathf.Min(t, td);
        float b = index == 0 ? 30f + 60f * Smooth(Seg01(tb, 2.6f, 5.8f)) : 205f + 2f * (tb - 7f);
        if (!live) b += defl * EaseOut01(Seg01(t, td, td + 2.2f));
        m = new SimMissile(index + 1, index == 0 ? "SARH" : "IR", live, tti, tti * v, (b % 360f + 360f) % 360f, t - t0, live ? 0f : ad);
        return true;
    }

    static partial void FixtureThreat(float t, ScopeScene s)
    {
        var list = new System.Collections.Generic.List<SimMissile>();
        for (int i = 0; i < 2; i++) if (ThreatMissileAt(i, t, out SimMissile m) && s.Variant != "longnames") list.Add(m);
        Array missiles = s.DomainArray("ThreatMissile", 8);
        int liveCount = 0;
        for (int i = 0; i < list.Count; i++)
        {
            SimMissile m = list[i];
            if (m.Live) liveCount++;
            var trail = new float[3 * 12];
            int n = 0;
            for (int k = 9; k >= 0; k--)
                if (ThreatMissileAt(m.Number - 1, t - k * 0.22f, out SimMissile q) && q.Live == m.Live) { trail[3 * n] = q.Bearing; trail[3 * n + 1] = q.Tti; trail[3 * n + 2] = q.Km * 1000f; n++; }
            missiles.SetValue(s.Fill(s.Domain("ThreatMissile"), "Id", m.Number, "Number", m.Number, "Seeker", m.Kind, "Class", s.DomainEnum("ThreatSeeker", m.Kind == "SARH" ? "Radar" : "Ir"),
                "Live", m.Live, "Locked", true, "BearingDeg", m.Bearing, "RangeM", m.Km * 1000f, "ClosureMps", 280f, "TtiS", m.Tti, "AgeS", m.Age, "DefeatedS", m.Since,
                "Trail", trail, "TrailCount", n), i);
        }

        Array emitters = s.DomainArray("ThreatEmitter", 8);
        float brg = 30f + 60f * Smooth(Seg01(t, 2.6f, 5.8f));
        bool lockOn = t >= 0.8f && t < 8.4f;
        float lockAge = t >= 8.4f ? t - 8.4f : t >= 0.8f ? t - 0.8f : t;
        int emitterCount = 0;
        if (s.Variant == "longnames")
        {
            // What the game's first run showed: a carrier, a corvette, a SAM, a radar and a truck within a few degrees of one bearing (the ones that printed over each other) and a bandit; the nearest, spiking us, has the longest name.
            string[] names = { "HYPERION CLASS CARRIER", "SHARD CLASS CORVETTE", "T9K41 BOLTSTRIKE", "MSV RADAR", "HLT RADAR TRUCK", "FS-20 VORTEX" };
            float[] bearing = { 183f, 187f, 192f, 190f, 197f, 176f }, km = { 12f, 13.4f, 18f, 18.4f, 16f, 14f };
            for (int i = 0; i < names.Length; i++)
                emitters.SetValue(s.Fill(s.Domain("ThreatEmitter"), "Id", i + 1, "Name", names[i], "Lock", false, "BearingDeg", bearing[i], "RangeM", km[i] * 1000f, "AgeS", 3f), emitterCount++);
        }
        else
        {
            emitters.SetValue(s.Fill(s.Domain("ThreatEmitter"), "Id", 1, "Name", "SA-19", "Lock", lockOn, "BearingDeg", ((brg - 7f) % 360f + 360f) % 360f, "RangeM", (11.6f - 0.35f * t) * 1000f, "AgeS", lockAge), emitterCount++);
            if (t >= 12.6f) emitters.SetValue(s.Fill(s.Domain("ThreatEmitter"), "Id", 2, "Name", "ZSU", "Lock", false, "BearingDeg", 288f, "RangeM", (4.1f - 0.1f * (t - 12.6f)) * 1000f, "AgeS", t - 12.6f), emitterCount++);
        }

        StoresSpec stores = StoresSpecFor("threat", t, null);
        Array cm = BuildCm(s, stores, out int cmCount);
        s.Publish(s.Fill(s.Domain("ThreatScope"), "MissileCount", list.Count, "Missiles", missiles, "EmitterCount", emitterCount, "Emitters", emitters, "Cm", cm, "CmCount", cmCount,
            "HasSam", true, "SamRings", s.Variant == "longnames" ? (t < 1f ? 0 : 2) : (t < 12.6f ? 2 : 1)));

        // Lamps: LOCK flashing for its first 1.5 s, SPKE while only a search radar paints us, MSL n while anything is inbound.
        object picture = s.Domain("ThreatPicture");
        s.Fill(picture, "Missiles", liveCount, "SinceLock", lockOn ? (lockAge < 1.5f ? 0.5f : 1.5f) : 99f, "SinceSpike", !lockOn ? 0.5f : 99f, "HasBearing", false);
        s.SetFrame("Threat", picture);
        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", false, "EngineRunning", true));

        string text = t >= 12.6f ? "SPIKE - ZSU SEARCH 4KM - SA-19" : liveCount > 0 ? (list[0].Kind == "SARH" && list[0].Live ? (t < 5.8f ? "SAM LAUNCH - SA-19 - BEAM" : t < 6.6f ? "SARH BEAMED - DISPENSE CHAFF" : "SARH BEAMED - CHAFF") : "IR LAUNCH - REAR - FLARES") : "SA-19 SPIKE";
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", liveCount > 0 ? "Warning" : "Caution"), "Channel", "missile", "Text", text, "Count", 1, "Age", 2f));
        s.SetFrame("HasTicker", true);

        s.Gauge("ace", s.Fill(s.Contract("GaugeSpec"), "Id", "ace", "Label", "ACE HUNT", "Kind", s.ContractEnum("GaugeKind", "Needle"), "Angle", (140f + 5f * t) % 360f, "Text", "VOSS",
            "Sub", Mathf.RoundToInt(38f - 0.4f * t) + "KM", "Tone", s.ContractEnum("ScopeTone", "Ink")));
        s.Gauge("radio", s.Fill(s.Contract("GaugeSpec"), "Id", "radio", "Label", "RADIO", "Kind", s.ContractEnum("GaugeKind", "Bars"), "Value", 0.75f, "Text", "VIPER 2",
            "Sub", liveCount > 0 ? "BREAK!" : lockOn ? "COVER" : "ON STA", "Tone", s.ContractEnum("ScopeTone", "Ink")));
    }

    /// <summary>
    /// The THREAT page with the names the game gave its radars (the first run printed them over each other): no two words of the page overlap, the call box's second line keeps its range whole, a radar's tag is the whole
    /// words that fit twelve characters and nothing says "SAM ENV x0" (that word shows only inside an envelope; t 1.5 is inside two).
    /// </summary>
    private static void ThreatLongNamesChecks(object status, string tag, float t)
    {
        var page = (RectTransform)Field(status, "pageRect");
        var words = new List<(string text, Rect ink)>();
        foreach (TextMeshProUGUI label in page.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (!label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text)) continue;
            label.ForceMeshUpdate();
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < label.textInfo.characterCount; i++)
            {
                TMP_CharacterInfo c = label.textInfo.characterInfo[i];
                if (!c.isVisible) continue;
                foreach (Vector3 p in new[] { c.bottomLeft, c.topRight })
                {
                    Vector3 local = page.InverseTransformPoint(label.transform.TransformPoint(p));
                    lo = Vector2.Min(lo, local); hi = Vector2.Max(hi, local);
                }
            }
            if (lo.x <= hi.x) words.Add((label.text, Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y)));
        }
        for (int i = 0; i < words.Count; i++)
            for (int j = i + 1; j < words.Count; j++)
            {
                Rect a = words[i].ink, b = words[j].ink;
                float iw = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), ih = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                bool overlap = iw > 1f && ih > 2f && iw * ih > 0.12f * Mathf.Min(a.width * a.height, b.width * b.height);
                Check(!overlap, "THREAT with the game's radar names: [" + words[i].text + "] and [" + words[j].text + "] overlap (" + tag + ")");
            }
        string sub = null;
        foreach (var w in words)
        {
            Check(!w.text.StartsWith("HYPERION CLASS") && !w.text.StartsWith("SHARD CLASS C"), "THREAT: a radar's tag is the words that fit twelve characters, not the game's whole name: [" + w.text + "] (" + tag + ")");
            if (w.text.EndsWith("KM") && w.text.Contains(" ")) sub = w.text;
            Check(!w.text.StartsWith("SAM ENV") || t >= 1f, "THREAT: no SAM ENV word with no envelope round us: [" + w.text + "] (" + tag + ")");
        }
        bool tagged = false;
        foreach (var w in words) tagged |= w.text == "HYPERION";
        Check(tagged, "THREAT: the carrier's tag reads HYPERION (" + tag + ")");
        // The call box's second line is a name and a range ("HYPE… 12KM"): found by its range, so a line the box cut off ("HYPERI… 1…", seen in game) is not found at all.
        Check(sub != null, "THREAT: the call box's second line keeps its range whole, the name gives way (" + tag + ")");
        Check(sub != null && sub.Length <= 10 && sub.IndexOf('…') == sub.LastIndexOf('…'), "...and it fits the 84 units the box has (ten characters, one cut at most): [" + sub + "] (" + tag + ")");
        if (t >= 1f)
        {
            bool env = false;
            foreach (var w in words) env |= w.text == "SAM ENV x2";
            Check(env, "THREAT: inside two envelopes the page says SAM ENV x2 (" + tag + ")");
        }
    }
}
#endif
