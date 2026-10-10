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
//   one fixture file per page:  HudLogUnityCheck.Scope.<Name>.cs   (Systems is done; the others are empty stubs a page group fills in)
//   a fixture is a  static partial void Fixture<Name>(float t, ScopeScene s)  that makes the page live for scenario time t:
//       s.Publish(<the page's feed struct, filled by name>)   what the page's provider returns at t (ScopeFeeds.Publish)
//       s.Gauge(id, <GaugeSpec>)                                a registered gauge (the prototype's defaults: lance, perk, radio, ap)
//       s.SetFrame("Systems", <SystemStates>) ...               lamps, threat picture, ticker line, anything on the BoardFrame
//   and reproduces the prototype page's scenario for that t (pages/<id>.js sim(t)). Fixtures are the ONLY place scripted data lives.
//   A fixture that does not touch the scene is a stub: that page is skipped.
// Output: <page>-t<sec>-<W>x<H>.png (t = 2, 6, 10, 14 at 1920x1080 and t = 6 at 1280x720), the same names as the spec shots.
// Command line (after -executeMethod): -pages systems,threat   only those pages' fixtures (default: all, plus the rest of the check; 'shell' = the board shell alone)
//                                      -spec <spec folder>     draws the shots over the spec's cockpit capture (assets/bg-cockpit-hud-clean.jpg)
public static partial class HudLogUnityCheck
{
    private static readonly string[] ScopePageNames = { "systems", "threat", "shot", "missilecam", "approach", "wing", "strike", "dock", "objective" };
    private static readonly (float t, int w, int h)[] ScopeShotTimes = { (2f, 1920, 1080), (6f, 1920, 1080), (10f, 1920, 1080), (14f, 1920, 1080), (6f, 1280, 720) };

    /// <summary>(page, t, variant): one more 1080p shot of a page, its fixture branching on <c>s.Variant</c>.</summary>
    private static readonly (string page, float t, string variant)[] ScopeVariants = { ("systems", 10f, "rotary") };

    static partial void FixtureSystems(float t, ScopeScene s);
    static partial void FixtureThreat(float t, ScopeScene s);
    static partial void FixtureShot(float t, ScopeScene s);
    static partial void FixtureMissileCam(float t, ScopeScene s);
    static partial void FixtureApproach(float t, ScopeScene s);
    static partial void FixtureWing(float t, ScopeScene s);
    static partial void FixtureStrike(float t, ScopeScene s);
    static partial void FixtureDock(float t, ScopeScene s);
    static partial void FixtureObjective(float t, ScopeScene s);

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
            if (page == "shell") continue;      // the board shell itself (HudLogUnityCheck.Scope.Shell.cs), below
            Check(Array.IndexOf(ScopePageNames, page) >= 0, "Unknown scope page in -pages: " + page);
            int drawn = 0;
            foreach (var shot in ScopeShotTimes) if (RenderScope(page, shot.t, new Vector2(shot.w, shot.h))) drawn++;
            // Extra shots a page asks for on top of the standard five (named <page>-t<sec>-<variant>-<W>x<H>.png; the compare sheet leaves them out).
            foreach (var extra in ScopeVariants) if (extra.page == page && RenderScope(page, extra.t, new Vector2(1920, 1080), extra.variant)) drawn++;
            Debug.Log("[HudLogCheck] scope page " + page + ": " + (drawn > 0 ? drawn + " shots" : "stub fixture, skipped"));
        }
        if (pages.Length == 0 || Array.IndexOf(pages, "shell") >= 0) ScopeShell();
    }

    /// <summary>The spec's cockpit capture (the backdrop of its shots), else a hazy sky.</summary>
    private static void ScopeBackdrop(Canvas canvas)
    {
        string spec = ArgValue("-spec");
        string path = spec != null ? Path.Combine(spec, "assets", "bg-cockpit-hud-clean.jpg") : null;
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
    /// docked where the prototype puts it (right edge x = 1904, top y = 152 at 1080p), handed one frame by the page's fixture.
    /// </summary>
    private static bool RenderScope(string page, float t, Vector2 size, string variant = null)
    {
        MakeBoard(size, t, out Canvas canvas, out object status, out object frame);
        var scene = new ScopeScene(t, size, frame, variant);
        Fixture(page, t, scene);
        if (!scene.Touched)
        {
            Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
            return false;
        }
        Call(status, "Present", frame);
        Canvas.ForceUpdateCanvases();
        string tag = page + "-t" + t.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + (variant != null ? "-" + variant : "") + "-" + (int)size.x + "x" + (int)size.y;
        ScopeChecks(status, tag);
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
        float k = size.y / 1080f;
        var dock = new GameObject("WeaponsDock", typeof(RectTransform)).GetComponent<RectTransform>();
        dock.SetParent(canvas.transform, false); dock.sizeDelta = new Vector2(452f * k, 150f * k);
        dock.anchoredPosition = new Vector2(1904f * k - size.x / 2f - dock.sizeDelta.x / 2f, size.y / 2f - 152f * k + dock.sizeDelta.y / 2f);
        TMP_FontAsset font = HudFont();
        var cell = new GameObject("NativeCell", typeof(RectTransform)).GetComponent<RectTransform>();
        cell.SetParent(canvas.transform, false); cell.sizeDelta = new Vector2(95f * k, 95f * k);   // one design unit = k pixels, as the prototype scales
        TextMeshProUGUI nameLabel = NativeLabel(canvas.transform, "NativeName", font, 28f * k);
        status = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.StatusPanel"), true);
        Call(status, "Build", dock, nameLabel, cell, null);
        cell.gameObject.SetActive(false); nameLabel.gameObject.SetActive(false);
        frame = Boxed("BoardFrame");
        Set(frame, "Now", t); Set(frame, "BootAge", -1f); Set(frame, "Units", AvUnits.Metric);
    }

    /// <summary>What every scope render must keep true: the board shows, every word stays inside the plate, nothing takes a click, no mesh overflowed.</summary>
    private static void ScopeChecks(object status, string tag)
    {
        var root = (RectTransform)Field(status, "root");
        Check(root.gameObject.activeSelf, "The board shows: " + tag);
        foreach (Graphic g in root.GetComponentsInChildren<Graphic>(true)) Check(!g.raycastTarget, "HUD must not intercept pointer input: " + tag);
        foreach (string mesh in new[] { "shellVector", "pageVector", "overlayVector", "frameVector" })
            Check(!((AvVector)Field(status, mesh)).Buffer.Overflowed, "The " + mesh + " buffer did not fill: " + tag);
        var corners = new Vector3[4];
        foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (string.IsNullOrEmpty(label.text)) continue;
            label.ForceMeshUpdate();
            Bounds b = label.textBounds;
            Vector3[] box = { new Vector3(b.min.x, b.min.y), new Vector3(b.max.x, b.max.y) };
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
