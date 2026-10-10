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
                foreach (string state in new[] { "calm", "hot", "notice", "bingo" }) Board(size, state);
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
        var font = AvBundle.Font("NOA Rajdhani Medium SDF");
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
    /// <summary>A hazy sky behind the HUD: thin ink on a bright backdrop is the case the scrims and tones are for.</summary>
    private static void Sky(Canvas canvas)
    {
        var texture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) texture.SetPixel(0, y, Color.Lerp(new Color(.82f, .89f, .95f), new Color(.40f, .58f, .82f), y / 63f));
        texture.Apply();
        var sky = new GameObject("Sky", typeof(RectTransform), typeof(Image));
        sky.transform.SetParent(canvas.transform, false); sky.transform.SetAsFirstSibling();
        var rect = (RectTransform)sky.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = sky.GetComponent<Image>(); image.raycastTarget = false; image.sprite = Sprite.Create(texture, new Rect(0, 0, 1, 64), new Vector2(.5f, .5f));
    }
    /// <summary>
    /// The board at the native size for this resolution. The offline dock has no native weapon cell to measure, so the unit and the
    /// text sizes are set the way the build would from the real one at 1080p (cell about 71 px tall; figures about 1.7x and words
    /// about 1.3x the fallback sizes, read off the in-game crop) and scale with the screen height.
    /// </summary>
    private static void NativeScale(object status, float height)
    {
        float k = height / 1080f;
        Set(status, "unit", 1.04f * k);
        foreach (var label in ((RectTransform)Field(status, "root")).GetComponentsInChildren<TextMeshProUGUI>(true)) {
            float m = (label.name.StartsWith("Value") ? 1.7f : 1.3f) * k;
            label.fontSize *= m;
            if (label.enableAutoSizing) { label.fontSizeMax *= m; label.fontSizeMin *= m; }
        }
    }
    private static Transform Child(object status, string name) => ((RectTransform)Field(status, "root")).Find(name);
    private static string TextOf(object status, string name) => Child(status, name).GetComponent<TMP_Text>().text;
    private static object ThreatOf(int missiles, float sinceLock, float sinceSpike, bool bearing, float bearingDeg, float elevation, bool bearingIsMissile)
    {
        object threat = Type(HudDomain + "ThreatPicture").GetProperty("Clear").GetValue(null);
        Set(threat, "Missiles", missiles); Set(threat, "SinceLock", sinceLock); Set(threat, "SinceSpike", sinceSpike); Set(threat, "SinceNewMissile", missiles > 0 ? 0.5f : 99f);
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
    private static object SystemsOf(bool radar, bool safe)
    {
        object systems = Boxed("SystemStates");
        Set(systems, "Gear", EnumOf("GearLamp", "Down")); Set(systems, "FlightAssist", true); Set(systems, "AutoHover", false);
        Set(systems, "RadarEmitting", radar); Set(systems, "NightVision", false); Set(systems, "WeaponSafe", safe); Set(systems, "EngineRunning", true);
        return systems;
    }
    /// <summary>
    /// The status board in one named state, at one resolution, on a hazy sky with a stand-in for the native weapons block above it.
    /// calm: idle dial, CM tiles at full load, fuel with endurance, cells with a cue strip. hot: an inbound radar missile with time to
    /// impact, a lock, shot pips, a LOW flare station and the chaff highlighted. notice: a x3 warning on the ticker, JOKER, a search
    /// spike, damage cautions. bingo: BINGO, FIRE and a dead engine, empty flares, a caution notice.
    /// </summary>
    private static void Board(Vector2 size, string state)
    {
        Canvas canvas = MakeCanvas("Board-" + state, size); CombatHUD hud = NativeDock(canvas);
        Sky(canvas);
        float k = size.y / 1080f;
        var dock = new GameObject("WeaponsDock", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        dock.SetParent(canvas.transform, false); dock.sizeDelta = new Vector2(452f * k, 190f * k);
        dock.anchoredPosition = new Vector2(size.x / 2f - 30f * k - dock.sizeDelta.x / 2f, size.y / 2f - 20f * k - dock.sizeDelta.y / 2f);
        dock.GetComponent<Image>().color = new Color(.02f, .04f, .06f, .9f); dock.GetComponent<Image>().raycastTarget = false;
        object status = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.StatusPanel"), true);
        Call(status, "Build", hud, dock);
        SyntheticFont((RectTransform)Field(status, "root"));
        NativeScale(status, size.y);

        object frame = Boxed("BoardFrame");
        Set(frame, "BootAge", -1f);
        Set(frame, "Now", 0f);   // the lit half of the flash beat, so every flashing lamp is drawn lit
        object ledger = Boxed("ShotLedger");
        Array rows;
        switch (state) {
        case "calm":
            Set(frame, "Systems", SystemsOf(true, false)); Set(frame, "Threat", ThreatOf(0, 99f, 99f, false, 0f, 0f, false));
            CmStation(frame, 0, "IR", 72, 72, false, 1); CmStation(frame, 1, "RDR", 30, 30, false, 2); CmStation(frame, 2, "JAM", 1, 1, true, 2); Set(frame, "CmCount", 3);
            FuelOf(frame, .72f, 5400f, false, false, "Info");
            rows = Rows(Msg("ils", "ILS", "LOCALIZER LEFT 2 · GLIDESLOPE HIGH", "Caution", .45f), Msg("radio", "RADIO · AGRAPOL FM", "", "Info", 0f),
                Msg("autopilot", "AUTOPILOT", "NAV 3/5 · ALT HOLD 6,000 FT", "Info", .6f));
            break;
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
        default:
            Set(frame, "Systems", SystemsOf(true, false)); Set(frame, "Threat", ThreatOf(1, 99f, 99f, true, 300f, -20f, true));
            Contact(frame, 0, "Missile", null, "IR", false, 900f, 310f, 2.9f, 0.8f); Set(frame, "ContactCount", 1); Set(frame, "InboundMask", (byte)1);
            CmStation(frame, 0, "IR", 0, 72, false, 1); CmStation(frame, 1, "RDR", 4, 30, false, 2); CmStation(frame, 2, "JAM", 1, 1, true, 2); Set(frame, "CmCount", 3);
            DamageOf(frame, "FuelLeak, Fire", 2, 1, .30f);
            FuelOf(frame, .09f, 240f, true, true, "Warning");
            TickerOf(frame, "Caution", "weather", "ENV: ICING CONDITIONS", "FREEZING LEVEL 1,500 M · CLIMB OUT OF CLOUD", 1, 1.5f);
            rows = Rows(Msg("lance", "LANCE", "RG-12 CHARGE", "Warning", .9f), Msg("missile", "MSL 400m · NO TGT", "AIM-9 → NO TARGET", "Caution", 0f));
            break;
        }
        int count = rows.Length;
        Call(status, "Present", rows, count, frame, ledger);
        Canvas.ForceUpdateCanvases();
        string tag = state + "-" + size.x + "x" + size.y;
        RectTransform root = (RectTransform)Field(status, "root");
        Check(root.gameObject.activeSelf, "The board shows: " + tag);
        foreach (Graphic g in root.GetComponentsInChildren<Graphic>()) Check(!g.raycastTarget, "HUD must not intercept pointer input: " + tag);
        switch (state) {
        case "calm":
            Check(((TMP_Text)Field(status, "stripLabel")).text == "LOCALIZER LEFT 2 · GLIDESLOPE HIGH", "The cue strip carries the top line's detail: " + ((TMP_Text)Field(status, "stripLabel")).text);
            Check(TextOf(status, "Label0") == "ILS" && TextOf(status, "Value0") == "45%", "A constant-text cell shows its bar as the figure: " + TextOf(status, "Value0"));
            Check(TextOf(status, "Cm0") == "IR 72" && TextOf(status, "Cm2") == "JAM", "Every station shows: " + TextOf(status, "Cm0"));
            Check(TextOf(status, "FuelLevel") == "72%  END 90MIN", "Fuel reads its endurance: " + TextOf(status, "FuelLevel"));
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
        default:
            Check(TextOf(status, "FuelState") == "BINGO", "BINGO outranks JOKER in words.");
            Check(TextOf(status, "Damage0") == "FIRE" && TextOf(status, "Damage1") == "ENG 1 OUT", "FIRE and a dead engine head the damage row: " + TextOf(status, "Damage0") + "/" + TextOf(status, "Damage1"));
            Check(TextOf(status, "Cm0") == "IR 0 EMPTY" && TextOf(status, "Cm1") == "RDR 4 LOW", "An empty station says EMPTY in words: " + TextOf(status, "Cm0"));
            Check(TextOf(status, "TickerTag") == "WEATHER" && TextOf(status, "Ticker") == "ENV: ICING CONDITIONS", "A caution notice reads in full on the ticker: " + TextOf(status, "Ticker"));
            break;
        }
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>()) {
            Check(!label.isTextOverflowing, "Board text is not cut off: " + label.text + " (" + tag + ")");
            if (label.name.StartsWith("Lamp") || label.name.StartsWith("Threat") || label.name.StartsWith("Cm") || label.name.StartsWith("Damage") || label.name == "FuelState")
                Check(label.preferredWidth <= label.rectTransform.rect.width * 1.02f, "A tile's words fit the tile: " + label.text + " " + label.preferredWidth + " > " + label.rectTransform.rect.width + " (" + tag + ")");
        }
        Capture(canvas, "HUD-board-" + tag + ".png");
        Call(status, "Destroy"); Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
    }
    /// <summary>The readout while the orbit camera rides one of our missiles: locked on a target, then searching blind.</summary>
    private static void MissileCam(Vector2 size)
    {
        Canvas canvas = MakeCanvas("MissileCam", size); CombatHUD hud = NativeDock(canvas);
        Sky(canvas);
        object panel = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.MissileCamPanel"), true);
        Call(panel, "Build", hud, canvas);
        SyntheticFont((RectTransform)Field(panel, "root"));
        object data = Boxed("MissileCamData");
        Set(data, "Valid", true); Set(data, "Index", 0); Set(data, "Count", 2); Set(data, "Weapon", "AIM-120"); Set(data, "Seeker", "SARH");
        Set(data, "State", EnumOf("SeekerState", "Locked")); Set(data, "HasTarget", true); Set(data, "Target", "MiG-29");
        Set(data, "RangeM", 2400f); Set(data, "ClosureMps", 233f); Set(data, "TtiS", 3.4f);
        Call(panel, "Present", true, data, AvUnits.Metric, 1f);
        Canvas.ForceUpdateCanvases();
        RectTransform root = (RectTransform)Field(panel, "root");
        Check(root.gameObject.activeSelf, "The readout shows while riding a missile.");
        Check(root.Find("Header").GetComponent<TMP_Text>().text == "MSL 1/2 · AIM-120", "Header names the missile and its place in the list.");
        Check(root.Find("Seeker").GetComponent<TMP_Text>().text == "SARH · LOCKED", "A locked seeker says LOCKED.");
        Check(root.Find("Tti").GetComponent<TMP_Text>().text.Contains("3S"), "Time to impact is shown.");
        Capture(canvas, "HUD-missilecam-locked-" + size.x + "x" + size.y + ".png");
        Set(data, "State", EnumOf("SeekerState", "Search")); Set(data, "HasTarget", false); Set(data, "RangeM", float.NaN); Set(data, "ClosureMps", float.NaN); Set(data, "TtiS", float.NaN);
        Call(panel, "Present", true, data, AvUnits.Metric, 1f);
        Check(root.Find("Seeker").GetComponent<TMP_Text>().text == "SARH · NO TGT" && root.Find("Target").GetComponent<TMP_Text>().text == "NO TARGET", "A blind missile says NO TGT.");
        Capture(canvas, "HUD-missilecam-blind-" + size.x + "x" + size.y + ".png");
        foreach (Graphic g in root.GetComponentsInChildren<Graphic>()) Check(!g.raycastTarget, "The missile-cam readout must not intercept pointer input.");
        Call(panel, "Present", false, data, AvUnits.Metric, 1f);
        Check(!root.gameObject.activeSelf, "The readout hides when the camera leaves the missile.");
        Call(panel, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
    }
    /// <summary>The third-person cluster on a hazy sky: cruising with AoA and a real G, then every caution lit (over-G, low ground, BINGO fuel); once at the player's larger text setting.</summary>
    private static void Cluster(Vector2 size)
    {
        foreach (string state in new[] { "cruise", "cautions", "large-text" }) {
            if (state == "large-text" && size.x != 1920f) continue;
            Canvas canvas = MakeCanvas("Cluster", size); CombatHUD hud = NativeDock(canvas);
            Sky(canvas);
            object cluster = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.ThirdPersonHudCluster"), true);
            Call(cluster, "Build", hud, canvas);
            SyntheticFont((RectTransform)Field(cluster, "root"));
            Type flightType = Type("BoscaliSummer.Modules.Hud.Presentation.ThirdPersonHudCluster+Flight");
            object flight = Activator.CreateInstance(flightType);
            bool bad = state == "cautions";
            foreach (var kv in new[] { ("SpeedMps", bad ? 130f : 240f), ("AltitudeM", bad ? 420f : 1800f), ("RadarAltM", bad ? 120f : 900f), ("ClimbMps", bad ? -32f : 3f),
                ("HeadingDeg", 359.6f), ("Mach", bad ? .35f : .72f), ("G", bad ? 7.6f : 1.1f), ("GLimit", 9f), ("Fuel", bad ? .09f : .62f), ("Throttle", .8f),
                ("BankDeg", bad ? 38f : 6f), ("SlipDeg", bad ? 4f : -1f), ("AoaDeg", bad ? 14.2f : 3.4f) })
                Set(flight, kv.Item1, kv.Item2);
            if (bad) { Set(flight, "FuelBingo", true); Set(flight, "FuelJoker", true); }
            float scale = state == "large-text" ? 1.5f : 1f;
            Call(cluster, "Present", true, AvUnits.Imperial, flight, Texture2D.whiteTexture, "SAM SITE", 9400f, 120f, scale);
            Canvas.ForceUpdateCanvases();
            Capture(canvas, "HUD-cluster-" + state + "-" + size.x + "x" + size.y + ".png");
            object heading = Field(cluster, "hdg");
            Check(((TMP_Text)Field(heading, "Value")).text.StartsWith("000°"), "Rounded north wraps to 000 degrees.");
            Call(cluster, "WriteHeading", -1f);
            Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text.StartsWith("359°"), "Negative heading normalizes.");
            Call(cluster, "WriteHeading", 5f);
            Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text.StartsWith("005°"), "Heading retains three digits.");
            Check(((TMP_Text)Field(Field(cluster, "alt"), "Caption")).text == (bad ? "PULL UP" : "ALT"), "The ALT caption names the ground cue: " + ((TMP_Text)Field(Field(cluster, "alt"), "Caption")).text);
            Check(((TMP_Text)Field(Field(cluster, "spd"), "CaptionRight")).text == (bad ? "AOA 14.2°" : "AOA 3.4°"), "AoA reads in the SPD caption row: " + ((TMP_Text)Field(Field(cluster, "spd"), "CaptionRight")).text);
            Check(((TMP_Text)Field(Field(cluster, "spd"), "SubLine")).text == (bad ? "M 0.35  G 7.6" : "M 0.72  G 1.1"), "G is the load factor: " + ((TMP_Text)Field(Field(cluster, "spd"), "SubLine")).text);
            Check(((TMP_Text)Field(Field(cluster, "alt"), "BarLabel")).text == (bad ? "BINGO 9" : "FUEL 62"), "The fuel scale says BINGO as the board does: " + ((TMP_Text)Field(Field(cluster, "alt"), "BarLabel")).text);
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

