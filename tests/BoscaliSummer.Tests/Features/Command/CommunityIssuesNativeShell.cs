#if UNITY_EDITOR
using System.Collections.Generic;

// Suppress scene/network startup while retaining the installed game's native types.
public sealed class CommunityPreviewTruck : GroundVehicle
{
    public readonly List<UnitPart> Parts = new List<UnitPart>();
    public override void Awake() { }
    public override void OnEnable() { }
    protected override void OnDestroy() { }
    public override List<UnitPart> GetAllParts() => Parts;
}
public sealed class CommunityPreviewScreen : MFDScreen { private void Start() { } }
public sealed class CommunityPreviewMfd : VirtualMFD { private void Start() { } private void Update() { } }
public sealed class CommunityPreviewFaction : FactionHQ { private void Awake() { } }
public sealed class CommunityPreviewPart : UnitPart { protected override void Awake() { } }
public sealed class CommunityPreviewRadar : Radar { private void Awake() { } }
public sealed class CommunityPreviewSpawner : Spawner { protected override void Awake() { } }
#endif
