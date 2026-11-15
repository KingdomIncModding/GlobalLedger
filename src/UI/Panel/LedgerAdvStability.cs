using System;
using System.Collections.Generic;

namespace KingdomGlobalLedger
{
    /// <summary>
    /// Preserves Advanced view item visibility and row ordering while the ledger popup remains open.
    /// </summary>
    internal sealed class LedgerAdvStability
    {
        internal readonly Dictionary<string, int> OrderRank = new Dictionary<string, int>();
        readonly HashSet<string> pinnedItems = new HashSet<string>();
        readonly Dictionary<string, List<LedgerRowData>> pinnedChildren = new Dictionary<string, List<LedgerRowData>>();
        readonly Dictionary<string, Dictionary<string, LedgerRowData>> pinnedByParentKey = new Dictionary<string, Dictionary<string, LedgerRowData>>();
        int nextOrderRank;

        internal void Clear()
        {
            pinnedItems.Clear();
            pinnedChildren.Clear();
            pinnedByParentKey.Clear();
            ResetOrder();
        }

        internal void ResetOrder()
        {
            OrderRank.Clear();
            nextOrderRank = 0;
        }

        internal bool IsPinnedItem(string itemId) => pinnedItems.Contains(itemId);

        internal void PinItem(string itemId) => pinnedItems.Add(itemId);

        internal void RememberChild(string parentKey, LedgerRowData row)
        {
            if (!pinnedChildren.TryGetValue(parentKey, out var list))
            {
                list = new List<LedgerRowData>();
                pinnedChildren[parentKey] = list;
                pinnedByParentKey[parentKey] = new Dictionary<string, LedgerRowData>();
            }

            var byKey = pinnedByParentKey[parentKey];
            if (!byKey.TryGetValue(row.Key, out var template))
            {
                template = CloneStaticTemplate(row);
                byKey[row.Key] = template;
                list.Add(template);
                return;
            }

            template.PrefixIcon = row.PrefixIcon;
            template.Icon = row.Icon;
            template.Target = row.Target;
        }

        internal bool EmitMissingChildren(
            string parentKey,
            HashSet<string> emittedKeys,
            List<LedgerRowData> targetRows,
            HashSet<string> expanded,
            string search)
        {
            if (!pinnedChildren.TryGetValue(parentKey, out var list) || list.Count == 0)
                return false;

            bool hasAny = false;
            for (int i = 0; i < list.Count; i++)
            {
                var template = list[i];
                if (!emittedKeys.Add(template.Key))
                {
                    hasAny = true;
                    continue;
                }

                var row = CloneStaticTemplate(template);
                row.Open = expanded.Contains(row.Key);
                targetRows.Add(row);
                hasAny = true;

                if (!row.Expandable || (!row.Open && string.IsNullOrEmpty(search)))
                    continue;

                bool childHasAny = EmitMissingChildren(row.Key, emittedKeys, targetRows, expanded, search);
                if (!childHasAny && row.Key.EndsWith("/costs", StringComparison.Ordinal))
                    targetRows.Add(new LedgerRowData(row.Key + "/empty", "No observed operating costs", row.Depth + 1));
            }

            return hasAny;
        }

        internal void RecordOrder(IReadOnlyList<LedgerRowData> orderedRows)
        {
            for (int i = 0; i < orderedRows.Count; i++)
            {
                string key = orderedRows[i].Key;
                if (!OrderRank.ContainsKey(key))
                    OrderRank[key] = nextOrderRank++;
            }
        }

        static LedgerRowData CloneStaticTemplate(LedgerRowData row)
        {
            return new LedgerRowData(row.Key, row.Label, row.Depth)
            {
                GameCurrency = row.GameCurrency,
                Item = row.Item,
                Expandable = row.Expandable,
                IconOnly = row.IconOnly,
                PrefixIcon = row.PrefixIcon,
                Icon = row.Icon,
                Target = row.Target
            };
        }
    }
}

