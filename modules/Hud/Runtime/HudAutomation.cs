using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Modules.Autopilot.Runtime;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Presentation;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/missile-hud.json); inert outside a
    /// sim run. Fires the seated player's own missiles through the game's own
    /// <c>WeaponStation.Fire</c> and reports the tracker, the C-menu entries and the follow view
    /// through the same seams the HUD and the menu use.
    /// </summary>
    public static class HudAutomation
    {
        private static bool Guard(out Dictionary<string, object> failure)
        {
            failure = null;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                failure = Failure("HudAutomation", "guard", "requires a hosted single-player nomodkit scenario");
            return failure == null;
        }

        /// <summary>Fire one own missile station at the nearest enemy aircraft (blind if none).</summary>
        public static Dictionary<string, object> Fire(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!GameManager.GetLocalAircraft(out Aircraft a) || a == null || a.disabled)
                return Failure("HudAutomation", "Fire", "no live local aircraft");
            WeaponStation station = null;
            if (a.weaponStations != null)
                foreach (WeaponStation s in a.weaponStations)
                {
                    if (s == null || s.WeaponInfo == null || !s.WeaponInfo.missile || s.WeaponInfo.bomb || s.Ammo <= 0)
                        continue;
                    station = s;
                    break;
                }
            if (station == null)
                return Failure("HudAutomation", "Fire", "no loaded missile station");
            Unit target = NearestEnemy(a, Number(args, "range", 15000f));
            int before = OwnMissiles(a);
            try { station.Fire(a, target); }
            catch (Exception e) { return Failure("HudAutomation", "Fire", "station threw: " + e.Message); }
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "station", station.WeaponInfo.shortName ?? station.WeaponInfo.weaponName ?? "MSL" },
                { "ammo_left", station.Ammo },
                { "target", target != null && target.definition != null ? target.definition.unitName : target != null ? "TGT" : "BLIND" },
                { "missiles_before", before },
                { "missiles_after", OwnMissiles(a) },
            };
            Debug.Log("[HudAutomation] Fire: " + Describe(result));
            return result;
        }

        /// <summary>Tracker snapshot: preview lines exactly as the HUD publishes them.</summary>
        public static Dictionary<string, object> Missiles(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            GameManager.GetLocalAircraft(out Aircraft a);
            var tracker = new MissileTracker();
            var tracks = new List<MissileTrack>(MissileTracks.MaxShown);
            tracker.Read(a, tracks);
            AvUnits units = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric ? AvUnits.Metric : AvUnits.Imperial;
            var lines = new List<object>(tracks.Count);
            foreach (MissileTrack t in tracks)
                lines.Add(new Dictionary<string, object>
                {
                    { "line", MissileTracks.LineText(t, units) },
                    { "detail", MissileTracks.DetailText(t) },
                    { "bar", MissileTracks.BarFor(t) },
                    { "tone", MissileTracks.ToneFor(t).ToString() },
                });
            ModuleServices.TryGet(out IMissileView view);
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "count", tracker.Missiles.Count },
                { "shown", tracks.Count },
                { "tracks", lines },
                { "view_active", view?.Active ?? false },
                { "view_label", view?.CurrentLabel ?? "" },
                { "view_status", view?.Status ?? "" },
                { "following", cam != null && cam.followingUnit != null ? cam.followingUnit.GetType().Name : "none" },
            };
            Debug.Log("[HudAutomation] Missiles: " + Describe(result));
            return result;
        }

        /// <summary>C-menu missile entries: visibility/enabled/status of the four VIEW leaves.</summary>
        public static Dictionary<string, object> Menu(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            AceRadialNode root;
            try { root = AceRadialNode.Collect(AceRadialCatalog.Build(), AceRadialMenuTree.MaxNodes, AceRadialMenuTree.MaxDepth); }
            catch (Exception e) { return Failure("HudAutomation", "Menu", "catalog threw: " + e.Message); }
            AceRadialNode view = null;
            if (root != null)
                foreach (AceRadialNode child in root.Children)
                    if (child.Action != null && child.Action.Id == "view") view = child;
            var entries = new List<object>();
            if (view != null)
                foreach (AceRadialNode leaf in view.Children)
                {
                    if (leaf.Action == null || !leaf.Action.Id.StartsWith("msl-", StringComparison.Ordinal)) continue;
                    entries.Add(new Dictionary<string, object>
                    {
                        { "id", leaf.Action.Id },
                        { "label", leaf.Action.Label },
                        { "enabled", leaf.Enabled },
                        { "status", leaf.Status.Text ?? "" },
                    });
                }
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "view_found", view != null },
                { "entries", entries },
            };
            Debug.Log("[HudAutomation] Menu: view=" + (view != null) + " msl_entries=" + entries.Count);
            return result;
        }

        /// <summary>Drive the follow view through IMissileView (the seam the C-menu uses).</summary>
        public static Dictionary<string, object> View(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!ModuleServices.TryGet(out IMissileView view) || view == null)
                return Failure("HudAutomation", "View", "no IMissileView service");
            switch (Text(args, "action"))
            {
                case "enter": view.Enter(); break;
                case "next": view.Next(); break;
                case "prev": view.Prev(); break;
                case "exit": view.Exit(); break;
                default: return Failure("HudAutomation", "View", "action must be enter/next/prev/exit");
            }
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "active", view.Active },
                { "count", view.Count },
                { "label", view.CurrentLabel ?? "" },
                { "following", cam != null && cam.followingUnit != null ? cam.followingUnit.GetType().Name : "none" },
            };
            Debug.Log("[HudAutomation] View: " + Describe(result));
            return result;
        }

        /// <summary>Dock geometry probe: native container rect, children, and our board vs visible content.</summary>
        public static Dictionary<string, object> Dock(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null)
                return Failure("HudAutomation", "Dock", "no CombatHUD");
            var topField = HarmonyLib.AccessTools.Field(typeof(CombatHUD), "topRightPanel");
            var dock = (topField?.GetValue(hud) as GameObject)?.transform as RectTransform;
            if (dock == null)
                return Failure("HudAutomation", "Dock", "no topRightPanel");
            Canvas canvas = dock.GetComponentInParent<Canvas>();
            var kids = new List<object>();
            float cMinX = float.MaxValue, cMaxX = float.MinValue, cMinY = float.MaxValue, cMaxY = float.MinValue;
            string ours = null;
            var corners = new Vector3[4];
            for (int i = 0; i < dock.childCount; i++)
            {
                var child = dock.GetChild(i) as RectTransform;
                if (child == null) continue;
                bool isOurs = child.name == "BoscaliStatusPanel";
                if (isOurs) ours = RectIn(dock, child, corners);
                bool active = child.gameObject.activeInHierarchy;
                bool visual = active && child.GetComponentInChildren<UnityEngine.UI.Graphic>() != null;
                kids.Add(new Dictionary<string, object>
                {
                    { "name", child.name }, { "active", active }, { "visual", visual },
                    { "rect", RectIn(dock, child, corners) },
                    { "components", Components(child) },
                });
                if (!isOurs && visual)
                {
                    child.GetWorldCorners(corners);
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 p = dock.InverseTransformPoint(corners[k]);
                        if (p.x < cMinX) cMinX = p.x;
                        if (p.x > cMaxX) cMaxX = p.x;
                        if (p.y < cMinY) cMinY = p.y;
                        if (p.y > cMaxY) cMaxY = p.y;
                    }
                }
            }
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "dock_rect", $"x[{dock.rect.xMin:F1},{dock.rect.xMax:F1}] y[{dock.rect.yMin:F1},{dock.rect.yMax:F1}] pivot[{dock.pivot.x:F2},{dock.pivot.y:F2}]" },
                { "dock_components", Components(dock) },
                { "content", cMinX <= cMaxX ? $"x[{cMinX:F1},{cMaxX:F1}] y[{cMinY:F1},{cMaxY:F1}]" : "none" },
                { "ours", ours ?? "missing" },
                { "canvas_scale", canvas != null ? canvas.scaleFactor : 0f },
                { "screen", $"{UnityEngine.Screen.width}x{UnityEngine.Screen.height}" },
                { "children", kids },
            };
            Debug.Log("[HudAutomation] Dock: " + Describe(result));
            return result;
        }

        private static string RectIn(RectTransform dock, RectTransform child, Vector3[] corners)
        {
            child.GetWorldCorners(corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = dock.InverseTransformPoint(corners[k]);
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            return $"x[{minX:F1},{maxX:F1}] y[{minY:F1},{maxY:F1}]";
        }

        private static string Components(RectTransform t)
        {
            var list = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c is RectTransform || c is Transform) continue;
                list.Add(c.GetType().Name);
            }
            return string.Join(",", list.ToArray());
        }

        private static int OwnMissiles(Aircraft own)
        {
            int count = 0;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || own == null) return 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is Missile m && m != null && !m.disabled && m.owner == own) count++;
            return count;
        }

        private static Unit NearestEnemy(Aircraft own, float maxM)
        {
            Unit best = null;
            float bestSqr = maxM * maxM;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || own == null) return null;
            Vector3 p = own.transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is Aircraft a) || a == null || a.disabled || a == own) continue;
                if (a.NetworkHQ == null || own.NetworkHQ == null || a.NetworkHQ == own.NetworkHQ) continue;
                float d = (a.transform.position - p).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = a; }
            }
            return best;
        }
    }
}
