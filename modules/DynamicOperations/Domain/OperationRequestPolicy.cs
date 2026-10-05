namespace BoscaliSummer.Modules.DynamicOperations.Domain
{
    internal static class OperationRequestPolicy
    {
        // A real action supersedes an outstanding read query; actions never supersede actions.
        public static bool Allows(bool pending, int pendingOperationId, int requestedOperationId, float sinceLastQuery) =>
            (!pending || pendingOperationId <= 0 && requestedOperationId > 0) &&
            (requestedOperationId > 0 || sinceLastQuery >= 2f);

        public static bool Correlated(bool isServer, bool pending, byte protocol, byte expectedProtocol,
            uint scene, uint expectedScene, uint token, uint expectedToken, bool currentConnection, bool sameFaction) =>
            !isServer && pending && protocol == expectedProtocol && scene == expectedScene && token == expectedToken &&
            currentConnection && sameFaction;

        public static bool SelectAccepted(bool actionPending, bool cancel, int pendingId, int activeCardId) =>
            actionPending && !cancel && pendingId > 0 && pendingId == activeCardId;
    }
}
