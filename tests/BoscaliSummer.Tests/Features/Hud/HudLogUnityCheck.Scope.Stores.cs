#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The stores side of the TAC BOARD (HERO, PWR BUS, EXPENDABLES): the fixtures (takeover/stores.js and every page's stores(t) hook in takeover/pages/*.js: the default CI-22 CRICKET loadout, a gun with 500 rounds, two AAM-4,
// two AGM-48, a GBU-12, a MALD-X kit, an AAM-1 DART; 48 flares, 30 chaff, a jammer, a 500 kJ capacitor; and what each page's scenario does to it) and the block cases (ScopeStoresBlocks, below). The blocks are drawn on every
// page's shot (RenderScope calls StoresFixture after the page's own fixture) and on their own over the cruise strip for the variants of the "stores" page. Fixtures are the ONLY place scripted data lives.
//
// The game's pictures: the sprites are white lines on an opaque black ground, drawn with the game's additive material, neither of which the offline project has. The fixtures stand in for both: a procedural sprite with a
// TRANSPARENT ground (its outline is drawn inside the very box StoreArtBounds says the real one's ink lives in, so the crop is what is being judged), a stand-in material, both handed to the board the way the game's
// are (StoreIcons).
public static partial class HudLogUnityCheck
{
    private struct LoadStation
    {
        public readonly string Cls, Name;
        public readonly int Max;
        public LoadStation(string cls, string name, int max) { Cls = cls; Name = name; Max = max; }
    }

    private static LoadStation[] CricketLoad() => new[]
    {
        new LoadStation("Gun", "20MM ROTARY CANNON", 500), new LoadStation("Aam", "AAM-4 SKYWAY", 1), new LoadStation("Aam", "AAM-4 SKYWAY", 1),
        new LoadStation("Agm", "AGM-48", 1), new LoadStation("Agm", "AGM-48", 1), new LoadStation("Bomb", "GBU-12", 1), new LoadStation("Kit", "MALD-X", 1),
        new LoadStation("Aam", "AAM-1 DART", 1),
    };

    /// <summary>What the stores feed says at one moment of a scenario (the prototype's <c>stores(t)</c> overrides over its defaults).</summary>
    private sealed class StoresSpec
    {
        public LoadStation[] Load = CricketLoad();
        public readonly Dictionary<int, int> Rounds = new Dictionary<int, int>();
        public int Sel;
        public bool Safe, Link = true, Trt;
        public int ReloadAt = -1, ReloadAt2 = -1;
        public float ReloadLeft, ReloadTotal = 1f, Reload2Left = 1f, Reload2Total = 1f;
        public int FiredAt = -1;
        public float FiredAge = 99f;
        public bool HasIr = true, HasRdr = true, HasJam = true;
        public int Ir = 48, IrMax = 48, Rdr = 30, RdrMax = 30, Active;       // Active: 0 = the flares are selected, 1 = the chaff
        public bool JamOn;
        public float JamCharge = 1f;
        public bool Cap = true;
        public float Kj = 500f, Rate, CostKj;
        public byte Call;
        /// <summary>The game's pictures are at hand (stand-in sprites on the stations of a class that has one, and the stand-in material); false: none of them, so the board draws its glyphs.</summary>
        public bool Pictures = true;
    }

    // ------------------------------------------------------------------------------------------------ the stand-in pictures

    private static Material standInMaterial;
    private static readonly Dictionary<string, Sprite> standIns = new Dictionary<string, Sprite>();

    private static Type StoreIconsType() => Type("BoscaliSummer.Modules.Hud.Presentation.StoreIcons");

    /// <summary>The ink box (fractions of the sprite, y down) the generated table gives <paramref name="sprite"/>; the whole sprite when it does not know it.</summary>
    private static Vector4 StoresInkBox(string sprite)
    {
        object[] args = { sprite, null };
        if (!(bool)Type(HudDomain + "StoreArtBounds").GetMethod("TryGet", All).Invoke(null, args)) return new Vector4(0f, 0f, 1f, 1f);
        object b = args[1];
        return new Vector4((float)Field(b, "X0"), (float)Field(b, "Y0"), (float)Field(b, "X1"), (float)Field(b, "Y1"));
    }

