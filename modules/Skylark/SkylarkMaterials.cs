using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Skylark
{
    internal static class SkylarkMaterials
    {
        private const string SkinName = "BS_Skylark_Skin";
        private const string NativeShader = "Shader Graphs/AircraftSkin";
        private static Material nativeSkin;
        private static Texture damageBase, damageNormal;
        internal static string DamageSource { get; private set; }

        internal static void Refit(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            Renderer[] skin = nodes.Values.Select(t => t.GetComponent<Renderer>())
                .Where(r => r != null && r.sharedMaterial != null
                    && r.sharedMaterial.name.StartsWith(SkinName, StringComparison.Ordinal)
                    && !r.name.StartsWith("Fittings_", StringComparison.Ordinal)
                    && !r.name.StartsWith("Rail_", StringComparison.Ordinal)).Distinct().ToArray();
            if (skin.Length == 0) throw new InvalidOperationException("Skylark skin renderers are missing.");
            if (skin.Any(r => r.sharedMaterials.Length != 1))
                throw new InvalidOperationException("Skylark native damage expects one skin material per renderer.");
            if (nativeSkin == null)
            {
                Material source = skin[0].sharedMaterial;
                Texture color = OwnTexture(source, "_BaseMap", "_Basecolor");
                Texture normal = OwnTexture(source, "_BumpMap", "_Normal");
                Texture metallic = OwnTexture(source, "_MetallicGlossMap", "_Metallic");
                Texture ao = OwnTexture(source, "_OcclusionMap", "_AO");
                Material donor = aircraft.GetComponentsInChildren<Renderer>(true).Where(r => !skin.Contains(r))
                    .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.shader.name == NativeShader);
                if (donor == null) throw new InvalidOperationException("Native AircraftSkin material is unavailable on the donor.");
                nativeSkin = new Material(donor) { name = SkinName + "_Native", hideFlags = HideFlags.DontUnloadUnusedAsset };
                damageBase = BundledTexture("bs_skylark_skin_dmg_b.png");
                damageNormal = BundledTexture("bs_skylark_skin_dmg_n.png") ?? normal;
                DamageSource = damageBase != null ? "Skylark authored damage atlas" : "Skylark base atlas GPU fallback";
                if (damageBase == null) damageBase = FallbackDamage(color);
                nativeSkin.SetTexture("_Basecolor", color);
                nativeSkin.SetTexture("_Normal", normal);
                nativeSkin.SetTexture("_Metallic", metallic);
                nativeSkin.SetTexture("_AO", ao);
                nativeSkin.SetTexture("_BasecolorDmg", damageBase);
                nativeSkin.SetTexture("_NormalDmg", damageNormal);
                nativeSkin.SetTexture("_Livery", color);
                nativeSkin.SetFloat("_HitPoints", 100f);
            }
            foreach (Renderer renderer in skin) renderer.sharedMaterial = nativeSkin;
            foreach (UnitPart part in aircraft.GetComponentsInChildren<UnitPart>(true))
            {
                Renderer[] owned = skin.Where(r => r.GetComponentInParent<UnitPart>(true) == part).ToArray();
                SkylarkTemplate.Set(part, "damageMaterial", new DamageMaterial
                {
                    threshold = 100f, renderers = owned.ToList(), indices = new byte[owned.Length]
                });
                SkylarkPartMaterials cleanup = part.gameObject.AddComponent<SkylarkPartMaterials>();
                cleanup.Configure(owned);
            }
        }

        private static Texture OwnTexture(Material source, string imported, string native)
        {
            Texture texture = source.HasProperty(imported) ? source.GetTexture(imported) : source.GetTexture(native);
            if (texture == null) throw new InvalidOperationException("Skylark skin is missing " + imported + ".");
            return texture;
        }

        private static Texture BundledTexture(string filename)
        {
            string path = "assets/skylarkreview/textures/" + filename;
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
                if (bundle.Contains(path)) return bundle.LoadAsset<Texture2D>(path);
            return null;
        }

        private static Texture2D FallbackDamage(Texture original)
        {
            // Authored maps are preferred. This one-time fallback only reads our UV atlas.
            const int size = 1024;
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D texture = null;
            try
            {
                Graphics.Blit(original, temporary);
                RenderTexture.active = temporary;
                texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
                {
                    name = SkinName + "_Dmg_Fallback", hideFlags = HideFlags.DontUnloadUnusedAsset,
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear
                };
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                Color32[] pixels = texture.GetPixels32();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int i = y * size + x;
                        Color32 c = pixels[i];
                        int hi = Math.Max(c.r, Math.Max(c.g, c.b)), lo = Math.Min(c.r, Math.Min(c.g, c.b));
                        if (hi - lo > 45 || hi < 35) continue; // Preserve coloured lenses and dark rubber.
                        float broad = Mathf.Sin(x * .019f + y * .008f) * Mathf.Sin(y * .024f - x * .011f);
                        float scorch = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((broad - .05f) * 2f));
                        float amount = scorch * .78f;
                        pixels[i] = new Color32((byte)Mathf.Lerp(c.r, 31, amount),
                            (byte)Mathf.Lerp(c.g, 24, amount), (byte)Mathf.Lerp(c.b, 20, amount), c.a);
                    }
                texture.SetPixels32(pixels);
                texture.Apply(true, true);
                return texture;
            }
            catch
            {
                if (texture != null) Object.Destroy(texture);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }

    // UnitPart.Awake may auto-add its hidden donor Renderer when its list is empty.
    // Run afterwards to restore only the authored skin renderers, including LOD1.
    [DefaultExecutionOrder(100)]
    internal sealed class SkylarkPartMaterials : MonoBehaviour
    {
        [SerializeField] private Renderer[] renderers;
        private readonly HashSet<Material> owned = new HashSet<Material>();
        internal int OwnedCount => owned.Count;
        internal void Configure(Renderer[] value) => renderers = value;

        private void Awake()
        {
            UnitPart part = GetComponent<UnitPart>();
            DamageMaterial damage = SkylarkTemplate.Get<DamageMaterial>(part, "damageMaterial");
            damage.renderers = new List<Renderer>(renderers);
            damage.indices = new byte[renderers.Length];
            foreach (Renderer renderer in renderers)
            {
                Material material = renderer.material;
                material.SetFloat("_HitPoints", part.hitPoints);
                owned.Add(material);
            }
        }

        private void OnDestroy()
        {
            // This component travels with the owning damage part. Detached debris
            // keeps its material until that part is destroyed, even after root cleanup.
            foreach (Material material in owned) if (material != null) Object.Destroy(material);
            owned.Clear();
        }
    }

    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.SetLivery))]
    internal static class SkylarkLiveryPatch
    {
        private static bool Prefix(UnitPart __instance)
        {
            // The donor's liveries use its UV layout. Our default livery is our own atlas.
            return __instance.GetComponent<SkylarkPartMaterials>() == null;
        }
    }
}
