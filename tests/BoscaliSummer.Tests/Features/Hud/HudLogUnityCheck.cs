#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Production presenters, synthetic native dock/font, telemetry and message traffic.
// No native asset, live input or game-state adapter claim.
public static class HudLogUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly Mod => typeof(AvConsole).Assembly;
    private static int captures, assertions;
    private static Type Type(string name) => Mod.GetType(name, true);
    private static FieldInfo Member(object owner, string name)
    {
        for (Type t = owner as Type ?? owner.GetType(); t != null; t = t.BaseType) {
            FieldInfo field = t.GetField(name, All | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException((owner as Type ?? owner.GetType()).FullName, name);
    }
    private static object Field(object owner, string name) => Member(owner, name).GetValue(owner is Type ? null : owner);
    private static void Set(object owner, string name, object value) => Member(owner, name).SetValue(owner is Type ? null : owner, value);
    private static object Call(object owner, string name, params object[] args)
    {
        Type t = owner as Type ?? owner.GetType();
        foreach (MethodInfo m in t.GetMethods(All))
            if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(owner is Type ? null : owner, args);
        throw new MissingMethodException(t.FullName, name);
    }
    public static void Run()
    {
        try {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("HudLogCheck.exe");
            AvBundle.Load(Debug.Log); Check(AvBundle.Available && AvIcons.Available, "Production UI assets loaded.");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            new GameObject("Events", typeof(EventSystem));
            var colors = ScriptableObject.CreateInstance<ColorTheme>();
            Set(colors, "allClear", new Color(.65f, 1f, .87f)); Set(colors, "warning", new Color(1f, .75f, .3f));
            Set(colors, "alert", new Color(1f, .3f, .25f));
            var theme = ScriptableObject.CreateInstance<ThemeGroup>(); theme.SetColorTheme(colors);
            typeof(ThemeManager).GetProperty("Active", All).SetValue(null, theme);
            foreach (Vector2 size in BoardSizes) {
                foreach (string state in new[] { "calm", "hot", "notice", "bingo", "boot-a", "boot-b", "boot-c", "minimal", "long-a", "long-b", "long-c", "full" }) {
                    Board(size, state, false);
                    if (Array.IndexOf(new[] { "hot", "notice", "bingo", "boot-b", "full" }, state) >= 0) Board(size, state, true);   // the dark half of the flash beat
                }
                MissileCam(size);
            }
            foreach (Vector2 size in new[] { new Vector2(1280,720), new Vector2(1920,1080), new Vector2(2560,1080) }) Cluster(size);
            Contracts();
            foreach (float height in new[] { 160f, 420f, 596f })
                foreach (bool merged in new[] { false, true }) Log(height, merged);
            Wire();
            File.WriteAllText("result.txt", "PASS: " + captures + " production HUD/status/log/wire renders, " + assertions +
                " input-transparency/layout/lifecycle assertions. Synthetic native dock/font and telemetry; no native asset, live input or game adapter acceptance.");
            EditorApplication.Exit(0);
        } catch (Exception e) { File.WriteAllText("result.txt", "FAIL: " + e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static Canvas MakeCanvas(string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace;
        ((RectTransform)go.transform).sizeDelta = size; return c;
    }
    private static CombatHUD NativeDock(Canvas canvas)
    {
        // Imported Assembly-CSharp MonoBehaviours cannot be added by the editor.
        // Builders only read the HUD's font metadata; a managed shell leaves that null,
        // then the owned labels receive an explicit synthetic font below.
        return (CombatHUD)FormatterServices.GetUninitializedObject(typeof(CombatHUD));
    }
    private static void SyntheticFont(RectTransform root)
    {
        var font = HudFont();   // the HUD's face is mono: proportional type hides collisions
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
            label.font = font; label.fontSharedMaterial = font.material;
        }
    }
    private const string HudDomain = "BoscaliSummer.Modules.Hud.Domain.";
    private static readonly Vector2[] BoardSizes = { new Vector2(1280, 720), new Vector2(1920, 1080) };
    private static object Boxed(string name) => Activator.CreateInstance(Type(HudDomain + name));
    private static object EnumOf(string name, string value) => Enum.Parse(Type(HudDomain + name), value);
    private static object Tone(string tone) => Enum.Parse(Type("BoscaliSummer.Core.Contracts.HudTone"), tone);
    private static object Msg(string channel, string text, string detail, string tone, float bar)
    {
        object m = Boxed("HudMessage");
        Set(m, "Channel", channel); Set(m, "Text", text); Set(m, "Detail", detail); Set(m, "Bar", bar); Set(m, "Tone", Tone(tone));
        return m;
    }
    private static Array Rows(params object[] messages)
    {
        Array rows = Array.CreateInstance(Type(HudDomain + "HudMessage"), messages.Length);
        for (int i = 0; i < messages.Length; i++) rows.SetValue(messages[i], i);
        return rows;
    }
    /// <summary>A hazy sky behind the HUD (near white when asked): thin ink on a bright backdrop is the case the scrims and tones are for.</summary>
    private static void Sky(Canvas canvas, bool white = false)
    {
        var texture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) texture.SetPixel(0, y, white ? Color.Lerp(new Color(.94f, .95f, .96f), new Color(.86f, .88f, .91f), y / 63f)
            : Color.Lerp(new Color(.82f, .89f, .95f), new Color(.40f, .58f, .82f), y / 63f));
        texture.Apply();
        var sky = new GameObject("Sky", typeof(RectTransform), typeof(Image));
        sky.transform.SetParent(canvas.transform, false); sky.transform.SetAsFirstSibling();
        var rect = (RectTransform)sky.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = sky.GetComponent<Image>(); image.raycastTarget = false; image.sprite = Sprite.Create(texture, new Rect(0, 0, 1, 64), new Vector2(.5f, .5f));
    }
    private static TMP_FontAsset HudFont() => AvBundle.Font("NOA JetBrains Mono Regular SDF");
    /// <summary>A stand-in for one of the native weapon labels: Build reads only its font and its size.</summary>
    private static TextMeshProUGUI NativeLabel(Transform parent, string name, TMP_FontAsset font, float size)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false); label.font = font; label.fontSharedMaterial = font.material; label.fontSize = size; label.text = "X";
        return label;
    }
    /// <summary>
    /// Whether a word fits its box once its allowed shrink is used. TMP reports an autosized label's preferred width at its largest size,
    /// and <c>isTextOverflowing</c> is false for Overflow-mode and autosized text, so neither says whether a word would be cut.
    /// </summary>
    private static bool Fits(TMP_Text t, float tolerance)
    {
        float width = t.preferredWidth, smallest = t.enableAutoSizing && t.fontSizeMax > 0f ? width * t.fontSizeMin / t.fontSizeMax : width;
        return smallest <= t.rectTransform.rect.width * tolerance;
    }
    private static Transform Child(object status, string name) => ((RectTransform)Field(status, "root")).Find(name);
    private static string TextOf(object status, string name) => Child(status, name).GetComponent<TMP_Text>().text;
    private static object ThreatOf(int missiles, float sinceLock, float sinceSpike, bool bearing, float bearingDeg, float elevation, bool bearingIsMissile)
    {
        object threat = Type(HudDomain + "ThreatPicture").GetProperty("Clear").GetValue(null);
        Set(threat, "Missiles", missiles); Set(threat, "SinceLock", sinceLock); Set(threat, "SinceSpike", sinceSpike);
        Set(threat, "HasBearing", bearing); Set(threat, "BearingDeg", bearingDeg); Set(threat, "ElevationDeg", elevation); Set(threat, "BearingIsMissile", bearingIsMissile);
        return threat;
    }
    private static void Contact(object frame, int slot, string kind, string name, string seeker, bool locked, float range, float closure, float tti, float age)
    {
        object c = Boxed("ThreatContact");
        Set(c, "Kind", EnumOf("ThreatKind", kind)); Set(c, "Name", name); Set(c, "Seeker", seeker); Set(c, "Locked", locked);
        Set(c, "RangeM", range); Set(c, "ClosureMps", closure); Set(c, "TtiS", tti); Set(c, "AgeS", age);
        ((Array)Field(frame, "Contacts")).SetValue(c, slot);
    }
    private static void CmStation(object frame, int slot, string tag, int ammo, int max, bool chargeable, int mask)
    {
        object s = Boxed("CmStation");
        Set(s, "Tag", tag); Set(s, "Ammo", ammo); Set(s, "Max", max); Set(s, "Chargeable", chargeable); Set(s, "Mask", (byte)mask);
        ((Array)Field(frame, "Cm")).SetValue(s, slot);
    }
    private static void FuelOf(object frame, float level, float enduranceS, bool bingo, bool joker, string tone)
    {
        object fuel = Boxed("FuelState");
        Set(fuel, "Valid", true); Set(fuel, "Level", level); Set(fuel, "EnduranceS", enduranceS); Set(fuel, "Bingo", bingo); Set(fuel, "Joker", joker);
        Set(fuel, "Field", "HighwayStrip2"); Set(fuel, "FieldM", 23000f); Set(fuel, "Tone", Tone(tone));
        Set(frame, "Fuel", fuel);
    }
    private static void TickerOf(object frame, string tone, string channel, string text, string detail, int count, float age)
    {
        object tick = Boxed("TickerLine");
        Set(tick, "Tone", Tone(tone)); Set(tick, "Channel", channel); Set(tick, "Text", text); Set(tick, "Detail", detail); Set(tick, "Count", count); Set(tick, "Age", age);
        Set(frame, "Ticker", tick); Set(frame, "HasTicker", true);
    }
    private static void DamageOf(object frame, string flags, int engines, int out_, float detached)
    {
        object damage = Boxed("DamageState");
        Set(damage, "Flags", Enum.Parse(Type(HudDomain + "DamageFlags"), flags)); Set(damage, "Engines", engines); Set(damage, "EnginesOut", out_); Set(damage, "DetachedRatio", detached);
        Set(frame, "Damage", damage);
    }
    private static object SystemsOf(bool radar, bool safe, bool minimal = false)
    {
        object systems = Boxed("SystemStates");
        Set(systems, "Gear", EnumOf("GearLamp", "Down")); Set(systems, "EngineRunning", true);
        if (minimal) return systems;   // an airframe with nothing else to annunciate
        Set(systems, "FlightAssist", true); Set(systems, "AutoHover", false);
        Set(systems, "RadarEmitting", radar); Set(systems, "NightVision", false); Set(systems, "WeaponSafe", safe);
        return systems;
    }
    /// <summary>
    /// The status board in one named state, at one resolution, on a hazy sky, built the way production builds it: from stand-in native
    /// labels (ammo about 40, name about 28 at 1080p, read off the in-game crop) that go through the same Share() derivation, in the mono
    /// face the HUD uses. <paramref name="dark"/> pins the clock on the dark half of the flash beat.
    /// calm: idle dial, CM tiles at full load, fuel with endurance, cells with a cue strip. hot: an inbound radar missile with time to
    /// impact, a lock, shot pips, a LOW flare station and the chaff highlighted. notice: a x3 warning on the ticker, JOKER, a search
    /// spike, damage cautions. bingo: BINGO, FIRE and a dead engine, empty flares, a caution notice. boot-a/b/c: the lamp test sweep,
    /// its blink, then SYSTEMS ONLINE. minimal: an airframe with two lamps and a full pip row. long-a/b/c: a 90-character notice on
    /// its first page, its second (a warning) and its detail. full: every row at once with four cells.
    /// </summary>
    private static void Board(Vector2 size, string state, bool dark)
    {
        Canvas canvas = MakeCanvas("Board-" + state, size);
        Sky(canvas);
        float k = size.y / 1080f;
        var dock = new GameObject("WeaponsDock", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        dock.SetParent(canvas.transform, false); dock.sizeDelta = new Vector2(452f * k, 190f * k);
        dock.anchoredPosition = new Vector2(size.x / 2f - 30f * k - dock.sizeDelta.x / 2f, size.y / 2f - 20f * k - dock.sizeDelta.y / 2f);
        dock.GetComponent<Image>().color = new Color(.02f, .04f, .06f, .9f); dock.GetComponent<Image>().raycastTarget = false;
        // The stand-in native pieces sit beside the dock (not in it: the board measures the dock's own visuals) and are only read by Build.
        TMP_FontAsset font = HudFont();
        var cell = new GameObject("NativeCell", typeof(RectTransform)).GetComponent<RectTransform>();
        cell.SetParent(canvas.transform, false); cell.sizeDelta = new Vector2(95f * k, 95f * 1.04f * k);
        TextMeshProUGUI nameLabel = NativeLabel(canvas.transform, "NativeName", font, 28f * k), ammoLabel = NativeLabel(canvas.transform, "NativeAmmo", font, 40f * k);
        object status = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.StatusPanel"), true);
        Call(status, "Build", dock, nameLabel, ammoLabel, cell, null);
        cell.gameObject.SetActive(false); nameLabel.gameObject.SetActive(false); ammoLabel.gameObject.SetActive(false);

        const string LongText = "SESSION HOST: THE MISSION WILL RESTART IN FIVE MINUTES FOR A SCHEDULED MAINTENANCE WINDOW";
        const string LongDetail = "AREA 4 EAST · RETURN TO BASE OR AIRBORNE REFUEL BEFORE THE RESTART";
        object frame = Boxed("BoardFrame");
        Set(frame, "BootAge", state == "boot-a" ? .4f : state == "boot-b" ? 1.2f : state == "boot-c" ? 2f : -1f);
        Set(frame, "Now", dark ? .25f : 0f);   // 0: the lit half of the flash beat; .25: the dark half
        object ledger = Boxed("ShotLedger");
        Array rows;
        switch (state) {
        case "hot":
            Set(frame, "Systems", SystemsOf(true, true)); Set(frame, "Threat", ThreatOf(2, 0.4f, 0.2f, true, 65f, 20f, true));
            Contact(frame, 0, "Missile", null, "SARH", true, 2400f, 340f, 7.1f, 1.5f); Contact(frame, 1, "Lock", "SA-19", null, false, 9400f, float.NaN, float.NaN, 10f);
            Set(frame, "ContactCount", 2); Set(frame, "InboundMask", (byte)2);
            CmStation(frame, 0, "IR", 6, 72, false, 1); CmStation(frame, 1, "RDR", 30, 30, false, 2); CmStation(frame, 2, "JAM", 1, 1, true, 2); Set(frame, "CmCount", 3);
            FuelOf(frame, .55f, 3300f, false, false, "Info");
            Call(ledger, "Launch", 1, false, 0f); Call(ledger, "Launch", 2, true, 0f); Call(ledger, "Launch", 3, false, 0f);
            Call(ledger, "Detonate", 1, true, 1f); Call(ledger, "Launch", 4, true, 1f); Call(ledger, "Detonate", 3, false, 1.2f);
            rows = Rows(Msg("missile", "MSL 800m · TGT 2.1km", "AIM-120 → MIG-29", "Info", .28f), Msg("support", "SUPPORT · STRIKE", "ARTILLERY · 6 ROUNDS · TOT 00:40", "Caution", .5f),
                Msg("contracts", "OPS · DEPOT", "DESTROY THE DEPOT · 4.2KM", "Info", .3f));
            break;
        case "notice":
            Set(frame, "Systems", SystemsOf(false, false)); Set(frame, "Threat", ThreatOf(0, 99f, 0.3f, true, 200f, -5f, false));
            Contact(frame, 0, "Spike", "SA-19", null, false, 22000f, float.NaN, float.NaN, 20f); Set(frame, "ContactCount", 1);
            CmStation(frame, 0, "IR", 30, 72, false, 1); CmStation(frame, 1, "RDR", 30, 30, false, 2); Set(frame, "CmCount", 2);
            DamageOf(frame, "EngineDamaged, FuelLeak", 2, 0, .14f);
            FuelOf(frame, .38f, 2520f, false, true, "Caution");
            TickerOf(frame, "Warning", "events", "SUPEREVENT · NIGHTFALL 2 INBOUND", "ALL THEATER · SEEK COVER", 3, 0.5f);
            rows = Rows(Msg("skywell", "SKYWELL  TANKER  780m", "DOWN 12m  ·  LEFT 14m  ·  FWD 40m", "Info", .6f), Msg("ils", "ILS", "LOCALIZER LEFT 2 · GLIDESLOPE HIGH", "Caution", .7f),
                Msg("lance", "LANCE", "RG-12 CHARGE", "Info", .8f));
            break;
        case "bingo":
            Set(frame, "Systems", SystemsOf(true, false)); Set(frame, "Threat", ThreatOf(1, 99f, 99f, true, 300f, -20f, true));
            Contact(frame, 0, "Missile", null, "IR", false, 900f, 310f, 2.9f, 0.8f); Set(frame, "ContactCount", 1); Set(frame, "InboundMask", (byte)1);
            CmStation(frame, 0, "IR", 0, 72, false, 1); CmStation(frame, 1, "RDR", 4, 30, false, 2); CmStation(frame, 2, "JAM", 1, 1, true, 2); Set(frame, "CmCount", 3);
            DamageOf(frame, "FuelLeak, Fire", 2, 1, .30f);
            FuelOf(frame, .09f, 240f, true, true, "Warning");
            TickerOf(frame, "Caution", "weather", "ENV: ICING CONDITIONS", "FREEZING LEVEL 1,500 M · CLIMB OUT OF CLOUD", 1, 1.5f);
            rows = Rows(Msg("lance", "LANCE", "RG-12 CHARGE", "Warning", .9f), Msg("missile", "MSL 400m · NO TGT", "AIM-9 → NO TARGET", "Caution", 0f));
            break;
        case "minimal":
            // Two lamps only (gear and engine) and a full pip row: the threat row must not depend on the lamp count (Q8).
            Set(frame, "Systems", SystemsOf(true, false, minimal: true)); Set(frame, "Threat", ThreatOf(1, 99f, 99f, true, 300f, -20f, true));
            Contact(frame, 0, "Missile", null, "IR", false, 3400f, 310f, 11f, 6f); Set(frame, "ContactCount", 1); Set(frame, "InboundMask", (byte)1);
            for (int i = 1; i <= 8; i++) Call(ledger, "Launch", i, i % 4 == 0, 0f);
            Call(ledger, "Detonate", 1, true, 1f); Call(ledger, "Detonate", 2, false, 1.1f); Call(ledger, "Detonate", 4, true, 1.2f);
            FuelOf(frame, .62f, 3720f, false, false, "Info");
            rows = Rows(Msg("autopilot", "AUTOPILOT", "NAV 3/5 · ALT HOLD 6,000 FT", "Info", .6f));
            break;
        case "full":
            Set(frame, "Systems", SystemsOf(true, true)); Set(frame, "Threat", ThreatOf(2, 0.4f, 0.2f, true, 140f, 20f, true));
            Contact(frame, 0, "Missile", null, "SARH", true, 2400f, 340f, 7.1f, 1.5f); Contact(frame, 1, "Lock", "SA-19", null, false, 9400f, float.NaN, float.NaN, 10f);
            Set(frame, "ContactCount", 2); Set(frame, "InboundMask", (byte)2);
            CmStation(frame, 0, "IR", 6, 72, false, 1); CmStation(frame, 1, "RDR", 30, 30, false, 2); CmStation(frame, 2, "JAM", 1, 1, true, 2); CmStation(frame, 3, "IR", 0, 72, false, 1); Set(frame, "CmCount", 4);
            DamageOf(frame, "EngineDamaged, FuelLeak, Fire", 2, 1, .30f);
            FuelOf(frame, .21f, 900f, false, true, "Caution");
            TickerOf(frame, "Warning", "events", "SAM LAUNCH · TRACKING YOU", "BREAK NOW", 1, 0.5f);
            for (int i = 1; i <= 5; i++) Call(ledger, "Launch", i, i % 2 == 0, 0f);
            Call(ledger, "Detonate", 1, true, 1f); Call(ledger, "Detonate", 2, false, 1.1f);
            rows = Rows(Msg("lance", "LANCE", "RG-12 CHARGE", "Warning", .9f), Msg("missile", "MSL 800m · TGT 2.1km", "AIM-120 → MIG-29", "Caution", .28f),
                Msg("support", "SUPPORT · STRIKE", "ARTILLERY · 6 ROUNDS · TOT 00:40", "Caution", .5f), Msg("ils", "ILS", "LOCALIZER LEFT 2 · GLIDESLOPE HIGH", "Info", .45f));
            break;
        default:   // calm, boot-*, long-*
            Set(frame, "Systems", SystemsOf(true, false)); Set(frame, "Threat", ThreatOf(0, 99f, 99f, false, 0f, 0f, false));
            CmStation(frame, 0, "IR", 72, 72, false, 1); CmStation(frame, 1, "RDR", 30, 30, false, 2); CmStation(frame, 2, "JAM", 1, 1, true, 2); Set(frame, "CmCount", 3);
            FuelOf(frame, .72f, 5400f, false, false, "Info");
            if (state == "long-a") TickerOf(frame, "Info", "session", LongText, LongDetail, 1, .5f);
            if (state == "long-b") TickerOf(frame, "Warning", "events", LongText, LongDetail, 1, 3.5f);
            if (state == "long-c") TickerOf(frame, "Info", "session", LongText, LongDetail, 1, 9.5f);
            rows = Rows(Msg("ils", "ILS", "LOCALIZER LEFT 2 · GLIDESLOPE HIGH", "Caution", .45f), Msg("radio", "RADIO · AGRAPOL FM", "", "Info", 0f),
                Msg("autopilot", "AUTOPILOT", "NAV 3/5 · ALT HOLD 6,000 FT", "Info", .6f));
            break;
        }
        int count = rows.Length;
        Call(status, "Present", rows, count, frame, ledger);
        Canvas.ForceUpdateCanvases();
        string tag = state + (dark ? "-dark-" : "-") + size.x + "x" + size.y;
        RectTransform root = (RectTransform)Field(status, "root");
        Check(root.gameObject.activeSelf, "The board shows: " + tag);
        foreach (Graphic g in root.GetComponentsInChildren<Graphic>()) Check(!g.raycastTarget, "HUD must not intercept pointer input: " + tag);
        switch (state) {
        case "calm":
            Check(((TMP_Text)Field(status, "stripLabel")).text == "LOCALIZER LEFT 2 · GLIDESLOPE HIGH", "The cue strip carries the top line's detail: " + ((TMP_Text)Field(status, "stripLabel")).text);
            Check(TextOf(status, "Label0") == "ILS" && TextOf(status, "Value0") == "45%", "A constant-text cell shows its bar as the figure: " + TextOf(status, "Value0"));
            Check(TextOf(status, "Cm0") == "IR 72" && TextOf(status, "Cm2") == "JAM", "Every station shows: " + TextOf(status, "Cm0"));
            Check(TextOf(status, "FuelLevel") == "72% END 90MIN", "Fuel reads its endurance: " + TextOf(status, "FuelLevel"));
            Check(!Child(status, "HiLo").gameObject.activeSelf && !Child(status, "ShotLegend").gameObject.activeSelf, "An idle threat row has no HI/LO and no shot legend");
            Call(status, "Present", Rows(), 0, Boxed("BoardFrame"), ledger);
            Check(!root.gameObject.activeSelf, "Empty status hides owned tree.");
            Call(status, "Present", rows, count, frame, ledger);
            break;
        case "hot":
            Check(TextOf(status, "ShotLegend") == "SHOT", "Pips in flight carry the SHOT legend.");
            Check(TextOf(status, "HiLo") == "HI", "A threat 20 degrees up reads HI.");
            Check(TextOf(status, "DetailRow0") == "SARH 2.4KM TTI 7S" && TextOf(status, "DetailChip0") == "MSL", "The inbound missile reads seeker, range and time to impact: " + TextOf(status, "DetailRow0"));
            Check(TextOf(status, "DetailRow1") == "SA-19 9.4KM" && TextOf(status, "DetailChip1") == "LOCK", "A lock reads the emitter and range: " + TextOf(status, "DetailRow1"));
            Check(TextOf(status, "Cm0") == "IR 6 LOW" && TextOf(status, "Cm1") == "RDR 30", "A low station says LOW in words: " + TextOf(status, "Cm0"));
            Check(TextOf(status, "Threat2") == "MSL 2", "The missile lamp counts inbound missiles: " + TextOf(status, "Threat2"));
            break;
        case "notice":
            Check(TextOf(status, "Ticker") == "WARNING · SUPEREVENT · NIGHTFALL 2 INBOUND X3" && TextOf(status, "TickerTag") == "EVENTS", "A repeated warning coalesces to x3 and stays a WARNING: " + TextOf(status, "Ticker"));
            Check(TextOf(status, "FuelState") == "JOKER", "JOKER says so in words.");
            Check(TextOf(status, "Damage0") == "FUEL LEAK" && TextOf(status, "Damage1") == "STR 86%" && TextOf(status, "Damage2") == "ENG DMG", "Damage cautions persist, worst first: " + TextOf(status, "Damage0") + "/" + TextOf(status, "Damage1"));
            Check(TextOf(status, "DetailRow0") == "SA-19 22KM" && TextOf(status, "DetailChip0") == "SPKE", "A search-radar spike reads its emitter: " + TextOf(status, "DetailRow0"));
            break;
        case "bingo":
            Check(TextOf(status, "FuelState") == "BINGO", "BINGO outranks JOKER in words.");
            Check(TextOf(status, "Damage0") == "FIRE" && TextOf(status, "Damage1") == "ENG 1 OUT", "FIRE and a dead engine head the damage row: " + TextOf(status, "Damage0") + "/" + TextOf(status, "Damage1"));
            Check(TextOf(status, "Cm0") == "IR 0 EMPTY" && TextOf(status, "Cm1") == "RDR 4 LOW", "An empty station says EMPTY in words: " + TextOf(status, "Cm0"));
            Check(TextOf(status, "TickerTag") == "WEATHER" && TextOf(status, "Ticker") == "ENV: ICING CONDITIONS", "A caution notice reads in full on the ticker: " + TextOf(status, "Ticker"));
            break;
        case "boot-a": case "boot-b":
            Check(((TMP_Text)Field(status, "stripLabel")).text == "LAMP TEST", "The lamp test says so in the cue strip: " + ((TMP_Text)Field(status, "stripLabel")).text);
            break;
        case "boot-c":
            Check(((TMP_Text)Field(status, "stripLabel")).text == "SYSTEMS ONLINE", "...then SYSTEMS ONLINE: " + ((TMP_Text)Field(status, "stripLabel")).text);
            break;
        case "minimal": {
            Check(Child(status, "Lamp1").gameObject.activeSelf && !Child(status, "Lamp2").gameObject.activeSelf, "An airframe with two systems shows two lamps.");
            object f = Field(status, "f"); float width = (float)Field(Field(f, "G"), "Width"), u = (float)Field(f, "U");
            object t = Call(Type(HudDomain + "HudBoardLayout"), "ThreatRow", width, u);
            float pad = (float)Field(Type(HudDomain + "HudBoardLayout"), "Pad") * u;
            Check((float)Field(t, "PipX") + (float)Field(t, "PipStep") * (int)Field(Type(HudDomain + "HudBoardLayout"), "Pips") <= width - pad + .01f && (float)Field(t, "DialX") - (float)Field(t, "DialR") > pad,
                "With few lamps the dial and all eight pips still sit inside the board: " + tag);
            Check(TextOf(status, "ShotLegend") == "SHOT", "A full pip row carries the legend.");
            break; }
        case "long-a":
            Check(TextOf(status, "Ticker") == "SESSION HOST: THE MISSION WILL RESTART" && TextOf(status, "TickerTag") == "SESSION", "A long notice opens on its first page: " + TextOf(status, "Ticker"));
            break;
        case "long-b":
            Check(TextOf(status, "Ticker") == "WARNING · IN FIVE MINUTES FOR A SCHEDULED", "...a warning's second page keeps its prefix: " + TextOf(status, "Ticker"));
            break;
        case "long-c":
            Check(TextOf(status, "Ticker") == "AREA 4 EAST · RETURN TO BASE OR AIRBORNE", "...and its detail follows the text pages: " + TextOf(status, "Ticker"));
            break;
        case "full":
            Check(Child(status, "Value3").gameObject.activeSelf && Child(status, "Label3").gameObject.activeSelf && TextOf(status, "Cm3") == "IR 0 EMPTY", "Four cells and four CM tiles all show.");
            Check(TextOf(status, "Damage0") == "FIRE" && TextOf(status, "Damage3") == "STR 70%", "Four damage lamps show: " + TextOf(status, "Damage3"));
            break;
        }
        BoardGeometryChecks(status, root, tag);
        Capture(canvas, "HUD-board-" + tag + ".png");
        Call(status, "Destroy"); Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
    }
    /// <summary>What the offline render must keep true of every board state: every word fits its box (after its allowed shrink), stays inside the board, and no cell figure runs into the symbol under it.</summary>
    private static void BoardGeometryChecks(object status, RectTransform root, string tag)
    {
        foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>()) {
            if (string.IsNullOrEmpty(label.text)) continue;
            Check(Fits(label, 1.02f), "Board text fits its box: " + label.name + " [" + label.text + "] " + label.preferredWidth + " > " + label.rectTransform.rect.width + " (" + tag + ")");
            Check(label.preferredHeight <= label.rectTransform.rect.height * 1.1f + .5f, "Board text fits its box's height: " + label.name + " [" + label.text + "] " + label.preferredHeight + " > " + label.rectTransform.rect.height + " (" + tag + ")");
            var corners = new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
            foreach (Vector3 corner in corners) {
                Vector3 local = root.InverseTransformPoint(corner);
                Check(local.x >= root.rect.xMin - 1f && local.x <= root.rect.xMax + 1f && local.y >= root.rect.yMin - 1f && local.y <= root.rect.yMax + 1f,
                    "Board text sits inside the board: " + label.name + " (" + tag + ")");
            }
        }
        // A cell's figure ends at least 3 px above the symbol under it (the symbol's box is HudBoardLayout.GlyphLift / GlyphSize).
        object f = Field(status, "f"); object g = Field(f, "G"); float width = (float)Field(g, "Width"), u = (float)Field(f, "U"), cellsY = (float)Field(Field(g, "Cells"), "Y");
        Type layout = Type(HudDomain + "HudBoardLayout");
        float glyphTop = cellsY + ((float)Field(layout, "CellH") - (float)Field(layout, "GlyphLift")) * u - (float)Call(layout, "GlyphSize", width, u);
        for (int i = 0; i < (int)Field(f, "Count"); i++) {
            TMP_Text value = Child(status, "Value" + i).GetComponent<TMP_Text>();
            value.ForceMeshUpdate();
            float ink = float.MaxValue;
            for (int c = 0; c < value.textInfo.characterCount; c++)
                if (value.textInfo.characterInfo[c].isVisible) ink = Mathf.Min(ink, value.textInfo.characterInfo[c].bottomLeft.y);
            if (ink == float.MaxValue) continue;
            float bottom = -root.InverseTransformPoint(value.transform.TransformPoint(new Vector3(0f, ink, 0f))).y;   // down from the board's top edge
            Check(bottom + 3f * u / 1.04f <= glyphTop, "The figure clears the symbol under it by 3 px at 1080p (scaled with the board): cell " + i + " figure ends " + bottom + ", symbol starts " + glyphTop + " (" + tag + ")");
        }
    }
    /// <summary>The readout while the orbit camera rides one of our missiles: locked, blind, then the long and fast cases that need the figures to shrink to their columns.</summary>
    private static void MissileCam(Vector2 size)
    {
        foreach (string state in new[] { "locked", "blind", "fast-metric", "fast-imperial", "cruise" }) {
            Canvas canvas = MakeCanvas("MissileCam", size); CombatHUD hud = NativeDock(canvas);
            Sky(canvas);
            object panel = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.MissileCamPanel"), true);
            Call(panel, "Build", hud, canvas);
            SyntheticFont((RectTransform)Field(panel, "root"));
            object data = Boxed("MissileCamData");
            Set(data, "Valid", true); Set(data, "Index", 0); Set(data, "Count", 2); Set(data, "Weapon", "AIM-120"); Set(data, "Seeker", "SARH");
            Set(data, "State", EnumOf("SeekerState", state == "blind" ? "Search" : "Locked")); Set(data, "HasTarget", state != "blind"); Set(data, "Target", "MiG-29");
            bool fast = state.StartsWith("fast"), blind = state == "blind";
            Set(data, "RangeM", blind ? float.NaN : fast ? 18500f : state == "cruise" ? 300000f : 2400f);
            Set(data, "ClosureMps", blind ? float.NaN : fast ? 700f : state == "cruise" ? 250f : 233f);
            Set(data, "TtiS", blind ? float.NaN : fast ? 26.4f : state == "cruise" ? 1200f : 3.4f);
            Call(panel, "Present", true, data, state == "fast-imperial" ? AvUnits.Imperial : AvUnits.Metric, 1f);
            Canvas.ForceUpdateCanvases();
            RectTransform root = (RectTransform)Field(panel, "root");
            string tag = state + "-" + size.x + "x" + size.y;
            Check(root.gameObject.activeSelf, "The readout shows while riding a missile.");
            Check(root.Find("Header").GetComponent<TMP_Text>().text == "MSL 1/2 · AIM-120", "Header names the missile and its place in the list.");
            Check(root.Find("Seeker").GetComponent<TMP_Text>().text == (blind ? "SARH · NO TGT" : "SARH · LOCKED"), "The seeker says what it is doing.");
            if (state == "locked") Check(root.Find("Tti").GetComponent<TMP_Text>().text.Contains("3S"), "Time to impact is shown.");
            if (state == "cruise") Check(root.Find("Tti").GetComponent<TMP_Text>().text.Contains("20:00"), "A cruise missile 20 minutes out reads M:SS: " + root.Find("Tti").GetComponent<TMP_Text>().text);
            if (blind) Check(root.Find("Target").GetComponent<TMP_Text>().text == "NO TARGET", "A blind missile says NO TGT.");
            foreach (string figure in new[] { "Range", "Closure", "Tti", "Header", "Seeker", "Target" }) {
                TMP_Text label = root.Find(figure).GetComponent<TMP_Text>();
                Check(Fits(label, .96f), "A missile-cam figure fits its column with room to spare: " + figure + " [" + label.text + "] " + label.preferredWidth + " > " + label.rectTransform.rect.width + " (" + tag + ")");
            }
            Capture(canvas, "HUD-missilecam-" + tag + ".png");
            foreach (Graphic g in root.GetComponentsInChildren<Graphic>()) Check(!g.raycastTarget, "The missile-cam readout must not intercept pointer input.");
            Call(panel, "Present", false, data, AvUnits.Metric, 1f);
            Check(!root.gameObject.activeSelf, "The readout hides when the camera leaves the missile.");
            Call(panel, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
        }
    }
    private sealed class ClusterCase
    {
        public string Name, SpdCaption, AltCaption, FuelScale;
        public float Speed, Alt, Radar, Climb, Mach, G, Fuel, Bank, Slip, Aoa, Scale = 1f;
        public bool Bingo, Joker, White;
    }
    /// <summary>
    /// The third-person cluster on a hazy sky (blue, or near-white where thin ink is hardest to read): cruising with AoA and a real G;
    /// every caution lit (over-G, PULL UP, BINGO fuel); low ground alone; over-G with JOKER at once; and once at the player's larger text setting.
    /// </summary>
    private static void Cluster(Vector2 size)
    {
        var cases = new[] {
            new ClusterCase { Name = "cruise", Speed = 240f, Alt = 1800f, Radar = 900f, Climb = 3f, Mach = .72f, G = 1.1f, Fuel = .62f, Bank = 6f, Slip = -1f, Aoa = 3.4f, SpdCaption = "SPD", AltCaption = "ALT", FuelScale = "FUEL 62" },
            new ClusterCase { Name = "cautions", Speed = 130f, Alt = 420f, Radar = 120f, Climb = -32f, Mach = .35f, G = 7.6f, Fuel = .09f, Bank = 38f, Slip = 4f, Aoa = 14.2f, Bingo = true, Joker = true, SpdCaption = "OVER-G", AltCaption = "PULL UP", FuelScale = "BINGO 9" },
            new ClusterCase { Name = "low", Speed = 130f, Alt = 420f, Radar = 120f, Climb = -8f, Mach = .35f, G = 1.4f, Fuel = .62f, Bank = 12f, Slip = 2f, Aoa = 9f, SpdCaption = "SPD", AltCaption = "LOW R ", FuelScale = "FUEL 62" },
            new ClusterCase { Name = "joker", Speed = 240f, Alt = 1800f, Radar = 900f, Climb = 3f, Mach = .72f, G = 7.6f, Fuel = .20f, Bank = 50f, Slip = 0f, Aoa = 6f, Joker = true, SpdCaption = "OVER-G", AltCaption = "ALT", FuelScale = "JOKER 20" },
            new ClusterCase { Name = "haze", Speed = 130f, Alt = 420f, Radar = 120f, Climb = -32f, Mach = .35f, G = 7.6f, Fuel = .09f, Bank = 38f, Slip = 4f, Aoa = 14.2f, Bingo = true, Joker = true, White = true, SpdCaption = "OVER-G", AltCaption = "PULL UP", FuelScale = "BINGO 9" },
            new ClusterCase { Name = "large-text", Speed = 240f, Alt = 1800f, Radar = 900f, Climb = 3f, Mach = .72f, G = 1.1f, Fuel = .62f, Bank = 6f, Slip = -1f, Aoa = 3.4f, Scale = 1.5f, SpdCaption = "SPD", AltCaption = "ALT", FuelScale = "FUEL 62" },
        };
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (ClusterCase c in cases) {
            if (c.Name == "large-text" && size.x != 1920f) continue;
            Canvas canvas = MakeCanvas("Cluster", size); CombatHUD hud = NativeDock(canvas);
            Sky(canvas, c.White);
            object cluster = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.ThirdPersonHudCluster"), true);
            Call(cluster, "Build", hud, canvas);
            SyntheticFont((RectTransform)Field(cluster, "root"));
            object flight = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.ThirdPersonHudCluster+Flight"));
            foreach (var kv in new[] { ("SpeedMps", c.Speed), ("AltitudeM", c.Alt), ("RadarAltM", c.Radar), ("ClimbMps", c.Climb), ("HeadingDeg", 359.6f), ("Mach", c.Mach),
                ("G", c.G), ("GLimit", 9f), ("Fuel", c.Fuel), ("Throttle", .8f), ("BankDeg", c.Bank), ("SlipDeg", c.Slip), ("AoaDeg", c.Aoa) })
                Set(flight, kv.Item1, kv.Item2);
            Set(flight, "FuelBingo", c.Bingo); Set(flight, "FuelJoker", c.Joker);
            Call(cluster, "Present", true, AvUnits.Imperial, flight, Texture2D.whiteTexture, "SAM SITE", 9400f, 120f, c.Scale);
            Canvas.ForceUpdateCanvases();
            string tag = c.Name + "-" + size.x + "x" + size.y;
            Capture(canvas, "HUD-cluster-" + tag + ".png");
            object spd = Field(cluster, "spd"), alt = Field(cluster, "alt");
            Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text.StartsWith("000°"), "Rounded north wraps to 000 degrees.");
            Call(cluster, "WriteHeading", -1f);
            Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text.StartsWith("359°"), "Negative heading normalizes.");
            Call(cluster, "WriteHeading", 5f);
            Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text.StartsWith("005°"), "Heading retains three digits.");
            string altCaption = ((TMP_Text)Field(alt, "Caption")).text, spdCaption = ((TMP_Text)Field(spd, "Caption")).text;
            Check(c.AltCaption == "LOW R " ? altCaption.StartsWith("LOW R ") : altCaption == c.AltCaption, "The ALT caption names the ground cue in words: " + altCaption + " (" + tag + ")");
            Check(spdCaption == c.SpdCaption, "The SPD caption says OVER-G in words when the box is amber: " + spdCaption + " (" + tag + ")");
            Check((((TMP_Text)Field(spd, "CaptionRight")).text ?? "") == (c.SpdCaption == "OVER-G" ? "" : "AOA " + c.Aoa.ToString("0.0", inv) + "°"), "AoA reads in the SPD caption row (OVER-G takes the row): " + ((TMP_Text)Field(spd, "CaptionRight")).text);
            Check(((TMP_Text)Field(spd, "SubLine")).text == "M " + c.Mach.ToString("0.00", inv) + "  G " + c.G.ToString("0.0", inv), "G is the load factor: " + ((TMP_Text)Field(spd, "SubLine")).text);
            Check(((TMP_Text)Field(alt, "BarLabel")).text == c.FuelScale, "The fuel scale says BINGO / JOKER as the board does: " + ((TMP_Text)Field(alt, "BarLabel")).text);
            foreach (object box in new[] { spd, alt })
                foreach (string name in new[] { "Caption", "CaptionRight", "Value", "SubLine", "BarLabel" }) {
                    var label = (TMP_Text)Field(box, name);
                    if (label != null && !string.IsNullOrEmpty(label.text))
                        Check(Fits(label, 1f), "A cluster word fits its box: " + name + " [" + label.text + "] " + label.preferredWidth + " > " + label.rectTransform.rect.width + " (" + tag + ")");
                }
            // The SPD caption row holds the caption and the AoA: they must not run into each other.
            TMP_Text left = (TMP_Text)Field(spd, "Caption"), right = (TMP_Text)Field(spd, "CaptionRight");
            left.ForceMeshUpdate(); right.ForceMeshUpdate();
            Check(left.renderedWidth + right.renderedWidth + 6f <= ((RectTransform)Field(spd, "Root")).rect.width - 14f, "The SPD caption and the AoA do not collide: " + left.renderedWidth + " + " + right.renderedWidth + " (" + tag + ")");
            foreach (Graphic g in ((RectTransform)Field(cluster, "root")).GetComponentsInChildren<Graphic>())
                Check(!g.raycastTarget, "Flight cluster must not intercept pointer input.");
            Call(cluster, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
        }
    }
    private static void Log(float height, bool merged)
    {
        Canvas canvas = MakeCanvas("Log", new Vector2(480, height));
        Type log = Type("BoscaliSummer.Modules.Command.Presentation.MapUi.MfdLogPanel");
        var panel = new GameObject("LogSurface", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(canvas.transform, false); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0,1);
        panel.sizeDelta = new Vector2(480,height); Set(log, "panel", panel);
        Call(log, "Rebuild", new Vector2(480,height), merged);
        var history = (IList)Field(log, "history"); history.Clear();
        for (int i=0;i<14;i++) Call(log, "AddEntry", (i % 3 == 0 ? "SAM BATTERY DESTROYED / NORTHERN RIDGE" :
            i % 3 == 1 ? "CONTRACT ACCEPTED / SURVEY THE AFTERMATH" : "HOSTILE AIRCRAFT DETECTED / SECTOR C4") + " / REPORT " + i, i%3==0);
        Call(log, "RefreshFeed", true);
        Capture(canvas, "LOG-" + height + "-" + merged + ".png");
        var scroll = (ScrollRect)Field(log,"scroll"); scroll.verticalNormalizedPosition = 0f;
        Capture(canvas, "LOG-" + height + "-" + merged + "-bottom.png");
        Canvas.ForceUpdateCanvases();
        Call(log, "AddEntry", "AIRBASE NORTHERN CAPTURED / NEW TRAFFIC", false);
        Call(log, "RefreshFeed", true);
        Canvas.ForceUpdateCanvases();
        Check(scroll.verticalNormalizedPosition < .95f, "New traffic does not jump a scrolled reader to latest.");
        Call(log, "TogglePause");
        string held = ((TMP_Text)Field(log, "body")).text;
        Call(log, "AddEntry", "HOSTILE AIRCRAFT DESTROYED / WHILE HELD", true);
        Call(log, "RefreshFeed", true);
        Check(((TMP_Text)Field(log, "body")).text == held, "Pause freezes local traffic.");
        Check(((TMP_Text)Field(log, "trafficCount")).text.Contains("1 NEW"), "Held view reports incoming traffic.");
        Capture(canvas, "LOG-" + height + "-" + merged + "-held.png");
        Call(log, "Latest");
        Check(!(bool)Field(log, "paused") && (scroll.content.rect.height <= scroll.viewport.rect.height + .5f ||
            scroll.verticalNormalizedPosition > .99f), "Latest resumes the live edge.");
        Check(scroll.content.rect.height >= scroll.viewport.rect.height, "Log content retains readable scroll area.");
        foreach (Selectable s in panel.GetComponentsInChildren<Selectable>())
            Check(s.navigation.mode == Navigation.Mode.None, "Log controls yield flight-stick focus.");
        Call(log,"Restore"); Object.DestroyImmediate(canvas.gameObject);
    }
    private static void Wire()
    {
        Canvas canvas = MakeCanvas("Wire", new Vector2(1280,720));
        Type layout = Type("BoscaliSummer.Modules.Command.Presentation.MapUi.MfdLayout");
        object columns = Call(layout,"Resolve",new Vector2(1280,720),480f);
        Type wire=Type("BoscaliSummer.Modules.Command.Presentation.MapUi.MfdNewsTicker");
        Call(wire,"Ensure",canvas,columns,null);
        var feed=Field(wire,"feed"); Call(feed,"IngestGameEvent","HOSTILE SAM LAUNCH / RIDGE BATTERY",0f);
        Set(wire,"currentMarqueeString",Call(feed,"BuildMarqueeText",5));
        foreach(string label in new[]{"labelA","labelB"}) ((TMP_Text)Field(wire,label)).text=(string)Field(wire,"currentMarqueeString");
        Capture(canvas,"WIRE-1280x720.png");
        Call(wire,"Reset"); Object.DestroyImmediate(canvas.gameObject);
    }
    private static void Contracts()
    {
        float oldSize = PlayerSettings.overlayTextSize;
        PlayerSettings.overlayTextSize = 32f;
        try {
            Canvas canvas = MakeCanvas("Contract overlays", new Vector2(1280,720));
            Type styleOwner = Type("BoscaliSummer.Core.Game.VanillaHudStyle");
            object style = Activator.CreateInstance(styleOwner.GetNestedType("CockpitStyle", All));
            var font = AvBundle.Font("NOA Rajdhani Medium SDF");
            Set(style,"Font",font); Set(style,"FontMaterial",font.material); Set(style,"Colour",Color.cyan);
            Set(style,"Pointer",AvSprites.White); Set(style,"Dot",AvSprites.White); Set(style,"Ring",AvSprites.White);
            Set(style,"PointerSize",new Vector2(12,12)); Set(style,"DotSize",new Vector2(4,4));
            Set(style,"RingSize",new Vector2(32,32)); Set(style,"LabelScale",.5f);
            Type cardType=Type("BoscaliSummer.Modules.DynamicOperations.Domain.ContractCard");
            Type viewType=Type("BoscaliSummer.Core.Contracts.SecondaryObjectiveView");
            Type markerType=Type("BoscaliSummer.Modules.DynamicOperations.Runtime.ContractMarker");
            for(int i=0;i<3;i++) {
                object[] facts={i+26,"SURVEY THE AFTERMATH / NORTHERN DEPOT","Synthetic contract briefing.","NORTHERN DEPOT",
                    i==2?"RETURN TO BASE":"IN PROGRESS","$1,500 + 125 XP",.55f, i==1?42f:192f,1500,125,false,false,true,true,0f,0f,500f,"NIGHTFALL-2"};
                object view=Activator.CreateInstance(viewType,All,null,facts,null);
                object card=Activator.CreateInstance(cardType,All,null,new[]{view},null);
                object marker=Activator.CreateInstance(markerType,All,null,new object[]{canvas.transform,"Contract "+i,style},null);
                float x=i==1?440f:-440f, y=170f-i*170f;
                Call(marker,"SetVisible",true); Call(marker,"Place",x,y);
                Call(marker,"SetIcons",Vector3.forward,new Vector3(500,0,5000),90f);
                Call(marker,"SetLabel",card,9400f,i!=2,x,470f,i==1,i==1?Color.yellow:Color.cyan);
                Call(marker,"GlideLabel");
                if(i==1) Check(((RectTransform)Field(marker,"label")).pivot.x==1f,"Contract right-edge label flips inward.");
            }
            Capture(canvas,"CONTRACT-HUD-1280x720.png");
            foreach(Graphic g in canvas.GetComponentsInChildren<Graphic>()) Check(!g.raycastTarget,"Contract overlay yields flight input.");
            Object.DestroyImmediate(canvas.gameObject);

            canvas=MakeCanvas("Contract map tags",new Vector2(1280,720));
            object mapStyle=Activator.CreateInstance(styleOwner.GetNestedType("MapStyle",All));
            foreach(string icon in new[]{"WaypointIcon","DestroyIcon","ReconIcon","CaptureIcon","Ring"}) Set(mapStyle,icon,AvSprites.White);
            Set(mapStyle,"LabelFont",Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            Set(mapStyle,"LabelSize",24f); Set(mapStyle,"LabelColour",Color.cyan); Set(mapStyle,"LabelScale",.5f);
            Type tagType=Type("BoscaliSummer.Modules.DynamicOperations.Runtime.ContractMapTag");
            object[] mapFacts={26,"SURVEY THE AFTERMATH / NORTHERN DEPOT","Synthetic briefing.","DEPOT","IN PROGRESS","$1,500",.55f,192f,1500,125,false,false,true,true,0f,0f,500f,"NIGHTFALL-2"};
            object mapView=Activator.CreateInstance(viewType,All,null,mapFacts,null);
            object mapCard=Activator.CreateInstance(cardType,All,null,new[]{mapView},null);
            object tag=Activator.CreateInstance(tagType,All,null,new object[]{canvas.transform,"Map contract",mapStyle,AvSprites.White},null);
            Call(tag,"SetVisible",true); Call(tag,"Apply",mapCard,mapStyle,Color.cyan);
            foreach(float zoom in new[]{.5f,1f,2f}) {
                Call(tag,"Place",0f,0f,1f/zoom,zoom); Call(tag,"SetRing",true,120f,new Color(0,1,1,.15f));
                Capture(canvas,"CONTRACT-MAP-zoom-"+zoom+".png");
                foreach(Graphic g in canvas.GetComponentsInChildren<Graphic>()) Check(!g.raycastTarget,"Contract map labels yield native clicks.");
            }
            Object.DestroyImmediate(canvas.gameObject);
        } finally { PlayerSettings.overlayTextSize=oldSize; }
    }
    private static void Capture(Canvas canvas, string path)
    {
        foreach(AvReveal r in canvas.GetComponentsInChildren<AvReveal>(true)) r.Finish();
        Canvas.ForceUpdateCanvases();
        foreach (var label in canvas.GetComponentsInChildren<TextMeshProUGUI>()) {
            if (string.IsNullOrEmpty(label.text)) continue;
            label.ForceMeshUpdate();
            Check(label.textInfo.characterCount > 0, "Visible text has geometry: " + label.text);
        }
        Vector2 size=((RectTransform)canvas.transform).rect.size;
        var camera = OrthoCamera("Camera", size.y / 2, new Color(.08f, .12f, .15f));
        CapturePng(camera, (int)size.x, (int)size.y, path);
        Object.DestroyImmediate(camera.gameObject); captures++;
    }
    private static void Check(bool okay,string message) { assertions++; if(!okay) throw new Exception(message); }
}
#endif

