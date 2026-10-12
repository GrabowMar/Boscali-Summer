#if UNITY_EDITOR
using System;
using System.IO;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// The TAC BOARD scope check: renders every page of the board from FIXTURES at the prototype's scenario times and writes the shots the
// compare sheet (Compare-ScopeShots.py) lines up against the spec's shots/.
//
//   one fixture file per page:  HudLogUnityCheck.Scope.<Name>.cs   (a page whose file is an empty stub is skipped)
//   the STORES HEADER on top of every page: HudLogUnityCheck.Scope.Stores.cs (the prototype's default loadout and what each page's scenario does to it)
//   a fixture is a  static partial void Fixture<Name>(float t, ScopeScene s)  that makes the page live for scenario time t:
//       s.Publish(<the page's feed struct, filled by name>)   what the page's provider returns at t (ScopeFeeds.Publish)
//       s.Gauge(id, <GaugeSpec>)                                a registered gauge (the prototype's defaults: lance, perk, radio, ap)
//       s.SetFrame("Systems", <SystemStates>) ...               lamps, threat picture, ticker line, anything on the BoardFrame
//   and reproduces the prototype page's scenario for that t (pages/<id>.js sim(t)). Fixtures are the ONLY place scripted data lives.
//   A fixture that does not touch the scene is a stub: that page is skipped.
// Output: <page>-t<sec>-<W>x<H>.png (t = 2, 6, 10, 14 at 1920x1080 and t = 6 at 1280x720), the same names as the spec shots.
// Command line (after -executeMethod): -pages systems,threat   only those pages' fixtures (default: all, plus the rest of the check; 'shell' = the board shell alone, 'frame' = the board's frame alone, 'lines' = the wing's lamp, gauge and ticker lines)
//                                      -spec <spec folder>     draws the shots over the spec's cockpit capture (takeover/assets/bg-cockpit-hud-takeover.jpg: the native top-right panel painted out)
public static partial class HudLogUnityCheck
{
    private static readonly string[] ScopePageNames = { "systems", "threat", "shot", "missilecam", "approach", "wing", "strike", "dock", "objective", "stores" };
    private static readonly (float t, int w, int h)[] ScopeShotTimes = { (2f, 1920, 1080), (6f, 1920, 1080), (10f, 1920, 1080), (14f, 1920, 1080), (6f, 1280, 720) };

    /// <summary>(page, t, variant): one more 1080p shot of a page, its fixture branching on <c>s.Variant</c>.</summary>
    private static readonly (string page, float t, string variant)[] ScopeVariants =
    {
        ("dock", 6f, "service-zone"), ("dock", 12f, "service-zone"), ("dock", 14f, "service-zone"),
        // The names the game gave its radars (seen in game: six at nearly one bearing, the longest 22 characters), no SAM envelope round us, then inside two.
        ("threat", 0.5f, "longnames"), ("threat", 1.5f, "longnames"),
        ("systems", 10f, "rotary"), ("systems", 2f, "annex"), ("systems", 2f, "longname"), ("systems", 2f, "hardg"), ("strike", 6f, "solo"), ("strike", 6f, "sat"), ("objective", 6f, "far"), ("objective", 6f, "bare"), ("missilecam", 8f, "blind"),
        // The stores header alone over the cruise strip: the default loadout, a gun and flares running low (chaff gone), an empty weapon, a gun reload with another station reloading,
        // an airframe without a capacitor, a flare call (DISPENSE) with the chaff selected, a bare helicopter (one dispenser, no jammer) and a gun-only aircraft with no countermeasures.
        ("stores", 6f, "default"), ("stores", 6f, "low"), ("stores", 6f, "empty"), ("stores", 6f, "reload"), ("stores", 6f, "nocap"), ("stores", 6f, "dispense"),
        ("stores", 6f, "rotary"), ("stores", 6f, "gunonly"),
    };

    static partial void FixtureSystems(float t, ScopeScene s);
    static partial void FixtureThreat(float t, ScopeScene s);
    static partial void FixtureShot(float t, ScopeScene s);
    static partial void FixtureMissileCam(float t, ScopeScene s);
    static partial void FixtureApproach(float t, ScopeScene s);
    static partial void FixtureWing(float t, ScopeScene s);
    static partial void FixtureStrike(float t, ScopeScene s);
    static partial void FixtureDock(float t, ScopeScene s);
    static partial void FixtureObjective(float t, ScopeScene s);
    static partial void FixtureStores(float t, ScopeScene s);

