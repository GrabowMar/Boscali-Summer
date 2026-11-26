using System;
using System.Collections.Generic;
using System.Linq;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>
    /// Physical cockpit controls, animated switches, gear handle, rudder pedals, annunciator cluster,
    /// HUD collimator projector, consoles, canopy interior hardware, and balanced night instrument lighting.
    /// </summary>
    internal sealed class SkylarkCockpitDetails : MonoBehaviour
    {
        private static readonly string[] AnnunciatorLabels = { "EV", "BST", "BAT", "DRY", "C2", "GR", "FUL", "CNP" };
        private static readonly Color LedOff = new Color(.04f, .05f, .055f);
        private static readonly Color LedGreen = new Color(.14f, .95f, .34f);
        private static readonly Color LedAmber = new Color(1f, .62f, .12f);
        private static readonly Color LedRed = new Color(1f, .18f, .14f);
        private static readonly Color LedCyan = new Color(.18f, .86f, 1f);

        [SerializeField] private Aircraft aircraft;
        [SerializeField] private UnitPart cockpitPart;
        [SerializeField] private Transform gearLever, modeSwitch, driveKnob, mastGuard, mastSwitch, armSwitch;
        [SerializeField] private Transform canopyLockHandle, thumbwheel, ecsToggle, navToggle;
        [SerializeField] private Transform pedalL, pedalR, pedalPlateL, pedalPlateR;
        [SerializeField] private Renderer gearKnobLed, flapLed, modeDrnLed, modePlnLed, c2TxLed;
        [SerializeField] private Renderer[] gearWheelLeds; // F, L, R
        [SerializeField] private Renderer[] coilChargeLeds; // 5 segments
        [SerializeField] private Renderer[] annunciatorLeds; // 8 lamps
        [SerializeField] private TextMeshPro[] annunciatorTexts;
        [SerializeField] private Renderer[] keypadLeds; // backlit keypad strip
        [SerializeField] private Light[] consoleFloods;
        [SerializeField] private int controlCount;
        private Vector3 pedalRestL, pedalRestR;
        private Quaternion plateRestL, plateRestR, thumbRest;
        private LandingGear[] gears;
        private NavLights navLights;
        private Renderer navSourceLens;
        private Canopy canopy;
        private SkylarkFlightMode flight;
        private SkylarkDrive drive;
        private SkylarkMast mast;
        private SkylarkTurret turret;
        private ControlInputs inputs;
        private MaterialPropertyBlock mpb;
        private float gearPos, modePos, driveAngle, mastPos, armPos, canopyPos, shotFlash;
        private int lastShots = -1;
        private float nextSlow;

        internal int InteractiveControlCount => controlCount;

        internal static SkylarkCockpitDetails Install(Aircraft aircraft, UnitPart cockpitPart, Dictionary<string, Transform> nodes, int cockpitLayer)
        {
            Transform cockpitRoot = nodes["Cockpit"];
            float size = SkylarkTemplate.Scale / 1.15f;
            var host = new GameObject("SkylarkCockpitDetails") { layer = cockpitLayer };
            host.transform.SetParent(cockpitRoot, false);
            host.transform.position = aircraft.transform.position;
            host.transform.rotation = aircraft.transform.rotation;
            host.transform.localScale = Vector3.one * size;

            var details = host.AddComponent<SkylarkCockpitDetails>();
            details.aircraft = aircraft;
            details.cockpitPart = cockpitPart;
            details.BuildAll(aircraft, nodes, host.transform, cockpitLayer, size);
            return details;
        }

        private void BuildAll(Aircraft owner, Dictionary<string, Transform> nodes, Transform root, int layer, float size)
        {
            TMP_FontAsset font = AvType.Face(AvFace.Mono);
            int controls = 0;

            // 1. HUD collimator housing & combiner lens on top of glareshield (kept below eye-line).
            Transform hud = Frame("HudProjector", root, new Vector3(0f, .435f, 2.09f), Quaternion.Euler(12f, 0f, 0f), layer);
            var hudBox = new SkylarkMeshKit.Builder();
            hudBox.Box(new Vector3(0f, -.005f, 0f), new Vector3(.21f, .026f, .11f));
            hudBox.Box(new Vector3(-.098f, .028f, -.02f), new Vector3(.014f, .052f, .055f));
            hudBox.Box(new Vector3(.098f, .028f, -.02f), new Vector3(.014f, .052f, .055f));
            hudBox.Attach("HudCollimatorBody", hud, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.ConsoleDark, layer, false);
            var hudLens = new SkylarkMeshKit.Builder();
            hudLens.Cylinder(new Vector3(0f, .006f, .005f), new Vector3(0f, .014f, .005f), .062f, .062f, 14);
            hudLens.Box(new Vector3(0f, .036f, -.018f), new Vector3(.17f, .038f, .004f), Quaternion.Euler(-38f, 0f, 0f));
            hudLens.Attach("HudCombinerLens", hud, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Optical, layer, false);
            var hudKnobs = new SkylarkMeshKit.Builder();
            hudKnobs.Cylinder(new Vector3(-.115f, .008f, -.01f), new Vector3(-.102f, .008f, -.01f), .010f, .010f, 8);
            hudKnobs.Cylinder(new Vector3(.102f, .008f, -.01f), new Vector3(.115f, .008f, -.01f), .010f, .010f, 8);
            hudKnobs.Attach("HudKnobs", hud, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer, false);
            controls += 2;

            // 2. Left Instrument Sub-Panel: Landing Gear Lever, N/L/R + FLAP LEDs, DRONE/PLANE Switch, AUTO/EV/HYB Dial.
            Transform leftSub = Frame("LeftSubPanel", root, new Vector3(-.392f, .148f, 1.77f), Quaternion.Euler(34f, 19f, 0f), layer);
            var leftPlate = new SkylarkMeshKit.Builder();
            leftPlate.Box(Vector3.zero, new Vector3(.136f, .112f, .014f));
            leftPlate.Box(new Vector3(-.038f, .004f, -.009f), new Vector3(.018f, .068f, .006f)); // gear track slot
            leftPlate.Attach("LeftSubPlate", leftSub, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.ConsoleDark, layer, false);

            gearLever = Frame("GearHandle", leftSub, new Vector3(-.038f, .006f, -.010f), Quaternion.identity, layer);
            var leverMesh = new SkylarkMeshKit.Builder();
            leverMesh.Cylinder(Vector3.zero, new Vector3(0f, 0f, -.042f), .005f, .005f, 8);
            leverMesh.Attach("GearShaft", gearLever, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer, false);
            var knobMesh = new SkylarkMeshKit.Builder();
            knobMesh.Cylinder(new Vector3(-.009f, 0f, -.045f), new Vector3(.009f, 0f, -.045f), .013f, .013f, 12);
            gearKnobLed = knobMesh.Attach("GearKnob", gearLever, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.LampBase, layer, false).GetComponent<Renderer>();

            gearWheelLeds = new Renderer[3];
            Vector3[] wheelOff = { new Vector3(.012f, .034f, -.008f), new Vector3(-.004f, .014f, -.008f), new Vector3(.028f, .014f, -.008f) };
            for (int i = 0; i < 3; i++)
                gearWheelLeds[i] = MakeLed("GearLed_" + i, leftSub, wheelOff[i], new Vector3(.015f, .011f, .004f), layer);
            flapLed = MakeLed("FlapLed", leftSub, new Vector3(.048f, .034f, -.008f), new Vector3(.018f, .011f, .004f), layer);

            modeSwitch = MakeToggle("ModeSwitch", leftSub, new Vector3(-.002f, -.026f, -.009f), layer);
            modeDrnLed = MakeLed("ModeDrnLed", leftSub, new Vector3(-.016f, -.012f, -.008f), new Vector3(.014f, .007f, .004f), layer);
            modePlnLed = MakeLed("ModePlnLed", leftSub, new Vector3(-.016f, -.040f, -.008f), new Vector3(.014f, .007f, .004f), layer);

            driveKnob = Frame("DriveDial", leftSub, new Vector3(.038f, -.026f, -.010f), Quaternion.identity, layer);
            var dialMesh = new SkylarkMeshKit.Builder();
            dialMesh.Cylinder(Vector3.zero, new Vector3(0f, 0f, -.018f), .016f, .014f, 10);
            dialMesh.Box(new Vector3(0f, .008f, -.019f), new Vector3(.004f, .014f, .004f));
            dialMesh.Attach("DriveDialKnob", driveKnob, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer, false);
            AddCaption(font, leftSub, "GEAR", new Vector3(-.024f, .048f, -.0085f), .00075f, layer);
            AddCaption(font, leftSub, "VEC   DRV", new Vector3(.018f, -.004f, -.0085f), .00070f, layer);
            controls += 3;

            // 3. Right Instrument Sub-Panel: Halberd Arm + 5-Segment Capacitor Bar, Guarded C2 Mast Switch, 8-Lamp Caution Matrix.
            Transform rightSub = Frame("RightSubPanel", root, new Vector3(.392f, .148f, 1.77f), Quaternion.Euler(34f, -19f, 0f), layer);
            var rightPlate = new SkylarkMeshKit.Builder();
            rightPlate.Box(Vector3.zero, new Vector3(.136f, .112f, .014f));
            rightPlate.Attach("RightSubPlate", rightSub, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.ConsoleDark, layer, false);

            armSwitch = MakeToggle("HalberdArmSwitch", rightSub, new Vector3(-.044f, .024f, -.009f), layer);
            coilChargeLeds = new Renderer[5];
            for (int i = 0; i < 5; i++)
                coilChargeLeds[i] = MakeLed("CoilCapLed_" + i, rightSub, new Vector3(-.056f + i * .0095f, .002f, -.008f), new Vector3(.0075f, .010f, .004f), layer);

            mastSwitch = MakeToggle("C2MastSwitch", rightSub, new Vector3(-.006f, .024f, -.009f), layer);
            mastGuard = Frame("C2MastGuard", rightSub, new Vector3(-.006f, .042f, -.008f), Quaternion.identity, layer);
            var guardMesh = new SkylarkMeshKit.Builder();
            guardMesh.Box(new Vector3(0f, -.018f, -.016f), new Vector3(.020f, .034f, .004f));
            guardMesh.Box(new Vector3(-.009f, -.018f, -.008f), new Vector3(.003f, .034f, .016f));
            guardMesh.Box(new Vector3(.009f, -.018f, -.008f), new Vector3(.003f, .034f, .016f));
            guardMesh.Attach("C2GuardCap", mastGuard, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.SwitchRed, layer, false);
            c2TxLed = MakeLed("C2TxLed", rightSub, new Vector3(-.006f, .002f, -.008f), new Vector3(.020f, .008f, .004f), layer);

            annunciatorLeds = new Renderer[AnnunciatorLabels.Length];
            annunciatorTexts = new TextMeshPro[AnnunciatorLabels.Length];
            for (int i = 0; i < AnnunciatorLabels.Length; i++)
            {
                int col = i % 4, row = i / 4;
                Vector3 pos = new Vector3(-.042f + col * .028f, -.020f - row * .022f, -.008f);
                annunciatorLeds[i] = MakeLed("Ann_" + AnnunciatorLabels[i], rightSub, pos, new Vector3(.024f, .016f, .004f), layer);
                annunciatorTexts[i] = AddCaption(font, rightSub, AnnunciatorLabels[i], pos + new Vector3(0f, 0f, -.0025f), .00060f, layer);
            }
            AddCaption(font, rightSub, "COIL  C2", new Vector3(-.025f, .047f, -.0085f), .00070f, layer);
            controls += 2;

            // 4. Center Pedestal: UFC Keypad, Emergency Gear T-Handle, and Yellow/Black Ejection Handle.
            Transform ped = Frame("CenterPedestal", root, new Vector3(0f, -.175f, 1.63f), Quaternion.Euler(38f, 0f, 0f), layer);
            var pedBox = new SkylarkMeshKit.Builder();
            pedBox.Box(Vector3.zero, new Vector3(.18f, .24f, .08f));
            pedBox.Attach("PedestalHousing", ped, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.ConsoleDark, layer, false);
            keypadLeds = new Renderer[12];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                    keypadLeds[r * 4 + c] = MakeLed("UfcKey_" + (r * 4 + c), ped, new Vector3((c - 1.5f) * .028f, .055f - r * .025f, -.042f), new Vector3(.019f, .016f, .005f), layer);

            var tHandle = new SkylarkMeshKit.Builder();
            tHandle.Cylinder(new Vector3(.055f, -.05f, -.042f), new Vector3(.055f, -.05f, -.068f), .005f, .005f, 8);
            tHandle.Box(new Vector3(.055f, -.05f, -.072f), new Vector3(.042f, .012f, .012f));
            tHandle.Attach("EmergencyGearHandle", ped, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.HazardYellow, layer, false);

            Transform eject = Frame("EjectionHandle", root, new Vector3(0f, -.365f, 1.38f), Quaternion.Euler(-20f, 0f, 0f), layer);
            var ejYel = new SkylarkMeshKit.Builder();
            ejYel.HollowRing(Vector3.zero, Vector3.right, .032f, .044f, .016f, 12);
            ejYel.Attach("EjectPullLoop", eject, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.HazardYellow, layer, false);
            var ejBlk = new SkylarkMeshKit.Builder();
            ejBlk.Box(new Vector3(0f, .036f, 0f), new Vector3(.018f, .014f, .022f));
            ejBlk.Box(new Vector3(0f, -.036f, 0f), new Vector3(.018f, .014f, .022f));
            ejBlk.Attach("EjectPullStripes", eject, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.ConsoleDark, layer, false);
            controls++;

            // 5. Left & Right Side Consoles + Canopy Lock Lever.
            Transform leftCon = Frame("LeftConsole", root, new Vector3(-.53f, -.10f, 1.54f), Quaternion.Euler(10f, 0f, -15f), layer);
            var lcMesh = new SkylarkMeshKit.Builder();
            lcMesh.Box(Vector3.zero, new Vector3(.12f, .03f, .44f));
            lcMesh.Box(new Vector3(0f, .02f, .12f), new Vector3(.09f, .012f, .16f));
            lcMesh.Attach("LeftConsoleDeck", leftCon, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.ConsoleDark, layer, false);
            for (int i = 0; i < 4; i++)
            {
                Transform t = MakeToggle("LeftToggle_" + i, leftCon, new Vector3(-.018f + (i % 2) * .034f, .026f, .07f + (i / 2) * .05f), layer, upward: true);
                if (i == 0) ecsToggle = t;
                else if (i == 1) navToggle = t;
            }

            canopyLockHandle = Frame("CanopyLockHandle", leftCon, new Vector3(.036f, .024f, .18f), Quaternion.identity, layer);
            var lockMesh = new SkylarkMeshKit.Builder();
            lockMesh.Box(new Vector3(0f, .012f, -.045f), new Vector3(.014f, .014f, .09f));
            lockMesh.Attach("CanopyLockBar", canopyLockHandle, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.HazardYellow, layer, false);

            Transform rightCon = Frame("RightConsole", root, new Vector3(.53f, -.10f, 1.38f), Quaternion.Euler(10f, 0f, 15f), layer);
            var rcMesh = new SkylarkMeshKit.Builder();
            rcMesh.Box(Vector3.zero, new Vector3(.12f, .03f, .48f));
            rcMesh.Attach("RightConsoleDeck", rightCon, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.ConsoleDark, layer, false);
            var cbKit = new SkylarkMeshKit.Builder();
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 3; c++)
                    cbKit.Cylinder(new Vector3(-.028f + c * .026f, .015f, -.14f + r * .032f), new Vector3(-.028f + c * .026f, .028f, -.14f + r * .032f), .005f, .005f, 6);
            cbKit.Attach("CircuitBreakers", rightCon, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer, false);
            var knobKit = new SkylarkMeshKit.Builder();
            knobKit.Cylinder(new Vector3(0f, .015f, .12f), new Vector3(0f, .034f, .12f), .016f, .014f, 10);
            knobKit.Cylinder(new Vector3(0f, .015f, .18f), new Vector3(0f, .034f, .18f), .014f, .012f, 10);
            knobKit.Attach("RightConsoleDials", rightCon, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer, false);
            controls += 3;

            // 6. Animated Rudder Pedals with Toe Brakes in Footwell.
            var rails = new SkylarkMeshKit.Builder();
            foreach (float sx in new[] { -.17f, .17f })
            {
                rails.Cylinder(new Vector3(sx - .04f, -.52f, 1.72f), new Vector3(sx - .04f, -.52f, 2.02f), .009f, .009f, 8);
                rails.Cylinder(new Vector3(sx + .04f, -.52f, 1.72f), new Vector3(sx + .04f, -.52f, 2.02f), .009f, .009f, 8);
            }
            rails.Attach("PedalRails", root, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer, false);

            (Transform carriage, Transform plate) BuildPedal(string name, float sx)
            {
                Transform carriage = Frame(name + "Carriage", root, new Vector3(sx, -.51f, 1.88f), Quaternion.identity, layer);
                var cMesh = new SkylarkMeshKit.Builder();
                cMesh.Box(new Vector3(0f, .022f, 0f), new Vector3(.096f, .028f, .06f));
                cMesh.Attach(name + "Base", carriage, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer, false);
                Transform plate = Frame(name + "Plate", carriage, new Vector3(0f, .04f, 0f), Quaternion.Euler(-16f, 0f, 0f), layer);
                var pMesh = new SkylarkMeshKit.Builder();
                pMesh.Box(new Vector3(0f, .055f, 0f), new Vector3(.10f, .11f, .014f));
                pMesh.Box(new Vector3(0f, .005f, -.016f), new Vector3(.10f, .016f, .030f)); // heel cup
                pMesh.Attach(name + "Face", plate, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.ConsoleDark, layer, false);
                return (carriage, plate);
            }
            (pedalL, pedalPlateL) = BuildPedal("RudderPedalL", -.17f);
            (pedalR, pedalPlateR) = BuildPedal("RudderPedalR", .17f);
            controls += 2;

            // 7. Fan-Tilt Thumbwheel attached to Throttle grip.
            if (nodes.TryGetValue("Throttle", out Transform throttleNode))
            {
                thumbwheel = new GameObject("ThrottleFanThumbwheel") { layer = layer }.transform;
                thumbwheel.SetParent(throttleNode, false);
                thumbwheel.localPosition = new Vector3(-.02f, .02f, .09f);
                thumbwheel.localRotation = Quaternion.identity;
                var wheelMesh = new SkylarkMeshKit.Builder();
                wheelMesh.Cylinder(new Vector3(-.012f, 0f, 0f), new Vector3(.012f, 0f, 0f), .016f, .016f, 10);
                wheelMesh.Box(new Vector3(0f, .014f, 0f), new Vector3(.014f, .010f, .006f));
                wheelMesh.Attach("ThumbwheelGrip", thumbwheel, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer, false);
                controls++;
            }

            // 8. Canopy Bow Interior Mirrors, Grab Handles & Latch Hooks on Canopy_Frame_Int.
            if (nodes.TryGetValue("Canopy_Frame_Int", out Transform bowInt))
            {
                Transform bowFrame = new GameObject("SkylarkBowDetails") { layer = layer }.transform;
                bowFrame.SetParent(bowInt, false);
                bowFrame.position = owner.transform.position;
                bowFrame.rotation = owner.transform.rotation;
                bowFrame.localScale = Vector3.one * size;

                var bowKit = new SkylarkMeshKit.Builder();
                var mirrorGlass = new SkylarkMeshKit.Builder();
                foreach (float sx in new[] { -.34f, .34f })
                {
                    // High upper-bow rear-view mirror housings (clear of forward HUD sightline).
                    Vector3 mPos = new Vector3(sx, .80f, 1.72f);
                    Quaternion mRot = Quaternion.Euler(14f, sx < 0 ? 18f : -18f, 0f);
                    bowKit.Box(mPos, new Vector3(.10f, .042f, .022f), mRot);
                    mirrorGlass.Box(mPos + mRot * new Vector3(0f, 0f, -.011f), new Vector3(.086f, .030f, .004f), mRot);

                    // Side grab handles & sill latch hooks.
                    float hx = sx < 0 ? -.49f : .49f;
                    bowKit.Cylinder(new Vector3(hx, .42f, 1.38f), new Vector3(hx, .42f, 1.56f), .011f, .011f, 8);
                    bowKit.Box(new Vector3(hx, .39f, 1.38f), new Vector3(.024f, .045f, .022f));
                    bowKit.Box(new Vector3(hx, .39f, 1.56f), new Vector3(.024f, .045f, .022f));
                    bowKit.Box(new Vector3(hx * 1.06f, .29f, 1.15f), new Vector3(.022f, .028f, .042f));
                }
                bowKit.Attach("CanopyBowHardware", bowFrame, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer, false);
                mirrorGlass.Attach("CanopyBowMirrors", bowFrame, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer, false);
            }

            // 9. Console & Footwell Flood Lights (cockpitLayer only, soft slate-cyan glow so night cockpit reads cleanly).
            consoleFloods = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                var lgo = new GameObject("SkylarkConsoleFlood_" + i) { layer = layer };
                lgo.transform.SetParent(root, false);
                lgo.transform.localPosition = new Vector3(i == 0 ? -.26f : .26f, .30f, 1.54f);
                Light flood = lgo.AddComponent<Light>();
                flood.type = LightType.Point;
                flood.range = .95f * size;
                flood.color = new Color(.64f, .80f, .90f);
                flood.intensity = .35f;
                flood.shadows = LightShadows.None;
                flood.cullingMask = 1 << layer;
                flood.enabled = false;
                consoleFloods[i] = flood;
            }

            controlCount = controls;
        }

        private static Transform Frame(string name, Transform parent, Vector3 localPos, Quaternion localRot, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            return go.transform;
        }

        private static Renderer MakeLed(string name, Transform parent, Vector3 localPos, Vector3 size, int layer)
        {
            var builder = new SkylarkMeshKit.Builder();
            builder.Box(Vector3.zero, size);
            return builder.Attach(name, parent, localPos, Quaternion.identity, Vector3.one, SkylarkMeshKit.LampBase, layer, false).GetComponent<Renderer>();
        }

        private static Transform MakeToggle(string name, Transform parent, Vector3 localPos, int layer, bool upward = false)
        {
            var baseMesh = new SkylarkMeshKit.Builder();
            baseMesh.Cylinder(Vector3.zero, upward ? new Vector3(0f, .008f, 0f) : new Vector3(0f, 0f, -.008f), .007f, .007f, 8);
            baseMesh.Attach(name + "Base", parent, localPos, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer, false);

            Transform pivot = Frame(name + "Pivot", parent, localPos + (upward ? new Vector3(0f, .006f, 0f) : new Vector3(0f, 0f, -.006f)), Quaternion.identity, layer);
            var batMesh = new SkylarkMeshKit.Builder();
            batMesh.Cylinder(Vector3.zero, upward ? new Vector3(0f, .022f, 0f) : new Vector3(0f, 0f, -.022f), .0025f, .004f, 8);
            batMesh.Attach(name + "Bat", pivot, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer, false);
            return pivot;
        }

        private static TextMeshPro AddCaption(TMP_FontAsset font, Transform parent, string textValue, Vector3 localPos, float scale, int layer)
        {
            if (font == null) return null;
            var go = new GameObject("Lbl_" + textValue, typeof(RectTransform), typeof(TextMeshPro)) { layer = layer };
            var tmp = go.GetComponent<TextMeshPro>();
            tmp.rectTransform.SetParent(parent, false);
            tmp.rectTransform.localPosition = localPos;
            tmp.rectTransform.localRotation = Quaternion.identity;
            tmp.rectTransform.localScale = Vector3.one * scale;
            tmp.rectTransform.sizeDelta = new Vector2(80f, 18f);
            tmp.font = font;
            tmp.fontSize = 14f;
            tmp.isOrthographic = true;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.color = new Color(.68f, .76f, .78f);
            tmp.text = textValue;
            tmp.renderer.shadowCastingMode = ShadowCastingMode.Off;
            return tmp;
        }

        private void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (pedalL != null) pedalRestL = pedalL.localPosition;
            if (pedalR != null) pedalRestR = pedalR.localPosition;
            if (pedalPlateL != null) plateRestL = pedalPlateL.localRotation;
            if (pedalPlateR != null) plateRestR = pedalPlateR.localRotation;
            if (thumbwheel != null) thumbRest = thumbwheel.localRotation;
            if (aircraft == null) return;
            LandingGear[] allGears = aircraft.GetComponentsInChildren<LandingGear>(true);
            gears = new LandingGear[3];
            foreach (LandingGear g in allGears)
            {
                if (SkylarkTemplate.Get<bool>(g, "steering")) gears[0] = g;
                else if (g.name.Contains("_L_") || g.name.Contains("_L")) gears[1] = g;
                else gears[2] = g;
            }
            Canopy[] canopies = SkylarkTemplate.Get<Canopy[]>(aircraft, "canopies");
            if (canopies != null && canopies.Length > 0) canopy = canopies[0];
            flight = aircraft.GetComponent<SkylarkFlightMode>();
            drive = aircraft.GetComponent<SkylarkDrive>();
            mast = aircraft.GetComponent<SkylarkMast>();
            turret = aircraft.GetComponent<SkylarkTurret>();
            navLights = aircraft.GetComponentInChildren<NavLights>(true);
            if (navLights != null)
            {
                Array navArr = SkylarkTemplate.Get<Array>(navLights, "navLights");
                if (navArr != null && navArr.Length > 0) navSourceLens = SkylarkTemplate.Get<Renderer>(navArr.GetValue(0), "renderer");
            }
            inputs = aircraft.GetInputs();
        }

        private void LateUpdate()
        {
            if (aircraft == null || gearKnobLed == null) return;
            bool visible = gearKnobLed.enabled && gearKnobLed.gameObject.activeInHierarchy;
            if (consoleFloods != null)
            {
                bool floodOn = visible && !aircraft.disabled && aircraft.Ignition && cockpitPart != null && !cockpitPart.IsDetached();
                for (int i = 0; i < consoleFloods.Length; i++)
                    if (consoleFloods[i] != null && consoleFloods[i].enabled != floodOn) consoleFloods[i].enabled = floodOn;
            }
            if (!visible) return;

            float dt = Time.deltaTime;
            bool powered = !aircraft.disabled && aircraft.Ignition && cockpitPart != null && !cockpitPart.IsDetached();

            // Mechanical movements (follow aircraft state smoothly).
            bool gearDown = aircraft.gearState == LandingGear.GearState.LockedExtended || aircraft.gearState == LandingGear.GearState.Extending;
            gearPos = Mathf.MoveTowards(gearPos, gearDown ? 1f : 0f, dt * 6f);
            if (gearLever != null) gearLever.localRotation = Quaternion.Euler(Mathf.Lerp(-26f, 26f, gearPos), 0f, 0f);

            bool plane = flight != null && flight.Mode == VectorMode.Plane;
            modePos = Mathf.MoveTowards(modePos, plane ? 1f : 0f, dt * 8f);
            if (modeSwitch != null) modeSwitch.localRotation = Quaternion.Euler(Mathf.Lerp(-26f, 26f, modePos), 0f, 0f);

            float targetDial = drive == null ? 0f : drive.Mode == DriveMode.Auto ? -42f : drive.Mode == DriveMode.Ev ? 0f : 42f;
            driveAngle = Mathf.MoveTowards(driveAngle, targetDial, dt * 240f);
            if (driveKnob != null) driveKnob.localRotation = Quaternion.Euler(0f, 0f, -driveAngle);

            bool mastUp = mast != null && mast.Deployed;
            mastPos = Mathf.MoveTowards(mastPos, mastUp ? 1f : 0f, dt * 7f);
            if (mastGuard != null) mastGuard.localRotation = Quaternion.Euler(Mathf.Lerp(0f, -72f, mastPos), 0f, 0f);
            if (mastSwitch != null) mastSwitch.localRotation = Quaternion.Euler(Mathf.Lerp(26f, -26f, mastPos), 0f, 0f);

            bool armed = aircraft.weaponStations.Count > 0 && aircraft.weaponManager.currentWeaponStation == aircraft.weaponStations[0];
            armPos = Mathf.MoveTowards(armPos, armed ? 1f : 0f, dt * 8f);
            if (armSwitch != null) armSwitch.localRotation = Quaternion.Euler(Mathf.Lerp(26f, -26f, armPos), 0f, 0f);

            float openAmt = canopy != null ? SkylarkTemplate.Get<float>(canopy, "openAmount") : 0f;
            canopyPos = Mathf.MoveTowards(canopyPos, openAmt > .05f ? 1f : 0f, dt * 5f);
            if (canopyLockHandle != null) canopyLockHandle.localRotation = Quaternion.Euler(0f, 0f, canopyPos * -55f);
            if (ecsToggle != null) ecsToggle.localRotation = Quaternion.Euler(aircraft.Ignition ? 24f : -24f, 0f, 0f);
            if (navToggle != null)
            {
                bool navOn = navSourceLens != null && navSourceLens.sharedMaterial != null && navSourceLens.sharedMaterial.name.Contains("Lit");
                navToggle.localRotation = Quaternion.Euler(navOn ? 24f : -24f, 0f, 0f);
            }

            if (inputs != null)
            {
                if (pedalL != null) pedalL.localPosition = pedalRestL + new Vector3(0f, 0f, inputs.yaw * .05f);
                if (pedalR != null) pedalR.localPosition = pedalRestR + new Vector3(0f, 0f, -inputs.yaw * .05f);
                Quaternion toe = Quaternion.Euler(inputs.brake * 18f, 0f, 0f);
                if (pedalPlateL != null) pedalPlateL.localRotation = plateRestL * toe;
                if (pedalPlateR != null) pedalPlateR.localRotation = plateRestR * toe;
            }
            if (thumbwheel != null && flight != null)
                thumbwheel.localRotation = thumbRest * Quaternion.Euler(flight.Angle, 0f, 0f);

            if (turret != null)
            {
                if (lastShots >= 0 && turret.Shots > lastShots) shotFlash = .08f;
                lastShots = turret.Shots;
                if (shotFlash > 0f) shotFlash = Mathf.Max(0f, shotFlash - dt);
            }

            if (Time.unscaledTime < nextSlow && shotFlash <= 0f) return;
            nextSlow = Time.unscaledTime + .10f;

            if (!powered)
            {
                SetAllDark();
                return;
            }

            bool blink = Mathf.FloorToInt(Time.unscaledTime * 3.5f) % 2 == 0;
            bool transit = aircraft.gearState == LandingGear.GearState.Extending || aircraft.gearState == LandingGear.GearState.Retracting;
            SetLed(gearKnobLed, transit ? LedRed : gearDown ? LedGreen : LedOff, transit ? 1.2f : gearDown ? .75f : 0f);

            if (gearWheelLeds != null)
            {
                for (int i = 0; i < gearWheelLeds.Length; i++)
                {
                    bool missing = gears == null || i >= gears.Length || gears[i] == null || !gears[i].enabled && gearDown;
                    Color col = missing ? LedRed : transit ? (blink ? LedAmber : LedOff) : gearDown ? LedGreen : LedOff;
                    float mul = missing ? 1.1f : transit ? (blink ? 1.1f : 0f) : gearDown ? 1.1f : 0f;
                    SetLed(gearWheelLeds[i], col, mul);
                }
            }
            SetLed(flapLed, gearDown ? LedGreen : LedOff, gearDown ? .95f : 0f);
            SetLed(modeDrnLed, !plane ? LedCyan : LedOff, !plane ? 1.0f : 0f);
            SetLed(modePlnLed, plane ? LedGreen : LedOff, plane ? 1.0f : 0f);
            SetLed(c2TxLed, mastUp ? (blink ? LedAmber : LedAmber * .35f) : LedOff, mastUp ? 1.1f : 0f);

            float charge = drive != null ? drive.Charge : 0f;
            bool lowCoil = charge < SkylarkDrive.Floor;
            if (coilChargeLeds != null)
            {
                for (int i = 0; i < coilChargeLeds.Length; i++)
                {
                    bool segLit = charge >= (i + .5f) / coilChargeLeds.Length;
                    Color c = lowCoil ? (i == 0 && blink ? LedRed : LedOff)
                        : segLit ? (shotFlash > 0f ? LedCyan * 1.4f : (drive != null && drive.Electric ? LedCyan * .65f : LedCyan))
                        : LedOff;
                    float mul = lowCoil ? (i == 0 && blink ? 1.2f : 0f) : segLit ? (shotFlash > 0f ? 1.6f : 1.0f) : 0f;
                    SetLed(coilChargeLeds[i], c, mul);
                }
            }

            bool ev = drive != null && drive.Electric;
            bool boost = drive != null && drive.Boost;
            bool battLow = charge < .15f;
            bool dry = drive != null && drive.Dry;
            bool canopyOpen = openAmt > .05f;
            Color[] annCols =
            {
                ev ? LedCyan : LedOff,
                boost ? LedAmber : LedOff,
                battLow ? (blink ? LedAmber : LedOff) : LedOff,
                lowCoil ? LedRed : LedOff,
                mastUp ? LedAmber : LedOff,
                transit ? LedAmber : gearDown ? LedGreen : LedOff,
                dry ? LedAmber : LedOff,
                canopyOpen ? LedRed : LedOff
            };
            if (annunciatorLeds != null)
            {
                for (int i = 0; i < annunciatorLeds.Length; i++)
                {
                    Color c = annCols[i];
                    bool lit = c != LedOff;
                    SetLed(annunciatorLeds[i], c, lit ? 1.05f : 0f);
                    if (annunciatorTexts != null && i < annunciatorTexts.Length && annunciatorTexts[i] != null)
                        annunciatorTexts[i].color = lit ? new Color(.03f, .04f, .04f) : new Color(.64f, .72f, .74f);
                }
            }
            if (keypadLeds != null)
                foreach (Renderer key in keypadLeds)
                    SetLed(key, new Color(.14f, .46f, .34f), .45f);
        }

        private void SetAllDark()
        {
            SetLed(gearKnobLed, LedOff, 0f);
            SetLed(flapLed, LedOff, 0f);
            SetLed(modeDrnLed, LedOff, 0f);
            SetLed(modePlnLed, LedOff, 0f);
            SetLed(c2TxLed, LedOff, 0f);
            if (gearWheelLeds != null) foreach (Renderer r in gearWheelLeds) SetLed(r, LedOff, 0f);
            if (coilChargeLeds != null) foreach (Renderer r in coilChargeLeds) SetLed(r, LedOff, 0f);
            if (annunciatorLeds != null) foreach (Renderer r in annunciatorLeds) SetLed(r, LedOff, 0f);
            if (annunciatorTexts != null) foreach (TextMeshPro t in annunciatorTexts) if (t != null) t.color = new Color(.45f, .50f, .52f);
            if (keypadLeds != null) foreach (Renderer r in keypadLeds) SetLed(r, LedOff, 0f);
        }

        private void SetLed(Renderer renderer, Color color, float emissionMultiplier)
        {
            if (renderer == null) return;
            SkylarkMeshKit.SetLamp(renderer, mpb, color == LedOff ? LedOff : color * .45f, color * emissionMultiplier);
        }

#if !PUBLIC_RELEASE
        internal object Inspect() => new
        {
            controls = InteractiveControlCount,
            gearLeverPos = gearPos,
            modeSwitchPos = modePos,
            driveAngle,
            mastSwitchPos = mastPos,
            armSwitchPos = armPos,
            pedalOffsetL = pedalL != null ? pedalL.localPosition.z - pedalRestL.z : 0f,
            floodsLit = consoleFloods != null && consoleFloods.Any(l => l != null && l.enabled)
        };
#endif
    }
}
