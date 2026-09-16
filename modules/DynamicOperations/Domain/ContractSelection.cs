namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    /// <summary>
    /// Which contracts the vicinity card lists, and in what order: the area you are already
    /// inside first, then the closest marked contracts, then anything whose contact the host
    /// lost (it still has to be visible — that is news the pilot needs). Pure.
    /// </summary>
    internal static class ContractSelection
    {
        public static int Select(
            ContractCard[] cards, int count, float selfX, float selfZ,
            ContractCard[] selected, float[] distances, int maximum)
        {
            int used = 0;
            if (cards == null || selected == null || distances == null) return 0;
            int limit = count < cards.Length ? count : cards.Length;
            if (maximum < limit) limit = maximum;
            if (limit > selected.Length) limit = selected.Length;

            for (int i = 0; i < count && i < cards.Length; i++)
            {
                ContractCard card = cards[i];
                float distance = card.HasMarker && OperationMarkerCopy.Finite(selfX) && OperationMarkerCopy.Finite(selfZ)
                    ? ContractMarkerMath.Distance(selfX, selfZ, card.X, card.Z)
                    : float.NaN;
                if (!Visible(card, distance)) continue;

                // A full list still takes a better candidate: the worst row makes way.
                if (used == limit)
                {
                    if (!ComesFirst(card, distance, selected[used - 1], distances[used - 1])) continue;
                    used--;
                }

                int slot = used;
                while (slot > 0 && ComesFirst(card, distance, selected[slot - 1], distances[slot - 1]))
                {
                    selected[slot] = selected[slot - 1];
                    distances[slot] = distances[slot - 1];
                    slot--;
                }
                selected[slot] = card;
                distances[slot] = distance;
                used++;
            }
            return used;
        }

        public static bool Visible(ContractCard card, float distance)
        {
            if (!card.HasMarker) return true;
            if (!OperationMarkerCopy.Finite(distance)) return true;
            return distance <= OperationMarkerCopy.VicinityBand(card.Radius);
        }

        /// <summary>Does the candidate outrank the item already in the row?</summary>
        public static bool ComesFirst(ContractCard candidate, float candidateDistance, ContractCard current, float currentDistance)
        {
            int a = Group(candidate, candidateDistance);
            int b = Group(current, currentDistance);
            if (a != b) return a < b;
            if (a == 1 && candidateDistance != currentDistance) return candidateDistance < currentDistance;
            return candidate.Id < current.Id;
        }

        private static int Group(ContractCard card, float distance)
        {
            if (!card.HasMarker) return 2;
            if (!OperationMarkerCopy.Finite(distance)) return 2;
            return distance <= card.Radius ? 0 : 1;
        }
    }
}
