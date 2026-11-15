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
                    Totals.Earned[c] += source.Earned[c];
                    Totals.Spent[c] += source.Spent[c];
                }
                foreach (var item in source.ItemEarned)
                {
                    Totals.ItemEarned.TryGetValue(item.Key, out long previous);
                    Totals.ItemEarned[item.Key] = previous + item.Value;
                    if (item.Value != 0) Add(item.Key, source);
                }
                foreach (var item in source.ItemSpent)
                {
                    Totals.ItemSpent.TryGetValue(item.Key, out long previous);
                    Totals.ItemSpent[item.Key] = previous + item.Value;
                    if (item.Value != 0 && source.ItemProduced(item.Key) == 0) Add(item.Key, source);
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
