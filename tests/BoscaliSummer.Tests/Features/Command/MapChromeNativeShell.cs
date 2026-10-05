#if UNITY_EDITOR
// Compiled in a runtime asmdef: Unity requires runtime registration even for a
// synthetic MonoBehaviour used only during an editor Play Mode preview.
public sealed class ChromePreviewDynamicMap : DynamicMap
{
    protected override void Awake() { }
    private void OnEnable() { }
    private void Start() { }
    private void Update() { }
    private void OnDestroy() { }
}
#endif
