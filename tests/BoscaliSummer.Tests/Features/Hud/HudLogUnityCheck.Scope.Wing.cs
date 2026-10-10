#if UNITY_EDITOR
using System;
using UnityEngine;

// WING fixture: pages/wing.js sim(t), the 16 s loop. VIPER 3 rejoins from 180 m (0-4.6 s), all in slot -> ENGAGE ordered at 5.8 (V2 + V3 WILCO, V4 holds on you) -> V2 and V3 run
// out to SU-30 / MIG-29, V2 fires 4 AAM (8.8-12.1) -> WINCHESTER 12.5 -> RTB 13.9 | V4 DEFENDs a SARH shot 9.8-15.4 (decoyed 13.4) | V3 hit and DAMAGED at 15.2.
// Positions are metres in the player's frame (x right, y ahead), so the leader frame of the prototype IS the feed's frame.
public static partial class HudLogUnityCheck
{
    private static readonly float[][] WingSlots = { new[] { 210f, -140f }, new[] { -210f, -140f }, new[] { -420f, -280f } };
    private static readonly float[][] WingFire = { new[] { 8.8f, 9.9f, 11.0f, 12.1f }, new[] { 9.4f, 10.4f, 12.8f }, new float[0] };

    /// <summary>The prototype's pos(i, t) for the wingman at index i (0 = V2).</summary>
    private static Vector2 WingPos(int i, float t)
    {
        const float tHit3 = 15.2f, tDef0 = 9.8f, tDef1 = 15.4f;
        float[] s = WingSlots[i];
        int num = i + 2;
        float wob = 1f - Smooth(Seg01(t, 6.0f, 7.2f));
        float x = s[0] + 9f * Mathf.Sin(t * 0.9f + num * 2.1f) * wob, y = s[1] + 6f * Mathf.Sin(t * 0.6f + num * 1.3f) * wob;
        if (i == 0)
        {
            float e = Smooth(Seg01(t, 6.2f, 12.5f)), r = Smooth(Seg01(t, 12.8f, 17.6f));
            x += (760f - s[0]) * e + (-420f - 760f) * r; y += (2140f - s[1]) * e + (-900f - 2140f) * r;
        }
        else if (i == 1)
        {
            float j = Smooth(Seg01(t, 0f, 4.6f)), e = Smooth(Seg01(t, 6.5f, 15f));
            x += (-140f - s[0]) * (1f - j) + (-700f - s[0]) * e - 70f * Seg01(t, tHit3, 18f); y += (-320f - s[1]) * (1f - j) + (1900f - s[1]) * e - 60f * Seg01(t, tHit3, 18f);
        }
        else
        {
            float b = Mathf.Sin(Mathf.PI * Seg01(t, tDef0, tDef1));
            x += -150f * b; y += 190f * b;
        }
        return new Vector2(x, y);
    }

    private static float WingHdg(int i, float t)
    {
        Vector2 a = WingPos(i, t - 0.06f), b = WingPos(i, t + 0.06f);
        float vx = (b.x - a.x) / 0.12f, vy = (b.y - a.y) / 0.12f;
        return Mathf.Atan2(vx, 110f + vy) * Mathf.Rad2Deg;
    }

