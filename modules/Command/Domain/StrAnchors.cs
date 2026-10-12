namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>Pure: where on the ground an item of the board is, so a select can fly the map to it.</summary>
    internal static class StrAnchors
    {
        /// <summary>
        /// The global metres of the item named by id, read from the row it points at. False when the row is gone or its place is not known
        /// (a sortie with no site, a position that is not finite): the caller then flies nowhere rather than to the origin.
        /// </summary>
        public static bool TryGet(StrViewModel vm, StrItemId id, out float x, out float z)
        {
            x = z = float.NaN;
            if (vm == null) return false;
            switch (id.Kind)
            {
                case StrItemKind.Effort:
                    if (vm.Effort.Live && vm.Effort.Id == id) { x = vm.Effort.X; z = vm.Effort.Z; }
                    break;
                case StrItemKind.Proposal:
                    StrProposalRow p = StrGate.Proposal(vm, id);
                    if (p != null) { x = p.X; z = p.Z; }
                    break;
                case StrItemKind.Front:
                    for (int i = 0; i < vm.FrontCount && i < vm.Fronts.Length; i++)
                        if (vm.Fronts[i] != null && vm.Fronts[i].Id == id) { x = vm.Fronts[i].X; z = vm.Fronts[i].Z; break; }
                    break;
                case StrItemKind.Sortie:
                    StrSortieRow s = StrGate.Sortie(vm, id);
                    if (s != null && s.SiteKnown) { x = s.X; z = s.Z; }
                    break;
                case StrItemKind.Post:
                    for (int i = 0; i < vm.PostCount && i < vm.Posts.Length; i++)
                        if (vm.Posts[i] != null && vm.Posts[i].Id == id) { x = vm.Posts[i].X; z = vm.Posts[i].Z; break; }
                    break;
            }
            bool known = !float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(z) && !float.IsInfinity(z);
            if (!known) x = z = float.NaN;
            return known;
        }
    }
}
