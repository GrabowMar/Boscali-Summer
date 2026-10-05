#if UNITY_EDITOR
// A source-registered runtime-assembly subclass provides a real native Component
// for production GetComponentInChildren calls. It stays inactive throughout the fixture.
public sealed class WingHudNativeAdapter : CombatHUD
{
    protected override void Awake() { }
}
#endif
