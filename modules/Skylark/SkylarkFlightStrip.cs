using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Skylark
{
    // A separate standby instrument leaves the native tactical and warning displays unobstructed.
    internal sealed class SkylarkFlightStrip : MonoBehaviour
    {
        private static readonly Color Normal = new Color(.66f, .84f, .83f);
        private static readonly Color Standby = new Color(.63f, .72f, .76f);
        private static readonly Color Caution = new Color(1f, .68f, .31f);
        private static readonly string[] Headers = { "FLIGHT", "FANS DEG", "DRIVE / PACK", "C2 MAST" };

        [SerializeField] private Aircraft aircraft;
        [SerializeField] private UnitPart cockpit;
        [SerializeField] private MeshRenderer housing;
        [SerializeField] private TextMeshPro[] readouts;
        [SerializeField] private Transform batteryBar;
        [SerializeField] private MeshRenderer batteryBarRenderer;
        private MaterialPropertyBlock barBlock;
        private SkylarkFlightMode flight;
        private SkylarkDrive drive;
        private SkylarkMast mast;
        private float nextSample;
        private int modeShown = -1, angleShown = -1, chargeShown = -1, mastShown = -1;
        private string driveShown;
        private bool dark, cautionShown;

        internal static void Install(Aircraft aircraft, UnitPart cockpit, Transform screen)
        {
            if (GameManager.IsHeadless || !screen.TryGetComponent(out MeshFilter screenMesh) || screenMesh.sharedMesh == null) return;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            TMP_FontAsset font = AvType.Face(AvFace.Mono);
            if (lit == null || font == null) return;

            Bounds bounds = screenMesh.sharedMesh.bounds;
            // Use the authored display's local frame so the bezel moves with the rest of the panel.
            float size = bounds.size.x / .98139f;
            var panel = new GameObject("SkylarkFlightStrip", typeof(MeshFilter), typeof(MeshRenderer), typeof(SkylarkFlightStrip));
            Transform mount = panel.transform;
            mount.SetParent(screen, false);
            mount.localPosition = bounds.center + new Vector3(0f, -.103f, -.196f) * size;
            mount.localRotation = Quaternion.LookRotation(new Vector3(0f, .848f, -.53f), new Vector3(0f, .53f, .848f));
            mount.localScale = Vector3.one * (.92f * size);
            panel.GetComponent<MeshFilter>().sharedMesh = MakeHousing();
            var surface = panel.GetComponent<MeshRenderer>();
            surface.sharedMaterials = new[]
            {
                Finish(lit, "Skylark graphite bezel", new Color(.09f, .105f, .112f), .24f),
                Finish(lit, "Skylark anti-glare instrument glass", new Color(.018f, .029f, .033f), .18f)
            };
            surface.shadowCastingMode = ShadowCastingMode.Off;

            var strip = panel.GetComponent<SkylarkFlightStrip>();
            strip.aircraft = aircraft;
            strip.cockpit = cockpit;
            strip.housing = surface;
            strip.readouts = new TextMeshPro[Headers.Length];
            for (int i = 0; i < strip.readouts.Length; i++)
            {
                var field = new GameObject(Headers[i], typeof(RectTransform), typeof(TextMeshPro));
                var text = field.GetComponent<TextMeshPro>();
                text.rectTransform.SetParent(mount, false);
                text.rectTransform.localPosition = new Vector3((i - 1.5f) * .151f, .001f, -.013f);
                text.rectTransform.localScale = Vector3.one * .00155f;
                text.rectTransform.sizeDelta = new Vector2(94f, 38f);
                text.font = font;
                text.fontSize = 17f;
                text.isOrthographic = true;
                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.color = Normal;
                text.text = string.Empty;
                text.renderer.shadowCastingMode = ShadowCastingMode.Off;
                strip.readouts[i] = text;
            }

            // Subtle column dividers, corner fasteners, and a compact live battery reserve bar under column 3.
            var trim = new SkylarkMeshKit.Builder();
            for (int d = 0; d < 3; d++)
                trim.Box(new Vector3((d - 1f) * .151f, 0f, -.0126f), new Vector3(.0022f, .048f, .002f));
            foreach (float cx in new[] { -.298f, .298f })
                foreach (float cy in new[] { -.024f, .024f })
                    trim.Cylinder(new Vector3(cx, cy, -.016f), new Vector3(cx, cy, -.013f), .004f, .004f, 8);
            trim.Attach("FlightStripTrim", mount, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, mount.gameObject.layer, castShadows: false);

            var barMesh = new SkylarkMeshKit.Builder();
            barMesh.Box(new Vector3(.052f, 0f, 0f), new Vector3(.104f, .0045f, .0018f));
            Transform bar = barMesh.Attach("FlightStripBattBar", mount, new Vector3(1.5f * .151f - .203f, -.0275f, -.0132f), Quaternion.identity, Vector3.one, SkylarkMeshKit.LampBase, mount.gameObject.layer, castShadows: false);
            strip.batteryBar = bar;
            strip.batteryBarRenderer = bar.GetComponent<MeshRenderer>();
        }

        private static Material Finish(Shader shader, string name, Color color, float smoothness)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static Mesh MakeHousing()
        {
            // Chamfered graphite lip around a recessed, matte instrument face. All dimensions are in metres.
            Vector2[] outline =
            {
                new Vector2(-.310f, -.026f), new Vector2(-.301f, -.036f), new Vector2(.301f, -.036f), new Vector2(.310f, -.026f),
                new Vector2(.310f, .026f), new Vector2(.301f, .036f), new Vector2(-.301f, .036f), new Vector2(-.310f, .026f)
            };
            var vertices = new Vector3[25];
            var bezel = new int[96];
            var face = new int[24];
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = outline[i];
                int next = (i + 1) % outline.Length;
                vertices[i] = new Vector3(p.x, p.y, .012f);
                vertices[i + 8] = new Vector3(p.x, p.y, -.015f);
                vertices[i + 16] = new Vector3(p.x * .976f, p.y * .82f, -.012f);
                for (int ring = 0; ring < 2; ring++)
                {
                    int a = i + ring * 8, b = next + ring * 8, n = (ring * 8 + i) * 6;
                    bezel[n] = b; bezel[n + 1] = a; bezel[n + 2] = a + 8;
                    bezel[n + 3] = b; bezel[n + 4] = a + 8; bezel[n + 5] = b + 8;
                }
                face[i * 3] = 24; face[i * 3 + 1] = next + 16; face[i * 3 + 2] = i + 16;
            }
            vertices[24] = new Vector3(0f, 0f, -.012f);
            var mesh = new Mesh { name = "Skylark flight strip housing", vertices = vertices, subMeshCount = 2 };
            mesh.SetTriangles(bezel, 0);
            mesh.SetTriangles(face, 1);
            mesh.RecalculateNormals();
            return mesh;
        }

        private void Start()
        {
            if (aircraft == null) return;
            flight = aircraft.GetComponent<SkylarkFlightMode>();
            drive = aircraft.GetComponent<SkylarkDrive>();
            mast = aircraft.GetComponent<SkylarkMast>();
            barBlock = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (housing == null || !housing.enabled || Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + .15f;
            bool offline = aircraft == null || cockpit == null || flight == null || drive == null || mast == null
                || aircraft.disabled || !aircraft.Ignition || cockpit.IsDetached();
            if (offline)
            {
                if (!dark)
                {
                    foreach (TextMeshPro readout in readouts) readout.text = string.Empty;
                    SkylarkMeshKit.SetLamp(batteryBarRenderer, barBlock, new Color(.03f, .04f, .04f), Color.black);
                }
                dark = true;
                return;
            }

            VectorMode mode = flight.Mode;
            int angle = Mathf.RoundToInt(flight.Angle);
            int charge = Mathf.RoundToInt(drive.Charge * 100f);
            string power = drive.Electric ? "EV" : drive.Dry ? "OFF" : drive.Boost ? "BST" : "HYB";
            bool caution = drive.Boost || drive.Charge <= SkylarkDrive.Floor;
            int mastState = mast.Deployed ? 1 : 0;
            if (dark || modeShown != (int)mode) Write(0, SkylarkFlightMode.Name(mode), Normal);
            if (dark || angleShown != angle) Write(1, angle.ToString("00"), Standby);
            if (dark || chargeShown != charge || driveShown != power || cautionShown != caution)
            {
                Write(2, power + " " + charge + "%", caution ? Caution : Normal);
                if (batteryBar != null)
                {
                    batteryBar.localScale = new Vector3(Mathf.Max(.04f, drive.Charge), 1f, 1f);
                    Color barColor = caution ? Caution : Normal;
                    SkylarkMeshKit.SetLamp(batteryBarRenderer, barBlock, barColor * .35f, barColor * .95f);
                }
            }
            if (dark || mastShown != mastState) Write(3, mast.Deployed ? "DEPLOYED" : "STOWED", mast.Deployed ? Caution : Standby);
            modeShown = (int)mode;
            angleShown = angle;
            chargeShown = charge;
            driveShown = power;
            mastShown = mastState;
            cautionShown = caution;
            dark = false;
        }

        private void Write(int index, string value, Color color)
        {
            readouts[index].color = color;
            readouts[index].text = "<size=62%>" + Headers[index] + "</size>\n" + value;
        }

#if !PUBLIC_RELEASE
        internal object Inspect() => new
        {
            visible = housing != null && housing.enabled && isActiveAndEnabled,
            powered = !dark && modeShown >= 0,
            mode = modeShown >= 0 ? SkylarkFlightMode.Name((VectorMode)modeShown) : null,
            angle = angleShown, drive = driveShown, charge = chargeShown, caution = cautionShown
        };
#endif
    }
}