    static partial void FixtureWing(float t, ScopeScene s)
    {
        const float tOrder = 5.8f, tHit3 = 15.2f, tDef0 = 9.8f, tMslEnd = 13.4f;
        string[] names = { "SU-30", "MIG-29" };
        float[] brg = { 22f, -14f }, r0 = { 8400f, 9100f }, speed = { 380f, 400f };
        string[] state = new string[3];
        state[0] = t < 6.2f ? "Form" : t < 12.5f ? "Engage" : t < 13.9f ? "Winch" : "Rtb";
        state[1] = t < 4.6f ? "Join" : t < 6.5f ? "Form" : t < tHit3 ? "Engage" : "Damaged";
        state[2] = t >= tDef0 && t < 15.4f ? "Defend" : "Form";

        bool five = s.Variant == "five", client = s.Variant == "client";     // add ("wing", 10f, "five") / ("wing", 10f, "client") to ScopeVariants to render them
        Array members = Array.CreateInstance(s.Contract("WingBoardMember").GetType(), five ? 5 : 3);
        Vector2 v4 = WingPos(2, t);
        for (int i = 0; i < 3; i++)
        {
            Vector2 p = WingPos(i, t);
            float[] slot = WingSlots[i];
            float err = Vector2.Distance(p, new Vector2(slot[0], slot[1]));
            int fired = 0;
            foreach (float x in WingFire[i]) if (t >= x) fired++;
            float fuel = i == 0 ? 58f - 0.8f * t : i == 1 ? 74f - 0.5f * t - 3.5f * Mathf.Max(0f, t - tHit3) : 88f - 0.3f * t;
            float ammo = (i == 2 ? 4 : 4 - fired) / 4f;
            object m = s.Contract("WingBoardMember");
            s.Fill(m, "Number", i + 2, "Name", "#" + (i + 2) + " VIPER", "State", s.ContractEnum("WingState", state[i]), "X", p.x, "Y", p.y, "HeadingDeg", WingHdg(i, t),
                "HasSlot", true, "SlotX", slot[0], "SlotY", slot[1], "SlotErrM", err, "Fuel", fuel / 100f, "Ammo", ammo);
            if (i < 2 && state[i] == "Engage")
                s.Fill(m, "HasTarget", true, "TargetBearingDeg", brg[i], "TargetRangeM", r0[i] - speed[i] * Mathf.Max(0f, t - 6f), "TargetName", names[i]);
            if (state[i] == "Rtb") s.Fill(m, "HasField", true, "Field", "K92", "FieldM", Mathf.Max(8f, Mathf.Round(41f - 3.2f * (t - 13.9f))) * 1000f);
            if (state[i] == "Damaged") s.Fill(m, "HasHull", true, "Hull", 0.62f);
            if (i == 2 && t >= 9.6f && t < tMslEnd)
            {
                float d = Mathf.Lerp(1400f, 80f, Seg01(t, 9.6f, tMslEnd));
                s.Fill(m, "HasThreat", true, "ThreatX", v4.x - 0.77f * d, "ThreatY", v4.y - 0.64f * d, "ThreatTtiS", Mathf.Max(0f, (d - 80f) / 350f), "ThreatSeeker", "SARH", "ThreatReply", "CHAFF");
            }
            if (client) s.Fill(m, "HasSlot", false, "HasTarget", false, "HasThreat", false, "HasHull", false, "HasField", false);
            members.SetValue(m, i);
        }
        if (five)
            for (int i = 3; i < 5; i++)
            {
                object m = s.Contract("WingBoardMember");
                s.Fill(m, "Number", i + 2, "Name", "#" + (i + 2) + (i == 3 ? " GHOST" : " RAVEN"), "State", s.ContractEnum("WingState", i == 3 ? "Form" : "Join"), "X", i == 3 ? 560f : -700f, "Y", i == 3 ? -380f : -420f, "HeadingDeg", 4f,
                    "HasSlot", true, "SlotX", i == 3 ? 630f : -840f, "SlotY", i == 3 ? -420f : -560f, "SlotErrM", i == 3 ? 40f : 190f, "Fuel", i == 3 ? 0.91f : 0.22f, "Ammo", 1f, "Joker", i == 4);
                members.SetValue(m, i);
            }

        // The player's own order acknowledgements, newest first (the prototype's radio log).
        var acks = new[]
        {
            new { t = 0.8f, who = "#3", text = "REJOIN - WILCO", ok = true }, new { t = 6.2f, who = "#2", text = "ENGAGE - WILCO", ok = true }, new { t = 6.6f, who = "#3", text = "ENGAGE - WILCO", ok = true },
            new { t = 6.9f, who = "#4", text = "HOLD ON LEAD", ok = true }, new { t = 8.8f, who = "#2", text = "FOX 3 AAM-4", ok = true }, new { t = 12.4f, who = "#2", text = "WINCHESTER", ok = false },
            new { t = 13.9f, who = "#2", text = "RTB K92 - WILCO", ok = true }, new { t = 15.3f, who = "#3", text = "HIT - UNABLE", ok = false },
        };
        int count = 0;
        Array ackArray = Array.CreateInstance(s.Contract("WingBoardAck").GetType(), 3);
        for (int k = acks.Length - 1; k >= 0 && count < 3; k--)
        {
            if (t < acks[k].t) continue;
            object a = s.Contract("WingBoardAck");
            s.Fill(a, "Who", acks[k].who, "Text", acks[k].text, "Id", acks[k].t, "AgeS", t - acks[k].t, "Accepted", acks[k].ok);
            ackArray.SetValue(a, count++);
        }

        object wing = s.Contract("WingBoardScope");
        s.Fill(wing, "Count", five ? 5 : 3, "Members", members, "Shape", client ? null : "FINGER FOUR", "Client", client, "Engaged", t >= tOrder && t < 15.6f, "AckCount", count, "Acks", ackArray,
            "HasAutopilot", true, "ApText", "NAV 2/5", "ApSub", "K92 STRIP", "ApProgress", (t * 0.2f) % 1f);
        s.Publish(wing);

        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", false, "EngineRunning", true));

        // The ticker the page's notices would put there at this moment.
        string tone = "Info", text; int n = 1;
        float err1 = Vector2.Distance(WingPos(1, t), new Vector2(WingSlots[1][0], WingSlots[1][1]));
        if (state[1] == "Damaged") { tone = "Warning"; text = "#3 HIT - HULL 62%"; }
        else if (state[2] == "Defend") { tone = "Caution"; text = "#4 DEFENDING - " + (t < tMslEnd ? "SARH INBOUND" : "MISSILE DECOYED"); }
        else if (state[0] == "Winch") { tone = "Caution"; text = "#2 WINCHESTER - NO AMMUNITION"; }
        else if (state[0] == "Rtb") text = "#2 RTB - K92";
        else if (t >= tOrder) text = "ENGAGING - FINGER FOUR";
        else if (state[1] == "Join") text = "#3 REJOINING - " + Mathf.RoundToInt(err1 / 10f) * 10 + " M";
        else { tone = "Info"; text = "FINGER FOUR - ALL IN SLOT"; }
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", tone), "Channel", "wing", "Text", text, "Count", n, "Age", 2f));
        s.SetFrame("HasTicker", true);

        s.Gauge("radio", s.Fill(s.Contract("GaugeSpec"), "Id", "radio", "Label", "RADIO", "Kind", s.ContractEnum("GaugeKind", "Bars"), "Value", 1f, "Text", "VIPER", "Sub", "SIG 4/4", "Tone", s.ContractEnum("ScopeTone", "Ink")));
    }
}
#endif
