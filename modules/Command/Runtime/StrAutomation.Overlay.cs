using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using UnityEngine;
using UnityEngine.EventSystems;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    // The overlay's hooks (annex 05 P11): the raycasts that prove a tag is the first thing under the pointer and an airbase icon still is, and the overlay block of Read.
    public static partial class StrAutomation
    {
        /// <summary>
        /// <c>{id}</c> of a tag ("map.tag.prop.1"): <c>EventSystem.RaycastAll</c> at the tag's centre. Returns found (the tag is on screen), hit (1 only when that tag is the
        /// first result) and blockedBy (the name of the first other object). Reads only.
        /// </summary>
        public static Dictionary<string, object> Probe(Dictionary<string, object> args)
        {
            StrMapOverlay overlay = StrMapOverlay.Active;
            StrViewHub hub = StrViewHub.Active;
            if (overlay == null || hub == null) return Fail("Probe", "no STR overlay in this scene");
            hub.TickNow();
            overlay.RefreshNow();
            string id = Text(args, "id");
            if (!StrControlIds.TryItem(hub.Current, id, out StrItemId item)) return Fail("Probe", "no board item named '" + id + "'");
            var r = new Dictionary<string, object> { { "ok", true }, { "found", 0 }, { "hit", 0 }, { "blockedBy", "" } };
            if (!overlay.TryGetTagScreenPoint(item, out Vector2 at)) return r;
            r["found"] = 1;
            GameObject first = FirstAt(at, out int count);
            StrMapTag tag = first != null ? first.GetComponentInParent<StrMapTag>() : null;
            StrMapTag shown = overlay.Tag(item);
            Canvas canvas = shown.GetComponentInParent<Canvas>();
            r["debug"] = new Dictionary<string, object>
            {
                { "x", at.x }, { "y", at.y }, { "results", count }, { "es", EventSystem.current != null ? 1 : 0 }, { "esAny", Resources.FindObjectsOfTypeAll<EventSystem>().Length },
                { "canvasMode", canvas != null ? (int)canvas.rootCanvas.renderMode : -1 }, { "nested", canvas != null && !canvas.isRootCanvas ? 1 : 0 },
                { "raycasters", Object.FindObjectsOfType<UnityEngine.UI.GraphicRaycaster>().Length },
                { "plateRaycast", shown.GetComponentInChildren<UnityEngine.UI.Graphic>().raycastTarget ? 1 : 0 },
            };
            bool hit = tag != null && tag.Id == item;
            r["hit"] = hit ? 1 : 0;
            r["blockedBy"] = hit || first == null ? "" : first.name;
            return r;
        }

        /// <summary>
        /// <c>{kind: "airbase", n}</c>: the same raycast at the centre of the n-th (1-based) native airbase icon of the player's side. found is 1 when such an icon is on
        /// screen; hit is 1 when something native is the first result there and it is not an STR tag or card, which proves no tag sits on it (icon says whether it is the airbase's own).
        /// </summary>
        public static Dictionary<string, object> ProbeAt(Dictionary<string, object> args)
        {
            if (Text(args, "kind") != "airbase") return Fail("ProbeAt", "only kind 'airbase' is probed");
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            StrMapOverlay overlay = StrMapOverlay.Active;
            if (map == null || !DynamicMap.mapMaximized || map.HQ == null) return Fail("ProbeAt", "the map is closed or has no side");
            overlay?.RefreshNow();
            int want = Mathf.Max(1, (int)Number(args, "n", 1f)), seen = 0;
            Canvas canvas = map.mapImage != null ? map.mapImage.GetComponentInParent<Canvas>() : null;
            if (canvas != null) canvas = canvas.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var r = new Dictionary<string, object> { { "ok", true }, { "found", 0 }, { "hit", 0 }, { "blockedBy", "" } };
            RectTransform viewport = map.mapBackground.rectTransform;
            // The airbase icons live in the scene, not in map.mapIcons (which holds the unit icons).
            AirbaseMapIcon[] icons = Object.FindObjectsOfType<AirbaseMapIcon>();
            int withAirbase = 0, friendly = 0, active = 0, onScreen = 0;
            for (int i = 0; i < icons.Length; i++)
            {
                AirbaseMapIcon icon = icons[i];
                if (icon.airbase == null) continue;
                withAirbase++;
                if (icon.airbase.CurrentHQ != map.HQ) continue;
                friendly++;
                if (icon.iconImage == null || !icon.iconImage.gameObject.activeInHierarchy) continue;
                active++;
                // Only icons on screen count: one behind the map edge cannot be raycast.
                if (!viewport.rect.Contains(viewport.InverseTransformPoint(icon.iconImage.transform.position))) continue;
                onScreen++;
                if (++seen != want) continue;
                Vector2 at = RectTransformUtility.WorldToScreenPoint(camera, icon.iconImage.transform.position);
                r["found"] = 1;
                GameObject first = FirstAt(at);
                // The claim is that an STR tag or card does not sit on the icon: whatever the map itself puts first there (the airbase mark, or a unit stacked on the base) is a hit.
                bool ours = first != null && (first.GetComponentInParent<StrMapTag>() != null || first.GetComponentInParent<StrMapCardMarker>() != null);
                r["hit"] = first != null && !ours ? 1 : 0;
                r["blockedBy"] = ours ? first.name : "";
                r["icon"] = first != null && (first.GetComponentInParent<AirbaseMapIcon>() == icon || first.GetComponent<AirbaseHitTarget>()?.Icon == icon) ? 1 : 0;
            }
            r["debug"] = new Dictionary<string, object>
            {
                { "airbaseObjects", icons.Length }, { "withAirbase", withAirbase }, { "friendly", friendly }, { "active", active }, { "onScreen", onScreen }, { "hasHq", map.HQ != null ? 1 : 0 },
            };
            return r;
        }

        private static GameObject FirstAt(Vector2 screen) => FirstAt(screen, out _);

        // What the pointer would hit: the event system's own RaycastAll. The game keeps its event system disabled while the cursor is locked (a sim run never unlocks
        // it), so then a disabled one is used if any exists, and otherwise every active GraphicRaycaster is asked and the results are put in the event system's order.
        private static GameObject FirstAt(Vector2 screen, out int count)
        {
            count = 0;
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                EventSystem[] all = Resources.FindObjectsOfTypeAll<EventSystem>();
                if (all.Length > 0) es = all[0];
            }
            var results = new List<RaycastResult>();
            var data = new PointerEventData(es) { position = screen };
            if (es != null) es.RaycastAll(data, results);
            else
            {
                foreach (UnityEngine.UI.GraphicRaycaster raycaster in Object.FindObjectsOfType<UnityEngine.UI.GraphicRaycaster>()) raycaster.Raycast(data, results);
                results.Sort(Order);
            }
            count = results.Count;
            return results.Count > 0 ? results[0].gameObject : null;
        }

        // The event system's raycast order: render camera depth, module priorities, sorting layer and order, then graphic depth (higher first), then distance.
        private static int Order(RaycastResult a, RaycastResult b)
        {
            if (a.module != b.module)
            {
                Camera ca = a.module.eventCamera, cb = b.module.eventCamera;
                if (ca != null && cb != null && ca.depth != cb.depth) return ca.depth < cb.depth ? 1 : -1;
                if (a.module.sortOrderPriority != b.module.sortOrderPriority) return b.module.sortOrderPriority.CompareTo(a.module.sortOrderPriority);
                if (a.module.renderOrderPriority != b.module.renderOrderPriority) return b.module.renderOrderPriority.CompareTo(a.module.renderOrderPriority);
            }
            if (a.sortingLayer != b.sortingLayer) return SortingLayer.GetLayerValueFromID(b.sortingLayer).CompareTo(SortingLayer.GetLayerValueFromID(a.sortingLayer));
            if (a.sortingOrder != b.sortingOrder) return b.sortingOrder.CompareTo(a.sortingOrder);
            if (a.depth != b.depth && a.module.rootRaycaster == b.module.rootRaycaster) return b.depth.CompareTo(a.depth);
            if (a.distance != b.distance) return a.distance.CompareTo(b.distance);
            return a.index.CompareTo(b.index);
        }

        // overlay { visible, relief, quads, pulseQuads, overflow, rebuilds, rebuildsPerSecond, msPerSecond, lastRebuildMs, tags { effort, prop, front, sortie, post } }
        private static void AddOverlay(Dictionary<string, object> r)
        {
            StrMapOverlay o = StrMapOverlay.Active;
            if (o == null)
            {
                r["overlay"] = new Dictionary<string, object> { { "present", 0 }, { "visible", 0 } };
                return;
            }
            r["overlay"] = new Dictionary<string, object>
            {
                { "present", 1 }, { "visible", o.Visible ? 1 : 0 }, { "relief", o.Relief ? 1 : 0 }, { "quads", o.InkQuads }, { "pulseQuads", o.PulseQuads },
                { "overflow", o.Overflowed ? 1 : 0 }, { "card", o.CardView != null && o.CardView.Visible ? 1 : 0 }, { "rebuilds", o.Rebuilds }, { "rebuildsPerSecond", o.RebuildsPerSecond }, { "msPerSecond", o.MsPerSecond },
                { "lastRebuildMs", o.LastRebuildMs },
                { "tags", new Dictionary<string, object>
                    {
                        { "effort", o.TagCount(StrTagKind.Effort) }, { "prop", o.TagCount(StrTagKind.Proposal) }, { "front", o.TagCount(StrTagKind.Front) },
                        { "sortie", o.TagCount(StrTagKind.Sortie) }, { "post", o.TagCount(StrTagKind.Post) },
                    } },
            };
        }
    }
}
