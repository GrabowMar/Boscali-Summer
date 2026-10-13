#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Hud.Configuration;
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
    /// <c>WeaponStation.Fire</c> and reports the tracker and the follow view
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

        private static object[] pilotOptions;

        /// <summary>
        /// Set any of the TAC BOARD's options through the settings contract (<see cref="ITacBoardSettings"/>, the same door the SET console and the C menu use) so ONE sim run can try several looks, and return every option as it
        /// now stands. The names are the contract's, in any case (enabled, layout, scale, offsetX, offsetY, opacity, shade, declutter, scopeMode, sway, corners, showLamps, showSystemsStrip, showGauges, showTicker, pageThreat,
        /// pageShot, pageMissileCam, pageApproach, pageWing, pageStrike, pageDock, pageObjective, pageSystems, threatHoldSeconds, pageMinSeconds); an enum is given by name (wide, stack, full, lean, minimal, auto, off). A name that is
        /// none of them fails the call, so a typo cannot waste a run. IN MEMORY ONLY: the contract's setters save the config file on every set, so the file's save-on-set is held off around them and put back; the file on
        /// disk is never written by this hook and the pilot's saved options come back with the next start. <c>restore</c> true first puts every option back to what it was at the first call of the run (the pilot's own values).
        /// The board takes a change on its next 10 Hz tick: wait before a capture.
        /// </summary>
        public static Dictionary<string, object> Configure(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!ModuleServices.TryGet(out ITacBoardSettings options) || options == null)
                return Failure("HudAutomation", "Configure", "no TAC BOARD settings service");
            PropertyInfo[] properties = typeof(ITacBoardSettings).GetProperties();
            if (args != null)
                foreach (string key in args.Keys)
                    if (key != "restore" && Array.FindIndex(properties, p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) < 0)
                        return Failure("HudAutomation", "Configure", "no TAC BOARD option called '" + key + "'");
            if (pilotOptions == null)
            {
                pilotOptions = new object[properties.Length];
                for (int i = 0; i < properties.Length; i++) pilotOptions[i] = properties[i].GetValue(options);
            }
            ConfigFile file = (options as HudSettings)?.Enabled.ConfigFile;
            bool save = file != null && file.SaveOnConfigSet;
            if (file != null) file.SaveOnConfigSet = false;
            var result = new Dictionary<string, object> { { "ok", true }, { "saved", false } };
            try
            {
                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo option = properties[i];
                    if (Bool(args, "restore")) option.SetValue(options, pilotOptions[i]);
                    object given = args != null ? FindIgnoringCase(args, option.Name) : null;
                    if (given != null) option.SetValue(options, OptionValue(option.PropertyType, given));
                }
            }
            catch (Exception e) { return Failure("HudAutomation", "Configure", "could not set an option: " + e.Message); }
            finally { if (file != null) file.SaveOnConfigSet = save; }
            foreach (PropertyInfo option in properties)
            {
                object value = option.GetValue(options);
                result[char.ToLowerInvariant(option.Name[0]) + option.Name.Substring(1)] = value is Enum ? value.ToString() : value;
            }
            Debug.Log("[HudAutomation] Configure: " + Describe(result));
            return result;
        }

        private static object FindIgnoringCase(Dictionary<string, object> args, string name)
        {
            foreach (KeyValuePair<string, object> pair in args)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        /// <summary>A scenario's JSON value as the option's own type: a bool, a number, or an enum by name (any case).</summary>
        private static object OptionValue(Type type, object given)
        {
            if (type.IsEnum) return given is string name ? Enum.Parse(type, name, true) : Enum.ToObject(type, Convert.ToInt32(given, CultureInfo.InvariantCulture));
            if (type == typeof(bool)) return Convert.ToBoolean(given, CultureInfo.InvariantCulture);
            return Convert.ToSingle(given, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Pushes DL EVT LOG's test lines through the game's own feed (<c>MessageUI.KillFeed</c> and <c>GameMessage</c>, both ending in <c>MessageFeed.Enqueue</c>, the one place the log's patch listens): a kill line with colour tags, one in
        /// which the pilot's own aircraft is the killer, a plain game message and a "Name: text" chat line. Returns how many it pushed and the pilot's names as the log reads them. Wait two seconds, then capture.
        /// </summary>
        public static Dictionary<string, object> Feed(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            MessageUI ui = SceneSingleton<MessageUI>.i;
            if (ui == null) return Failure("HudAutomation", "Feed", "no MessageUI");
            string own = BoscaliSummer.Modules.Hud.Patches.EventFeed.OwnAircraft();
            ui.KillFeed("<color=#58A8FFFF>Left Hook</color> shot down <color=#FF4D4DFF>Bravo Two</color>");
            ui.KillFeed("<color=#58A8FFFF>" + (own ?? "Anumeric") + "</color> shot down <color=#FF4D4DFF>T9K41 Boltstrike</color>");
            ui.GameMessage("Enemy airbase captured");
            ui.GameMessage("<color=#AABBCCFF>Pilot One:</color> Rolling in on the airfield now");
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "pushed", 4 },
                { "ownPlayer", BoscaliSummer.Modules.Hud.Patches.EventFeed.OwnPlayer() ?? "" }, { "ownAircraft", own ?? "" },
            };
            Debug.Log("[HudAutomation] Feed: " + Describe(result));
            return result;
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

        /// <summary>
        /// Select the first loaded fixed gun, designate the nearest enemy aircraft and report the native
        /// lead pipper (tests/ingame/gun-pipper.json). Call it three times about a second apart: the first
        /// sights the target, the second designates it, the third reads the pipper.
        /// </summary>
        public static Dictionary<string, object> Gun(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!GameManager.GetLocalAircraft(out Aircraft a) || a == null || a.disabled)
                return Failure("HudAutomation", "Gun", "no live local aircraft");
            WeaponStation gun = null;
            if (a.weaponStations != null)
                foreach (WeaponStation s in a.weaponStations)
                {
                    if (s == null || s.WeaponInfo == null || s.WeaponInfo.muzzleVelocity <= 0f || s.HasTurret() || s.Ammo <= 0)
                        continue;
                    gun = s;
                    break;
                }
            if (gun == null)
                return Failure("HudAutomation", "Gun", "no loaded fixed gun");
            Unit target = NearestEnemy(a, Number(args, "range", 3000f));
            if (target == null)
                return Failure("HudAutomation", "Gun", "no enemy aircraft in range");
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.aircraft != a)
                return Failure("HudAutomation", "Gun", "the combat HUD is not attached to the local aircraft");
            // A sighting keeps the track "accurate" for 4 s and spawns the map icon and HUD marker SelectUnit needs.
            a.NetworkHQ.RpcUpdateTrackingInfo(target.persistentID);
            if (a.weaponManager.currentWeaponStation != gun) a.SetActiveStation(gun.Number);
            if (!hud.GetTargetList().Contains(target)) hud.SelectUnit(target);
            HUDBoresightState sight = UnityEngine.Object.FindObjectOfType<HUDBoresightState>();
            var pipper = sight == null ? null
                : HarmonyLib.AccessTools.Field(typeof(HUDBoresightState), "projectedPosition")?.GetValue(sight) as UnityEngine.UI.Image;
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "gun", gun.WeaponInfo.shortName ?? gun.WeaponInfo.weaponName ?? "GUN" },
                { "range_m", Vector3.Distance(target.transform.position, a.transform.position) },
                { "designated", hud.GetTargetList().Contains(target) ? 1 : 0 },
                { "state", sight == null ? "none"
                    : HarmonyLib.AccessTools.Field(typeof(HUDBoresightState), "displayState")?.GetValue(sight)?.ToString() ?? "?" },
                { "pipper", pipper != null && pipper.enabled ? 1 : 0 },
            };
            Debug.Log("[HudAutomation] Gun: " + Describe(result));
            return result;
        }

        /// <summary>Tracker snapshot: preview lines exactly as the HUD publishes them.</summary>
        public static Dictionary<string, object> Missiles(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            GameManager.GetLocalAircraft(out Aircraft a);
            MissileTracker tracker = HudBoard.Instance != null ? HudBoard.Instance.Tracker : null;
            if (tracker == null) return Failure("HudAutomation", "Missiles", "no HudBoard");
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
#endif
