using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Progression.Configuration;
using BoscaliSummer.Modules.Progression.Networking;
using BoscaliSummer.Modules.Progression.Presentation;
using BoscaliSummer.Modules.Progression.Runtime;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Progression.Visuals
{
    /// <summary>
    /// Paints squadron insignia on tail fins: the player's own on their aircraft and their wingmen, the crest
    /// every other player published on theirs, and each enemy ace wing's crest on that wing (host side, where
    /// the wing is known). A marking is baked once, on
    /// the GPU, into a copy of the livery atlas; the copy is then packed to the livery's own compressed
    /// format on a worker thread and replaces the <c>_Livery</c> texture on the material instances the game
    /// already made for each part. Nothing is added to the scene and nothing runs per frame: same renderers,
    /// same shader, same draw calls, and a packed copy costs what the stock atlas does.
    /// The painting is client-local. The only thing that leaves the machine is the player's own generated design
    /// and paint mode, published through <see cref="ProgressionNet"/> (a local PNG never does).
    /// </summary>
    internal sealed class TailPaint : MonoBehaviour, ISceneService
    {
        private const float PollSeconds = 1f;
        private const int MaximumCoats = 8;      // packed, each is the size of a stock atlas (about 3 to 6 MB)
        private const int MaximumWorn = 16;
        private const int MaximumArt = 8;
        private const int MaximumWaits = 30;     // polls spent waiting for an aircraft's livery before giving up on it
        private const int ArtIdlePolls = 30;
        private const float Opacity = 0.94f, HalfDepth = 0.6f, Margin = 0.9f;

        private static readonly int LiveryId = Shader.PropertyToID("_Livery");
        private static readonly int RightId = Shader.PropertyToID("_EmblemRight");
        private static readonly int UpId = Shader.PropertyToID("_EmblemUp");
        private static readonly int NormalId = Shader.PropertyToID("_EmblemNormal");
        private static readonly int ParamsId = Shader.PropertyToID("_EmblemParams");
        private static readonly FieldInfo DamageField = AccessTools.Field(typeof(UnitPart), "damageMaterial");

        /// <summary>One line for the STUDIO footer: what the paint shop last did, in words.</summary>
        internal static string Status { get; private set; } = string.Empty;

        /// <summary>The live service, for acceptance scenarios only.</summary>
        internal static TailPaint Live { get; private set; }

        /// <summary>Where a marking sits on one fin mesh, in that mesh's own space (metres).</summary>
        private readonly struct Spot
        {
            public readonly Vector3 Centre, Normal, Up;
            public readonly float Half;

            /// <summary>Both faces of this fin use one mirrored UV island, so its inboard face shows the marking reversed.</summary>
            public readonly bool Shared;

            public Spot(float cx, float cy, float cz, float nx, float ny, float nz, float ux, float uy, float uz, float half, bool shared)
            {
                Centre = new Vector3(cx, cy, cz); Normal = new Vector3(nx, ny, nz); Up = new Vector3(ux, uy, uz);
                Half = half; Shared = shared;
            }
        }

        // Measured offline from the shipped meshes: the largest circle that stays on each fin's outboard face,
        // its plane, the airframe's up laid into that plane, and whether the two faces share one UV island.
        // A mesh that is not listed (a new or modded airframe) is simply left unpainted.
        private static readonly Dictionary<string, Spot> Spots = new Dictionary<string, Spot>(StringComparer.Ordinal)
        {
            { "fighter1_vstab1", new Spot(-0.097f, 0.037f, -0.115f, -0.998f, 0.066f, 0.016f, 0.066f, 0.998f, -0.001f, 0.45f, false) },
            { "smallFighter1_vstab_L", new Spot(0.349f, -0.451f, 0.415f, -0.773f, -0.632f, -0.048f, -0.631f, 0.774f, -0.039f, 0.48f, true) },
            { "smallFighter1_vstab_R", new Spot(-0.350f, -0.451f, 0.403f, 0.773f, -0.632f, -0.048f, 0.631f, 0.774f, -0.039f, 0.48f, true) },
            { "multirole1_tail_L", new Spot(-0.120f, -0.154f, -0.468f, -0.688f, -0.723f, -0.061f, -0.720f, 0.691f, -0.064f, 0.40f, true) },
            { "multirole1_tail_R", new Spot(0.134f, -0.154f, -0.321f, 0.688f, -0.723f, -0.061f, 0.720f, 0.691f, -0.064f, 0.40f, true) },
            { "attackhelo1_tailfin", new Spot(0.001f, -0.132f, -0.242f, -1.000f, 0.013f, 0.001f, 0.013f, 1.000f, -0.000f, 0.42f, true) },
            { "utilityHelo1_vstab_L", new Spot(-0.042f, -0.038f, -0.164f, -1.000f, 0.000f, 0.003f, 0.000f, 1.000f, -0.000f, 0.66f, false) },
            { "utilityHelo1_vstab_R", new Spot(0.042f, -0.018f, -0.170f, 1.000f, 0.000f, 0.003f, -0.000f, 1.000f, -0.000f, 0.66f, false) },
            { "quadVTOL1_vstab1_L", new Spot(0.147f, -0.518f, 0.149f, -0.960f, -0.280f, 0.001f, -0.280f, 0.960f, 0.000f, 0.80f, true) },
            { "quadVTOL1_vstab1_R", new Spot(-0.142f, -0.499f, 0.152f, 0.960f, -0.280f, 0.001f, 0.280f, 0.960f, 0.000f, 0.80f, true) },
            { "sfb_vstab1_L", new Spot(0.390f, 0.602f, -0.507f, -0.537f, 0.843f, -0.043f, 0.840f, 0.539f, 0.067f, 0.80f, true) },
            { "sfb_vstab1_R", new Spot(-0.397f, 0.613f, -0.390f, 0.537f, 0.843f, -0.043f, -0.840f, 0.539f, 0.067f, 0.80f, true) },
            { "fastBomber1_rudder_L", new Spot(0.029f, 0.016f, -0.070f, -0.931f, -0.361f, 0.047f, -0.361f, 0.932f, 0.018f, 0.54f, true) },
            { "fastBomber1_rudder_R", new Spot(-0.046f, -0.021f, -0.024f, 0.931f, -0.361f, 0.047f, 0.361f, 0.932f, 0.018f, 0.54f, true) },
            { "CAS1_vstab_L", new Spot(0.073f, 0.175f, -0.012f, -0.918f, 0.397f, -0.005f, 0.397f, 0.918f, 0.002f, 0.60f, true) },
            { "CAS1_vstab_R", new Spot(-0.073f, 0.175f, -0.026f, 0.918f, 0.397f, -0.005f, -0.397f, 0.918f, 0.002f, 0.60f, true) },
            { "trainer_vstab2_L", new Spot(0.120f, -0.379f, 0.116f, -0.950f, -0.312f, -0.028f, -0.312f, 0.950f, -0.009f, 0.47f, true) },
            { "trainer_vstab2_R", new Spot(-0.126f, -0.398f, 0.123f, 0.950f, -0.312f, -0.028f, 0.312f, 0.950f, -0.009f, 0.47f, true) },
            { "VTOLTrainer1_vstab2", new Spot(0.000f, 0.044f, -0.130f, -1.000f, 0.000f, 0.000f, 0.000f, 1.000f, 0.000f, 0.36f, true) },
            { "COIN_vstab1_L_LOD0", new Spot(-0.103f, -0.145f, -0.000f, -0.943f, 0.333f, -0.001f, 0.333f, 0.943f, 0.000f, 0.40f, false) },
            { "COIN_vstab1_R_LOD0", new Spot(0.117f, -0.186f, 0.016f, 0.943f, 0.333f, -0.001f, -0.333f, 0.943f, -0.007f, 0.40f, false) },
            { "EW1_vstab_L", new Spot(-0.167f, -0.347f, 0.350f, -0.898f, 0.440f, 0.008f, 0.440f, 0.898f, -0.004f, 0.80f, true) },
            { "EW1_vstab_R", new Spot(0.167f, -0.347f, 0.350f, 0.898f, 0.440f, 0.008f, -0.440f, 0.898f, -0.004f, 0.80f, true) },
        };

        /// <summary>What one aircraft is asked to carry: a generated design in a finish, or a PNG.</summary>
        private readonly struct Marking : IEquatable<Marking>
        {
            public readonly EmblemDesign Design;
            public readonly EmblemFinish Finish;
            public readonly string File;

            public Marking(EmblemDesign design, EmblemFinish finish, string file)
            { Design = design; Finish = finish; File = file ?? string.Empty; }

            /// <summary>A reversed numeral reads as a different number, so a fin that mirrors its marking goes without.</summary>
            public Marking WithoutNumber() => new Marking(Design.With(EmblemLayer.Number, -Design.Number), Finish, File);

            public bool Equals(Marking other) =>
                Design == other.Design && Finish == other.Finish && string.Equals(File ?? string.Empty, other.File ?? string.Empty, StringComparison.Ordinal);
        }

        /// <summary>A generated marking as a texture; drawn on a worker thread, dropped when no bake has asked for it lately.</summary>
        private sealed class Art
        {
            public Marking Marking;
            public Texture2D Texture;
            public Task<byte[]> Drawing;
            public int Idle;
        }

        /// <summary>A livery atlas with one marking baked in, shared by every aircraft flying that livery under that marking.</summary>
        private sealed class Coat
        {
            public Texture Source;
            public Marking Marking;
            public RenderTexture Painted;   // as baked; released once the packed copy takes over
            public Texture2D Packed;
            public Task<byte[]> Packing;
            public bool Reading, Alpha, Srgb;
            public Texture Current => Packed != null ? (Texture)Packed : Painted;
            public bool Busy => Reading || Packing != null;
        }

        private struct Worn
        {
            public Aircraft Aircraft;
            public Marking Wanted;
            public Coat Coat;
            public Material Probe;
        }

        private ProgressionSettings settings;
        private ManualLogSource log;
        private CrestBoard crests;
        private ProgressionNet net;
        private string ownText;
        private EmblemDesign ownDesign = EmblemDesign.Default;
        private Material bake;
        private bool bakeTried;
        private IAceWingRoster aces;
        private bool acesTried;
        private float nextPoll;
        private double lastBakeMs;
        private int bakes, packs;
        // Acceptance scenarios drive the paint shop through these, so a test run never rewrites the player's config.
        private EmblemPaint? forcedMode;
        private string forcedEmblem;
        private Transform shotFin;
        private Spot shotSpot;
        private bool finsShared;

        private readonly List<Coat> coats = new List<Coat>(MaximumCoats);
        private readonly List<Worn> worn = new List<Worn>(MaximumWorn);
        private readonly List<Art> arts = new List<Art>(MaximumArt);
        private readonly HashSet<int> unfit = new HashSet<int>();
        private readonly Dictionary<int, int> waits = new Dictionary<int, int>();
        private readonly List<Aircraft> flight = new List<Aircraft>(8);
        private readonly List<KeyValuePair<string, Aircraft>> hostile = new List<KeyValuePair<string, Aircraft>>(16);
        private readonly List<KeyValuePair<Aircraft, Marking>> wanted = new List<KeyValuePair<Aircraft, Marking>>(MaximumWorn);
        private readonly List<Material> skins = new List<Material>(96);
        private readonly List<KeyValuePair<Mesh, Spot>> fins = new List<KeyValuePair<Mesh, Spot>>(4);

        public void Configure(ProgressionSettings progressionSettings, ManualLogSource logger, CrestBoard board, ProgressionNet network)
        {
            settings = progressionSettings;
            log = logger;
            crests = board;
            net = network;
            Live = this;
        }

        public void ResetForScene()
        {
            StripAll();
            unfit.Clear();
            waits.Clear();
            aces = null;
            acesTried = false;
            nextPoll = 0f;
            Status = string.Empty;
        }

        private void OnDestroy()
        {
            StripAll();
            for (int i = 0; i < arts.Count; i++)
                if (arts[i].Texture != null) Destroy(arts[i].Texture);
            arts.Clear();
            if (bake != null) Destroy(bake);
            if (Live == this) Live = null;
        }

        private void Update()
        {
            if (settings == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollSeconds;
            Poll();
        }

        // ---- The once-a-second pass ----------------------------------------------------------

        private void Poll()
        {
            // The player's real settings go to the host whatever is painted here, Off included (it withdraws the
            // crest); acceptance-scenario overrides below stay local. The parse is redone only when the text changed.
            if (net != null)
            {
                string text = settings.Emblem.Value;
                if (!string.Equals(text, ownText, StringComparison.Ordinal))
                {
                    ownText = text;
                    ownDesign = EmblemDesign.Parse(text, EmblemDesign.Default);
                }
                net.PublishCrest(ownDesign, settings.EmblemPaint.Value);
            }
            EmblemPaint mode = forcedMode ?? settings.EmblemPaint.Value;
            if (mode == EmblemPaint.Off)
            {
                if (worn.Count > 0 || coats.Count > 0) StripAll();
                Status = string.Empty;
                return;
            }
            if (!BakeReady())
            {
                Status = "TAIL PAINT UNAVAILABLE: BAKE SHADER MISSING";
                return;
            }

            EmblemFinish finish = mode == EmblemPaint.LowVis ? EmblemFinish.LowVis : EmblemFinish.Colour;
            Gather(finish);

            // An aircraft that left the scene, left the flight, or is now asked for a different marking hands its coat back.
            for (int i = worn.Count - 1; i >= 0; i--)
            {
                Worn w = worn[i];
                if (w.Aircraft != null && Wants(w.Aircraft, out Marking still) && still.Equals(w.Wanted)) continue;
                if (w.Aircraft != null) Repaint(w.Aircraft, w.Coat.Current, w.Coat.Source);
                worn.RemoveAt(i);
            }
            for (int i = 0; i < wanted.Count; i++) Dress(wanted[i].Key, wanted[i].Value);
            for (int i = coats.Count - 1; i >= 0; i--)
            {
                bool used = false;
                for (int k = 0; k < worn.Count && !used; k++) used = worn[k].Coat == coats[i];
                if (used) continue;
                Release(coats[i]);
                coats.RemoveAt(i);
            }
            AdvancePacking();
            for (int i = arts.Count - 1; i >= 0; i--)
            {
                if (++arts[i].Idle <= ArtIdlePolls) continue;
                if (arts[i].Texture != null) Destroy(arts[i].Texture);
                arts.RemoveAt(i);
            }

            Status = worn.Count > 0 ? "TAIL PAINT ON " + worn.Count + " AIRCRAFT"
                : wanted.Count == 0 ? "TAIL PAINT WAITS FOR AN AIRCRAFT"
                : "TAIL PAINT: MIXING, OR NO FIN DATA FOR THIS AIRFRAME";
        }

        /// <summary>Who should carry what this second: the player's flight in the player's marking, each ace wing in its crest.</summary>
        private void Gather(EmblemFinish finish)
        {
            wanted.Clear();
            var own = new Marking(EmblemDesign.Parse(forcedEmblem ?? settings.Emblem.Value, EmblemDesign.Default), finish,
                forcedEmblem != null ? string.Empty : settings.EmblemFile.Value);

            // The resolver clears its list, so the wing goes in first and the player's own aircraft after.
            flight.Clear();
            if (WingLink.WingCount > 0) WingLink.ResolveWingAircraft(flight);
            if (GameManager.GetLocalAircraft(out Aircraft mine) && mine != null && !flight.Contains(mine)) flight.Add(mine);
            for (int i = 0; i < flight.Count && wanted.Count < MaximumWorn; i++)
                if (flight[i] != null) wanted.Add(new KeyValuePair<Aircraft, Marking>(flight[i], own));

            // Every other player in their published crest. The board is keyed by the id the host judged, and a
            // player's aircraft is read off the player, so nothing is searched for and nothing allocates.
            if (crests != null && crests.Count > 0 && UnitRegistry.playerLookup != null)
            {
                ulong self = GameManager.GetLocalPlayer<Player>(out Player local) ? PlayerIdentity.Of(local) : PlayerIdentity.None;
                foreach (Player player in UnitRegistry.playerLookup.Values)
                {
                    if (wanted.Count >= MaximumWorn) break;
                    Aircraft aircraft = player != null ? player.Aircraft : null;
                    if (aircraft == null || Wants(aircraft, out _)) continue;
                    ulong id = PlayerIdentity.Of(player);
                    if (id == PlayerIdentity.None || id == self || !crests.TryGet(id, out CrestBoard.Entry entry) ||
                        entry.Paint == EmblemPaint.Off) continue;
                    wanted.Add(new KeyValuePair<Aircraft, Marking>(aircraft, new Marking(entry.Design,
                        entry.Paint == EmblemPaint.LowVis ? EmblemFinish.LowVis : EmblemFinish.Colour, string.Empty)));
                }
            }

            if (!acesTried) { acesTried = true; ModuleServices.TryGet(out aces); }
            if (aces == null) return;
            hostile.Clear();
            aces.CopyAceAircraft(hostile);
            for (int i = 0; i < hostile.Count && wanted.Count < MaximumWorn; i++)
                if (hostile[i].Value != null && !Wants(hostile[i].Value, out _))
                    wanted.Add(new KeyValuePair<Aircraft, Marking>(hostile[i].Value,
                        new Marking(EmblemDesign.Hostile(hostile[i].Key), finish, string.Empty)));
        }

        private bool Wants(Aircraft aircraft, out Marking marking)
        {
            for (int i = 0; i < wanted.Count; i++)
                if (wanted[i].Key == aircraft) { marking = wanted[i].Value; return true; }
            marking = default;
            return false;
        }

        private void Dress(Aircraft aircraft, Marking asked)
        {
            int id = aircraft.GetInstanceID();
            if (unfit.Contains(id)) return;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].Aircraft != aircraft) continue;
                // Still wearing it unless the game set a livery again since.
                if (worn[i].Probe != null && worn[i].Probe.GetTexture(LiveryId) == worn[i].Coat.Current) return;
                worn.RemoveAt(i);
                break;
            }
            if (worn.Count >= MaximumWorn || DamageField == null) return;
            if (!aircraft.TryGetLiveryBehaviour(out LiveryBehaviour livery) || livery == null || livery.IsLoading) return;

            Texture source = Survey(aircraft);
            if (skins.Count == 0)
            {
                // The livery has not reached the materials yet. An airframe that never takes one is dropped
                // after half a minute, so it stops costing a survey every second.
                waits.TryGetValue(id, out int waited);
                if (waited >= MaximumWaits) unfit.Add(id); else waits[id] = waited + 1;
                return;
            }
            if (fins.Count == 0 || source == null) { unfit.Add(id); return; }

            Marking marking = finsShared && asked.Design.Number > 0 && asked.File.Length == 0 ? asked.WithoutNumber() : asked;
            Coat coat = null;
            for (int i = 0; i < coats.Count; i++)
                if (coats[i].Source == source && coats[i].Marking.Equals(marking)) { coat = coats[i]; break; }
            if (coat == null)
            {
                // One unpacked copy at a time: the 22 MB working copy never stacks up when a whole wing spawns at once.
                if (coats.Count >= MaximumCoats || AnyBusy()) return;
                // A streamed atlas must be at full resolution before it is copied.
                if (source is Texture2D atlas && atlas.streamingMipmaps && atlas.loadedMipmapLevel > 0)
                {
                    atlas.requestedMipmapLevel = 0;
                    return;
                }
                Texture art = ArtFor(marking);
                if (art == null) return;                        // still being drawn
                coat = Bake(source, art, marking);
                if (coat == null) { unfit.Add(id); return; }
                coats.Add(coat);
                StartReadback(coat);
            }
            for (int i = 0; i < skins.Count; i++) skins[i].SetTexture(LiveryId, coat.Current);
            worn.Add(new Worn { Aircraft = aircraft, Wanted = asked, Coat = coat, Probe = skins[0] });
        }

        private bool AnyBusy()
        {
            for (int i = 0; i < coats.Count; i++) if (coats[i].Busy) return true;
            return false;
        }

        /// <summary>
        /// Fills <see cref="skins"/> with the aircraft's per-part skin materials and <see cref="fins"/>
        /// with its known fin meshes; returns the plain livery those materials carry (or carried before a coat).
        /// Only materials the game itself instanced are touched, never a shared asset.
        /// </summary>
        private Texture Survey(Aircraft aircraft)
        {
            skins.Clear();
            fins.Clear();
            finsShared = false;
            Texture source = null;
            List<UnitPart> parts = aircraft.partLookup;
            if (parts == null) return null;
            for (int p = 0; p < parts.Count; p++)
            {
                UnitPart part = parts[p];
                if (part == null) continue;
                List<Renderer> renderers = (DamageField.GetValue(part) as DamageMaterial)?.renderers;
                if (renderers == null) continue;
                for (int r = 0; r < renderers.Count; r++)
                {
                    Renderer renderer = renderers[r];
                    Material material = renderer != null ? renderer.sharedMaterial : null;
                    if (material == null || !material.HasProperty(LiveryId) ||
                        !material.name.EndsWith("(Instance)", StringComparison.Ordinal)) continue;
                    Texture carried = material.GetTexture(LiveryId);
                    if (carried == null) continue;
                    if (source == null) source = carried;
                    if (carried != source) continue;
                    skins.Add(material);
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh != null && fins.Count < fins.Capacity && Spots.TryGetValue(mesh.name, out Spot spot))
                    {
                        if (fins.Count == 0) { shotFin = renderer.transform; shotSpot = spot; }
                        finsShared |= spot.Shared;
                        fins.Add(new KeyValuePair<Mesh, Spot>(mesh, spot));
                    }
                }
            }
            // A coat of ours means the plain livery under it is the real source.
            for (int i = 0; i < coats.Count; i++)
                if (source != null && coats[i].Current == source) return coats[i].Source;
            return source;
        }

        /// <summary>Sets every skin material of the aircraft that carries <paramref name="from"/> to <paramref name="to"/>.</summary>
        private void Repaint(Aircraft aircraft, Texture from, Texture to)
        {
            List<UnitPart> parts = aircraft.partLookup;
            if (parts == null || DamageField == null || from == null) return;
            for (int p = 0; p < parts.Count; p++)
            {
                if (parts[p] == null) continue;
                List<Renderer> renderers = (DamageField.GetValue(parts[p]) as DamageMaterial)?.renderers;
                if (renderers == null) continue;
                for (int r = 0; r < renderers.Count; r++)
                {
                    Material material = renderers[r] != null ? renderers[r].sharedMaterial : null;
                    if (material != null && material.HasProperty(LiveryId) && material.GetTexture(LiveryId) == from)
                        material.SetTexture(LiveryId, to);
                }
            }
        }

        // ---- The marking as a texture -----------------------------------------------------------

        /// <summary>The marking as a texture, or null while its pixels are still being drawn on a worker thread.</summary>
        private Texture ArtFor(Marking marking)
        {
            if (marking.File.Length > 0)
            {
                Sprite file = EmblemRenderer.File(marking.File);
                if (file != null) return file.texture;
            }
            Art art = null;
            for (int i = 0; i < arts.Count; i++)
                if (arts[i].Marking.Design == marking.Design && arts[i].Marking.Finish == marking.Finish) { art = arts[i]; break; }
            if (art == null)
            {
                if (arts.Count >= MaximumArt) return null;      // the idle sweep makes room within the half minute
                EmblemDesign design = marking.Design;
                EmblemFinish finish = marking.Finish;
                arts.Add(new Art { Marking = marking, Drawing = Task.Run(() => EmblemRenderer.PaintPixels(design, finish)) });
                return null;
            }
            art.Idle = 0;
            if (art.Texture != null) return art.Texture;
            // A drawing that faulted stays parked: no paint and no retry until the entry idles out.
            if (art.Drawing == null || !art.Drawing.IsCompleted || art.Drawing.Status != TaskStatus.RanToCompletion) return null;
            art.Texture = EmblemRenderer.PaintTexture(art.Drawing.Result);
            art.Drawing = null;
            return art.Texture;
        }

        // ---- The bake -------------------------------------------------------------------------

        private bool BakeReady()
        {
            if (bake != null) return true;
            if (bakeTried) return false;
            bakeTried = true;
            try
            {
                byte[] data = EmbeddedResources.ReadAll(typeof(TailPaint).Assembly, "BoscaliSummer.Progression.emblem.bundle", 256 * 1024);
                AssetBundle bundle = data != null ? AssetBundle.LoadFromMemory(data) : null;
                if (bundle == null) return false;
                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                bundle.Unload(false);
                for (int i = 0; i < shaders.Length; i++)
                    if (shaders[i] != null && shaders[i].isSupported && shaders[i].name == "Hidden/BoscaliSummer/EmblemBake")
                        bake = new Material(shaders[i]) { name = "Boscali tail paint bake", hideFlags = HideFlags.HideAndDontSave };
            }
            catch (Exception error) { log?.LogWarning("[TailPaint] bake shader failed to load: " + error.Message); }
            return bake != null;
        }

        /// <summary>Copies the livery, then draws each surveyed fin into the copy at its own UVs.</summary>
        private Coat Bake(Texture source, Texture art, Marking marking)
        {
            var watch = Stopwatch.StartNew();
            RenderTexture previous = RenderTexture.active;
            RenderTexture work = null, painted = null;
            try
            {
                bool srgb = GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
                RenderTextureReadWrite space = srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
                // The bake needs a stencil buffer; the copy it leaves behind does not.
                work = RenderTexture.GetTemporary(source.width, source.height, 24, RenderTextureFormat.ARGB32, space);
                Graphics.Blit(source, work);
                RenderTexture.active = work;
                GL.Clear(true, false, Color.clear);

                bake.mainTexture = art;
                for (int pass = 0; pass < 2; pass++)
                    for (int f = 0; f < fins.Count; f++)
                        Stamp(fins[f].Key, fins[f].Value, pass);

                painted = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, space)
                {
                    name = "BoscaliTailPaint_" + source.name,
                    useMipMap = true,
                    autoGenerateMips = false,
                    wrapMode = source.wrapMode,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = Mathf.Max(source.anisoLevel, 2),
                    hideFlags = HideFlags.HideAndDontSave,
                };
                Graphics.Blit(work, painted);
                painted.GenerateMips();
                if (source is Texture2D atlas && atlas.streamingMipmaps) atlas.ClearRequestedMipmapLevel();

                lastBakeMs = watch.Elapsed.TotalMilliseconds;
                bakes++;
                log?.LogInfo("[TailPaint] baked " + fins.Count + " fin(s) into " + source.name + " " + source.width + "x" +
                    source.height + " in " + lastBakeMs.ToString("0.0") + " ms (CPU submit; one-off).");
                // The stock atlases are DXT1 where alpha is unused and DXT5 where it is carried; the copy follows suit.
                bool opaque = source is Texture2D flat && (flat.format == TextureFormat.DXT1 || flat.format == TextureFormat.DXT1Crunched);
                return new Coat { Source = source, Marking = marking, Painted = painted, Alpha = !opaque, Srgb = srgb };
            }
            catch (Exception error)
            {
                log?.LogWarning("[TailPaint] bake failed for " + source.name + ": " + error.Message);
                if (painted != null) { painted.Release(); Destroy(painted); }
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (work != null) RenderTexture.ReleaseTemporary(work);
            }
        }

        /// <summary>One fin, one face: pass 0 is the outboard face, pass 1 the inboard face with a mirrored frame.</summary>
        private void Stamp(Mesh mesh, Spot spot, int pass)
        {
            float half = spot.Half * Margin, side = pass == 0 ? 1f : -1f;
            Vector3 normal = spot.Normal.normalized * side, up = spot.Up.normalized;
            Vector3 right = Vector3.Cross(normal, up);
            bake.SetVector(RightId, Axis(right / half, spot.Centre));
            bake.SetVector(UpId, Axis(up / half, spot.Centre));
            bake.SetVector(NormalId, Axis(normal, spot.Centre));
            // Fin UVs run past the 0..1 tile on some airframes; the atlas repeats, so every neighbour tile is drawn.
            for (int du = -1; du <= 1; du++)
                for (int dv = -1; dv <= 1; dv++)
                {
                    bake.SetVector(ParamsId, new Vector4(Opacity, HalfDepth, du, dv));
                    bake.SetPass(pass);
                    Graphics.DrawMeshNow(mesh, Matrix4x4.identity, 0);
                }
        }

        private static Vector4 Axis(Vector3 direction, Vector3 origin) =>
            new Vector4(direction.x, direction.y, direction.z, -Vector3.Dot(direction, origin));

        // ---- Packing: the 22 MB working copy becomes a copy the size of the stock atlas -----------

        private void StartReadback(Coat coat)
        {
            coat.Reading = true;
            AsyncGPUReadback.Request(coat.Painted, 0, TextureFormat.RGBA32, request => OnRead(coat, request));
        }

        private void OnRead(Coat coat, AsyncGPUReadbackRequest request)
        {
            coat.Reading = false;
            // Released while the read was in flight, or the driver refused it: the copy simply stays unpacked.
            if (this == null || request.hasError || coat.Painted == null || !coats.Contains(coat)) return;
            byte[] pixels = request.GetData<byte>().ToArray();
            int width = coat.Painted.width, height = coat.Painted.height;
            bool alpha = coat.Alpha;
            coat.Packing = Task.Run(() => LiveryCodec.Encode(pixels, width, height, alpha));
        }

        /// <summary>Swaps in every packed copy that has finished encoding and frees the working copy behind it.</summary>
        private void AdvancePacking()
        {
            for (int i = 0; i < coats.Count; i++)
            {
                Coat coat = coats[i];
                if (coat.Packing == null || !coat.Packing.IsCompleted) continue;
                Task<byte[]> done = coat.Packing;
                coat.Packing = null;
                if (done.Status != TaskStatus.RanToCompletion || coat.Painted == null) continue;
                try
                {
                    RenderTexture working = coat.Painted;
                    var packed = new Texture2D(working.width, working.height, coat.Alpha ? TextureFormat.DXT5 : TextureFormat.DXT1, true, !coat.Srgb)
                    {
                        name = working.name,
                        wrapMode = working.wrapMode,
                        filterMode = FilterMode.Trilinear,
                        anisoLevel = working.anisoLevel,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    packed.LoadRawTextureData(done.Result);
                    packed.Apply(false, true);
                    for (int k = 0; k < worn.Count; k++)
                        if (worn[k].Coat == coat && worn[k].Aircraft != null) Repaint(worn[k].Aircraft, working, packed);
                    coat.Packed = packed;
                    coat.Painted = null;
                    working.Release();
                    Destroy(working);
                    packs++;
                }
                catch (Exception error) { log?.LogWarning("[TailPaint] packing failed; keeping the unpacked copy: " + error.Message); }
            }
        }

        // ---- Teardown -------------------------------------------------------------------------

        private void StripAll()
        {
            for (int i = 0; i < worn.Count; i++)
                if (worn[i].Aircraft != null) Repaint(worn[i].Aircraft, worn[i].Coat.Current, worn[i].Coat.Source);
            worn.Clear();
            // ponytail: a part already blown off its aircraft keeps the released copy until it despawns.
            for (int i = 0; i < coats.Count; i++) Release(coats[i]);
            coats.Clear();
        }

        private static void Release(Coat coat)
        {
            if (coat.Painted != null) { coat.Painted.Release(); Destroy(coat.Painted); coat.Painted = null; }
            if (coat.Packed != null) { Destroy(coat.Packed); coat.Packed = null; }
            coat.Packing = null;
        }

        // ---- Acceptance hooks -------------------------------------------------------------------

        internal Dictionary<string, object> Readout()
        {
            long bytes = 0;
            int packedCoats = 0;
            for (int i = 0; i < coats.Count; i++)
            {
                Texture current = coats[i].Current;
                if (current == null) continue;
                if (coats[i].Packed != null) { packedCoats++; bytes += LiveryCodec.Size(current.width, current.height, coats[i].Alpha); }
                else bytes += (long)current.width * current.height * 4L * 4L / 3L;
            }
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["mode"] = (forcedMode ?? settings.EmblemPaint.Value).ToString(),
                ["emblem"] = forcedEmblem ?? settings.Emblem.Value,
                ["status"] = Status,
                ["shader"] = bake != null ? 1 : 0,
                ["aircraftPainted"] = worn.Count,
                ["coats"] = coats.Count,
                ["coatsPacked"] = packedCoats,
                ["coatBytes"] = bytes,
                ["bakes"] = bakes,
                ["packs"] = packs,
                ["lastBakeMs"] = lastBakeMs,
                ["unfit"] = unfit.Count,
                ["numberLeftOff"] = finsShared ? 1 : 0,
            };
        }

        /// <summary>
        /// A close-up of the last surveyed fin from its outboard (side 1) or inboard (side -1) face, rendered by a
        /// throwaway camera so the proof does not depend on the game's view mode. Written under BepInEx/cache.
        /// </summary>
        internal Dictionary<string, object> Shoot(string name, float side, float distance)
        {
            if (shotFin == null) return new Dictionary<string, object> { ["ok"] = false, ["error"] = "no fin surveyed yet" };
            var rig = new GameObject("BoscaliTailPaintShot");
            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Vector3 centre = shotFin.TransformPoint(shotSpot.Centre);
                Vector3 normal = shotFin.TransformDirection(shotSpot.Normal).normalized * (side < 0f ? -1f : 1f);
                Vector3 up = shotFin.TransformDirection(shotSpot.Up).normalized;
                rig.transform.position = centre + normal * distance + up * (distance * 0.12f);
                rig.transform.rotation = Quaternion.LookRotation(centre - rig.transform.position, up);
                Camera camera = rig.AddComponent<Camera>();
                camera.fieldOfView = 30f; camera.nearClipPlane = 0.3f; camera.farClipPlane = 30000f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var read = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                read.Apply();
                string folder = System.IO.Path.Combine(BepInEx.Paths.CachePath, "BoscaliSummer");
                System.IO.Directory.CreateDirectory(folder);
                string file = System.IO.Path.Combine(folder, "tailpaint-" + System.IO.Path.GetFileNameWithoutExtension(name ?? "shot") + ".png");
                System.IO.File.WriteAllBytes(file, read.EncodeToPNG());
                Destroy(read);
                Dictionary<string, object> result = Readout();
                result["file"] = file;
                result["fin"] = shotFin.name;
                return result;
            }
            catch (Exception error) { return new Dictionary<string, object> { ["ok"] = false, ["error"] = error.Message }; }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(rig);
            }
        }

        internal Dictionary<string, object> Apply(Dictionary<string, object> args)
        {
            if (args != null)
            {
                if (args.TryGetValue("mode", out object mode) && mode is string modeText &&
                    Enum.TryParse(modeText, true, out EmblemPaint parsed)) forcedMode = parsed;
                if (args.TryGetValue("emblem", out object emblem) && emblem is string emblemText &&
                    EmblemDesign.TryParse(emblemText, out _)) forcedEmblem = emblemText;
            }
            nextPoll = 0f;
            return Readout();
        }
    }

    /// <summary>Explicit client-local hooks for nomodkit acceptance scenarios (tests/ingame/tail-paint.json).</summary>
    public static class TailPaintAutomation
    {
        /// <summary>Args: mode ("Off" | "Colour" | "LowVis"), emblem (encoded design). Both optional. Returns the readout.</summary>
        public static Dictionary<string, object> Configure(Dictionary<string, object> args)
        {
            TailPaint paint = TailPaint.Live;
            if (paint == null) return new Dictionary<string, object> { ["ok"] = false, ["error"] = "no tail paint service in this scene" };
            return paint.Apply(args);
        }

        public static Dictionary<string, object> Readout(Dictionary<string, object> args)
        {
            TailPaint paint = TailPaint.Live;
            return paint != null ? paint.Readout()
                : new Dictionary<string, object> { ["ok"] = false, ["error"] = "no tail paint service in this scene" };
        }

        /// <summary>Args: name (file stem), side (1 outboard, -1 inboard), distance (metres, default 7). Returns the readout plus the PNG path.</summary>
        public static Dictionary<string, object> Capture(Dictionary<string, object> args)
        {
            TailPaint paint = TailPaint.Live;
            if (paint == null) return new Dictionary<string, object> { ["ok"] = false, ["error"] = "no tail paint service in this scene" };
            float Number(string key, float fallback) =>
                args != null && args.TryGetValue(key, out object value) && value != null
                    ? Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
            return paint.Shoot(args != null && args.TryGetValue("name", out object name) ? name as string : null,
                Number("side", 1f), Mathf.Clamp(Number("distance", 7f), 2f, 60f));
        }
    }
}