    private static void Fixture(string page, float t, ScopeScene s)
    {
        switch (page)
        {
            case "systems": FixtureSystems(t, s); break;
            case "threat": FixtureThreat(t, s); break;
            case "shot": FixtureShot(t, s); break;
            case "missilecam": FixtureMissileCam(t, s); break;
            case "approach": FixtureApproach(t, s); break;
            case "wing": FixtureWing(t, s); break;
            case "strike": FixtureStrike(t, s); break;
            case "dock": FixtureDock(t, s); break;
            case "objective": FixtureObjective(t, s); break;
            case "stores": FixtureStores(t, s); break;
            default: throw new ArgumentException("Unknown scope page: " + page);
        }
    }

    /// <summary>What a fixture sees: the scenario time, the frame the board is handed this tick and the helpers that fill game-internal types by name.</summary>
    private sealed class ScopeScene
    {
        public readonly float T;
        public readonly Vector2 Size;
        public readonly object Frame;
        /// <summary>null for the standard shots; a page's own extra variant (<see cref="ScopeVariants"/>) otherwise, e.g. "rotary".</summary>
        public readonly string Variant;
        public bool Touched;

        public ScopeScene(float t, Vector2 size, object frame, string variant = null) { T = t; Size = size; Frame = frame; Variant = variant; }

        /// <summary>Set a field of the board's frame (<c>Systems</c>, <c>Threat</c>, <c>Ticker</c>, <c>HasTicker</c>, <c>BootAge</c>...).</summary>
        public void SetFrame(string field, object value) { Set(Frame, field, value); Touched = true; }

        /// <summary>A new Hud domain value (struct or class) by name, e.g. <c>Domain("DamageState")</c>.</summary>
        public object Domain(string name) => Boxed(name);

        /// <summary>A new Core contract value by name, e.g. <c>Contract("GaugeSpec")</c>.</summary>
        public object Contract(string name) => Activator.CreateInstance(Type("BoscaliSummer.Core.Contracts." + name));

        public object DomainEnum(string name, string value) => EnumOf(name, value);

        public object ContractEnum(string name, string value) => Enum.Parse(Type("BoscaliSummer.Core.Contracts." + name), value);

        /// <summary>Set several fields by name on a boxed value: <c>s.Fill(box, "Level", .3f, "Valid", true)</c>.</summary>
        public object Fill(object box, params object[] nameValue)
        {
            for (int i = 0; i + 1 < nameValue.Length; i += 2) Set(box, (string)nameValue[i], nameValue[i + 1]);
            return box;
        }

        /// <summary>An array of <paramref name="count"/> Hud domain values of one type, filled by the caller with <see cref="Array.SetValue(object,int)"/>.</summary>
        public Array DomainArray(string name, int count) => Array.CreateInstance(Type(HudDomain + name), count);

        /// <summary>Make the feed struct <paramref name="data"/> what its page reads (<c>ScopeFeeds.Publish</c>). Hud-owned and Core feeds alike.</summary>
        public void Publish(object data)
        {
            Type(("BoscaliSummer.Core.Contracts.ScopeFeeds")).GetMethod("Publish", All).MakeGenericMethod(data.GetType()).Invoke(null, new[] { data });
            Touched = true;
        }

        /// <summary>A registered gauge (it fills the row after the page's own).</summary>
        public void Gauge(string id, object spec)
        {
            Type("BoscaliSummer.Core.Contracts.ScopeGauges").GetMethod("Publish", All).Invoke(null, new[] { id, spec });
            Touched = true;
        }

        /// <summary>An assertion about this render (counted, and it fails the check).</summary>
        public void Expect(bool condition, string message) => Check(condition, message);
    }

    private static string ArgValue(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return args[i + 1];
        return null;
    }

