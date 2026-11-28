using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Six conformal launch cells carried by the wing damage islands.</summary>
    internal sealed class MegaplaneWingBay : MonoBehaviour
    {
        internal const string HardpointPrefix = "Hardpoint_WingBay_";
        internal const float DoorHold = 4f;
        private const float RailDelay = 1.15f, RailLength = 6.4f, RailSpeed = 9f;
        private static readonly Vector2[] CellPositions = { new(64.25f, -8.5f), new(81.5f, -10f), new(101f, -17.4f) };

        private static readonly FieldInfo Direction = AccessTools.Field(typeof(MountedMissile), "railDirection");
        private static readonly FieldInfo Delay = AccessTools.Field(typeof(MountedMissile), "railDelay");
        private static readonly FieldInfo Length = AccessTools.Field(typeof(MountedMissile), "railLength");
        private static readonly FieldInfo Speed = AccessTools.Field(typeof(MountedMissile), "railSpeed");
        private static readonly FieldInfo BayDoors = AccessTools.Field(typeof(MountedMissile), "bayDoors");
        [SerializeField] private Transform[] points;
        private Aircraft aircraft;
        private WeaponManager manager;

        internal static void Install(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            if (Direction == null || Delay == null || Length == null || Speed == null || BayDoors == null) return;
            int[] sides = { -1, 1 };
            var points = new List<Transform>(6);
            Material guide = null, lens = null;
            for (int index = 0; index < CellPositions.Length; index++)
                foreach (int side in sides)
                {
                    string suffix = side < 0 ? "L" : "R";
                    string wingName = index < 2 ? "Wing_Inner_" : "Wing_Mid_";
                    if (!nodes.TryGetValue(wingName + suffix, out Transform wing) ||
                        !wing.TryGetComponent(out MeshRenderer shell)) continue;
                    AeroPart owner = wing.GetComponentInParent<AeroPart>(true);
                    if (owner == null) continue;
                    Material skin = shell.sharedMaterials.First(m => m.name == "BS_Megaplane_Skin");
                    Material inside = shell.sharedMaterials.First(m => m.name == "BS_Megaplane_Dark");
                    if (guide == null)
                    {
                        guide = Amber(inside.shader, "Wing Bay Caution Paint", .20f);
                        lens = Amber(inside.shader, "Wing Bay Open Indicator", .5f);
                    }
                    string label = index + "_" + suffix;
                    Vector2 plan = CellPositions[index];
                    Vector3 frame = new Vector3(side * plan.x, 0f, plan.y);
                    Transform cell = BuildCell(aircraft.transform, owner.transform, label, frame, skin, inside, guide, lens);
                    Transform point = Child(cell, HardpointPrefix + label, new Vector3(0f, .45f, 0f));
                    nodes.Add(point.name, point);
                    points.Add(point);
                }
            aircraft.gameObject.AddComponent<MegaplaneWingBay>().points = points.ToArray();
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            manager = aircraft.weaponManager;
            manager.OnWeaponsLoaded += SetDropRails;
        }

        private void OnDestroy()
        {
            if (manager != null) manager.OnWeaponsLoaded -= SetDropRails;
        }

        private void SetDropRails()
        {
            var byRail = new HashSet<Weapon>();
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] == null) continue;
                foreach (MountedMissile round in points[i].GetComponentsInChildren<MountedMissile>(true))
                {
                    Direction.SetValue(round, MountedMissile.RailDirection.Down);
                    Delay.SetValue(round, RailDelay);
                    Length.SetValue(round, RailLength);
                    Speed.SetValue(round, RailSpeed);
                    // Empty means MountedMissile opens its own Hardpoint's door instead of donor doors.
                    BayDoors.SetValue(round, Array.Empty<BayDoor>());
                    round.priority = i;
                    byRail.Add(round);
                }
            }
            foreach (WeaponStation station in aircraft.weaponStations)
                if (station.Weapons.Count > 0 && station.Weapons.All(byRail.Contains)) station.SortWeapons(aircraft);
        }

#if !PUBLIC_RELEASE
        internal static bool HasDropRail(MountedMissile round) =>
            (MountedMissile.RailDirection)Direction.GetValue(round) == MountedMissile.RailDirection.Down &&
            (float)Delay.GetValue(round) >= 1f && (float)Length.GetValue(round) >= 6f && (float)Speed.GetValue(round) > 0f;
