using KingdomFoundation.Api;
using System.Collections.Generic;

namespace KingdomGlobalLedger
{
    internal sealed class LedgerFrame
    {
        internal WorkspaceObject Element;
        internal LedgerFrame Parent;
        internal GameKingdom Player;
        internal LedgerObservationPlan Plan;
        internal bool CaptureOutputs;
        internal bool CustomNode;
        internal readonly decimal[] Income = new decimal[9], Spending = new decimal[9], Tray = new decimal[9];
        internal readonly HashSet<GameInventoryItem> OutputsBefore = new HashSet<GameInventoryItem>();
        internal readonly List<(GameInventoryItem item, GameItemType type)> VisitorsBefore = new List<(GameInventoryItem, GameItemType)>();
        internal readonly List<GameInventoryItem> ConsumedCandidates = new List<GameInventoryItem>();
        internal readonly HashSet<GameInventoryItem> RemainingVisitors = new HashSet<GameInventoryItem>();
        internal readonly Dictionary<string, decimal> Produced = new Dictionary<string, decimal>(), Consumed = new Dictionary<string, decimal>();
    }
}
