#if UNITY_EDITOR
using System;
using UnityEngine;

// SHOT fixture: a reduced version of pages/shot.js - three deliveries from one aircraft flying north at 250 m/s: 0.8 s AAM-4 SKYWAY at B1 MIG-29 (ARH, pitbull at half way, hit at 9.0 s),
// 3.6 s AAM-1 DART at B2 SU-27 (hit at 8.4 s), 6.0 s GBU-12 on G3 SA-19 (falls, splash at 13.0 s). The lead shot is the one soonest to arrive. The header's loadout hook is the prototype's
// (the weapon cell follows the selected station, rounds step down at every launch). Positions are global metres (x east, z north); the feed's arrays are rebuilt per call.
public static partial class HudLogUnityCheck
{
    private struct ShotSim
    {
        public string Short, Full;
        public float T0, T1;
        public int Target;
        public bool Bomb;
    }

    static partial void FixtureShot(float t, ScopeScene s)
    {
        var sims = new[]
        {
            new ShotSim { Short = "AAM-4", Full = "AAM-4 SKYWAY", T0 = 0.8f, T1 = 9.0f, Target = 0 },
            new ShotSim { Short = "DART", Full = "AAM-1 DART", T0 = 3.6f, T1 = 8.4f, Target = 1 },
            new ShotSim { Short = "GBU-12", Full = "GBU-12", T0 = 6.0f, T1 = 13.0f, Target = 2, Bomb = true },
        };
        var targets = new[] { new Vector3(6000f, 4800f, 26000f), new Vector3(-9000f, 5200f, 20000f), new Vector3(2500f, 300f, 12500f) };
        string[] names = { "B1 MIG-29", "B2 SU-27", "G3 SA-19" };
        float ownZ = 250f * t;
        object frame = Activator.CreateInstance(Type(HudDomain + "PlanFrame"), 0f, ownZ, 0f);

        Array missiles = s.DomainArray("ShotMissile", 8), results = s.DomainArray("ShotResult", 8), cells = s.DomainArray("ShotCell", 8), targetArr = s.DomainArray("ShotTarget", 8);
        int mc = 0, rc = 0, cc = 0;
        bool[] hot = new bool[3];
        int lead = -1, leadSim = -1;
        float leadTti = float.MaxValue;
        for (int i = 0; i < sims.Length; i++)
        {
            ShotSim m = sims[i];
            if (t < m.T0) continue;
            float p = Mathf.Clamp01((t - m.T0) / (m.T1 - m.T0));
            Vector3 aim = targets[m.Target], launch = new Vector3(0f, 5000f, 250f * m.T0);
            if (t < m.T1)
            {
                Vector3 pos = Vector3.Lerp(launch, aim, p) + Vector3.up * (m.Bomb ? 0f : 900f * Mathf.Sin(p * Mathf.PI));
                var trail = new float[3 * 12];
                int n = 0;
                for (int k = 9; k >= 0; k--)
                {
                    float tk = t - k * 0.25f;
                    if (tk < m.T0) continue;
                    float pk = Mathf.Clamp01((tk - m.T0) / (m.T1 - m.T0));
                    Vector3 q = Vector3.Lerp(launch, aim, pk) + Vector3.up * (m.Bomb ? 0f : 900f * Mathf.Sin(pk * Mathf.PI));
                    trail[3 * n] = q.x; trail[3 * n + 1] = q.y; trail[3 * n + 2] = q.z; n++;
                }
                Vector3 dir = aim - launch;
                float heading = (Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 360f) % 360f;
                float tti = m.T1 - t, range = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(aim.x, aim.z));
                string phase = m.Bomb ? "Fall" : p < 0.12f ? "Boost" : p < 0.45f ? "Mid" : p < 0.8f ? "Active" : "Term";
                bool arh = m.Short == "AAM-4", pit = arh && p >= 0.45f;
                missiles.SetValue(s.Fill(s.Domain("ShotMissile"), "Id", i + 1, "Short", m.Short, "Full", m.Full, "Kind", s.DomainEnum("ShotKind", m.Bomb ? "Bomb" : "Missile"),
                    "Phase", s.DomainEnum("ShotPhase", phase), "Arh", arh, "Pitbull", pit, "PitbullS", pit ? (p - 0.45f) * (m.T1 - m.T0) : 99f,
                    "Pos", s.Fill(s.Domain("GPos"), "X", pos.x, "Y", pos.y, "Z", pos.z), "HeadingDeg", heading, "Target", m.Target, "HasAim", true,
                    "Aim", s.Fill(s.Domain("GPos"), "X", aim.x, "Y", aim.y, "Z", aim.z), "RangeM", range, "ClosureMps", range / Mathf.Max(0.1f, tti), "TtiS", tti, "Progress", p,
                    "Trail", trail, "TrailCount", n), mc);
                hot[m.Target] = true;
                if (tti < leadTti) { leadTti = tti; lead = mc; leadSim = i; }
                mc++;
                cells.SetValue(s.Fill(s.Domain("ShotCell"), "Name", m.Short, "State", s.DomainEnum("ShotState", "InFlight"), "Frac", p, "AgeS", 0f), cc++);
            }
            else
            {
                float age = t - m.T1;
                cells.SetValue(s.Fill(s.Domain("ShotCell"), "Name", m.Short, "State", s.DomainEnum("ShotState", "Hit"), "Frac", 1f, "AgeS", age), cc++);
                if (age < 3f) results.SetValue(s.Fill(s.Domain("ShotResult"), "State", s.DomainEnum("ShotState", "Hit"), "AgeS", age, "Pos", s.Fill(s.Domain("GPos"), "X", aim.x, "Y", aim.y, "Z", aim.z)), rc++);
            }
        }
        if (mc == 0 && rc == 0) return;
        for (int i = 0; i < 3; i++)
            targetArr.SetValue(s.Fill(s.Domain("ShotTarget"), "Id", i + 1, "Name", names[i], "Air", i < 2, "Hot", hot[i], "Pos", s.Fill(s.Domain("GPos"), "X", targets[i].x, "Y", targets[i].y, "Z", targets[i].z),
                "VelX", i == 0 ? -120f : i == 1 ? 140f : 0f, "VelZ", i == 0 ? -200f : i == 1 ? -180f : 0f), i);

