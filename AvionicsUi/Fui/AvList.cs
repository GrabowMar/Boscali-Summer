using System;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>Paged, pooled row list with a pager line ("PREV  1–5 OF 23  NEXT"). Pool ceiling 64 rows.</summary>
    public sealed class AvList : AvPagedStack<AvRow>
    {
        public const int MaxPageSize = 64;

        public AvList(RectTransform parent, AvTicker ticker, int pageSize, Action<int, AvRow> binder)
            : base(parent, ticker, pageSize, 2f, NewRow, (item, row) => { binder?.Invoke(item, row); row.Restyle(); },
                "List", MaxPageSize, pagerLead: 2f, startHidden: false, trailingGap: true)
        {
        }

        /// <summary>Item index of a clicked row (rows are clickable only when this is set).</summary>
        public Action<int> RowClicked;

        private static AvRow NewRow(RectTransform parent, int slot)
        {
            AvRow row = null;
            row = new AvRow(parent, () => { var list = (AvList)row.Parent; list.RowClicked?.Invoke(list.Page * list.PageSize + slot); });
            return row;
        }

        // The rows repaint as they bind; the list has no restyle of its own.
        public override void Restyle() { }
    }
}
