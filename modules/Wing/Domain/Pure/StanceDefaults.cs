namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>BEHAVIOUR › STANCES' DEFAULT FOR keys (review fix: the defaults were saved but nothing applied them): which key an
    /// accepted player order answers to. taskKind is the TaskKind integer (0 Form, 1 Move, 2 Route, 3 Patrol, 4 Orbit, 5 Hold);
    /// guarded means a CAP or SWEEP area; scout a scouting route. Null: the order has no default stance.</summary>
    internal static class StanceDefaults
    {
        public static string Key(string orderKind, int taskKind, bool guarded, bool scout)
        {
            switch (orderKind)
            {
                case "Attack": return "attack";
                case "EscortMe":
                case "EscortTarget": return "escort";
                case "Task": break;
                default: return null;
            }
            if (scout) return "scout";
            if (guarded) return taskKind == 3 ? "sweep" : taskKind == 4 ? "cap" : null;
            switch (taskKind)
            {
                case 1:
                case 2: return "move";
                case 3: return "patrol";
                case 4:
                case 5: return "orbit";
                default: return null;
            }
        }
    }
}