#endif

        private static Transform BuildCell(Transform aircraft, Transform owner, string label, Vector3 position,
            Material skin, Material inside, Material guide, Material lens)
        {
            const float skinLip = 3.45f, fairingEnd = 3.8f;
            Transform cell = Child(owner, "GrozaWingBay_" + label, Vector3.zero);
            cell.SetPositionAndRotation(aircraft.TransformPoint(position), aircraft.rotation);

            // The top rim contacts the wing skin. The fore and aft skirts taper into the wing.
            foreach (int sign in new[] { -1, 1 })
            {
                string side = sign < 0 ? "Port" : "Starboard";
                if (sign < 0)
                    Volume(cell, "Armor_" + side, skin, new Vector3(-3.14f, -3.1f, -4.25f), new Vector3(-2.88f, -3.1f, 4.25f),
                        new Vector3(-3.38f, skinLip, -4.75f), new Vector3(-2.88f, skinLip, 4.75f));
                else
                    Volume(cell, "Armor_" + side, skin, new Vector3(2.88f, -3.1f, -4.25f), new Vector3(3.14f, -3.1f, 4.25f),
                        new Vector3(2.88f, skinLip, -4.75f), new Vector3(3.38f, skinLip, 4.75f));
                Box(cell, "GuideRail_" + side, inside, new Vector3(.64f * sign, 1.18f, 0f), new Vector3(.25f, .32f, 7.2f));
                Box(cell, "HingeBeam_" + side, inside, new Vector3(3.01f * sign, -3.03f, 0f), new Vector3(.28f, .49f, 8.8f));
            }
            // Draw the exposed noses up to the wing with a long, chamfered slope. The door opening retains its full clearance.
            Volume(cell, "ForwardFairing", skin, new Vector3(-3.14f, -3.10f, 4.25f), new Vector3(3.14f, -3.10f, 4.57f),
                new Vector3(-2.65f, fairingEnd, 4.33f), new Vector3(2.65f, fairingEnd, 7.55f));
            Volume(cell, "AftFairing", skin, new Vector3(-3.14f, -3.10f, -4.57f), new Vector3(3.14f, -3.10f, -4.25f),
                new Vector3(-2.65f, fairingEnd, -7.55f), new Vector3(2.65f, fairingEnd, -4.33f));
            Box(cell, "MagazineHeadliner", inside, new Vector3(0f, 1.55f, 0f), new Vector3(5.75f, .24f, 8.9f));
            Box(cell, "ForwardCrossbrace", inside, new Vector3(0f, .85f, 4.24f), new Vector3(5.8f, .28f, .19f));
            Box(cell, "AftCrossbrace", inside, new Vector3(0f, .85f, -4.24f), new Vector3(5.8f, .28f, .19f));

            Transform port = Child(cell, "Door_Port", new Vector3(-2.98f, -3.13f, 0f));
            Transform starboard = Child(cell, "Door_Starboard", new Vector3(2.98f, -3.13f, 0f));
            Panel(port, 1f, skin, inside);
            Panel(starboard, -1f, skin, inside);
            var indicators = new List<MeshRenderer>(4);
            foreach (float z in new[] { -4.4f, 4.4f })
            {
                for (int line = -1; line <= 1; line++)
                    Box(cell, "CautionStripe", guide, new Vector3(1.6f * line, -3.13f, z), new Vector3(1.16f, .075f, .17f));
                foreach (float x in new[] { -2.64f, 2.64f })
                    indicators.Add(Box(cell, "BayOpenIndicator", lens, new Vector3(x, -3.14f, z), new Vector3(.28f, .11f, .23f)));
            }
            Transform lamp = Child(cell, "ServiceLamp", new Vector3(0f, 1.35f, 3.4f));
            lamp.localRotation = Quaternion.Euler(60f, 180f, 0f);
            Light service = lamp.gameObject.AddComponent<Light>();
            service.type = LightType.Spot;
            service.color = new Color(1f, .87f, .70f);
            service.intensity = 5f;
            service.range = 10f;
            service.spotAngle = 115f;
            service.shadows = LightShadows.None;
            service.enabled = false;
            cell.gameObject.AddComponent<MegaplaneWingCell>().Bind(port, starboard, service, indicators.ToArray());
            return cell;
        }

        private static Material Amber(Shader shader, string label, float emission)
        {
            var material = new Material(shader) { name = label };
            Color paint = new Color(.92f, .57f, .12f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", paint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", paint);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", paint * emission);
            return material;
        }

        private static void Panel(Transform hinge, float side, Material skin, Material inside)
        {
            Box(hinge, "DoorPlate", skin, new Vector3(1.46f * side, -.07f, 0f), new Vector3(2.91f, .21f, 8.48f));
            foreach (float z in new[] { -3.25f, 0f, 3.25f })
                Box(hinge, "Rib", inside, new Vector3(1.48f * side, .115f, z), new Vector3(2.69f, .19f, .18f));
            Box(hinge, "LockingEdge", inside, new Vector3(2.83f * side, .129f, 0f), new Vector3(.16f, .24f, 8.17f));
        }

        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private static MeshRenderer Box(Transform parent, string name, Material material, Vector3 center, Vector3 size)
        {
            Vector3 min = center - size * .5f, max = center + size * .5f;
            return Volume(parent, name, material, min, new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), max);
        }

        // Individual face vertices keep the folded metal edges un-smoothed and give the wing skin its metre-scale UVs.
        private static MeshRenderer Volume(Transform parent, string name, Material material, Vector3 bottomMin, Vector3 bottomMax, Vector3 topMin, Vector3 topMax)
        {
            Vector3[] corners =
            {
                bottomMin, new Vector3(bottomMax.x, bottomMin.y, bottomMin.z), bottomMax, new Vector3(bottomMin.x, bottomMin.y, bottomMax.z),
                topMin, new Vector3(topMax.x, topMin.y, topMin.z), topMax, new Vector3(topMin.x, topMin.y, topMax.z),
            };
            int[] faces = { 0, 1, 2, 3, 4, 7, 6, 5, 0, 3, 7, 4, 1, 5, 6, 2, 0, 4, 5, 1, 3, 2, 6, 7 };
            var vertices = new Vector3[24];
            var uv = new Vector2[24];
            var triangles = new int[36];
            for (int face = 0; face < 6; face++)
            {
                int v = face * 4, t = face * 6;
                for (int corner = 0; corner < 4; corner++) vertices[v + corner] = corners[faces[v + corner]];
                float u = Vector3.Distance(vertices[v], vertices[v + 1]) / 16f;
                float w = Vector3.Distance(vertices[v], vertices[v + 3]) / 16f;
                uv[v + 1] = new Vector2(u, 0f);
                uv[v + 2] = new Vector2(u, w);
                uv[v + 3] = new Vector2(0f, w);
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            var part = Child(parent, name, Vector3.zero).gameObject;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }

    internal sealed class MegaplaneWingCell : BayDoor
    {
        [SerializeField] private Transform port, starboard;
        [SerializeField] private Light service;
        [SerializeField] private MeshRenderer[] lens;
        private MaterialPropertyBlock illuminated;
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        internal float Open => openAmount;

        internal void Bind(Transform leftDoor, Transform rightDoor, Light lamp, MeshRenderer[] indicators)
        {
            port = leftDoor;
            starboard = rightDoor;
            service = lamp;
            lens = indicators;
            openSpeed = 1.2f;
            closeSpeed = .72f;
        }

        public override void OpenDoor(float duration) => base.OpenDoor(Mathf.Max(openTimer, duration));

        protected override void Update()
        {
            base.Update();
            float angle = Mathf.SmoothStep(0f, 104f, openAmount);
            port.localRotation = Quaternion.AngleAxis(-angle, Vector3.forward);
            starboard.localRotation = Quaternion.AngleAxis(angle, Vector3.forward);
            bool warning = openAmount > .25f;
            if (service.enabled == warning) return;
            service.enabled = warning;
            if (illuminated == null)
            {
                illuminated = new MaterialPropertyBlock();
                illuminated.SetColor(Emission, new Color(3.1f, 1.1f, .14f));
            }
            foreach (MeshRenderer indicator in lens) indicator.SetPropertyBlock(warning ? illuminated : null);
        }
    }
}
