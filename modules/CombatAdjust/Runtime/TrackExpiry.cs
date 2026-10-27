using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.CombatAdjust.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Runtime
{
    /// <summary>
    /// Forgets moving contacts nobody has seen for their memory, on every peer (each runs it on its own copy, from its own report times).
    /// Never a contact a missile or a gun is on: ARHSeeker/ARMSeeker read its track unchecked. The forget sequence:
    /// unhook the discover-path handlers so a rediscovery does not stack
    /// them, deregister, then drop the map icon and HUD marker, which both cache the removed TrackingInfo.
    /// </summary>
    internal sealed class TrackExpiry
    {
        private static readonly MethodInfo ChangeFaction = AccessTools.Method(typeof(FactionHQ), "HQ_OnUnitChangeFaction", new[] { typeof(Unit) });
        private static readonly MethodInfo IconGone = AccessTools.Method(typeof(UnitMapIcon), "UnitMapIcon_OnUnitDisabled", new[] { typeof(Unit) });
        private static readonly MethodInfo MarkerGone = AccessTools.Method(typeof(HUDUnitMarker), "HUDIcon_OnDisableUnit", new[] { typeof(Unit) });
        private static readonly AccessTools.FieldRef<DynamicMap, Dictionary<Unit, UnitMapIcon>> Icons = Ref<DynamicMap, Dictionary<Unit, UnitMapIcon>>("iconLookup");
        private static readonly AccessTools.FieldRef<CombatHUD, Dictionary<Unit, HUDUnitMarker>> Markers = Ref<CombatHUD, Dictionary<Unit, HUDUnitMarker>>("markerLookup");

        private readonly List<PersistentID> due = new List<PersistentID>();
        private readonly HashSet<uint> engaged = new HashSet<uint>();
        private readonly object[] arg = new object[1];
        private bool warned;

        public void Sweep(float now, float expirySeconds, MissileBook book)
        {
            engaged.Clear();
            foreach (Missile m in book.Live)
            {
                if (m == null) continue;
                if (m.targetID.IsValid) engaged.Add(m.targetID.Id);
                // ARH terminal mode clears targetID but its seeker still reads the old target's track (SlowChecks).
                if (book.TryGet(m, out MissileBook.Entry e) && e.LastTarget.IsValid) engaged.Add(e.LastTarget.Id);
            }
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null) continue;
                due.Clear();
                foreach (KeyValuePair<PersistentID, TrackingInfo> kv in hq.trackingDatabase)
                {
                    TrackingInfo info = kv.Value;
                    if (info == null || !info.TryGetUnit(out Unit unit) || unit == null || unit.NetworkHQ == hq) continue;
                    bool busy = info.missileAttacks > 0 || info.attackers > 0 || engaged.Contains(kv.Key.Id);
                    if (TrackFog.Expired(CombatUnits.ClassOf(unit), now - info.lastSpottedTime, expirySeconds, busy)) due.Add(kv.Key);
                }
                foreach (PersistentID id in due) Forget(hq, id);
            }
        }

        private void Forget(FactionHQ hq, PersistentID id)
        {
            if (!UnitRegistry.TryGetUnit(id, out Unit unit) || unit == null) return;
            unit.onDisableUnit -= hq.DeregisterTrackedUnit;
            if (ChangeFaction != null) unit.onChangeFaction -= (Action<Unit>)Delegate.CreateDelegate(typeof(Action<Unit>), hq, ChangeFaction);
            hq.DeregisterTrackedUnit(unit);
            CombatStats.Expired++;
            DropUi(hq, unit);
        }

        private void DropUi(FactionHQ hq, Unit unit)
        {
            if (IconGone == null || MarkerGone == null || Icons == null || Markers == null || ChangeFaction == null)
            {
                if (!warned) Debug.LogWarning("[BoscaliSummer] CombatAdjust: a map/HUD member for track expiry is missing; expired contacts may keep a stale icon.");
                warned = true;
            }
            try
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map == null || map.HQ != hq) return;
                arg[0] = unit;
                if (Icons != null && IconGone != null && Icons(map).TryGetValue(unit, out UnitMapIcon icon) && icon != null) IconGone.Invoke(icon, arg);
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null && Markers != null && MarkerGone != null && Markers(hud).TryGetValue(unit, out HUDUnitMarker marker) && marker != null)
                    MarkerGone.Invoke(marker, arg);
            }
            catch (Exception e) { PatchGuard.Report("CombatAdjust.ExpiryUi", e); }
            finally { arg[0] = null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch { return null; }
        }
    }

    internal static class CombatUnits
    {
        public static MoverClass ClassOf(Unit u) =>
            u is Aircraft || u is Missile ? MoverClass.Air :
            u is Ship ? MoverClass.Ship :
            u is GroundVehicle || u is PilotDismounted ? MoverClass.Ground :
            MoverClass.Static;
    }
}
