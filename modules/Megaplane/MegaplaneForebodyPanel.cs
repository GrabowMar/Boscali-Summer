using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Air load on the four removable skins surrounding the Groza flight deck.</summary>
    internal sealed class MegaplaneForebodyPanel : MonoBehaviour
    {
        // Share of the original bridge-section mass. The reinforced occupied core retains the balance.
        internal static readonly (string name, float share, Vector3 face)[] Sections = {
            ("foreCheek_L", .036f, new Vector3(-.94f, .09f, .32f)),
            ("foreCheek_R", .036f, new Vector3( .94f, .09f, .32f)),
            ("bridgeCrown", .027f, new Vector3(0f, .97f, .24f)),
            ("noseChin", .101f, new Vector3(0f, -.93f, .37f))
        };

        [SerializeField] private Vector3 face;
        private Rigidbody body;
        private AeroPart part;

        internal void SetFace(Vector3 normal) => face = normal.normalized;

        private void Awake()
        {
            part = GetComponent<AeroPart>();
            part.onPartDetached += OnDetach;
            enabled = false;
        }

        // The native part creates its rigidbody immediately after raising this event.
        private void OnDetach(UnitPart _) => enabled = true;

        internal Vector3 ReleaseVelocity(Vector3 inherited)
        {
            // A broken fastener at rest gives no impulse. In flight, the skin peels
            // clear in the direction it actually faces and carries the hull's point velocity.
            float airflow = Mathf.Max(0f, Vector3.Dot(inherited, transform.forward));
            return inherited + transform.TransformDirection(face) * Mathf.Min(airflow * .039f, 6.5f);
        }

        private void FixedUpdate()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
                if (body == null) return;
                body.drag = .045f;
                body.angularDrag = .17f;
            }
            if (body.isKinematic || body.IsSleeping()) return;
            // Pressure acts behind the mass centre. A broadside piece tumbles and sheds
            // speed, while edge-on skin initially carries on along the aircraft's path.
            Vector3 faceWorld = transform.TransformDirection(face);
            Vector3 pressurePoint = body.worldCenterOfMass - transform.forward * 1.35f;
            float across = Vector3.Dot(body.GetPointVelocity(pressurePoint), faceWorld);
            float deceleration = Mathf.Clamp(across * Mathf.Abs(across) * .0022f, -24f, 24f);
            body.AddForceAtPosition(-faceWorld * (deceleration * body.mass), pressurePoint, ForceMode.Force);
        }

        private void OnDestroy()
        {
            if (part != null) part.onPartDetached -= OnDetach;
        }
    }
}
