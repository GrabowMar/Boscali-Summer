using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>Native gear/nav-light switching on separate Skylark lenses.</summary>
    internal static class SkylarkLights
    {
        private static Mesh lensMesh;

        internal static void Refit(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            NavLights manager = aircraft.GetComponentInChildren<NavLights>(true);
            if (manager == null) throw new InvalidOperationException("Skylark requires native NavLights.");
            Array original = SkylarkTemplate.Get<Array>(manager, "navLights");
            object left = Find(original, "navlight_L"), right = Find(original, "navlight_R");
            object landing = Find(original, "gearLight");
            foreach (object record in original)
                foreach (GameObject effect in SkylarkTemplate.Get<GameObject[]>(record, "objects"))
                    if (effect != null) effect.SetActive(false);

            Type recordType = original.GetType().GetElementType();
            Array records = Array.CreateInstance(recordType, 4);
            records.SetValue(Bind(manager, aircraft, nodes["NavLight_L"], left, recordType,
                "L", new Color(1f, .04f, .015f), null), 0);
            records.SetValue(Bind(manager, aircraft, nodes["NavLight_R"], right, recordType,
                "R", new Color(.02f, 1f, .10f), null), 1);
            records.SetValue(Bind(manager, aircraft, nodes["NavLight_Tail"], landing, recordType,
                "Tail", new Color(1f, .94f, .83f), null, false), 2);

            LandingGear gear = aircraft.GetComponentsInChildren<LandingGear>(true)
                .First(g => SkylarkTemplate.Get<bool>(g, "steering"));
            Transform lamp = new GameObject("SkylarkLandingLightAnchor").transform;
            lamp.SetParent(nodes["Gear_F"], false);
            lamp.position = nodes["Wheel_F"].position + aircraft.transform.up * .29f
                + aircraft.transform.forward * .08f;
            lamp.rotation = aircraft.transform.rotation;
            records.SetValue(Bind(manager, aircraft, lamp, landing, recordType,
                "Landing", Color.white, gear), 3);
            SkylarkTemplate.Set(manager, "aircraft", aircraft);
            SkylarkTemplate.Set(manager, "navLights", records);
        }

        private static object Find(Array records, string name)
        {
            foreach (object record in records)
            {
                Renderer renderer = SkylarkTemplate.Get<Renderer>(record, "renderer");
                if (renderer != null && renderer.name == name) return record;
            }
            throw new InvalidOperationException("Native light donor is absent: " + name);
        }

        private static object Bind(NavLights manager, Aircraft aircraft, Transform anchor,
            object donor, Type recordType, string suffix, Color color, LandingGear gear, bool reuseEffects = true)
        {
            var go = new GameObject("SkylarkLamp_" + suffix);
            go.layer = anchor.gameObject.layer;
            go.transform.SetParent(anchor, false);
            go.transform.rotation = gear == null
                ? Quaternion.LookRotation(aircraft.transform.up, aircraft.transform.forward)
                : Quaternion.LookRotation(aircraft.transform.forward - aircraft.transform.up * .08f, aircraft.transform.up);
            go.transform.localScale = Vector3.one * (gear == null ? .019f : .035f);
            go.AddComponent<MeshFilter>().sharedMesh = Lens();
            MeshRenderer lens = go.AddComponent<MeshRenderer>();
            lens.sharedMaterial = SkylarkTemplate.Get<Renderer>(donor, "renderer").sharedMaterial;
            lens.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lens.receiveShadows = false;
            var effects = new List<GameObject>();
            if (reuseEffects)
            {
                foreach (GameObject effect in SkylarkTemplate.Get<GameObject[]>(donor, "objects"))
                {
                    if (effect == null) continue;
                    effect.transform.SetParent(anchor, false);
                    effect.transform.localPosition = Vector3.zero;
                    effect.transform.rotation = aircraft.transform.rotation;
                    // Only native light effects are restored, never bulk donor airframe meshes.
                    foreach (Renderer renderer in effect.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                    foreach (Light light in effect.GetComponentsInChildren<Light>(true))
                    {
                        light.shadows = LightShadows.None;
                        light.range = Mathf.Min(light.range, gear == null ? 3f : 70f);
                    }
                    effects.Add(effect);
                }
            }
            if (gear == null)
            {
                var point = new GameObject("SkylarkNavGlow_" + suffix);
                point.transform.SetParent(anchor, false);
                Light light = point.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = .45f;
                light.range = 1.5f;
                light.shadows = LightShadows.None;
                effects.Add(point);
            }
            foreach (GameObject effect in effects) effect.SetActive(false);
            object result = Activator.CreateInstance(recordType, true);
            SkylarkTemplate.Set(result, "renderer", lens);
            SkylarkTemplate.Set(result, "objects", effects.ToArray());
            SkylarkTemplate.Set(result, "litMaterial", SkylarkTemplate.Get<Material>(donor, "litMaterial"));
            SkylarkTemplate.Set(result, "part", anchor.GetComponentInParent<UnitPart>(true));
            SkylarkTemplate.Set(result, "gear", gear);
            var state = AccessTools.Field(recordType, "controlState");
            state.SetValue(result, Enum.ToObject(state.FieldType, gear == null ? 1 : 0));
            SkylarkLampMaterial cleanup = go.AddComponent<SkylarkLampMaterial>();
            cleanup.manager = manager;
            cleanup.lens = lens;
            return result;
        }

        private static Mesh Lens()
        {
            if (lensMesh != null) return lensMesh;
            const int count = 12;
            var vertices = new Vector3[count + 1];
            var triangles = new int[count * 3];
            vertices[0] = new Vector3(0, 0, .42f);
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % count + 1;
            }
            lensMesh = new Mesh { name = "SkylarkLightLens", vertices = vertices, triangles = triangles };
            lensMesh.RecalculateNormals();
            lensMesh.RecalculateBounds();
            return lensMesh;
        }
    }

    // NavLights.Initialize instances the off material. Retain that exact instance
    // across later native material swaps and release it with this lamp/debris.
    [DefaultExecutionOrder(100)]
    internal sealed class SkylarkLampMaterial : MonoBehaviour
    {
        [SerializeField] internal NavLights manager;
        [SerializeField] internal Renderer lens;
        private Material owned;
        private void Awake()
        {
            foreach (object record in SkylarkTemplate.Get<Array>(manager, "navLights"))
                if (SkylarkTemplate.Get<Renderer>(record, "renderer") == lens)
                    owned = SkylarkTemplate.Get<Material>(record, "baseMaterial");
        }
        private void OnDestroy() { if (owned != null) Object.Destroy(owned); }
    }
}
