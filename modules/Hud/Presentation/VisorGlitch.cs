using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// BROKEN HELMET (2026-11-16): the visor's glitch shader, driven once per frame from <see cref="VisorFx"/>. The shader pair lives in avionics-fx.bundle (<c>NOA/UI/VisorGlitch</c> for line art and sprites,
    /// <c>NOA/TMP/VisorGlitchSDF</c> for words, see AvionicsUi/Assets/Source/NOA_VisorGlitch.cginc for the terms): the words get it because <c>VanillaHudStyle.Glow</c> makes its materials on it; the line art gets it
    /// because <see cref="Scan"/> puts the one shared material on every default-material graphic under the mod's own HUD holders (never the menu or MFD panels, never anything native). The terms are shader globals, so a frame
    /// of glitch costs a dozen global writes and no mesh is rebuilt; while no term is on the master is 0 and both shaders draw what the stock ones do. Nothing here allocates per frame. If the bundle or a shader is
    /// missing, <see cref="Ready"/> is false and the visor keeps its older per-group look.
    /// </summary>
    internal static class VisorGlitch
    {
        public const string UiShader = "NOA/UI/VisorGlitch";
        private static readonly int FxId = Shader.PropertyToID("_BH_Fx"), Tear0Id = Shader.PropertyToID("_BH_Tear0"), Tear1Id = Shader.PropertyToID("_BH_Tear1"), Tear2Id = Shader.PropertyToID("_BH_Tear2"),
            ChromaId = Shader.PropertyToID("_BH_Chroma"), DropId = Shader.PropertyToID("_BH_Drop"), BarsId = Shader.PropertyToID("_BH_Bars"), Calm0Id = Shader.PropertyToID("_BH_Calm0"), Calm1Id = Shader.PropertyToID("_BH_Calm1");
        private static readonly Vector4 NoRect = new Vector4(2f, 2f, -1f, -1f);
        private static readonly Vector3[] corners = new Vector3[4];
        private static readonly List<Transform> roots = new List<Transform>(8);
        private static readonly List<Graphic> graphics = new List<Graphic>(512);
        private static Material ui;
        private static float nextResolve, nextScan;
        private static bool live, dirty;
        private static RectTransform calmPage;

        /// <summary>The sim's pin (<c>HudAutomation.Visor shader=off</c>): the stock UI and text shaders are back everywhere and nothing glitches, for the pixel-for-pixel comparison of the healthy visor.</summary>
        internal static bool Disabled { get; private set; }

        /// <summary>Pin the glitch shaders off (the stock ones are put back on every graphic and label at once) or on again (the next <see cref="Scan"/> and the text copies take them back).</summary>
        internal static void SetDisabled(bool disabled)
        {
            if (Disabled == disabled) return;
            Disabled = disabled;
            Off();
            if (ui != null)
            {
                for (int r = 0; r < roots.Count; r++)
                {
                    if (roots[r] == null) continue;
                    roots[r].GetComponentsInChildren(true, graphics);
                    for (int i = 0; i < graphics.Count; i++) if (IsLineArt(graphics[i]) && graphics[i].material == ui) graphics[i].material = null;
                }
                graphics.Clear();
                nextScan = 0f;
                dirty = true;
            }
            BoscaliSummer.Core.Game.VanillaHudStyle.UseStockText(disabled);
        }

        /// <summary>Both shaders are in the bundle and the line-art material is made.</summary>
        public static bool Ready
        {
            get
            {
                if (Disabled) return false;
                if (ui != null) return true;
                if (Time.unscaledTime < nextResolve) return false;
                nextResolve = Time.unscaledTime + 5f;
                Shader shader = AvBundle.Shader(UiShader);
                if (shader == null || AvBundle.Shader(BoscaliSummer.Core.Game.VanillaHudStyle.GlitchTextShader) == null) return false;
                ui = new Material(shader) { name = "Boscali visor glitch", hideFlags = HideFlags.HideAndDontSave };
                return true;
            }
        }

        /// <summary>The material the mod's HUD line art takes (null while not <see cref="Ready"/>).</summary>
        internal static Material LineArt => ui;

        /// <summary>A mod-owned HUD holder whose graphics should glitch with the visor. Idempotent (safe to call every frame); a new holder is scanned on the next <see cref="Scan"/>.</summary>
        public static void Attach(Transform root)
        {
            if (root == null || roots.Contains(root)) return;
            roots.Add(root);
            dirty = true;
        }

        /// <summary>A block was (re)built under an attached holder: the next <see cref="Scan"/> puts the glitch material on its graphics.</summary>
        public static void Rescan() => dirty = true;

        // TMP owns its labels and fallback submeshes; their material getter may clone a null shared material.
        private static bool IsLineArt(Graphic graphic) => graphic != null && !(graphic is TMP_Text) && !(graphic is TMP_SubMeshUI);

        /// <summary>
        /// On a (re)build (<see cref="Attach"/> of a new holder, <see cref="Rescan"/>) and, while the glitch runs, twice a second: every graphic under an attached holder that still has the stock UI material takes the glitch material (a graphic with a material of its own, and every label, is left alone).
        /// Holders that were destroyed drop out. No allocation: one reused list.
        /// </summary>
        public static void Scan(float now)
        {
            if (!Ready || (!dirty && !live) || now < nextScan) return;      // a calm visor costs nothing here: no 2 Hz GetComponentsInChildren
            nextScan = now + 0.5f;
            dirty = false;
            Material stock = Graphic.defaultGraphicMaterial;
            for (int r = roots.Count - 1; r >= 0; r--)
            {
                Transform root = roots[r];
                if (root == null) { roots.RemoveAt(r); continue; }
                root.GetComponentsInChildren(true, graphics);
                for (int i = 0; i < graphics.Count; i++)
                {
                    Graphic g = graphics[i];
                    if (!IsLineArt(g)) continue;
                    if (g.material == stock) g.material = ui;
                }
            }
            graphics.Clear();
        }

        /// <summary>Hand the shader this frame's terms (the master drops to 0 once, the frame the last term goes off).</summary>
        public static void Drive(VisorFx fx)
        {
            if (!Ready) return;
            if (!fx.Glitching)
            {
                if (live) { Shader.SetGlobalVector(FxId, Vector4.zero); live = false; }
                return;
            }
            if (!live) nextScan = 0f;      // a glitch starts: take in the graphics built since the last scan on the next one
            live = true;
            ApplyCalm();
            float[] t = fx.Tear;
            Shader.SetGlobalVector(FxId, new Vector4(1f, fx.Clock, fx.Seed, 0f));
            Shader.SetGlobalVector(Tear0Id, new Vector4(t[0], t[1], t[2], t[3]));
            Shader.SetGlobalVector(Tear1Id, new Vector4(t[4], t[5], t[6], t[7]));
            Shader.SetGlobalVector(Tear2Id, new Vector4(t[8], t[9], t[10], t[11]));
            Shader.SetGlobalVector(ChromaId, new Vector4(fx.Split, fx.Fine, fx.Roll, 0f));
            Shader.SetGlobalVector(DropId, new Vector4(fx.DropDensity, fx.DropFloor, fx.Flick, fx.Seed * 0.37f));
            Shader.SetGlobalVector(BarsId, new Vector4(fx.BarStrength, fx.BarPos, fx.BarGap, fx.BarJitter));
        }

        /// <summary>The centre of the screen (the WARNING banner's lane, the heading tape, the reticle's arcs with the speed, altitude and G numbers and the shot callout): reference 1080p fractions, y from the top.</summary>
        private static readonly Vector4 CentreCalm = new Vector4(0.31f, 0.10f, 0.71f, 0.62f);

        /// <summary>Where the effect is eased to a mild version (a third of the tear, fewer and paler dropouts, fainter shimmer): the centre of the screen and the board's scope page (null: none).</summary>
        public static void SetCalm(RectTransform scopePage) => calmPage = scopePage;      // written to the shader by Drive, only while a glitch runs (the terms are off otherwise)

        private static void ApplyCalm()
        {
            Shader.SetGlobalVector(Calm0Id, CentreCalm);
            Shader.SetGlobalVector(Calm1Id, ScreenRect(calmPage));
        }

        private static Vector4 ScreenRect(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy || Screen.width <= 0) return NoRect;
            rt.GetWorldCorners(corners);      // an overlay canvas: world units are screen pixels, y up
            float w = Screen.width, h = Screen.height;
            return new Vector4(corners[0].x / w, 1f - corners[1].y / h, corners[2].x / w, 1f - corners[0].y / h);
        }

        private static Material staticMat;
        private static GameObject staticGo;

        /// <summary>
        /// The jam's rolling bars drawn as light (thin interference lines with grain, only inside a bar): one full-screen quad on the native HUD canvas, on the glitch shader's static material, shown while a bar is on and hidden
        /// the moment it is not. No dark plate and no tint: outside a bar the quad draws nothing at all.
        /// </summary>
        public static void ShowStatic(bool on)
        {
            if (!Ready) return;
            if (!on)
            {
                if (staticGo != null && staticGo.activeSelf) staticGo.SetActive(false);
                return;
            }
            if (staticGo == null)
            {
                if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return;
                if (staticMat == null)
                {
                    staticMat = new Material(ui) { name = "Boscali visor static", hideFlags = HideFlags.HideAndDontSave };
                    staticMat.SetFloat("_BH_Static", 1f);
                }
                staticGo = new GameObject("BoscaliVisorStatic", typeof(RectTransform), typeof(CanvasRenderer), typeof(VisorStaticGraphic));
                var rt = (RectTransform)staticGo.transform;
                rt.SetParent(canvas.transform, false);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; rt.localScale = Vector3.one;
                Graphic g = staticGo.GetComponent<Graphic>();
                g.raycastTarget = false;
                g.material = staticMat;
            }
            if (!staticGo.activeSelf) staticGo.SetActive(true);
            Transform tr = staticGo.transform;
            if (tr.parent != null && tr.GetSiblingIndex() != tr.parent.childCount - 1) tr.SetAsLastSibling();      // over the layer roots too, whenever they were built after it
        }

        /// <summary>Everything off (the board went down, the scene ended): the globals rest and the next frame draws the stock look.</summary>
        public static void Off()
        {
            if (live) { Shader.SetGlobalVector(FxId, Vector4.zero); live = false; }
            if (staticGo != null && staticGo.activeSelf) staticGo.SetActive(false);
        }
    }

    /// <summary>One white quad over its rect: the carrier of the static shader pass (the pass draws what it draws from the screen position, not from the quad).</summary>
    internal sealed class VisorStaticGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            var v = UIVertex.simpleVert;
            v.color = Color.white;
            v.position = new Vector3(r.xMin, r.yMin); vh.AddVert(v);
            v.position = new Vector3(r.xMin, r.yMax); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMax); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMin); vh.AddVert(v);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