    /// <summary>A procedural sprite named like a vanilla one: a transparent ground and a two pixel outline (a body, a nose, a tail fin and a centre line) inside the ink box the table says that sprite's ink lives in.</summary>
    private static Sprite StandIn(string name, int w = 512, int h = 256)
    {
        if (standIns.TryGetValue(name, out Sprite known) && known != null) return known;
        Vector4 box = StoresInkBox(name);
        int x0 = Mathf.RoundToInt(box.x * w), y0 = Mathf.RoundToInt(box.y * h), x1 = Mathf.RoundToInt(box.z * w) - 1, y1 = Mathf.RoundToInt(box.w * h) - 1, mid = (y0 + y1) / 2, nose = Mathf.Max(6, (y1 - y0) / 2);
        var pixels = new Color32[w * h];
        var white = new Color32(255, 255, 255, 255);
        void Dot(int x, int y)
        {
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    if (x + dx >= 0 && x + dx < w && y + dy >= 0 && y + dy < h) pixels[(h - 1 - (y + dy)) * w + x + dx] = white;
        }
        void Line(int ax, int ay, int bx, int by)
        {
            int n = Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(by - ay));
            for (int i = 0; i <= n; i++) Dot(ax + (bx - ax) * i / Mathf.Max(1, n), ay + (by - ay) * i / Mathf.Max(1, n));
        }
        Line(x0, y0, x1 - nose, y0); Line(x0, y1 - 1, x1 - nose, y1 - 1);             // the body
        Line(x1 - nose, y0, x1 - 1, mid); Line(x1 - nose, y1 - 1, x1 - 1, mid);       // the nose
        Line(x0, y0, x0, y1 - 1);                                                     // the tail
        Line(x0, mid, x1 - nose, mid);                                                // a centre line
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "StandIn_" + name, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
        sprite.name = name;
        standIns[name] = sprite;
        return sprite;
    }

    private static int StandInId(string name, int w = 512, int h = 256) =>
        (int)StoreIconsType().GetMethod("Id", All).Invoke(null, new object[] { StandIn(name, w, h) });

    /// <summary>The picture of a store class in the fixtures: one stand-in for a gun, an AAM, an AGM and a bomb under a vanilla sprite's name, one under a name the table does not know (a pod: a modded weapon's), none for a kit or a rail.</summary>
    private static int StoresIconOf(string cls)
    {
        switch (cls)
        {
            case "Gun": return StandInId("weaponIcon_gatlingGun");
            case "Aam": return StandInId("weaponIcon_AAM4");
            case "Agm": return StandInId("weaponIcon_AGM1");
            case "Bomb": return StandInId("weaponIcon_bomb_250_glide");
            case "Pod": return StandInId("modded_pod_icon");
            default: return 0;
        }
    }

    /// <summary>Hand the board the stand-in material (the game's additive one in game), or take it away.</summary>
    private static void StoresArt(bool on)
    {
        if (on && standInMaterial == null) standInMaterial = new Material(Shader.Find("UI/Default")) { name = "Text_additive (stand-in)", hideFlags = HideFlags.HideAndDontSave };
        Set(StoreIconsType(), "Material", on ? standInMaterial : null);
    }

    private static object BuildStores(ScopeScene s, StoresSpec x)
    {
        Array stations = s.DomainArray("StoreStation", 12);
        int slot = 0;
        for (int i = 0; i < x.Load.Length; i++)
        {
            LoadStation st = x.Load[i];
            int n = x.Rounds.TryGetValue(i, out int r) ? r : st.Max;
            bool rel = x.ReloadAt == i || x.ReloadAt2 == i;
            float left = x.ReloadAt == i ? x.ReloadLeft : x.Reload2Left, total = x.ReloadAt == i ? x.ReloadTotal : x.Reload2Total;
            stations.SetValue(s.Fill(s.Domain("StoreStation"), "Class", s.DomainEnum("StoreClass", st.Cls), "Name", st.Name, "Ammo", n, "Max", st.Max, "Slot", st.Cls == "Gun" ? 0 : ++slot,
                "Reloading", rel, "ReloadElapsed", rel ? 1f - left / total : float.NaN, "ReloadLeftS", rel ? left : float.NaN, "FiredAgeS", x.FiredAt == i ? x.FiredAge : 99f,
                "Icon", x.Pictures ? StoresIconOf(st.Cls) : 0), i);
        }
        Array cm = BuildCm(s, x, out int c);
        return s.Fill(s.Domain("StoresScope"), "Stations", stations, "StationCount", x.Load.Length, "Selected", x.Sel, "Safe", x.Safe, "Link", x.Link, "TurretAuto", x.Trt,
            "Cm", cm, "CmCount", c, "Call", x.Call, "HasCap", x.Cap, "CapKj", x.Kj, "CapMaxKj", 500f, "CapRate", x.Rate, "CapCostKj", x.CostKj);
    }

    /// <summary>The countermeasure stations of the spec (flares, chaff, a jammer), the same array the production frame carries.</summary>
    private static Array BuildCm(ScopeScene s, StoresSpec x, out int count)
    {
        Array cm = s.DomainArray("CmStation", 4);
        int c = 0;
        if (x.HasIr) cm.SetValue(s.Fill(s.Domain("CmStation"), "Tag", "IR", "Ammo", x.Ir, "Max", x.IrMax, "Mask", (byte)1, "Selected", x.Active == 0), c++);
        if (x.HasRdr) cm.SetValue(s.Fill(s.Domain("CmStation"), "Tag", "RDR", "Ammo", x.Rdr, "Max", x.RdrMax, "Mask", (byte)2, "Selected", x.Active == 1), c++);
        if (x.HasJam) cm.SetValue(s.Fill(s.Domain("CmStation"), "Tag", "JAM", "Ammo", 1, "Max", 1, "Chargeable", true, "Mask", (byte)2, "Charge", x.JamCharge, "Active", x.JamOn,
            "Icon", x.Pictures ? StandInId("weaponicon_radarJammer", 256, 256) : 0), c++);
        count = c;
        return cm;
    }

    /// <summary>The earlier moments of a page's scenario its header (and the page) must have seen before <paramref name="t"/>.</summary>
    private static float[] PreRoll(string page, float t)
    {
        if (page != "threat") return new float[0];
        var list = new List<float>();
        foreach (float before in new[] { 0.9f, 8.5f }) if (t > before + 0.05f) list.Add(before);
        return list.ToArray();
    }

    private static float CapKjSystems(float t) => t < 9f ? 500f : t < 11.5f ? 500f - 14f * (t - 9f) : Mathf.Min(500f, 465f + 30f * (t - 11.5f));

    private static float CapKjThreat(float t) => t < 3f ? 500f : t < 8.4f ? 500f - 14f * (t - 3f) : Mathf.Min(500f, 500f - 14f * 5.4f + 30f * (t - 8.4f));

    /// <summary>The stores feed of <paramref name="page"/> at <paramref name="t"/>, published like the production provider does (<c>ScopeFeeds.Publish</c>), with the stand-in pictures handed to the board.</summary>
    private static void StoresFixture(string page, float t, ScopeScene s)
    {
        StoresSpec x = StoresSpecFor(page, t, s.Variant);
        StoresArt(x.Pictures);
        s.Publish(BuildStores(s, x));
    }

    /// <summary>What the page's scenario does to the default loadout at <paramref name="t"/> (the "stores" page: what its <paramref name="variant"/> changes).</summary>
    private static StoresSpec StoresSpecFor(string page, float t, string variant)
    {
        var x = new StoresSpec();
        float Rate(Func<float, float> kj) => (kj(t + 0.25f) - kj(t - 0.25f)) / 0.5f;
        switch (page)
        {
            case "systems":
                {
                    float[] flares = { 9.0f, 9.15f, 9.3f, 9.45f }, chaff = { 9.2f, 9.5f };
                    int used = 0, usedChaff = 0;
                    foreach (float f in flares) if (t >= f) used++;
                    foreach (float c in chaff) if (t >= c) usedChaff++;
                    x.Ir = 48 - used; x.Rdr = 30 - usedChaff;
                    bool jamOn = t >= 9f && t < 11.5f;
                    x.JamOn = jamOn;
                    x.JamCharge = t < 9f ? 1f : t < 11.5f ? 1f - 0.45f * Seg01(t, 9f, 11.5f) : 0.55f + 0.45f * Seg01(t, 11.5f, 16f);
                    x.Kj = CapKjSystems(t); x.Rate = Rate(CapKjSystems);
                    break;
                }
            case "threat":
                {
                    float[] chaff = { 6.6f, 6.9f, 7.2f, 7.5f, 7.8f }, flares = { 9.2f, 9.5f, 9.8f, 10.1f, 10.4f };
                    int usedChaff = 0, used = 0;
                    foreach (float c in chaff) if (t >= c) usedChaff++;
                    foreach (float f in flares) if (t >= f) used++;
                    x.Trt = true; x.Ir = 48 - used; x.Rdr = 30 - usedChaff;
                    x.Active = t >= 4.8f && t < 8.4f ? 1 : 0;
                    bool jamOn = t >= 3f && t < 8.4f;
                    x.JamOn = jamOn;
                    x.JamCharge = t < 3f ? 1f : t < 8.4f ? 1f - 0.5f * Seg01(t, 3f, 8.4f) : 0.5f + 0.5f * Seg01(t, 8.4f, 16f);
                    x.Kj = CapKjThreat(t); x.Rate = Rate(CapKjThreat);
                    break;
                }
            case "shot":          // the weapon cell follows the selected station, rounds step down at every launch and the fired station's pip drains (0.8 AAM-4 stn 1, 3.6 DART stn 7, 6.0 GBU-12 stn 5)
                {
                    float[][] launches = { new[] { 0.8f, 1f }, new[] { 3.6f, 7f }, new[] { 6.0f, 5f } };
                    x.Rounds[7] = t >= 3.6f ? 0 : 1;
                    foreach (float[] l in launches)
                        if (t >= l[0])
                        {
                            if (l[1] != 7f) x.Rounds[(int)l[1]] = 0;
                            if (t - l[0] < 1.5f) { x.FiredAt = (int)l[1]; x.FiredAge = t - l[0]; }
                        }
                    x.Sel = t < 1.8f ? 1 : t < 2.9f ? 2 : t < 4.6f ? 7 : t < 7.3f ? 5 : 2;
                    break;
                }
            case "missilecam":    // the cam rides the AAM-4 off station 1: that rail is spent, the next one is selected
                x.Sel = 2; x.Rounds[1] = 0;
                break;
            case "approach":      // landing = master arm SAFE; the end of the sortie, so the dispensers show their low states
                x.Safe = true; x.Ir = 9; x.Rdr = 0;
                break;
            case "wing":          // the wingmen engage air targets: AAM-4 selected
                x.Sel = 1;
                break;
            case "objective":     // a strafing burst on the fuel depot 1.7 -> 3.2 s, then the cannon reloads 3.3 -> 5.4 s
                {
                    x.Rounds[0] = 500 - Mathf.RoundToInt(128f * Seg01(t, 1.7f, 3.2f));
                    if (t >= 1.7f && t < 3.2f) { x.FiredAt = 0; x.FiredAge = 0.2f; }
                    if (t >= 3.3f && t < 5.4f) { x.ReloadAt = 0; x.ReloadLeft = 5.4f - t; x.ReloadTotal = 2.1f; }
                    break;
                }
            case "dock":          // a foreign loadout (SKYWELL rearm): six AAM-4 rails, master arm SAFE from ENGAGE on; each transferred round loads the next rail
                {
                    var load = new List<LoadStation> { new LoadStation("Gun", "20MM ROTARY CANNON", 500) };
                    for (int i = 0; i < 6; i++) load.Add(new LoadStation("Aam", "AAM-4 SKYWAY", 1));
                    load.Add(new LoadStation("Pod", "HYDRA-70 POD", 38));
                    x.Load = load.ToArray();
                    x.Sel = 1; x.Safe = t >= 6.3f;
                    float[] at = { 11.9f, 12.5f, 13.1f, 13.6f };
                    x.Rounds[1] = 1; x.Rounds[2] = 1;
                    for (int j = 0; j < at.Length; j++)
                    {
                        x.Rounds[3 + j] = t >= at[j] ? 1 : 0;
                        if (t >= at[j] - 0.6f && t < at[j]) { x.ReloadAt = 3 + j; x.ReloadLeft = at[j] - t; x.ReloadTotal = 0.6f; }
                    }
                    break;
                }
        }
        if (page == "stores") StoresVariant(x, variant);
        return x;
    }

    /// <summary>The blocks alone: what each variant of the "stores" page changes in the default loadout.</summary>
    private static void StoresVariant(StoresSpec x, string variant)
    {
        switch (variant)
        {
            case "low":           // the gun at a fifth, the flares low, the chaff gone: LOW tag, amber figure and silhouette, LOW and EMPTY bands
                x.Rounds[0] = 90; x.Ir = 9; x.Rdr = 0;
                break;
            case "empty":         // both AAM-4 spent, one selected: EMPTY tab, the pips hollow
                x.Sel = 1; x.Rounds[1] = 0; x.Rounds[2] = 0;
                break;
            case "reload":        // the gun reloads (RELOAD tab, the pip fills amber) while the AGM on station 3 is also reloading (the amber call in the name row)
                x.Rounds[0] = 0; x.ReloadAt = 0; x.ReloadLeft = 2.1f; x.ReloadTotal = 3.3f; x.ReloadAt2 = 3; x.Reload2Left = 1.4f; x.Reload2Total = 3f; x.Rounds[3] = 0;
                break;
            case "nocap":         // an airframe without a capacitor: no PWR BUS block, EXPENDABLES moves up into its place
                x.Cap = false; x.HasJam = false;
                break;
            case "dispense":      // the THREAT page calls for flares: the IR dispenser pulses DISPENSE; the chaff is the selected one
                x.Call = 1; x.Active = 1; x.Sel = 1; x.Safe = false; x.Kj = 431f; x.Rate = -14f; x.JamOn = true; x.JamCharge = 0.7f;
                break;
            case "rotary":        // a helicopter: a gun pod and a rocket pod, one dispenser of flares and no capacitor
                x.Load = new[] { new LoadStation("Gun", "7.62MM DOOR GUN", 600), new LoadStation("Pod", "HYDRA-70 POD", 38), new LoadStation("Pod", "HYDRA-70 POD", 38), new LoadStation("Agm", "AGM-48", 1) };
                x.Sel = 1; x.Trt = true; x.HasRdr = false; x.HasJam = false; x.Cap = false; x.Ir = 60; x.IrMax = 60; x.Rounds[0] = 372 + 100;
                break;
            case "gunonly":       // a trainer: one gun, no countermeasures at all, no capacitor
                x.Load = new[] { new LoadStation("Gun", "20MM ROTARY CANNON", 500) };
                x.HasIr = x.HasRdr = x.HasJam = false; x.Cap = false; x.Rounds[0] = 372;
                break;
        }
    }

    static partial void FixtureStores(float t, ScopeScene s)
    {
        FixtureSystems(2f, s);      // the cruise strip under the header
    }

    // ------------------------------------------------------------------------------------------------ the block cases

    /// <summary>One word the stores surface draws: its text, the box of its drawn glyphs in board units (x right, y down from the box's top-left) and its label.</summary>
    private struct StoresWord
    {
        public string Text;
        public Rect Ink;
        public TextMeshProUGUI Label;
    }

    /// <summary>What a stores case sees of the board: the blocks' rectangles, every word and every picture, in board units.</summary>
    private sealed class StoresView
    {
        public object Status;
        public float Unit;
        public bool Stack;
        public Rect Hero, Pwr, Exp;
        public readonly List<StoresWord> Words = new List<StoresWord>();
        public readonly RawImage[] Art = new RawImage[3];
        public readonly Rect[] ArtBox = new Rect[3];
        public bool Says(string text) { foreach (StoresWord w in Words) if (w.Text == text) return true; return false; }
        public StoresWord Word(string text) { foreach (StoresWord w in Words) if (w.Text == text) return w; Check(false, "the stores board says [" + text + "]"); return default; }
    }

    private static Rect StoresRect(object boardRect) => new Rect((float)Field(boardRect, "X"), (float)Field(boardRect, "Y"), (float)Field(boardRect, "W"), (float)Field(boardRect, "H"));

    private static bool StoresInside(Rect inner, Rect outer, float slack) =>
        inner.xMin >= outer.xMin - slack && inner.xMax <= outer.xMax + slack && inner.yMin >= outer.yMin - slack && inner.yMax <= outer.yMax + slack;

    private static StoresView StoresViewOf(object status)
    {
        var v = new StoresView { Status = status, Unit = (float)Field(status, "unit") };
        object options = Field(status, "Options"), header = Field(status, "header");
        v.Stack = Field(options, "Layout").ToString() == "Stack";
        bool Has(string property) => (bool)header.GetType().GetProperty(property).GetValue(header);
        object blocks = Boxed("BoardBlocks");
        Set(blocks, "Layout", Field(options, "Layout")); Set(blocks, "Hero", Has("HasWeapon")); Set(blocks, "Pwr", Has("HasCap")); Set(blocks, "PwrCost", Has("HasCost")); Set(blocks, "Exp", Has("HasCm"));
        Set(blocks, "NoShelves", Field(options, "Declutter").ToString() != "Full");
        object geo = Type(HudDomain + "ScopeLayout").GetMethod("Compute", All).Invoke(null, new[] { blocks });
        v.Hero = StoresRect(Field(geo, "Hero")); v.Pwr = StoresRect(Field(geo, "Pwr")); v.Exp = StoresRect(Field(geo, "Exp"));

        var root = (RectTransform)Field(status, "root");
        var stores = (RectTransform)Field(status, "storesRect");
        Vector2 Board(Vector3 world)
        {
            Vector3 local = root.InverseTransformPoint(world);
            return new Vector2((local.x - root.rect.xMin) / v.Unit, (root.rect.yMax - local.y) / v.Unit);
        }
        foreach (TextMeshProUGUI label in stores.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (string.IsNullOrEmpty(label.text)) continue;
            label.ForceMeshUpdate();
            TMP_TextInfo info = label.textInfo;
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo c = info.characterInfo[i];
                if (!c.isVisible) continue;
                foreach (Vector3 p in new[] { c.bottomLeft, c.topRight })
                {
                    Vector2 b = Board(label.transform.TransformPoint(p));
                    lo = Vector2.Min(lo, b); hi = Vector2.Max(hi, b);
                }
            }
            if (lo.x > hi.x) continue;
            v.Words.Add(new StoresWord { Text = label.text, Ink = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y), Label = label });
        }
        var corners = new Vector3[4];
        foreach (RawImage image in stores.GetComponentsInChildren<RawImage>(true))
        {
            if (!image.gameObject.activeSelf) continue;
            int slot = int.Parse(image.name.Substring(3));
            image.rectTransform.GetWorldCorners(corners);
            Vector2 a = Board(corners[1]), b = Board(corners[3]);
            v.Art[slot] = image;
            v.ArtBox[slot] = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
        return v;
    }

    /// <summary>What every stores case must keep true: each word sits inside its block, none is narrower than its content, no two words overlap, and every picture is on the game's material, never the default, inside its block.</summary>
    private static void StoresChecks(StoresView v, string tag)
    {
        var blocks = new List<Rect>();
        if (v.Hero.width > 0f) blocks.Add(v.Hero);
        if (v.Pwr.width > 0f) blocks.Add(v.Pwr);
        if (v.Exp.width > 0f) blocks.Add(v.Exp);
        foreach (StoresWord w in v.Words)
        {
            bool inside = false;
            foreach (Rect block in blocks) inside |= StoresInside(w.Ink, block, 2f);
            Check(inside, tag + ": [" + w.Text + "] " + w.Ink + " sits inside its block");
            Check(w.Label.textBounds.size.x <= w.Label.rectTransform.rect.width + 0.5f, tag + ": [" + w.Text + "] is not narrower than its content: " + w.Label.textBounds.size.x + " > " + w.Label.rectTransform.rect.width);
        }
        for (int i = 0; i < v.Words.Count; i++)
            for (int j = i + 1; j < v.Words.Count; j++)
            {
                Rect a = v.Words[i].Ink, b = v.Words[j].Ink;
                float iw = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), ih = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                bool overlap = iw > 1f && ih > 2f && iw * ih > 0.12f * Mathf.Min(a.width * a.height, b.width * b.height);
                Check(!overlap, tag + ": [" + v.Words[i].Text + "] and [" + v.Words[j].Text + "] overlap");
            }
        object material = Field(StoreIconsType(), "Material");
        for (int i = 0; i < v.Art.Length; i++)
        {
            if (v.Art[i] == null) continue;
            Check(material != null && v.Art[i].material == (Material)material && v.Art[i].material != Canvas.GetDefaultCanvasMaterial(), tag + ": picture " + i + " is drawn on the game's material, never the default");
            Check(StoresInside(v.ArtBox[i], i == 2 ? v.Exp : v.Hero, 1.5f), tag + ": picture " + i + " " + v.ArtBox[i] + " sits inside " + (i == 2 ? "EXPENDABLES " + v.Exp : "HERO " + v.Hero));
        }
    }

    private static readonly Color StoresInk = new Color32(56, 222, 82, 255), StoresAmber = new Color32(243, 217, 74, 255);
    private static Color StoresBright => Color.Lerp(StoresInk, Color.white, 0.58f);

    private static bool StoresTint(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

    /// <summary>The picture of a vanilla sprite fills its box (the box's width or its height) and shows exactly the part of the sprite the table says its ink lives in.</summary>
    private static void StoresArtFills(StoresView v, int slot, string sprite, Rect box, string tag)
    {
        RawImage image = v.Art[slot];
        Check(image != null, tag + ": picture " + slot + " is drawn");
        Rect at = v.ArtBox[slot];
        Check(Mathf.Abs(at.width - box.width) < 1.5f || Mathf.Abs(at.height - box.height) < 1.5f, tag + ": picture " + slot + " fills its box (" + at.size + " in " + box.size + ")");
        Check(Mathf.Abs(at.center.x - box.center.x) < 1.5f && Mathf.Abs(at.center.y - box.center.y) < 1.5f, tag + ": picture " + slot + " is centred in its box");
        Check(Mathf.Abs(at.width / at.height - StoresInkAspect(sprite)) < 0.1f * StoresInkAspect(sprite), tag + ": picture " + slot + " keeps its aspect: " + at.width / at.height);
        Vector4 ink = StoresInkBox(sprite);
        Rect uv = image.uvRect;
        Check(Mathf.Abs(uv.xMin - ink.x) < 0.002f && Mathf.Abs(uv.xMax - ink.z) < 0.002f && Mathf.Abs(uv.yMin - (1f - ink.w)) < 0.002f && Mathf.Abs(uv.yMax - (1f - ink.y)) < 0.002f,
            tag + ": picture " + slot + " shows the ink's part of " + sprite + ": " + uv + " for " + ink);
    }

    private static float StoresInkAspect(string sprite)
    {
        Vector4 ink = StoresInkBox(sprite);
        return (ink.z - ink.x) * (sprite == "weaponicon_radarJammer" ? 256f : 512f) / ((ink.w - ink.y) * 256f);
    }

    /// <summary>One block case: the board built the way production builds it, under <paramref name="options"/>, handed the stores feed <paramref name="tweak"/> makes (and, for a case about a moment after a shot, a second tick <paramref name="next"/>), checked, then captured.</summary>
    private static void StoresBlockShot(string name, Vector2 size, object options, Action<StoresSpec> tweak, Action<StoresView, string> verify, Action<StoresSpec> next = null)
    {
        MakeBoard(size, 2f, out Canvas canvas, out object status, out object frame);
        Set(status, "Options", options ?? PilotOptions());
        var spec = new StoresSpec();
        tweak(spec);
        var scene = new ScopeScene(2f, size, frame);
        FixtureSystems(2f, scene);
        StoresArt(spec.Pictures);
        scene.Publish(BuildStores(scene, spec));
        Set(frame, "Now", 2f);
        Call(status, "Present", frame);
        if (next != null)
        {
            next(spec);
            scene.Publish(BuildStores(scene, spec));
            Set(frame, "Now", 2.4f);
            Call(status, "Present", frame);
        }
        Canvas.ForceUpdateCanvases();
        string tag = "stores-" + name + "-" + (int)size.x + "x" + (int)size.y;
        ScopeChecks(status, tag);
        StoresView view = StoresViewOf(status);
        StoresChecks(view, tag);
        verify(view, tag);
        Capture(canvas, tag + ".png");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }

    private static void StoresSays(StoresView v, string tag, params string[] words)
    {
        foreach (string w in words) Check(v.Says(w), tag + ": the board says [" + w + "]");
    }

    private static void StoresSaysNot(StoresView v, string tag, params string[] words)
    {
        foreach (string w in words) Check(!v.Says(w), tag + ": the board does not say [" + w + "]");
    }

    private static void StoresCruise(StoresSpec x) { x.Safe = true; x.Kj = 43f; x.Rate = 5f; x.Ir = 72; x.IrMax = 72; }

    private static void StoresArmed(StoresSpec x) { x.Sel = 2; x.Rounds[1] = 0; x.Rounds[5] = 0; x.Kj = 500f; }

    /// <summary>
    /// The three blocks of the stores side (HERO, PWR BUS, EXPENDABLES) as the board draws them over the cruise strip: a gun selected and SAFE, a guided store selected and armed, a store with no picture, a store whose picture
    /// the game gave an unknown name, no pictures at all, an empty dispenser, a dispense call, no capacitor, no countermeasures, a cost row, a shot, a crowded rail and LEAN; the first two also in STACK and at 1280 x 720.
    /// Renders stores-&lt;case&gt;-&lt;W&gt;x&lt;H&gt;.png (and stores-&lt;variant&gt;-t6-1920x1080.png of the older variants).
    /// </summary>
    private static void ScopeStoresBlocks()
    {
        var hd = new Vector2(1920, 1080);
        object stack = PilotOptions("Layout", LayoutOf("Stack"));
        foreach (var at in new[] { (suffix: "", size: hd, options: (object)null), (suffix: "-stack", size: hd, options: stack), (suffix: "-720", size: new Vector2(1280, 720), options: (object)null) })
        {
            bool isStack = at.options != null;
            // The cruise of the prototype: the gun SAFE, 500 of 500, the capacitor charging at 43 kJ, 72 flares and 30 chaff, the jammer ready.
            StoresBlockShot("cruise" + at.suffix, at.size, at.options, StoresCruise, (v, tag) =>
            {
                StoresSays(v, tag, "STORES MGMT", "HMD SYM  ", "DCL 0", "500", "/500", "SAFE", "LINK", "20MM ROTARY CANNON", "GUN", "PWR BUS", "43", "KJ", "CHG +5/S", "EXPENDABLES", "72", "FLR", "30", "CHF", "JAM", "RDY", "G", "1", "2", "3", "4", "5", "6", "7");
                StoresSaysNot(v, tag, "TRT AUTO", "ARMED", "EMPTY");
                Rect art = isStack ? new Rect(v.Hero.x + 240f, v.Hero.y + 18f, 200f, 56f) : new Rect(v.Hero.x, v.Hero.y + 76f, 272f, 56f);
                StoresArtFills(v, 0, "weaponIcon_gatlingGun", art, tag);
                Check(v.Art[1] == null, tag + ": a full magazine has no spent share");
                Check(StoresTint(v.Art[0].color, StoresInk) && v.Art[0].color.a > 0.99f, tag + ": SAFE tints the art INK");
                StoresArtFills(v, 2, "weaponicon_radarJammer", new Rect(v.Exp.x, v.Exp.y + 66f - 7f, 14f, 14f), tag);
                Check(Mathf.Abs(v.Word("43").Label.color.r - StoresAmber.r) < 0.01f, tag + ": a charge at 8 % is amber");
                Check(Mathf.Abs(v.Exp.y - (isStack ? 124f : 70f)) < 0.01f, tag + ": EXPENDABLES sits where the geometry has it (WIDE y 70 with no cost row)");
                if (!isStack) Check(Mathf.Abs(v.Word("EXPENDABLES").Ink.center.y - (v.Exp.y + 6f)) < 2f, tag + ": EXPENDABLES' shelf is 6 under its top");
            });
            // A guided store selected and armed, one AAM-4 of two left, the capacitor full.
            StoresBlockShot("armed" + at.suffix, at.size, at.options, StoresArmed, (v, tag) =>
            {
                StoresSays(v, tag, "1", "/2", "ARMED", "AAM-4 SKYWAY", "STA 2 AAM", "500", "FULL", "48");
                StoresSaysNot(v, tag, "SAFE", "LINK", "GUN");
                Rect art = isStack ? new Rect(v.Hero.x + 240f, v.Hero.y + 18f, 200f, 56f) : new Rect(v.Hero.x, v.Hero.y + 76f, 272f, 56f);
                StoresArtFills(v, 0, "weaponIcon_AAM4", art, tag);
                Check(v.Art[1] == null, tag + ": a missile pooled over two pylons is one picture, not a magazine");
                Check(StoresTint(v.Art[0].color, StoresBright), tag + ": ARMED tints the art BRIGHT");
            });
        }

        // A store the game has no picture of (the MALD-X kit): the class glyph in the art box, no image for it.
        StoresBlockShot("no-sprite", hd, null, x => { x.Sel = 6; }, (v, tag) =>
        {
            StoresSays(v, tag, "MALD-X", "STA 6 KIT", "ARMED");
            Check(v.Art[0] == null && v.Art[1] == null, tag + ": the fallback path draws no image for the store");
            Check(v.Art[2] != null, tag + ": the jammer still has its picture");
        });
        // No pictures at all (no material to draw them with): glyphs only, not a single image, and the board still reads.
        StoresBlockShot("no-pictures", hd, null, x => { x.Pictures = false; }, (v, tag) =>
        {
            StoresSays(v, tag, "500", "ARMED", "20MM ROTARY CANNON", "JAM", "RDY");
            Check(v.Art[0] == null && v.Art[1] == null && v.Art[2] == null, tag + ": the fallback path draws no image at all");
        });
        // A material but a store whose sprite the table does not know (a modded weapon's): its whole sprite, a magazine half spent, split at the share left.
        StoresBlockShot("unknown-sprite", hd, null, x =>
        {
            x.Load = new[] { new LoadStation("Gun", "20MM ROTARY CANNON", 500), new LoadStation("Pod", "HYDRA-70 POD", 38) };
            x.Sel = 1; x.Rounds[1] = 19;
        }, (v, tag) =>
        {
            StoresSays(v, tag, "19", "/38", "ARMED", "HYDRA-70 POD", "STA 1 POD");
            Check(v.Art[0] != null && v.Art[1] != null, tag + ": a spent magazine is two pictures, the share left and the share spent");
            Rect lit = v.ArtBox[0], spent = v.ArtBox[1];
            Check(Mathf.Abs(lit.width - spent.width) < 1.5f && Mathf.Abs(lit.xMax - spent.xMin) < 1.5f, tag + ": split at half of the picture: " + lit + " | " + spent);
            Check(Mathf.Abs(lit.height - 56f) < 1.5f && Mathf.Abs(lit.width + spent.width - 112f) < 2f, tag + ": a whole 2:1 sprite fills the box's height: " + lit + " " + spent);
            Check(Mathf.Abs(v.Art[0].uvRect.xMax - v.Art[1].uvRect.xMin) < 0.001f && Mathf.Abs(v.Art[0].uvRect.xMin) < 0.001f && Mathf.Abs(v.Art[1].uvRect.xMax - 1f) < 0.001f, tag + ": whole sprite, cut where the share ends");
            Check(StoresTint(v.Art[0].color, StoresBright) && StoresTint(v.Art[1].color, StoresInk) && Mathf.Abs(v.Art[1].color.a - 0.4f) < 0.01f, tag + ": the share left BRIGHT, the spent share dim INK");
        });
        // An empty dispenser says EMPTY instead of its matrix, and the flares running low say LOW.
        StoresBlockShot("empty-dispenser", hd, null, x => { x.Rdr = 0; x.Ir = 9; }, (v, tag) =>
        {
            StoresSays(v, tag, "EMPTY", "0", "9", "LOW", "FLR", "CHF");
            Check(v.Word("EMPTY").Label.color.r > 0.95f && v.Word("EMPTY").Label.color.g < 0.6f, tag + ": EMPTY is red");
            Check(Mathf.Abs(v.Word("EMPTY").Ink.xMin - (v.Exp.x + 68f)) < 1.5f, tag + ": EMPTY stands where the matrix would");
        });
        // The THREAT page calls for chaff: DISPENSE, the call brackets, the capacitor draining, the jammer on.
        StoresBlockShot("dispense", hd, null, x => { x.Call = 2; x.Active = 1; x.Trt = true; x.Kj = 431f; x.Rate = -14f; x.JamOn = true; x.JamCharge = 0.7f; }, (v, tag) =>
        {
            StoresSays(v, tag, "DISPENSE", "LINK", "TRT AUTO", "DRN -14/S", "ON", "431");
            Check(v.Word("DISPENSE").Ink.xMax <= v.Exp.xMax + 0.5f && v.Word("DISPENSE").Ink.xMin > v.Exp.x + 68f + 40f + 7f, tag + ": DISPENSE sits clear of the matrix, inside the block");
        });
        // An airframe with no capacitor has no PWR BUS, and EXPENDABLES moves up into its place.
        StoresBlockShot("no-capacitor", hd, null, x => { x.Cap = false; }, (v, tag) =>
        {
            StoresSays(v, tag, "EXPENDABLES", "500");
            StoresSaysNot(v, tag, "PWR BUS", "KJ");
            Check(v.Pwr.width == 0f && Mathf.Abs(v.Exp.y - 6f) < 0.01f, tag + ": EXPENDABLES moved up to y 6");
            Check(Mathf.Abs(v.Word("EXPENDABLES").Ink.center.y - 12f) < 2f, tag + ": ...and its shelf with it");
        });
        // A header with no countermeasures has no EXPENDABLES.
        StoresBlockShot("no-countermeasures", hd, null, x => { x.HasIr = x.HasRdr = x.HasJam = false; }, (v, tag) =>
        {
            StoresSays(v, tag, "PWR BUS", "500");
            StoresSaysNot(v, tag, "EXPENDABLES", "FLR", "CHF", "JAM");
            Check(v.Exp.width == 0f && v.Art[2] == null, tag + ": no EXPENDABLES block, no jammer picture");
        });
        // An energy weapon fitted: PWR BUS grows its cost row (the tick on the cells, the word under them) and EXPENDABLES moves 18 down.
        StoresBlockShot("cost-row", hd, null, x =>
        {
            x.Load = new[] { new LoadStation("Gun", "20MM ROTARY CANNON", 500), new LoadStation("Rail", "RG-12 LANCE RAILGUN", 1) };
            x.Sel = 1; x.Kj = 400f; x.CostKj = 300f;
        }, (v, tag) =>
        {
            StoresSays(v, tag, "LANCE 300", "RG-12 LANCE RAILGUN", "STA 1 RAIL", "400");
            Check(Mathf.Abs(v.Pwr.height - 72f) < 0.01f && Mathf.Abs(v.Exp.y - 88f) < 0.01f, tag + ": the cost row makes PWR BUS 72 high and EXPENDABLES 88");
            Check(Mathf.Abs(v.Word("LANCE 300").Ink.center.y - (v.Pwr.y + 66f)) < 2f, tag + ": the cost word is at +66");
            Check(v.Art[0] == null, tag + ": a rail has no picture in the fixtures: its glyph");
        });
        // A shot: the charge falls 300 in one tick, SHOT -300 and the cells it emptied still lit BRIGHT for a moment.
        StoresBlockShot("shot", hd, null, x => { x.Kj = 500f; }, (v, tag) =>
        {
            StoresSays(v, tag, "SHOT -300", "200");
        }, x => { x.Kj = 200f; });
        // A crowded rail (twelve stations) and a magazine of four digits: the numeral shrinks to keep its load clear of the arm column and the rail keeps every tag inside its marker.
        StoresBlockShot("crowded", hd, null, x =>
        {
            var load = new List<LoadStation> { new LoadStation("Gun", "20MM ROTARY CANNON", 1500) };
            for (int i = 0; i < 11; i++) load.Add(new LoadStation("Aam", "AAM-4 SKYWAY", 1));
            x.Load = load.ToArray();
        }, (v, tag) =>
        {
            StoresSays(v, tag, "1500", "/1500", "G", "1", "11", "ARMED");
            Check(v.Word("/1500").Ink.xMax <= v.Hero.x + 156f, tag + ": the load ends before the arm column: " + v.Word("/1500").Ink.xMax);
            Check(v.Word("1500").Ink.height < 35f, tag + ": the numeral shrank");
        });
        // LEAN: no captions, no shelves, DCL 1 where the arm word's row has room, and EXPENDABLES pulled up under the cells.
        StoresBlockShot("lean", hd, PilotOptions("Declutter", Declutter("Lean")), StoresCruise, (v, tag) =>
        {
            StoresSays(v, tag, "500", "/500", "SAFE", "LINK", "DCL 1", "43", "72", "FLR", "30", "CHF", "JAM", "RDY");
            StoresSaysNot(v, tag, "STORES MGMT", "HMD SYM  ", "PWR BUS", "EXPENDABLES", "DCL 0");
            Check(Mathf.Abs(v.Exp.y - 70f) < 0.01f && Mathf.Abs(v.Exp.height - 53f) < 0.01f, tag + ": EXPENDABLES is 19 shorter with no shelf row: " + v.Exp);
            Check(Mathf.Abs(v.Word("72").Ink.center.y - (v.Exp.y + 6f)) < 2.5f, tag + ": its first row starts at the top of the block, " + v.Word("72").Ink.center.y);
            StoresWord dcl = v.Word("DCL 1");
            Check(Mathf.Abs(dcl.Ink.xMax - (v.Hero.x + 266f)) < 1.5f && Mathf.Abs(dcl.Ink.center.y - (v.Hero.y + 23f)) < 2.5f, tag + ": the flag takes the arm word's row, right edge at 266: " + dcl.Ink);
        });
    }
}
#endif
