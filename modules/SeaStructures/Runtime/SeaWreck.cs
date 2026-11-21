using BoscaliSummer.Modules.SeaStructures.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>
    /// The burned skeleton left when a platform's last required point goes. Every peer builds it from the same
    /// model: the sections' core pieces, charred, with a few fires. It lists, settles and sinks over four minutes,
    /// taking its fire with it, then is removed. Debris chunks live on their own timer.
    /// </summary>
    internal sealed class SeaWreck : MonoBehaviour
    {
        private Mesh mesh;
        private float born, seconds, depth, tilt;
        private Vector3 from;
        private Quaternion rotation;

        internal void Configure(SeaStructure owner, SeaFire fire, Mesh mesh, float seconds, float depth, float tilt)
        {
            this.mesh = mesh;
            this.seconds = Mathf.Max(1f, seconds);
            this.depth = depth;
            this.tilt = tilt;
            // Seat the skeleton in the water: the section cores start at the deck/pontoon tops, and a wreck
            // that hovers above the surface reads as a bug. Lowering it also hides the missing underwater hull.
            transform.localPosition += Vector3.down * 8f;
            from = transform.localPosition;
            rotation = transform.localRotation;
            born = Time.time;

            // A few fires on the remains: every fourth section's core, so the hulk reads as burning, not uniform.
            SeaLayout layout = owner != null ? owner.Layout : null;
            if (layout == null || fire == null) return;
            for (int s = 0; s < layout.Sections.Length; s += 4)
            {
                SeaSection section = layout.Sections[s];
                SeaPiece piece = null;
                foreach (SeaPiece candidate in section.Pieces)
                    if (candidate.Core) { piece = candidate; break; }
                if (piece == null) continue;
                Vector3 local = section.At + piece.Centre;
                float scale = Mathf.Clamp(piece.Size.magnitude * 0.16f, 2f, 7f);
                fire.Attach(transform, local, scale, true);
                fire.Attach(transform, local + Vector3.up * 2f, scale * 0.8f, false);
            }
        }

        private void Update()
        {
            float age = Time.time - born;
            if (age > seconds + 45f) { Destroy(gameObject); return; }
            float k = Mathf.Clamp01(age / seconds);
            transform.localPosition = from + Vector3.down * (depth * k);
            transform.localRotation = rotation * Quaternion.Euler(tilt * k, 8f * k, tilt * 0.6f * k);
        }

        internal void Skip(float seconds)
        {
            born -= Mathf.Max(0f, seconds);
        }

        internal float Age => Time.time - born;
        internal float Sink => Mathf.Clamp01((Time.time - born) / seconds);

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            mesh = null;
        }
    }
}
