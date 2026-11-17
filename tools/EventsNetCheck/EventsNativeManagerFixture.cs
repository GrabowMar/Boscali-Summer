#if UNITY_EDITOR
using NuclearOption.Networking;

// Unity cannot attach the game's Assembly-CSharp MonoScript directly in this editor project.
// The fixture supplies only the native Client field, without running menu/transport startup.
public sealed class EventsNativeManagerFixture : NetworkManagerNuclearOption
{
    public new void Awake() { }
}
#endif
