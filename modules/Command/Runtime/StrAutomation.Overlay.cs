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
            GameObject first = FirstAt(at);
            StrMapTag tag = first != null ? first.GetComponentInParent<StrMapTag>() : null;
            bool hit = tag != null && tag.Id == item;
            r["hit"] = hit ? 1 : 0;
            r["blockedBy"] = hit || first == null ? "" : first.name;
            return r;
        }

        /// <summary>
        /// <c>{kind: "airbase", n}</c>: the same raycast at the centre of the n-th (1-based) native airbase icon of the player's side. found is 1 when such an icon is on
        /// screen; hit is 1 only when the first result is that icon or its hit target and not an STR tag or card, which proves no tag sits on it.
        /// </summary>
        public static Dictionary<string, object> ProbeAt(Dictionary<string, object> args)
        {
            if (Text(args, "kind") != "airbase") return Fail("ProbeAt", "only kind 'airbase' is probed");
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            StrMapOverlay overlay = StrMapOverlay.Active;
            if (map == null || !DynamicMap.mapMaximized || map.mapIcons == null || map.HQ == null) return Fail("ProbeAt", "the map is closed or has no icons");
            overlay?.RefreshNow();
            int want = Mathf.Max(1, (int)Number(args, "n", 1f)), seen = 0;
            Canvas canvas = map.mapImage != null ? map.mapImage.GetComponentInParent<Canvas>() : null;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var r = new Dictionary<string, object> { { "ok", true }, { "found", 0 }, { "hit", 0 }, { "blockedBy", "" } };
            RectTransform viewport = map.mapBackground.rectTransform;
            for (int i = 0; i < map.mapIcons.Count; i++)
            {
                if (!(map.mapIcons[i] is AirbaseMapIcon icon) || icon.airbase == null || icon.airbase.CurrentHQ != map.HQ || icon.iconImage == null ||
                    !icon.iconImage.enabled || !icon.iconImage.gameObject.activeInHierarchy) continue;
                // Only icons on screen count: one behind the map edge cannot be raycast.
                if (!viewport.rect.Contains(viewport.InverseTransformPoint(icon.iconImage.transform.position)) || ++seen != want) continue;
                Vector2 at = RectTransformUtility.WorldToScreenPoint(camera, icon.iconImage.transform.position);
                r["found"] = 1;
                GameObject first = FirstAt(at);
                bool ours = first != null && (first.GetComponentInParent<StrMapTag>() != null || first.GetComponentInParent<StrMapCardMarker>() != null);
                bool isIcon = first != null && (first.GetComponentInParent<AirbaseMapIcon>() == icon || first.GetComponent<AirbaseHitTarget>()?.Icon == icon);
                r["hit"] = isIcon && !ours ? 1 : 0;
                r["blockedBy"] = isIcon && !ours || first == null ? "" : first.name;
                break;
            }
            return r;
        }

        private static GameObject FirstAt(Vector2 screen)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return null;
            var results = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = screen }, results);
            return results.Count > 0 ? results[0].gameObject : null;
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
