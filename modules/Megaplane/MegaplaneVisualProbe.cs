#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Measures what the renderer and particle systems actually display in sandbox captures.</summary>
    public static class MegaplaneVisualProbe
    {
        public static object Inspect(Dictionary<string, object> args)
        {
            if ((string)args["step"] == "wreck") return InspectWreck();
            if (!args.TryGetValue("idUnit", out var value) || !(value is Aircraft aircraft))
                throw new ArgumentException("The comparison needs a spawned aircraft.");
            if ((string)args["step"] == "stock") return Stock(aircraft,args);

            AeroPart Part(string name) => aircraft.damageables.Select(x => x.Damageable).OfType<AeroPart>()
                .First(x => x.name == name);
            MeshRenderer[] Marks(AeroPart part) => part.GetComponentsInChildren<MeshRenderer>(true)
                .Where(m => m.GetComponentInParent<AeroPart>(true) == part && VisibleMark(m)).ToArray();
            AeroPart tip = Part("wingtip_L");
            if (args.ContainsKey("view"))
            {
                var shell = tip.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Wing_Tip_L").transform;
                Vector3 look = aircraft.transform.InverseTransformPoint(shell.TransformPoint(new Vector3(-3.9f,1.8f,-24f)));
                Vector3 eye = aircraft.transform.InverseTransformPoint(shell.TransformPoint(new Vector3(-9f,19.5f,-30f)));
                MegaplaneAutomation.View(new Dictionary<string,object> { {"idUnit",aircraft},{"x",eye.x},{"y",eye.y},{"z",eye.z},
                    {"lx",look.x},{"ly",look.y},{"lz",look.z},{"fov",53f} });
            }
            var marks = Marks(tip);
            string[] textures = marks.SelectMany(m => m.sharedMaterials).Where(m => m != null)
                .Select(m => m.mainTexture != null ? m.mainTexture.name : "").Distinct().ToArray();
            return new { tip = marks.Length, other = Marks(Part("wingtip_R")).Length,
                attached = !tip.IsDetached(), hp = tip.hitPoints, textures,
                mapped = textures.Any(n => n.Contains("DamageMarks")), geometry = marks.Sum(m => m.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount ?? 0) };
        }

        private static bool VisibleMark(MeshRenderer r) => r.enabled && !r.forceRenderingOff &&
            r.gameObject.activeInHierarchy && r.name.StartsWith("GrozaSkinDamage_", StringComparison.Ordinal);

        private static object Stock(Aircraft a, Dictionary<string, object> args)
        {
            if (!a.IsServer || MegaplaneModule.Is(a)) throw new ArgumentException("A stock reference aircraft is required.");
            var field = AccessTools.Field(typeof(UnitPart),"damageMaterial");
            // Follow the stock damage table. The Darkreach's registered skin renderer
            // can still be outside the current camera LOD when this view is first set.
            UnitPart part = a.damageables.Select(x => x.Damageable).OfType<UnitPart>().First(p => p.name == "wing2_L");
            Renderer renderer = ((DamageMaterial)field.GetValue(part)).renderers.First(r => r != null && r.sharedMaterial != null);
            if (args.TryGetValue("impact",out var damage))
                part.TakeDamage(0f,0f,1f,0f,Convert.ToSingle(damage), PersistentID.None);
            Material mat = renderer.sharedMaterial;
            if (args.ContainsKey("view"))
            {
                Vector3 center = a.transform.InverseTransformPoint(renderer.bounds.center);
                float radius = Mathf.Clamp(renderer.bounds.extents.magnitude, 3f, 35f);
                Vector3 eye = center + radius * new Vector3(center.x < 0 ? -.5f : .5f,1.8f,-.45f);
                MegaplaneAutomation.View(new Dictionary<string,object> { {"idUnit",a},{"x",eye.x},{"y",eye.y},{"z",eye.z},
                    {"lx",center.x},{"ly",center.y},{"lz",center.z},{"fov",48f} });
            }
            return new { owner = part.name, renderer = renderer.name, shader = mat.shader.name,
                hp = part.hitPoints, shaderHp = mat.HasProperty("_HitPoints") ? mat.GetFloat("_HitPoints") : -1f,
                maps = mat.GetTexturePropertyNames().Where(k => mat.GetTexture(k) != null)
                    .ToDictionary(k => k,k => mat.GetTexture(k).name) };
        }

        private static object InspectWreck()
        {
            var wreck = UnityEngine.Object.FindObjectsOfType<MegaplaneWreck>().OrderBy(w => w.Age).First();
            var systems = wreck.GetComponentsInChildren<ParticleSystem>(true);
            var plume = systems.FirstOrDefault(p => p.name == "Groza wreck smoke");
            int count = plume != null ? plume.particleCount : 0;
            float width = 0f, height = 0f;
            if (count > 0)
            {
                var puffs = new ParticleSystem.Particle[count];
                count = plume.GetParticles(puffs);
                var volume = new Bounds(puffs[0].position,Vector3.zero);
                for (int i = 0; i < count; i++)
                {
                    float radius = .5f * puffs[i].GetCurrentSize(plume);
                    volume.Encapsulate(puffs[i].position + Vector3.one*radius);
                    volume.Encapsulate(puffs[i].position - Vector3.one*radius);
                }
                width = Mathf.Max(volume.size.x,volume.size.z);
                height = volume.size.y;
            }
            var marks = wreck.GetComponentsInChildren<MeshRenderer>(true).Where(VisibleMark).ToArray();
            return new { age = wreck.Age, wreck.Impact.Water, count, width, height,
                capacity = plume != null ? plume.main.maxParticles : 0, marks = marks.Length,
                systems = systems.Select(p => new { p.name, count = p.particleCount }).ToArray() };
        }
    }
}
#endif
