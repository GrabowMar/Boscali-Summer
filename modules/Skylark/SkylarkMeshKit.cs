using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>Shared URP/Lit materials and lightweight procedural mesh builder for Skylark airframe, gear, internal, and cockpit details.</summary>
    internal static class SkylarkMeshKit
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");

        private static Material graphite, ceramic, darkAlloy, chrome, copperCoil, optical, hazardYellow, hvOrange, primerInterior, consoleDark, switchRed, lampBase;

        internal static Material Graphite => graphite != null ? graphite : (graphite = Coat("Graphite", new Color(.32f, .34f, .36f), .18f, .36f));
        internal static Material Ceramic => ceramic != null ? ceramic : (ceramic = Coat("Ceramic", new Color(.18f, .20f, .23f), .04f, .32f));
        internal static Material DarkAlloy => darkAlloy != null ? darkAlloy : (darkAlloy = Coat("DarkAlloy", new Color(.23f, .25f, .28f), .65f, .48f));
        internal static Material Chrome => chrome != null ? chrome : (chrome = Coat("Chrome", new Color(.78f, .82f, .86f), .92f, .88f));
        internal static Material CopperCoil
        {
            get
            {
                if (copperCoil != null) return copperCoil;
                copperCoil = Coat("CopperCoil", new Color(.36f, .25f, .20f), .72f, .55f);
                if (copperCoil != null)
                {
                    copperCoil.EnableKeyword("_EMISSION");
                    if (copperCoil.HasProperty(EmissionColorId)) copperCoil.SetColor(EmissionColorId, Color.black);
                }
                return copperCoil;
            }
        }
        internal static Material Optical => optical != null ? optical : (optical = Coat("Optical", new Color(.12f, .23f, .25f), .48f, .76f));
        internal static Material HazardYellow => hazardYellow != null ? hazardYellow : (hazardYellow = Coat("HazardYellow", new Color(.83f, .64f, .11f), .12f, .35f));
        internal static Material HvOrange => hvOrange != null ? hvOrange : (hvOrange = Coat("HvOrange", new Color(.79f, .32f, .09f), .15f, .40f));
        internal static Material PrimerInterior => primerInterior != null ? primerInterior : (primerInterior = Coat("PrimerInterior", new Color(.20f, .24f, .22f), .10f, .25f));
        internal static Material ConsoleDark => consoleDark != null ? consoleDark : (consoleDark = Coat("ConsoleDark", new Color(.095f, .11f, .125f), .12f, .28f));
        internal static Material SwitchRed => switchRed != null ? switchRed : (switchRed = Coat("SwitchRed", new Color(.62f, .13f, .12f), .15f, .42f));

        /// <summary>Shared emissive-capable material driven per-renderer via <see cref="SetLamp"/> and <see cref="MaterialPropertyBlock"/>.</summary>
        internal static Material LampBase
        {
            get
            {
                if (lampBase != null) return lampBase;
                lampBase = Coat("LampBase", new Color(.08f, .09f, .10f), .08f, .45f);
                if (lampBase != null)
                {
                    lampBase.EnableKeyword("_EMISSION");
                    if (lampBase.HasProperty(EmissionColorId)) lampBase.SetColor(EmissionColorId, Color.black);
                }
                return lampBase;
            }
        }

        internal static void SetLamp(Renderer renderer, MaterialPropertyBlock block, Color baseColor, Color emissionColor)
        {
            if (renderer == null || block == null) return;
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, baseColor);
            block.SetColor(ColorId, baseColor);
            block.SetColor(EmissionColorId, emissionColor);
            renderer.SetPropertyBlock(block);
        }

        private static Shader LitShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            return shader != null ? shader : Shader.Find("Standard");
        }

        private static Material Coat(string name, Color colour, float metallic, float smoothness)
        {
            Shader shader = LitShader();
            if (shader == null) return null;
            var material = new Material(shader) { name = "BS_Skylark_" + name, color = colour, hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, colour);
            if (material.HasProperty(MetallicId)) material.SetFloat(MetallicId, metallic);
            if (material.HasProperty(SmoothnessId)) material.SetFloat(SmoothnessId, smoothness);
            else if (material.HasProperty(GlossinessId)) material.SetFloat(GlossinessId, smoothness);
            return material;
        }

        /// <summary>Adds triangles and quads into a single flat-shaded mesh without colliders.</summary>
        internal sealed class Builder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> indices = new List<int>();

            internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                int first = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                bool forward = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) > 0f;
                indices.Add(first);
                indices.Add(first + (forward ? 1 : 2));
                indices.Add(first + (forward ? 2 : 1));
            }

            internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Triangle(a, b, c, outward);
                Triangle(a, c, d, outward);
            }

            internal void Box(Vector3 center, Vector3 size) => Box(center, size, Quaternion.identity);

            internal void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                Vector3 h = size * .5f;
                Vector3 P(float sx, float sy, float sz) => center + rotation * new Vector3(sx * h.x, sy * h.y, sz * h.z);
                Vector3 lbf = P(-1, -1, 1), rbf = P(1, -1, 1), rtf = P(1, 1, 1), ltf = P(-1, 1, 1);
                Vector3 lbb = P(-1, -1, -1), rbb = P(1, -1, -1), rtb = P(1, 1, -1), ltb = P(-1, 1, -1);
                Quad(lbf, rbf, rtf, ltf, rotation * Vector3.forward);
                Quad(rbb, lbb, ltb, rtb, rotation * Vector3.back);
                Quad(ltf, rtf, rtb, ltb, rotation * Vector3.up);
                Quad(lbb, rbb, rbf, lbf, rotation * Vector3.down);
                Quad(lbb, lbf, ltf, ltb, rotation * Vector3.left);
                Quad(rbf, rbb, rtb, rtf, rotation * Vector3.right);
            }

            internal void Prism(Vector3 a, Vector3 b, float width, float height)
            {
                Vector3 span = b - a;
                float len = span.magnitude;
                if (len < .0001f) return;
                Vector3 fwd = span / len;
                Vector3 upHint = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > .92f ? Vector3.forward : Vector3.up;
                Quaternion rot = Quaternion.LookRotation(fwd, upHint);
                Box((a + b) * .5f, new Vector3(width, height, len), rot);
            }

            internal void Cylinder(Vector3 a, Vector3 b, float radiusA, float radiusB, int sides = 10, bool capEnds = true)
            {
                Vector3 span = b - a;
                float len = span.magnitude;
                if (len < .0001f || sides < 3) return;
                Vector3 fwd = span / len;
                Vector3 upHint = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > .92f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(upHint, fwd).normalized;
                Vector3 up = Vector3.Cross(fwd, right);

                var ringA = new Vector3[sides];
                var ringB = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides;
                    Vector3 radial = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    ringA[i] = a + radial * radiusA;
                    ringB[i] = b + radial * radiusB;
                }
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    Vector3 mid = ((ringA[i] + ringA[next] + ringB[i] + ringB[next]) * .25f) - ((a + b) * .5f);
                    Quad(ringA[i], ringA[next], ringB[next], ringB[i], mid);
                    if (capEnds)
                    {
                        Triangle(a, ringA[next], ringA[i], -fwd);
                        Triangle(b, ringB[i], ringB[next], fwd);
                    }
                }
            }

            internal void HollowRing(Vector3 center, Vector3 axis, float innerRadius, float outerRadius, float length, int sides = 14)
            {
                Vector3 fwd = axis.normalized;
                Vector3 upHint = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > .92f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(upHint, fwd).normalized;
                Vector3 up = Vector3.Cross(fwd, right);
                Vector3 half = fwd * (length * .5f);

                var inA = new Vector3[sides];
                var inB = new Vector3[sides];
                var outA = new Vector3[sides];
                var outB = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides;
                    Vector3 radial = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    inA[i] = center - half + radial * innerRadius;
                    inB[i] = center + half + radial * innerRadius;
                    outA[i] = center - half + radial * outerRadius;
                    outB[i] = center + half + radial * outerRadius;
                }
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    Vector3 midRadial = ((outA[i] + outA[next]) * .5f) - (center - half);
                    Quad(outA[i], outA[next], outB[next], outB[i], midRadial);
                    Quad(inB[i], inB[next], inA[next], inA[i], -midRadial);
                    Quad(inA[i], inA[next], outA[next], outA[i], -fwd);
                    Quad(outB[i], outB[next], inB[next], inB[i], fwd);
                }
            }

            internal Mesh BuildMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            internal Transform Attach(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale,
                Material material, int layer, bool castShadows = true)
            {
                var go = new GameObject(name) { layer = layer };
                Transform t = go.transform;
                t.SetParent(parent, false);
                t.localPosition = localPosition;
                t.localRotation = localRotation;
                t.localScale = localScale;
                go.AddComponent<MeshFilter>().sharedMesh = BuildMesh(name);
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                return t;
            }

            internal Transform AttachInAircraftFrame(string name, Transform aircraft, Transform parent, Vector3 worldOrigin,
                float size, Material material, Dictionary<string, Transform> nodes = null, bool castShadows = true)
            {
                Transform t = SkylarkTemplate.Empty(name, parent, worldOrigin);
                t.rotation = aircraft.rotation;
                t.localScale = Vector3.one * size;
                t.gameObject.layer = parent.gameObject.layer;
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = BuildMesh(name);
                MeshRenderer renderer = t.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                if (nodes != null) nodes[name] = t;
                return t;
            }
        }
    }
}
