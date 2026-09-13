using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Scene service that feeds <see cref="FactionResourceHistoryStore"/> every frame,
    /// independent of whether the map or any panel is open. Mission-scoped: the scene
    /// lifecycle clears the buffer on load so a new mission never inherits readings.
    /// </summary>
    internal sealed class FactionResourceRecorder : MonoBehaviour, ISceneService
    {
        private void Update() => FactionResourceHistoryStore.Sample();

        public void ResetForScene() => FactionResourceHistoryStore.Clear();

        private void OnDestroy() => FactionResourceHistoryStore.Clear();
    }
}
