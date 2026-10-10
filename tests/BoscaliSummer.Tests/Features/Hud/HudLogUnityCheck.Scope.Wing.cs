#if UNITY_EDITOR
// WING fixture (spec pages/wing.js). STUB: tb-flight fills this in. Reproduce the prototype's sim(t) for t = 2, 6, 10, 14 here:
//   s.Publish(<WingScope filled by name: s.Fill(s.Domain("...") or s.Contract("..."), "Field", value, ...)>);
//   s.SetFrame("Threat" / "Ticker" / "HasTicker" / "Systems" ...), s.Gauge(id, spec), and s.Expect(...) for page-specific assertions.
// Touching the scene makes the page render; this empty body makes the check skip it.
public static partial class HudLogUnityCheck
{
    static partial void FixtureWing(float t, ScopeScene s)
    {
    }
}
#endif
