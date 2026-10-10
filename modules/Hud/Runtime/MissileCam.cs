
namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The missile-cam gate (2026-10-10): the C-menu's MISSILE VIEW rides one of our missiles with the orbit camera. <see cref="IsOwnMissileView"/> is the pure query (the local
    /// player's live aircraft, the orbit camera, the followed unit is that aircraft's own missile): the native HUD canvas is forced open on it and the MISSILE CAM page of the
    /// TAC BOARD is fed from it (<see cref="MissileCamScopeFeed"/>). Read-only.
    /// </summary>
    internal static class MissileCam
    {
        public static bool IsOwnMissileView(out Aircraft own, out Missile missile)
        {
            own = null;
            missile = null;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            Aircraft candidate = hud != null ? hud.aircraft : null;
            if (cam == null || candidate == null || candidate.disabled || candidate.HasEjected() || candidate.Player == null || !candidate.Player.IsLocalPlayer) return false;
            if (!(cam.followingUnit is Missile followed) || followed.disabled || followed.owner != candidate) return false;
            if (cam.currentState != cam.orbitState) return false;
            own = candidate;
            missile = followed;
            return true;
        }
    }
}
