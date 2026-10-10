#if UNITY_EDITOR
// DOCK fixture (spec pages/dock.js). STUB: tb-flight fills this in. Reproduce the prototype's sim(t) for t = 2, 6, 10, 14 here:
//   s.Publish(<DockScope filled by name: s.Fill(s.Domain("...") or s.Contract("..."), "Field", value, ...)>);
//   s.SetFrame("Threat" / "Ticker" / "HasTicker" / "Systems" ...), s.Gauge(id, spec), and s.Expect(...) for page-specific assertions.
// Touching the scene makes the page render; this empty body makes the check skip it.
public static partial class HudLogUnityCheck
{
    static partial void FixtureDock(float t, ScopeScene s)
    {
    }
}
#endif
