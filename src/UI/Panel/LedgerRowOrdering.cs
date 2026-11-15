using System;
using System.Collections.Generic;

namespace KingdomGlobalLedger
{
    internal sealed class LedgerRowOrdering
    {
        internal static readonly string[] Fields = { "Tier / Name", "Name", "Inflow", "Outflow", "Net" };
        internal List<LedgerRowData> Arrange(
            IReadOnlyList<LedgerRowData> rows,
            string search,
            int field,
            bool descending,
            IReadOnlyDictionary<string, int> lockedOrder = null)
            => Level(rows, 0, rows.Count, search.Trim(), field, descending, lockedOrder);

        List<LedgerRowData> Level(
            IReadOnlyList<LedgerRowData> rows,
            int start,
            int end,
            string search,
            int field,
            bool descending,
            IReadOnlyDictionary<string, int> lockedOrder)
        {
            var blocks = new List<(LedgerRowData Root, List<LedgerRowData> Children, int Original)>();
            int at = start;
            while (at < end)
            {
                var row = rows[at]; int next = at + 1;
                while (next < end && rows[next].Depth > row.Depth) next++;
                bool match = search.Length == 0 || row.Label.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
                var children = Level(rows, at + 1, next, match && !row.IsHeader ? "" : search, field, descending, lockedOrder);
                if (match && (!row.IsHeader || search.Length == 0) || children.Count != 0) blocks.Add((row, children, at));
                at = next;
            }
            if (field >= 2 || lockedOrder != null)
                blocks.Sort((a, b) => CompareBlocks(a, b, field, descending, lockedOrder));
            var result = new List<LedgerRowData>();
            foreach (var block in blocks) { result.Add(block.Root); result.AddRange(block.Children); }
            return result;
        }

        static int CompareBlocks(
            (LedgerRowData Root, List<LedgerRowData> Children, int Original) a,
            (LedgerRowData Root, List<LedgerRowData> Children, int Original) b,
            int field,
            bool descending,
            IReadOnlyDictionary<string, int> lockedOrder)
        {
            if (a.Root.IsHeader || b.Root.IsHeader) return a.Original.CompareTo(b.Original);
            if (lockedOrder != null)
            {
                bool hasA = lockedOrder.TryGetValue(a.Root.Key, out int rankA);
                bool hasB = lockedOrder.TryGetValue(b.Root.Key, out int rankB);
                if (hasA != hasB) return hasA ? -1 : 1;
                if (hasA && rankA != rankB) return rankA.CompareTo(rankB);
            }
            if (field < 2) return a.Original.CompareTo(b.Original);
            decimal av = field == 2 ? a.Root.Income : field == 3 ? a.Root.Outflow : a.Root.Amount;
            decimal bv = field == 2 ? b.Root.Income : field == 3 ? b.Root.Outflow : b.Root.Amount;
            int compare = av.CompareTo(bv);
            if (compare == 0) return a.Original.CompareTo(b.Original);
            return descending ? -compare : compare;
        }
    }
}
