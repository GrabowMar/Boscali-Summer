#if UNITY_EDITOR
// MISSILECAM fixture (spec pages/missilecam.js). STUB: tb-combat fills this in. Reproduce the prototype's sim(t) for t = 2, 6, 10, 14 here:
//   s.Publish(<MissileCamScope filled by name: s.Fill(s.Domain("...") or s.Contract("..."), "Field", value, ...)>);
//   s.SetFrame("Threat" / "Ticker" / "HasTicker" / "Systems" ...), s.Gauge(id, spec), and s.Expect(...) for page-specific assertions.
// Touching the scene makes the page render; this empty body makes the check skip it.
public static partial class HudLogUnityCheck
{
    static partial void FixtureMissileCam(float t, ScopeScene s)
    {
    }
}
#endif
