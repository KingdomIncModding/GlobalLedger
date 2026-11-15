using KingdomFoundation.Api;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KingdomGlobalLedger
{
    internal sealed class LedgerSourceCache
    {
        static ConditionalWeakTable<WorkspaceObject, LedgerSourceCache> cache = new ConditionalWeakTable<WorkspaceObject, LedgerSourceCache>();
        static readonly Regex Words = new Regex("([a-z])([A-Z])", RegexOptions.Compiled);
        LedgerBookSource source;
        string label, recipe, flowLabel, locale;
        Vector2Int position;
        GameWorkspace workspace;

        internal static LedgerBookSource Describe(WorkspaceObject element)
            => cache.GetValue(element, value => new LedgerSourceCache()).Read(element);

        LedgerBookSource Read(WorkspaceObject element)
        {
            var flow = GlobalLedgerPlugin.Instance?.Locations.WorkspaceOf(element) ?? element.ParentWorkspace;
            string nextLabel = element.Label;
            string nextRecipe = element.Production.RecipeDisplayName;
            string nextFlowLabel = flow?.Label;
            string nextLocale = LedgerLocations.CurrentLocale;
            if (source != null && workspace == flow && label == nextLabel && recipe == nextRecipe
                && flowLabel == nextFlowLabel && position == element.Position && locale == nextLocale)
                return source;
            workspace = flow;
            label = nextLabel;
            recipe = nextRecipe;
            flowLabel = nextFlowLabel;
            position = element.Position;
            locale = nextLocale;
            string rawDefaultLabel = element.Production.DefaultLabel;
            string typeName = FoundationApi.GameInfo.Translate(rawDefaultLabel);
            if (string.IsNullOrEmpty(typeName) || typeName.StartsWith("@"))
                typeName = Words.Replace(element.TypeId, "$1 $2");
            string rawDetailKey = element.Production.HasRecipe ? recipe : label;
            string detail = FoundationApi.GameInfo.Translate(rawDetailKey);
            string name = (LedgerLocations.CleanLabel(flowLabel) ?? "Workspace") + " / " + detail + " [" + position.x + ", " + position.y + "]";
            return source = new LedgerBookSource(element.Guid, element.TypeId, typeName, name, nextLocale, rawDefaultLabel, rawDetailKey, flowLabel, position);
        }

        internal static void Reset() => cache = new ConditionalWeakTable<WorkspaceObject, LedgerSourceCache>();
    }
}
