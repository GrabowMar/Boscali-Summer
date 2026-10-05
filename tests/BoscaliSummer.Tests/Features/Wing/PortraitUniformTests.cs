using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;

namespace BoscaliSummer.Tests.Features.Wing
{
    internal static class PortraitUniformTests
    {
        public static void Run()
        {
            AtlasAndSelectors();
            AutomaticRoles();
            LegacySelectors();
        }

        private static void AtlasAndSelectors()
        {
            string[] labels = {
                "BDF PILOT", "BDF COMMANDER", "PALA PILOT", "PALA COMMANDER",
                "BDF SOLDIER", "BDF CIVILIAN", "PALA SOLDIER", "PALA CIVILIAN",
                "BDF LIGHT FLIGHT", "BDF HEAVY FLIGHT", "PALA DESERT FLIGHT",
                "PALA HIGH-ALT FLIGHT", "BDF FIELD COMMAND", "PALA FIELD COMMAND"
            };
            TestAssert.That(PilotPortraitGenerator.UniformCount == labels.Length, "uniform selector count drifted");
            TestAssert.That(PilotPortraitGenerator.AtlasTileCount == 128, "expanded atlas must have 128 cells");
            for (int body = 0; body < 2; body++)
            for (int uniform = 0; uniform < labels.Length; uniform++)
            {
                var selection = new PortraitSelection((PortraitBody)body, 7, 8, uniform, 2, 7);
                ResolvedPortraitParts parts = PilotPortraitGenerator.Resolve(selection);
                bool pala = uniform == 2 || uniform == 3 || uniform == 6 || uniform == 7 ||
                    uniform == 10 || uniform == 11 || uniform == 13;
                int expectedBody = uniform < 8 ? 32 + body * 8 + uniform : 86 + body * 6 + uniform - 8;
                int expectedCollar = uniform < 8 ? 62 + body * 8 + uniform : 98 + body * 6 + uniform - 8;
                TestAssert.That(parts.UniformTile == expectedBody && parts.FrontCollarTile == expectedCollar,
                    "uniform or collar no longer resolves to its registered body-specific slot");
                TestAssert.That(parts.AccessoryTile == (pala ? 56 : 49), "equipment uses the wrong uniform faction");
                TestAssert.That(parts.FaceTile == body * 8 + 7 && parts.HairTile == 23 + body * 8 && parts.BackdropTile == 85,
                    "clothing selection changed unrelated portrait layers");
                TestAssert.That(PilotPortraitGenerator.UniformLabel(uniform) == labels[uniform], "uniform label drifted");
                TestAssert.That(PilotPortraitGenerator.Normalize(selection) == selection, "a valid uniform was clamped away");
            }
        }

        private static void AutomaticRoles()
        {
            for (int faction = 0; faction < 2; faction++)
            {
                var pilots = new HashSet<int>();
                var commanders = new HashSet<int>();
                for (int identity = 0; identity < 128; identity++)
                {
                    string name = "uniform-fixture-" + identity;
                    PortraitSelection baseline = PilotPortraitGenerator.Select(name, PortraitRole.Pilot, 0);
                    for (int role = 0; role < 4; role++)
                    {
                        PortraitSelection look = PilotPortraitGenerator.Select(name, (PortraitRole)role, faction);
                        TestAssert.That(look.Body == baseline.Body && look.Face == baseline.Face &&
                            look.Hair == baseline.Hair && look.Backdrop == baseline.Backdrop,
                            "changing role or native faction rerolled identity anatomy or its scene");
                        TestAssert.That(look == PilotPortraitGenerator.Select(name, (PortraitRole)role, faction),
                            "automatic uniforms must be deterministic");
                        bool valid = role == 0 ? (faction == 0 ? look.Uniform == 0 || look.Uniform == 8 || look.Uniform == 9
                            : look.Uniform == 2 || look.Uniform == 10 || look.Uniform == 11)
                            : role == 1 ? (faction == 0 ? look.Uniform == 1 || look.Uniform == 12 : look.Uniform == 3 || look.Uniform == 13)
                            : role == 2 ? look.Uniform == (faction == 0 ? 4 : 6) : look.Uniform == (faction == 0 ? 5 : 7);
                        TestAssert.That(valid, "automatic clothing departed from its role or actual faction");
                        if (role == 0) pilots.Add(look.Uniform);
                        if (role == 1) commanders.Add(look.Uniform);
                    }
                    TestAssert.That(PilotPortraitGenerator.Select(name, (PortraitRole)999, faction) ==
                        PilotPortraitGenerator.Select(name, PortraitRole.Pilot, faction), "invalid roles must fall back to pilot");
                }
                TestAssert.That(pilots.Count == 3 && commanders.Count == 2, "automatic clothing variety is unreachable");
            }
        }

        private static void LegacySelectors()
        {
            for (int uniform = 0; uniform < 8; uniform++)
            {
                TestAssert.That(PilotPortraitGenerator.FromLegacySelection(0, 0, uniform, 0).Uniform == uniform,
                    "old semantic uniform selectors changed meaning");
            }
            for (int tile = 14; tile <= 17; tile++)
            {
                PortraitSelection legacy = PilotPortraitGenerator.FromLegacySelection(0, 0, tile, 0);
                TestAssert.That(legacy.Uniform == (tile - 14) % 2 && legacy.Body == (tile >= 16 ? PortraitBody.Female : PortraitBody.Male),
                    "v1 resolved uniform tile IDs no longer migrate to their original outfit");
            }
        }
    }
}
