#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Exercises the collider selected by PhysX and reads the aircraft's native damage table.</summary>
    public static class MegaplaneSectionProbe
    {
        private sealed class Site
        {
            internal readonly string owner, support, art;
            internal readonly Vector3 origin, direction, eye, look;
            internal readonly float fov;

            internal Site(string owner, string support, string art, Vector3 origin, Vector3 direction, Vector3 eye, Vector3 look, float fov)
            {
                this.owner = owner; this.support = support; this.art = art;
                this.origin = origin; this.direction = direction; this.eye = eye; this.look = look; this.fov = fov;
            }
        }

        // Coordinates in the aircraft's native +Z-forward space. The check fails if another skin covers a target.
        private static readonly Dictionary<string, Site> Sites = new Dictionary<string, Site>
        {
            { "tip", new Site("wingtip_L", "wing2_L", "Wing_Tip_L", new Vector3(-166f, 30f, -48f), Vector3.down,
                new Vector3(-184f, 54f, 3f), new Vector3(-152f, 4f, -41f), 43f) },
            { "flap", new Site("innerFlap_L", "gearbay_L", "Flap_Inner_L", new Vector3(-61f, 32f, -30f), Vector3.down,
                new Vector3(-83f, 40f, -80f), new Vector3(-68f, 5f, -25f), 40f) },
            { "engine", new Site("pod4_L", "wing2_L", "Pod_L4", new Vector3(-134f, -35f, -27f), Vector3.up,
                new Vector3(-158f, -8f, 24f), new Vector3(-123f, -1f, -19f), 43f) },
            { "hull", new Site("hullPlate_L", null, "HullPlate_L", new Vector3(-23f, .4f, -7.25f), Vector3.right,
                new Vector3(-52f, -5f, -23f), new Vector3(-14f, 0f, -8f), 35f) },
        };

        public static object Section(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a && MegaplaneModule.Is(a)))
                throw new ArgumentException("A spawned Groza is required.");
            Site site = Sites[(string)args["site"]];
            if (args.ContainsKey("view"))
                return MegaplaneAutomation.View(new Dictionary<string, object> { { "idUnit", a }, { "x", site.eye.x }, { "y", site.eye.y },
                    { "z", site.eye.z }, { "lx", site.look.x }, { "ly", site.look.y }, { "lz", site.look.z }, { "fov", site.fov } });
            float impact = args.TryGetValue("impact", out object raw) ? Convert.ToSingle(raw) : 0f;
            return Observe(a, site, impact, args.ContainsKey("native"), true);
        }

        public static string Peer(string site, float impact)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new InvalidOperationException("Section replication checks require the loopback test sandbox.");
            Aircraft a = UnityEngine.Object.FindObjectsOfType<Aircraft>().FirstOrDefault(p => MegaplaneModule.Is(p) && p.NetworkUniqueName == "Groza peer test");
            return a == null ? "{}" : JsonConvert.SerializeObject(Observe(a, Sites[site], impact));
        }

        private static object Observe(Aircraft a, Site site, float impact, bool native = false, bool readHud = false)
        {
            if (impact < 0f || impact > 120f || float.IsNaN(impact)) throw new ArgumentOutOfRangeException(nameof(impact));
            var from = a.rb.position + a.rb.rotation * site.origin;
            var along = a.rb.rotation * site.direction;
            var hits = Physics.RaycastAll(from, along, 80f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.transform.IsChildOf(a.transform)).OrderBy(h => h.distance).ToArray();
            Collider skin = hits.Length == 0 ? null : hits[0].collider;
            MegaplaneHitbox box = skin == null ? null : skin.GetComponent<MegaplaneHitbox>();
            string selected = box != null && box.Owner != null ? box.Owner.name : "";
            float prior = box != null && box.Owner != null ? box.Owner.hitPoints : 0f;
            if (impact > 0f || native)
            {
                if (!a.IsServer) throw new InvalidOperationException("Only the simulation host may apply an impact.");
                if (selected != site.owner) throw new InvalidOperationException("Expected " + site.owner + "; ray hit " +
                    string.Join(" / ", hits.Select(h => h.collider.name + "@" + (Quaternion.Inverse(a.rb.rotation) * (h.point - a.rb.position)).ToString("F2"))));
                if (native)
                {
                    // Exercise the unmodified game's registered-contact solver. Its own linecast, surface normal,
                    // penetration loss and armor processing choose and damage the first exterior surface.
                    DamageEffects.ArmorPenetrate(hits[0].point - along * .03f, along * 1.25f, 1.25f, 400f, 0f, PersistentID.None);
                }
                else skin.GetComponent<IDamageable>().TakeDamage(0f, 0f, 1f, 0f, impact, PersistentID.None);
            }
            // Part IDs in this table are the native indexed references used by the game's damage and detach messages.
            var parts = a.damageables.Select(slot => slot.Damageable).OfType<AeroPart>().Where(p => p != null).ToDictionary(p => p.name);
            var target = parts.TryGetValue(site.owner, out AeroPart t) ? t : null;
            var opposite = parts[site.owner.Substring(0, site.owner.Length - 1) + "R"];
            var support = site.support == null ? a.GetComponent<AeroPart>() : parts[site.support];
            bool Attached(AeroPart p) => p != null && !p.IsDetached() && p.transform.IsChildOf(a.transform);
            float Health(AeroPart p) => Attached(p) ? Mathf.Clamp(p.hitPoints, 0f, 100f) : 0f;
            Transform art = a.GetComponentsInChildren<Transform>(true).FirstOrDefault(p => p.name == site.art);
            var damage = a.GetComponent<MegaplaneDamage>();
            StatusDisplay hud = readHud ? UnityEngine.Object.FindObjectsOfType<StatusDisplay>().FirstOrDefault(d => d.name.StartsWith("StatusDisplay_KY44") &&
                HarmonyLib.Traverse.Create(d).Field<Aircraft>("aircraft").Value == a) : null;
            var tiles = hud == null ? null : HarmonyLib.Traverse.Create(hud).Field<List<PartStatusDisplay>>("statusDisplays").Value;
            var tile = tiles?.FirstOrDefault(p => p.partImage.name == site.owner);
            var otherTile = tiles?.FirstOrDefault(p => p.partImage.name == opposite.name);
            return new
            {
                id = a.NetId, authority = a.IsServer, disabled = a.disabled, target = site.owner, registered = target != null,
                rayOwner = selected, hitCollider = skin == null ? "" : skin.name, pickedTarget = selected == site.owner,
                health = Health(target), attached = Attached(target), opposite = Health(opposite), support = Health(support),
                ownsArt = art != null && target != null && art.GetComponentInParent<AeroPart>() == target,
                affected = parts.Values.Count(p => Health(p) < 99.9f), leftLift = damage.LiftL, rightLift = damage.LiftR,
                thrust = a.GetComponent<MegaplaneFlight>().Thrust, removedHP = prior - (box != null && box.Owner != null ? box.Owner.hitPoints : prior),
                hudSections = tiles?.Count ?? 0, hudBound = tile != null && target != null && HarmonyLib.Traverse.Create(tile).Field<UnitPart>("unitPart").Value == target,
                hudAlpha = tile == null ? -1f : tile.partImage.color.a, hudGreen = tile == null ? -1f : tile.partImage.color.g,
                hudOpposite = otherTile == null ? -1f : otherTile.partImage.color.a,
                parts = parts.ToDictionary(x => x.Key, x => new { id = (int)x.Value.id, hp = Health(x.Value), on = Attached(x.Value) }),
            };
        }
    }
}
#endif

