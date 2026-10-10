#if UNITY_EDITOR
using UnityEngine;

// APPROACH fixture: pages/approach.js sim(t), the 15 s loop (time-lapsed about 6x). Autoland K92 RWY 27 with 6 kt from the left: 0 to 1.6 s gear in transit (GEAR
// amber), FAST +16 km/h with the airbrake out, LOC 2.2 deg left, GS 0.9 high (diamond amber); 4.8 INTERCEPT -> GLIDE (LOC captured, GS dips low at 7.2, SLOW blip
// at 8.6); 9.8 FLARE; 11.8 touchdown -> ROLLOUT. The prototype's numbers are the feed's (SI): this is the only place a scripted approach lives.
public static partial class HudLogUnityCheck
{
    static partial void FixtureApproach(float t, ScopeScene s)
    {
        const float gam = 5f, aim = 300f, tGear = 1.6f, tBrake = 4.2f, tGlide = 4.8f, tFlare = 9.8f, tTouch = 11.8f, vApp = 265f, kmh = 1f / 3.6f;
        float Gauss(float x, float c, float w) => Mathf.Exp(-((x - c) / w) * ((x - c) / w));
        float Gs(float x) => 0.9f * (1f - Smooth(Seg01(x, 0.6f, 6.4f))) - 0.42f * Gauss(x, 7.2f, 0.8f) + 0.07f * Mathf.Sin(x * 1.9f) * Seg01(x, 4f, 6f);
        float Loc(float x) => -2.2f * (1f - Smooth(Seg01(x, 0.2f, 5f))) + 0.06f * Mathf.Sin(x * 2.3f + 1f) * Seg01(x, 3.5f, 5f);
        float Az(float x) => x < tTouch ? -5600f + 500f * x : 300f + 500f * (x - tTouch) - 77.5f * (x - tTouch) * (x - tTouch);
        float HPath(float x) => Mathf.Max(0f, aim - Az(x)) * Mathf.Tan((gam + Gs(x)) * Mathf.Deg2Rad);
        float gs = Gs(t), loc = Loc(t), az = Az(t);
        int stage = t < tGlide ? 0 : t < tFlare ? 1 : t < tTouch ? 2 : 3;
        bool air = t < tTouch;
        float h = t < tFlare ? HPath(t) : t < tTouch ? HPath(tFlare) * Mathf.Pow(1f - Seg01(t, tFlare, tTouch), 1.5f) : 0f;
        float dv = t < tFlare ? 16f * (1f - Smooth(Seg01(t, 0f, 5.2f))) - 12f * Gauss(t, 8.6f, 0.8f) + 1.2f * Mathf.Sin(t * 2.4f) * Seg01(t, 2f, 4f) : 0f;
        float speed = air ? vApp + dv - 10f * Seg01(t, tFlare, tTouch) : Mathf.Max(28f, 255f - 70f * (t - tTouch));
        float vs = t < tFlare ? -6.4f + 0.5f * Mathf.Sin(t * 1.3f) : t < tTouch ? -6.4f + 5.4f * Seg01(t, tFlare, tTouch) : 0f;
        float d = aim - az, eLat = Mathf.Tan(loc * Mathf.Deg2Rad) * Mathf.Max(d, 400f);
        float pitch = gam * (1f - Smooth(Seg01(t, tFlare, tTouch + 0.3f)));

        object a = s.Contract("ApproachScope");
        s.Fill(a, "Autoland", true, "Stage", s.ContractEnum("ApproachStage", new[] { "Intercept", "Glide", "Flare", "Rollout" }[stage]),
            "Field", "K92 HIGHWAY STRIP", "Runway", "RWY 27", "RunwayLength", 1800f, "RunwayWidth", 60f, "AimM", aim,
            "AlongM", az, "CrossM", eLat, "HeightM", h, "RadarAltM", h, "PathDeg", pitch,
            "HasIls", stage <= 1, "LocDeg", loc, "GsDeg", gs, "SlopeDeg", gam,
            "SpeedMps", speed * kmh, "GroundMps", speed * kmh, "SinkMps", -vs, "HasTarget", air, "TargetMps", vApp * kmh,
            "TouchdownS", Mathf.Clamp(air ? (tTouch - t) * 2.5f : 0f, 0f, 30f), "HasWind", true, "CrosswindMps", 6f / 1.94384f,
            "GearDown", t >= tGear, "GearMoving", t < tGear, "Airbrake", t < tBrake, "DistanceM", Mathf.Abs(d));
        // Variants (add ("approach", 6f, "manual") etc. to ScopeVariants to render them): by hand with no strip, a vertical lander over a pad, an imperial player.
        if (s.Variant == "manual")
            s.Fill(a, "Autoland", false, "Occupied", false, "HasTarget", true, "TargetMps", 66f, "SpeedMps", 69f, "Airbrake", false, "GearDown", true, "GearMoving", false);
        else if (s.Variant == "pad")
            s.Fill(a, "Pad", true, "Field", "FOB CHAPEL", "Runway", null, "PadX", -35f, "PadY", 60f, "DistanceM", 70f, "RadarAltM", 22f, "HeightM", 22f, "SinkMps", 2.5f, "GroundMps", 6f, "HasTarget", false,
                "Stage", s.ContractEnum("ApproachStage", "Glide"), "HasIls", false, "GearDown", true, "GearMoving", false, "Airbrake", false);
        else if (s.Variant == "imperial") s.SetFrame("Units", NOAvionics.AvUnits.Imperial);
        s.Publish(a);

        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", t < tGear ? "Moving" : "Down"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", true, "EngineRunning", true));

        // The ticker the page's notices would put there at this moment.
        string tone = "Info", chip = "ils", text; int count = 1;
        bool cSpd = air && Mathf.Abs(dv) >= 10f, cGs = stage <= 1 && Mathf.Abs(gs) >= 0.6f;
        string One(float v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        if (t < tGear) { tone = "Caution"; chip = "gear"; text = "GEAR DOWN - NOT LOCKED"; }
        else if (cSpd) { tone = "Caution"; chip = "spd"; text = (dv > 0f ? "FAST +" : "SLOW ") + Mathf.RoundToInt(dv) + " KM/H" + (dv > 0f ? " - AIRBRAKE OUT" : " - ADD POWER"); count = dv > 0f ? 1 : 2; }
        else if (cGs) { tone = "Caution"; text = "GS +" + One(gs) + " HIGH - CAPTURE FROM ABOVE"; }
        else if (stage == 0) text = "AUTOLAND K92 RWY 27 - LOC CAPTURE";
        else if (stage == 1) text = "GLIDE - " + One(Mathf.Max(0f, -az) / 1000f) + " KM TO THRESHOLD";
        else if (stage == 2) text = "FLARE - RA " + Mathf.RoundToInt(h) + " M";
        else { chip = "land"; text = "TOUCHDOWN - ROLLOUT " + Mathf.RoundToInt(speed) + " KM/H - BRAKES AUTO"; }
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", tone), "Channel", chip, "Text", text, "Count", count, "Age", 2f));
        s.SetFrame("HasTicker", true);

        // The registered gauges the prototype's row ends with (the page brings AUTOPILOT and SINK RATE).
        s.Gauge("radio", s.Fill(s.Contract("GaugeSpec"), "Id", "radio", "Label", "RADIO", "Kind", s.ContractEnum("GaugeKind", "Bars"), "Value", 0.75f, "Text", "K92 TWR", "Sub", "SIG 3/4", "Tone", s.ContractEnum("ScopeTone", "Ink")));
        s.Gauge("ace", s.Fill(s.Contract("GaugeSpec"), "Id", "ace", "Label", "ACE HUNT", "Kind", s.ContractEnum("GaugeKind", "Needle"), "Angle", (140f + 5f * t) % 360f, "Text", "VOSS",
            "Sub", Mathf.RoundToInt(38f - 0.4f * t) + "KM", "Tone", s.ContractEnum("ScopeTone", "Ink")));
    }
}
#endif
