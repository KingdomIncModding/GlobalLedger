using System;
using System.Collections.Generic;

namespace KingdomGlobalLedger
{
    // Aggregate once per snapshot instead of scanning every source for each item.
    internal sealed class LedgerSnapshotIndex
    {
        internal readonly LedgerBookTotals Totals = new LedgerBookTotals(null);
        internal readonly Dictionary<string, List<LedgerBookTotals>> Items = new Dictionary<string, List<LedgerBookTotals>>(StringComparer.Ordinal);

        internal LedgerSnapshotIndex(IReadOnlyList<LedgerBookTotals> snapshot)
        {
            foreach (var source in snapshot)
            {
                for (int c = 0; c < LedgerBook.CurrencyCount; c++)
                {
                    Totals.AddEarned(c, source.EarnedAt(c));
                    Totals.AddSpent(c, source.SpentAt(c));
                }
                foreach (var item in source.EarnedItems)
                {
                    if (item.Value == 0m) continue;
                    Totals.AddItemProduced(item.Key, item.Value);
                    Add(item.Key, source);
                }
                foreach (var item in source.SpentItems)
                {
                    if (item.Value == 0m) continue;
                    Totals.AddItemConsumed(item.Key, item.Value);
                    if (source.ItemProduced(item.Key) == 0m) Add(item.Key, source);
                }
            }
        }

        void Add(string item, LedgerBookTotals source)
        {
            if (!Items.TryGetValue(item, out var sources)) Items[item] = sources = new List<LedgerBookTotals>();
            sources.Add(source);
        }
    }
}