        object scope = s.Fill(s.Domain("ShotScope"), "Frame", frame, "OwnAltM", 5000f, "MissileCount", mc, "Missiles", missiles, "TargetCount", 3, "Targets", targetArr,
            "ResultCount", rc, "Results", results, "CellCount", cc, "Cells", cells, "NeedKm", 32f);
        if (lead >= 0)
        {
            Vector3 aim = targets[sims[leadSim].Target];
            s.Fill(scope, "HasLead", true, "LeadName", names[sims[leadSim].Target], "LeadBearingDeg", (Mathf.Atan2(aim.x, aim.z - ownZ) * Mathf.Rad2Deg + 360f) % 360f,
                "LeadRangeM", Vector2.Distance(new Vector2(0f, ownZ), new Vector2(aim.x, aim.z)));
        }
        s.Publish(scope);
        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", false, "EngineRunning", true));
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", "Info"), "Channel", "shot", "Text", mc > 0 ? "AWAY " + sims[leadSim].Full : "HIT", "Count", 1, "Age", 2f));
        s.SetFrame("HasTicker", true);
        s.Gauge("ace", s.Fill(s.Contract("GaugeSpec"), "Id", "ace", "Label", "ACE HUNT", "Kind", s.ContractEnum("GaugeKind", "Needle"), "Angle", (140f + 5f * t) % 360f, "Text", "VOSS",
            "Sub", Mathf.RoundToInt(38f - 0.4f * t) + "KM", "Tone", s.ContractEnum("ScopeTone", "Ink")));
    }
}
#endif
