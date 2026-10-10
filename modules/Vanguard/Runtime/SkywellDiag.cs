using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    // TEMPORARY sim diagnostics for the SKYWELL multi-receiver break-up investigation. Not for commit.
    internal static class SkywellDiag
    {
        private static readonly bool On = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));
        private static readonly Dictionary<Aircraft, int> detached = new Dictionary<Aircraft, int>();
        private static readonly Dictionary<Aircraft, HashSet<UnitPart>> seen = new Dictionary<Aircraft, HashSet<UnitPart>>();
        private static float nextTick, nextBeat;
        private static float nextDump = 15f;
        private static readonly HashSet<Missile> missiles = new HashSet<Missile>();
        private static readonly Dictionary<Missile, string> lastInfo = new Dictionary<Missile, string>();

        public static void Log(string s)
        {
            if (On) Plugin.Logger?.LogInfo("[SKYDIAG] t=" + Time.timeSinceLevelLoad.ToString("F2") + " " + s);
        }

        public static string Att(Aircraft a)
        {
            if (a == null || a.rb == null) return "";
            Vector3 f = a.transform.forward, v = a.rb.velocity;
            float pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            float gamma = v.sqrMagnitude > 1f ? Mathf.Asin(Mathf.Clamp(v.normalized.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
            float aoa = Vector3.SignedAngle(Vector3.ProjectOnPlane(v, a.transform.right), a.transform.forward, a.transform.right) * -1f;
            var inp = a.GetInputs();
            return "pit=" + pitch.ToString("F1") + " gam=" + gamma.ToString("F1") + " aoa=" + aoa.ToString("F1") + " spd=" + v.magnitude.ToString("F0") +
                   " in(p" + inp.pitch.ToString("F2") + ",t" + inp.throttle.ToString("F2") + ")";
        }

        private static readonly List<KeyValuePair<Aircraft, float>> tracks = new List<KeyValuePair<Aircraft, float>>();
        public static void Track(Aircraft a) { if (On && a != null) tracks.Add(new KeyValuePair<Aircraft, float>(a, Time.timeSinceLevelLoad + 8f)); }
        private static float nextTrack;

        private static string Id(Aircraft a) => a == null ? "null" : a.persistentID.Id.ToString();

        public static void Tick(Aircraft tanker)
        {
            if (!On || tanker == null) return;
            Transform tt0 = tanker.transform;
            float now = Time.timeSinceLevelLoad;
            if (now < nextTick) return;
            nextTick = now + 0.1f;
            if (now > nextDump)
            {
                nextDump = now + 10f;
                var groups = new SortedDictionary<string, string>();
                foreach (Unit u in UnitRegistry.allUnits)
                {
                    if (u == null || u is Aircraft || u is Missile || u.NetworkHQ == null || u.NetworkHQ == tanker.NetworkHQ || u.definition == null) continue;
                    string n = u.GetType().Name + ":" + u.definition.unitName;
                    GlobalPosition gp = u.GlobalPosition();
                    float d = Mathf.Sqrt((float)(gp.x * gp.x + gp.z * gp.z));
                    groups.TryGetValue(n, out string cur);
                    int cnt = 1; float best = float.MaxValue;
                    if (cur != null) { var parts = cur.Split('|'); cnt = int.Parse(parts[0]) + 1; best = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); }
                    string at = cur != null ? cur.Substring(cur.IndexOf('|', cur.IndexOf('|') + 1) + 1) : "";
                    if (d < best) { best = d; at = gp.x.ToString("F0") + "," + gp.y.ToString("F0") + "," + gp.z.ToString("F0"); }
                    groups[n] = cnt + "|" + best.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "|" + at;
                }
                foreach (var kv in groups) if (kv.Key.StartsWith("GroundVehicle") || kv.Key.StartsWith("Ship")) Log("HOSTILE " + kv.Key + " count|nearestFromOrigin|at=" + kv.Value);
            }
            if (tracks.Count > 0 && now >= nextTrack)
            {
                nextTrack = now + 0.5f;
                for (int i = tracks.Count - 1; i >= 0; i--)
                {
                    Aircraft ta = tracks[i].Key;
                    if (ta == null || now > tracks[i].Value) { tracks.RemoveAt(i); continue; }
                    Log("TRACK " + Id(ta) + " rel=" + tt0.InverseTransformPoint(ta.transform.position).ToString("F0") + " " + Att(ta) + " tanker:" + Att(tanker));
                }
            }
            bool beat = now >= nextBeat;
            if (beat) nextBeat = now + 1f;
            IReadOnlyList<Aircraft> all = UnitRegistry.allAircraft;
            Transform tt = tanker.transform;
            var sb = new StringBuilder();
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft a = all[i];
                if (a == null) continue;
                int det = 0;
                if (!seen.TryGetValue(a, out HashSet<UnitPart> set)) seen[a] = set = new HashSet<UnitPart>();
                var fresh = new List<string>();
                foreach (UnitPart p in a.partLookup)
                    if (p != null && p.IsDetached())
                    {
                        det++;
                        if (set.Add(p)) fresh.Add(p.name);
                    }
                Vector3 rel = tt.InverseTransformPoint(a.transform.position);
                Vector3 vrel = a.rb != null && tanker.rb != null ? tt.InverseTransformVector(a.rb.velocity - tanker.rb.velocity) : Vector3.zero;
                if (fresh.Count > 0)
                {
                    // who is nearest?
                    string near = "";
                    float best = float.MaxValue;
                    for (int j = 0; j < all.Count; j++)
                    {
                        Aircraft b = all[j];
                        if (b == null || b == a) continue;
                        float d = Vector3.Distance(a.transform.position, b.transform.position);
                        if (d < best) { best = d; near = Id(b) + "@" + d.ToString("F1") + "m"; }
                    }
                    Log("DETACH " + Id(a) + " " + a.definition.unitName + (a == tanker ? " (TANKER)" : "") + " +" + fresh.Count + " [" + string.Join(",", fresh) + "] total=" +
                        det + " rel=" + rel.ToString("F1") + " vrel=" + vrel.ToString("F1") + " nearest=" + near + " disabled=" + a.disabled);
                }
                if (beat)
                    sb.Append(Id(a)).Append(a == tanker ? "T" : "").Append(" rel=").Append(rel.ToString("F0")).Append(" v=").Append(vrel.ToString("F0"))
                      .Append(" det=").Append(det).Append(a.disabled ? " DEAD" : "").Append(" ").Append(Att(a)).Append(" | ");
            }
            if (beat) Log("BEAT tankerGlobal=" + tanker.GlobalPosition() + " " + sb);
            foreach (Missile gone in new List<Missile>(missiles))
                if (gone == null || gone.disabled)
                {
                    missiles.Remove(gone);
                    if (gone != null && lastInfo.TryGetValue(gone, out string li))
                    {
                        string nearA = ""; float bd = float.MaxValue;
                        for (int j = 0; j < all.Count; j++)
                            if (all[j] != null) { float d = Vector3.Distance(gone.transform.position, all[j].transform.position); if (d < bd) { bd = d; nearA = Id(all[j]) + "@" + d.ToString("F0") + "m"; } }
                        if (!li.Contains("hq=Boscali")) Log("MISSILE END " + li + " nearestAircraftNow=" + nearA);
                    }
                }
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
                if (units[i] is Missile m && !m.disabled)
                {
                    bool first = missiles.Add(m);
                    if (!first && !beat) continue;
                    UnitRegistry.TryGetUnit(m.targetID, out Unit tgt);
                    Vector3 mv = m.rb != null ? m.rb.velocity : Vector3.zero;
                    UnitRegistry.TryGetUnit(m.ownerID, out Unit own);
                    string info = m.persistentID.Id + " " + m.definition?.unitName + " hq=" + (m.NetworkHQ != null ? m.NetworkHQ.faction?.factionName : "none") + " shooter=" + (own != null ? own.definition?.unitName + "@" + own.GlobalPosition() + (own.NetworkHQ != null ? "/" + own.NetworkHQ.faction?.factionName : "") : "?") +
                        " target=" + (tgt != null ? Id(tgt as Aircraft) : "?") + " rel=" + tt.InverseTransformPoint(m.transform.position).ToString("F0") + " toTgt=" + (tgt != null ? Vector3.Distance(tgt.transform.position, m.transform.position).ToString("F0") : "?");
                    lastInfo[m] = info;
                    if (info.Contains("hq=Boscali")) continue;
                    Log((first ? "MISSILE NEW " : "MISSILE ") + m.persistentID.Id + " " + m.definition?.unitName + " hq=" + (m.NetworkHQ != null ? m.NetworkHQ.faction?.factionName : "none") +
                        " owner=" + m.ownerID + " target=" + (tgt != null ? Id(tgt as Aircraft) + ":" + tgt.definition?.unitName : m.targetID.ToString()) +
                        " rel=" + tt.InverseTransformPoint(m.transform.position).ToString("F0") + " speed=" + mv.magnitude.ToString("F0") +
                        " toTgt=" + (tgt != null ? Vector3.Distance(tgt.transform.position, m.transform.position).ToString("F0") : "?"));
                }
        }
    }
}
