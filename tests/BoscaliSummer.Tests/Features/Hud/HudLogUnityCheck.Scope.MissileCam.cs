#if UNITY_EDITOR
using System;
using UnityEngine;

// MISSILE CAM fixture: pages/missilecam.js sim(t) - riding AAM-4 SKYWAY (MSL 1/2) to a MIG-29; the cam attaches at the loft apex, 12.4 km out and half way along a 24.8 km flight,
// impact at 13.0 s. 0.0 MID on the data-link, 4.5 pitbull (ACT at 8.1 km), 7.5 - 8.9 the target jinks out of the 35 degree cone (BLIND: the "blind" variant at t = 8; the target
// is still alive, so the feed keeps giving its place and the box follows it to the edge of the view), 10.4 TERM (the last 2.5 km), 13.0 HIT and the lost feed. The profile is the prototype's loft arc since the launch; the horizon is the arc's real
// pitch (the prototype drew it the other way up).
public static partial class HudLogUnityCheck
{
    static partial void FixtureMissileCam(float t, ScopeScene s)
    {
        const float r0 = 12400f, flight = 24800f, tImpact = 13f, tPitbull = 4.5f, cone = 35f;
        float Alt(float u) => 1000f * (Mathf.Lerp(7f, 6.2f, u) + 3.4f * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.65f)));
        float Jink(float x, float back0, float back1) => Smooth(Seg01(x, 7f, 7.8f)) - Smooth(Seg01(x, back0, back1));
        Vector2 Err(float x)        // the target off the boresight, degrees (x right, y down)
        {
            float fade = 1f - Smooth(Seg01(x, 0f, 5f)), calm = 1f - Seg01(x, 9.5f, 12f);
            return new Vector2(10f * fade + 44f * Jink(x, 8.6f, 9.8f) + 2f * Mathf.Sin(x * 1.3f) * calm, -5f * fade + 4f * Jink(x, 8.6f, 9.8f) + 1.2f * Mathf.Sin(x * 1.9f) * calm);
        }

        float tc = Mathf.Min(t, tImpact), u = 0.5f + 0.5f * tc / tImpact, range = r0 * (1f - tc / tImpact);
        Vector2 e = Err(tc);
        bool impact = t >= tImpact, pit = t >= tPitbull, blind = !impact && pit && e.magnitude > cone;
        if (s.Variant == "blind") s.Expect(blind, "The blind variant is inside the scenario's blind window (t = " + t + ")");
        string mode = !pit ? "DataLink" : blind ? "Blind" : "Locked", phase = !pit ? "Mid" : range <= 2500f ? "Term" : "Active";

        const int samples = 40;
        var pathD = new float[samples + 1];
        var pathA = new float[samples + 1];
        for (int i = 0; i <= samples; i++) { float v = u * i / samples; pathD[i] = flight * v; pathA[i] = Alt(v); }

        s.Publish(s.Fill(s.Domain("MissileCamScope"), "Index", 0, "Count", 2, "Weapon", "AAM-4", "Full", "AAM-4 SKYWAY", "Seeker", "ARH", "Target", "MIG-29", "Next", "AAM-4",
            "Mode", s.DomainEnum("CamMode", mode), "HasTarget", true, "HasLast", true, "ErrXDeg", e.x, "ErrYDeg", e.y, "ConeDeg", cone,
            "PitchDeg", Mathf.Atan2(Alt(u + 0.01f) - Alt(u), flight * 0.01f) * Mathf.Rad2Deg, "RollDeg", -(3f * Mathf.Sin(t * 0.7f) - 11f * Jink(t, 8.8f, 10f)), "HeadingDeg", 214f,
            "RangeM", range, "ClosureMps", 950f + 250f * Seg01(t, 10.5f, tImpact), "TtiS", Mathf.Max(0f, tImpact - t),
            "TapeTopM", r0, "ActiveAtM", r0 * (1f - tPitbull / tImpact), "ActivePassed", pit, "Phase", s.DomainEnum("ShotPhase", phase), "BurnLeftS", 0f, "BurnTotalS", 6f,
            "PathCount", samples + 1, "PathD", pathD, "PathA", pathA, "NowD", flight * u, "NowA", Alt(u), "HasTargetPlace", true, "TargetD", flight, "TargetA", Alt(1f),
            "Lost", impact, "LostS", Mathf.Max(0f, t - tImpact), "Result", s.DomainEnum("ShotState", impact ? "Hit" : "InFlight")));

        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", false, "EngineRunning", true));
        string line = impact ? "IMPACT - MIG-29 HIT - FEED LOST" : range <= 2500f ? "TERMINAL - 2.5KM" : t >= 8.9f ? "LOCK REGAINED - 4.1KM" : blind ? "SEEKER BLIND - TARGET NOTCHING" :
            pit ? "PITBULL - ACTIVE LOCK 8.1KM" : "DATA-LINK - AAM-4 SKYWAY - MIG-29 12.4KM";
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", blind ? "Caution" : "Info"), "Channel", "shot", "Text", line, "Count", 1, "Age", 2f));
        s.SetFrame("HasTicker", true);
    }
}
#endif
