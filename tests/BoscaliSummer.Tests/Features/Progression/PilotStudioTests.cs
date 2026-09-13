using BoscaliSummer.Features.Progression.Runtime;

namespace BoscaliSummer.Tests.Features.Progression
{
    /// <summary>Pilot studio draft bounds; the Wing Command call itself is runtime-only.</summary>
    internal static class PilotStudioTests
    {
        public static void Run()
        {
            Cycles();
            Normalize();
            Validity();
        }

        private static void Cycles()
        {
            PilotDraft draft = PilotDraft.New();
            draft.CycleBody(-1, 2);
            TestAssert.That(draft.Body == 1, "body cycling must wrap backwards");
            draft.CycleFace(7, 6);
            TestAssert.That(draft.Face == 1, "face cycling must wrap forwards");
            draft.CycleHair(-1, 7);
            TestAssert.That(draft.Hair == 6, "hair cycling must wrap backwards");
            draft.CycleUniform(1, 4);
            TestAssert.That(draft.Uniform == 1, "uniform cycling moved out of order");
            draft.CycleBackdrop(1, 4);
            TestAssert.That(draft.Backdrop == 1, "backdrop cycling moved out of order");
            draft.CyclePersona(3);
            TestAssert.That(draft.Persona == 3, "persona cycling must stay inside the four styles");
            draft.CyclePersona(1);
            TestAssert.That(draft.Persona == 0, "persona cycling must wrap");
            draft.CycleFace(1, 0);
            TestAssert.That(draft.Face == 0, "a zero-count selector must fail closed");
        }

        private static void Normalize()
        {
            var draft = new PilotDraft
            {
                Name = "  0123456789012345678901234567890  ",
                Callsign = "  verylongcallsign  ",
                DialogueTag = "  tag  ",
                Background = new string('x', 400),
                Persona = 9,
                Body = -3,
                Face = -1,
                Hair = -2,
                Uniform = -4,
                Accessory = -5,
                Backdrop = -6,
            };
            draft.Normalize();
            TestAssert.That(draft.Name.Length == PilotDraft.MaxName && draft.Name[0] == '0',
                "pilot names must be trimmed and truncated");
            TestAssert.That(draft.Callsign.Length == PilotDraft.MaxCallsign && draft.Callsign[0] == 'v',
                "callsigns must be trimmed and truncated");
            TestAssert.That(draft.DialogueTag == "tag", "dialogue tags must be trimmed");
            TestAssert.That(draft.Background.Length == PilotDraft.MaxBackground,
                "backgrounds must be truncated");
            TestAssert.That(draft.Persona == 0, "an invalid persona must fall back to Professional");
            TestAssert.That(draft.Body >= 0 && draft.Face >= 0 && draft.Hair >= 0 &&
                draft.Uniform >= 0 && draft.Accessory >= 0 && draft.Backdrop >= 0,
                "negative appearance selectors survived normalization");
        }

        private static void Validity()
        {
            PilotDraft draft = PilotDraft.New();
            draft.Name = "PILOT";
            draft.Callsign = "  ";
            TestAssert.That(!draft.IsValid, "a blank callsign was accepted");
            draft.Callsign = "JACKDAW";
            TestAssert.That(draft.IsValid, "a named pilot was rejected");
        }
    }
}
