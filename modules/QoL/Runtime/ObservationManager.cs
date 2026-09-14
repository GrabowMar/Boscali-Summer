using BoscaliSummer.Features.QoL.Configuration;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.MissionEditorScripts;
using UnityEngine;

namespace BoscaliSummer.Features.QoL.Runtime
{
    internal sealed class ObservationManager : MonoBehaviour, ISceneService, IObservationSource
    {
        private readonly ObservationStore store = new ObservationStore();
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private QoLSettings settings;
        private Aircraft owner;
        private FactionHQ faction;
        public string Status { get; private set; } = "No camera mark.";
        public static ObservationManager Instance { get; private set; }
        public string ShortcutHint => settings == null || !settings.ObservationEnabled.Value
            ? "CAMERA MARKS DISABLED" : settings.MarkCameraKey.Value != KeyCode.None
                ? settings.MarkCameraKey.Value + ": MARK CAMERA" : "MARK CAMERA IN TGT CAMERA";

        private void Awake() => Instance = this;
        public void Configure(QoLSettings config) => settings = config;
        public bool CanCapture => TryOwnship(out Aircraft aircraft) &&
            NativeCamera.TryGet(aircraft, out _, out _);

        private bool TryOwnship(out Aircraft aircraft)
        {
            aircraft = null;
            if (settings == null || !settings.ObservationEnabled.Value || Application.isBatchMode ||
                GameplayUI.GameIsPaused || !GameManager.GetLocalAircraft(out aircraft) || aircraft == null ||
                aircraft.disabled || aircraft.HasEjected() || aircraft.Player == null || !aircraft.Player.IsLocalPlayer ||
                aircraft.cockpit == null || aircraft.cockpit.IsDetached()) return false;
            CameraStateManager view = SceneSingleton<CameraStateManager>.i;
            return view != null && view.followingUnit == aircraft;
        }

        private void Update()
        {
            TryGet(out _);
            if (settings != null && !InputFieldChecker.InsideInputField && !GameplayUI.GameIsPaused &&
                Input.GetKeyDown(settings.MarkCameraKey.Value)) Capture();
        }

        public bool Capture()
        {
            Clear();
            if (!TryOwnship(out Aircraft aircraft) || !NativeCamera.TryGet(aircraft, out Camera camera, out string mode))
            {
                Status = "Native camera unavailable.";
                return false;
            }

            float range = Mathf.Min(60000f, camera.farClipPlane);
            if (!ObservationStore.Finite(range) || range <= 0f)
            {
                Status = "Camera range unavailable.";
                return false;
            }
            Ray ray = new Ray(camera.transform.position, camera.transform.forward);
            int count = Physics.RaycastNonAlloc(ray, hits, range, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
            {
                Status = "View obstructed: mark not resolved.";
                return false;
            }
            int nearest = -1;
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(aircraft.transform)) continue;
                UnitPart part = collider.GetComponentInParent<UnitPart>();
                if (part != null && part.parentUnit == aircraft) continue;
                if (nearest < 0 || hits[i].distance < hits[nearest].distance) nearest = i;
            }
            if (nearest < 0)
            {
                Status = "No surface in camera view.";
                return false;
            }
            GlobalPosition position = hits[nearest].point.ToGlobalPosition();
            if (!store.Set(new ObservationPoint(position.x, position.y, position.z,
                Time.unscaledTime, hits[nearest].distance, mode)))
            {
                Status = "Camera mark invalid.";
                return false;
            }
            owner = aircraft;
            faction = aircraft.NetworkHQ;
            Status = "Camera point marked. Select support, then CALL AT MARK.";
            return true;
        }

        public bool TryGet(out ObservationPoint point)
        {
            point = default;
            // Pause hides the UI but must not masquerade as an ownship change.
            if (owner == null)
            {
                if (!ReferenceEquals(owner, null)) Clear();
                return false;
            }
            CameraStateManager view = SceneSingleton<CameraStateManager>.i;
            if (settings == null || !settings.ObservationEnabled.Value || owner.disabled ||
                !GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft != owner ||
                view == null || view.followingUnit != owner || aircraft.HasEjected() ||
                aircraft.Player == null || !aircraft.Player.IsLocalPlayer || aircraft.NetworkHQ != faction ||
                aircraft.cockpit == null || aircraft.cockpit.IsDetached())
            {
                Clear();
                return false;
            }
            if (store.TryGet(Time.unscaledTime, out point)) return true;
            Clear();
            Status = "Camera mark expired. Mark again.";
            return false;
        }

        public void Clear()
        {
            store.Clear();
            System.Array.Clear(hits, 0, hits.Length);
            owner = null;
            faction = null;
            Status = "No camera mark.";
        }

        public void ResetForScene() => Clear();
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
        }
    }
}
