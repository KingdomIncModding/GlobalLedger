using System;
using System.Collections.Generic;

namespace KingdomGlobalLedger
{
    internal sealed class LedgerSampleWindow
    {
        readonly Queue<(decimal Income, decimal Outflow)> values = new Queue<(decimal, decimal)>();
        decimal income, outflow;

        internal void Apply(LedgerRowData row, int count, bool sample)
        {
            if (sample || values.Count == 0)
            {
                values.Enqueue((row.Income, row.Outflow));
                income = LedgerMath.AddSaturated(income, row.Income);
                outflow = LedgerMath.AddSaturated(outflow, row.Outflow);
            }
            while (values.Count > Math.Max(1, count))
            {
                var previous = values.Dequeue();
                income = LedgerMath.SubSaturated(income, previous.Income);
                outflow = LedgerMath.SubSaturated(outflow, previous.Outflow);
            }
            row.Income = LedgerMath.RoundWhole(income / values.Count);
            row.Outflow = LedgerMath.RoundWhole(outflow / values.Count);
            row.Amount = LedgerMath.SubSaturated(row.Income, row.Outflow);
        }
    }
}