    private static string[] ScopePagesArg()
    {
        string value = ArgValue("-pages");
        return string.IsNullOrEmpty(value) ? new string[0] : value.ToLowerInvariant().Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void ScopeShots(string[] pages)
    {
        string[] names = pages.Length == 0 ? ScopePageNames : pages;
        foreach (string page in names)
        {
            if (page == "shell" || page == "frame" || page == "lines" || page == "corners" || page == "repair") continue;      // the board shell (HudLogUnityCheck.Scope.Shell.cs), its frame (HudLogUnityCheck.Scope.Frame.cs), the wing lines (HudLogUnityCheck.Scope.Lines.cs) and the other corners (HudLogUnityCheck.Scope.Corners.cs) and the review fixes (HudLogUnityCheck.Scope.Repair.cs), below
            Check(Array.IndexOf(ScopePageNames, page) >= 0, "Unknown scope page in -pages: " + page);
            int drawn = 0;
            if (page != "stores") foreach (var shot in ScopeShotTimes) if (RenderScope(page, shot.t, new Vector2(shot.w, shot.h))) drawn++;
            // Extra shots a page asks for on top of the standard five (named <page>-t<sec>-<variant>-<W>x<H>.png; the compare sheet leaves them out).
            foreach (var extra in ScopeVariants) if (extra.page == page && RenderScope(page, extra.t, new Vector2(1920, 1080), extra.variant)) drawn++;
            Debug.Log("[HudLogCheck] scope page " + page + ": " + (drawn > 0 ? drawn + " shots" : "stub fixture, skipped"));
        }
        if (pages.Length == 0 || Array.IndexOf(pages, "shell") >= 0) ScopeShell();
        if (pages.Length == 0 || Array.IndexOf(pages, "frame") >= 0) ScopeFrame();
        if (pages.Length == 0 || Array.IndexOf(pages, "lines") >= 0) ScopeLines();
        if (pages.Length == 0 || Array.IndexOf(pages, "stores") >= 0) ScopeStoresBlocks();
        if (pages.Length == 0 || Array.IndexOf(pages, "corners") >= 0) ScopeCorners();
        if (pages.Length == 0 || Array.IndexOf(pages, "repair") >= 0) ScopeRepair();
    }

    /// <summary>The spec's cockpit capture with the native top-right panel painted out (the backdrop of the takeover shots), else the older clean capture, else a hazy sky.</summary>
    private static void ScopeBackdrop(Canvas canvas)
    {
        string spec = ArgValue("-spec");
        string path = spec != null ? Path.Combine(spec, "takeover", "assets", "bg-cockpit-hud-takeover.jpg") : null;
        if (path == null || !File.Exists(path)) path = spec != null ? Path.Combine(spec, "assets", "bg-cockpit-hud-clean.jpg") : null;
        if (path == null || !File.Exists(path)) { Sky(canvas); return; }
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        ImageConversion.LoadImage(texture, File.ReadAllBytes(path));
        var bg = new GameObject("Cockpit", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvas.transform, false); bg.transform.SetAsFirstSibling();
        var rect = (RectTransform)bg.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = bg.GetComponent<Image>(); image.raycastTarget = false; image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
    }

    private static void ScopeReset()
    {
        Type("BoscaliSummer.Core.Contracts.ScopeFeeds").GetMethod("Clear", All).Invoke(null, null);
        Type("BoscaliSummer.Core.Contracts.ScopeGauges").GetMethod("Clear", All).Invoke(null, null);
    }

    /// <summary>
    /// One page at one scenario time: the board built the way production builds it (stand-in native weapon cell and label in the mono face),
    /// docked at the screen's top-right corner (8 units in from the right edge, 6 down at 1080p), handed one frame by the page's fixture.
    /// </summary>
    private static bool RenderScope(string page, float t, Vector2 size, string variant = null)
    {
        MakeBoard(size, t, out Canvas canvas, out object status, out object frame);
        // Earlier moments of the scenario the page needs to have SEEN (a countermeasure call is judged against the rounds when its missile first became the nearest).
        foreach (float before in PreRoll(page, t))
        {
            var rehearsal = new ScopeScene(before, size, frame, variant);
            Fixture(page, before, rehearsal);
            StoresFixture(page, before, rehearsal);
            Set(frame, "Now", before);
            Call(status, "Present", frame);
        }
        Set(frame, "Now", t);
        var scene = new ScopeScene(t, size, frame, variant);
        Fixture(page, t, scene);
        if (!scene.Touched)
        {
            Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
            return false;
        }
        StoresFixture(page, t, scene);
        Call(status, "Present", frame);
        Canvas.ForceUpdateCanvases();
        string tag = page + "-t" + t.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + (variant != null ? "-" + variant : "") + "-" + (int)size.x + "x" + (int)size.y;
        ScopeChecks(status, tag);
        if (variant == "service-zone")
            foreach (var label in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (label != null && label.gameObject.activeInHierarchy)
                    Check(!(label.text ?? "").Contains("BRAKE"), "Service-zone presentation must not instruct a brake-to-dock maneuver: " + tag);
        if (variant == "longnames") ThreatLongNamesChecks(status, tag, t);
        Capture(canvas, tag + ".png");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
        return true;
    }

    /// <summary>The board on a canvas the way production builds it from stand-in native pieces, with the feeds cleared and an empty frame at time <paramref name="t"/>.</summary>
    private static void MakeBoard(Vector2 size, float t, out Canvas canvas, out object status, out object frame)
    {
        ScopeReset();
        canvas = MakeCanvas("Scope", size);
        ScopeBackdrop(canvas);
        // The stand-in for the native TopRightPanel: a panel anchored at the screen's top right like the real one (the board takes the screen's corner, wherever its dock is).
        float k = size.y / 1080f;
        var dock = new GameObject("TopRightPanel", typeof(RectTransform)).GetComponent<RectTransform>();
        dock.SetParent(canvas.transform, false); dock.sizeDelta = new Vector2(460f * k, 225f * k);
        dock.anchoredPosition = new Vector2(size.x / 2f - dock.sizeDelta.x / 2f, size.y / 2f - dock.sizeDelta.y / 2f);
        TMP_FontAsset font = HudFont();
        TextMeshProUGUI nameLabel = NativeLabel(canvas.transform, "NativeName", font, 28f * k);
        status = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.StatusPanel"), true);
        Call(status, "Build", dock, nameLabel, null);
        nameLabel.gameObject.SetActive(false);
        frame = Boxed("BoardFrame");
        Set(frame, "Now", t); Set(frame, "BootAge", -1f); Set(frame, "Units", AvUnits.Metric);
    }

    /// <summary>What every scope render must keep true: the board shows, every word stays inside the board's box, nothing takes a click, no mesh overflowed.</summary>
    private static void ScopeChecks(object status, string tag)
    {
        var root = (RectTransform)Field(status, "root");
        Check(root.gameObject.activeSelf, "The board shows: " + tag);
        foreach (Graphic g in root.GetComponentsInChildren<Graphic>(true)) Check(!g.raycastTarget, "HUD must not intercept pointer input: " + tag);
        foreach (string mesh in new[] { "shellVector", "pageVector", "overlayVector", "frameVector", "storesVector" })
            Check(!((AvVector)Field(status, mesh)).Buffer.Overflowed, "The " + mesh + " buffer did not fill: " + tag);
        var corners = new Vector3[4];
        foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (string.IsNullOrEmpty(label.text)) continue;
            label.ForceMeshUpdate();
            // The ink: the drawn glyphs' corners (textBounds is the line box, 1.3 em high: a 12 unit line at the very top of the box would fail on its padding).
            TMP_TextInfo info = label.textInfo;
            var lo = new Vector3(float.MaxValue, float.MaxValue);
            var hi = new Vector3(float.MinValue, float.MinValue);
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo c = info.characterInfo[i];
                if (!c.isVisible) continue;
                lo = new Vector3(Mathf.Min(lo.x, c.bottomLeft.x), Mathf.Min(lo.y, c.bottomLeft.y));
                hi = new Vector3(Mathf.Max(hi.x, c.topRight.x), Mathf.Max(hi.y, c.topRight.y));
            }
            if (lo.x > hi.x) continue;
            Vector3[] box = { lo, hi };
            foreach (Vector3 p in box)
            {
                Vector3 local = root.InverseTransformPoint(label.transform.TransformPoint(p));
                Check(local.x >= root.rect.xMin - 2f && local.x <= root.rect.xMax + 2f && local.y >= root.rect.yMin - 2f && local.y <= root.rect.yMax + 2f,
                    "Board text sits inside the board: [" + label.text + "] (" + tag + ")");
            }
        }
    }
}
#endif
