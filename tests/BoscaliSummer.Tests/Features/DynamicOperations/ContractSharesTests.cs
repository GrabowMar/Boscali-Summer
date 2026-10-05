using BoscaliSummer.Modules.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    /// <summary>Who may call off a contract, and what each faction pilot earns when one pays out.</summary>
    internal static class ContractSharesTests
    {
        public static void Run()
        {
            TestCancel();
            TestPayout();
        }

        private static void TestCancel()
        {
            TestAssert.That(ContractShares.MayCancel(OperationState.Offered, 2, 0, false),
                "anyone may dismiss an offer nobody took");
            TestAssert.That(ContractShares.MayCancel(OperationState.Active, 1, 1, true),
                "the accepting pilot may abort its own contract");
            TestAssert.That(!ContractShares.MayCancel(OperationState.Active, 2, 1, true),
                "a teammate may not abort a contract another pilot accepted");
            TestAssert.That(ContractShares.MayCancel(OperationState.Active, 2, 1, false),
                "a contract whose pilot left is anyone's to abort");
            TestAssert.That(ContractShares.MayCancel(OperationState.Active, 2, 0, true),
                "a contract with no recorded pilot is anyone's to abort");
        }

        private static void TestPayout()
        {
            TestAssert.That(ContractShares.Share(true, 4, 0.5f) == 1f, "the completing pilot earns the full reward");
            TestAssert.That(ContractShares.Share(false, 4, 0.5f) == 0.5f, "teammates earn the team share");
            TestAssert.That(ContractShares.Share(false, 1, 0.5f) == 1f, "a lone pilot always earns the full reward");
            TestAssert.That(ContractShares.Share(false, 4, 0f) == 0f, "a zero team share pays teammates nothing");
            TestAssert.That(ContractShares.Share(false, 4, 3f) == 1f && ContractShares.Share(false, 4, float.NaN) == 0f,
                "the team share stays inside 0..1");
            TestAssert.That(ContractShares.Scaled(1200, 0.5f) == 600 && ContractShares.Scaled(75, 0.5f) == 38,
                "a share rounds to whole money and XP");
        }
    }
}
