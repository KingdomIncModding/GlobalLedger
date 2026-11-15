using System;
using System.Collections.Generic;
using System.Linq;
using KingdomFoundation.Api;
using LightJson;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomGlobalLedger.Verification
{
    internal sealed class LedgerLayoutVerification
    {
        JsonArray checks;

        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }

        internal void Run(LedgerPanel panel, JsonArray checks)
        {
            this.checks = checks;
            var plugin = GlobalLedgerPlugin.Instance;
            var previousMode = panel.Mode;
            var previousTab = panel.SelectedTab;
            var previousUnit = panel.Unit;
            bool previousCollapsed = panel.ControlsCollapsed;
            int previousRefresh = panel.RefreshSeconds;
            bool previousRolling = plugin.EnableRollingAverage.Value;
            int previousEntries = plugin.RollingAverageEntries.Value;

            try
            {
                VerifySettingsAndGeometry(panel);
                VerifyTabsAndCategories(panel);
                VerifyColumnSorting(panel);
                VerifyPerUnitWindows(panel);
                VerifySmoothingAndRefresh(panel);
                VerifyAdvStabilityWhileOpen(panel);
            }
            finally
            {
                plugin.EnableRollingAverage.Value = previousRolling;
                plugin.RollingAverageEntries.Value = previousEntries;
                panel.SetRefresh(previousRefresh);
                panel.SetTimeUnit(previousUnit);
                panel.SetWindow(0);
                panel.SetViewMode(previousMode);
                panel.SelectTab(previousTab);
                panel.SetSearch("");
                if (panel.ControlsCollapsed != previousCollapsed)
                    panel.ToggleControls();
            }
        }

        void VerifySettingsAndGeometry(LedgerPanel panel)
        {
            if (!panel.ControlsCollapsed)
                panel.ToggleControls();
            Check(panel.ControlsCollapsed && !panel.Frame.Find("Controls").gameObject.activeSelf,
                "Settings starts collapsed with Controls hidden");
            Check(panel.Frame.Find("ControlsToggle").GetComponentInChildren<TMP_Text>().text == "Show Settings",
                "Collapsed settings button displays Show Settings");
            Check(!panel.Frame.Find("CollapseAll").gameObject.activeSelf,
                "Collapse All button is hidden in Basic mode");

            panel.ToggleControls();
            Check(!panel.ControlsCollapsed && panel.Frame.Find("Controls").gameObject.activeSelf,
                "Clicking Show Settings expands Controls");
            Check(panel.Frame.Find("ControlsToggle").GetComponentInChildren<TMP_Text>().text == "Hide Settings",
                "Expanded settings button displays Hide Settings");

            panel.SetViewMode(LedgerPanel.ViewMode.Adv);
            Check(panel.Frame.Find("CollapseAll").gameObject.activeSelf,
                "Collapse All button is visible in Advanced mode");

            Canvas.ForceUpdateCanvases();
            VerifyControlBounds(panel);

            var status = panel.Frame.Find("Window").GetComponent<TMP_Text>();
            status.ForceMeshUpdate();
            Check(!status.isTextTruncated, "Two-line status text fits within its allocated height");

            var divider = (RectTransform)panel.Frame.Find("TableDivider");
            Check(divider && divider.gameObject.activeInHierarchy && Math.Abs(divider.rect.height - 1f) < 0.1f,
                "Table divider renders cleanly between status and column headings");
        }

        void VerifyControlBounds(LedgerPanel panel)
        {
            var headerButtons = new[] { "ControlsToggle", "BasicMode", "AdvMode" }
                .Select(name => (RectTransform)panel.Frame.Find(name))
                .ToArray();
            VerifyNonOverlappingRow(headerButtons, "Header controls do not overlap");

            var controls = panel.Frame.Find("Controls");
            var rateRow = new[] { "RateLabel", "UnitSec", "UnitMin", "UnitHour", "Window0", "Window1", "Window2", "Freeze" }
                .Select(name => (RectTransform)controls.Find(name))
                .ToArray();
            VerifyNonOverlappingRow(rateRow, "Rate and observation window row controls do not overlap");

            var secondRow = new[] { "RollingToggle", "RefreshInterval", "SmoothingHint" }
                .Select(name => (RectTransform)controls.Find(name))
                .ToArray();
            VerifyNonOverlappingRow(secondRow, "Smoothing and refresh row controls do not overlap");

            var tabsRow = new[] { "AllTab", "CurrenciesTab", "ProductionTab", "CollapseAll" }
                .Select(name => (RectTransform)panel.Frame.Find(name))
                .ToArray();
            VerifyNonOverlappingRow(tabsRow, "Tab bar and Collapse All controls do not overlap");
        }

        void VerifyNonOverlappingRow(RectTransform[] rects, string label)
        {
            var bounds = new List<(float Left, float Right)>();
            foreach (var rect in rects)
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                bounds.Add((corners[0].x, corners[2].x));
            }
            for (int i = 1; i < bounds.Count; i++)
                Check(bounds[i - 1].Right <= bounds[i].Left + 0.5f, label);
        }

        void VerifyTabsAndCategories(LedgerPanel panel)
        {
            panel.SetViewMode(LedgerPanel.ViewMode.Basic);
            panel.SelectTab(0);
            Check(panel.VisibleRows.Any(r => r.Key == "category/currencies" && r.IsHeader)
                && panel.VisibleRows.Any(r => r.Key == "category/production" && r.IsHeader),
                "All tab displays both Currencies and Production category headers");

            panel.SelectTab(1);
            Check(panel.VisibleRows.Any(r => r.Key == "category/currencies")
                && !panel.VisibleRows.Any(r => r.Key == "category/production")
                && panel.VisibleRows.All(r => r.Item == null),
                "Currencies tab filters to currency rows only");

            panel.SelectTab(2);
            Check(panel.VisibleRows.Any(r => r.Key == "category/production")
                && !panel.VisibleRows.Any(r => r.Key == "category/currencies")
                && panel.VisibleRows.All(r => !r.GameCurrency.HasValue),
                "Production tab filters to production rows only");

            panel.SetViewMode(LedgerPanel.ViewMode.Adv);
            Check(panel.SelectedTab == 2, "Switching between Basic and Advanced preserves the selected tab");

            panel.SelectTab(0);
            var currenciesHeader = panel.VisibleRows.Single(r => r.Key == "category/currencies");
            panel.Activate(currenciesHeader);
            Check(panel.VisibleRows.Any(r => r.Key == "category/currencies" && !r.Open)
                && panel.VisibleRows.All(r => !r.GameCurrency.HasValue),
                "Clicking Currencies category header collapses only currency rows");

            panel.Activate(panel.VisibleRows.Single(r => r.Key == "category/currencies"));
            Check(panel.VisibleRows.Any(r => r.Key == "category/currencies" && r.Open)
                && panel.VisibleRows.Any(r => r.GameCurrency.HasValue),
                "Clicking collapsed Currencies header re-expands currency rows");

            panel.CollapseAll();
            Check(panel.VisibleRows.Count == 2
                && panel.VisibleRows.All(r => r.IsHeader && !r.Open),
                "Collapse All collapses both category headers and nested rows in Advanced mode");

            panel.SetSearch("iron");
            Check(panel.VisibleRows.Any(r => r.Item?.Identifier == "iron_ore"),
                "Searching reveals matching items even when category headers were collapsed");
            panel.SetSearch("");
        }

        void VerifyColumnSorting(LedgerPanel panel)
        {
            panel.SetViewMode(LedgerPanel.ViewMode.Basic);
            panel.SelectTab(0);
            var netButton = panel.Frame.Find("NetHeading").GetComponent<Button>();
            var nameButton = panel.Frame.Find("NameHeading").GetComponent<Button>();
            Check(netButton.interactable && nameButton.interactable,
                "Basic mode column headings are interactable for sorting and reset");

            var defaultBasicItems = panel.VisibleRows.Where(r => r.Item != null).Select(r => r.Item.Identifier).ToArray();
            netButton.onClick.Invoke();
            var descBasicItems = panel.VisibleRows.Where(r => r.Item != null).Select(r => r.Item.Identifier).ToArray();
            Check(descBasicItems[0] == "iron_ingot" && descBasicItems[descBasicItems.Length - 1] == "iron_ore",
                "Clicking NET heading in Basic mode sorts items descending by net rate");
            netButton.onClick.Invoke();
            var ascBasicItems = panel.VisibleRows.Where(r => r.Item != null).Select(r => r.Item.Identifier).ToArray();
            Check(ascBasicItems[0] == "iron_ore" && ascBasicItems[ascBasicItems.Length - 1] == "iron_ingot",
                "Second click on Basic NET heading sorts items ascending by net rate");
            nameButton.onClick.Invoke();
            Check(panel.VisibleRows.Where(r => r.Item != null).Select(r => r.Item.Identifier).SequenceEqual(defaultBasicItems),
                "Clicking RESOURCE heading in Basic mode restores default order");

            panel.SetViewMode(LedgerPanel.ViewMode.Adv);
            panel.SelectTab(2);
            var incomeButton = panel.Frame.Find("IncomeHeading").GetComponent<Button>();
            var outflowButton = panel.Frame.Find("OutflowHeading").GetComponent<Button>();
            Check(incomeButton.interactable && outflowButton.interactable,
                "Advanced mode IN and OUT headings are interactable");

            incomeButton.onClick.Invoke();
            var descAdvItems = panel.VisibleRows.Where(r => r.Depth == 0 && r.Item != null).Select(r => r.Item.Identifier).ToArray();
            Check(descAdvItems[0] == "iron_ore" && descAdvItems[descAdvItems.Length - 1] == "iron_sword",
                "Clicking IN heading in Advanced mode sorts items descending by production");
            outflowButton.onClick.Invoke();
            var descOutItems = panel.VisibleRows.Where(r => r.Depth == 0 && r.Item != null).Select(r => r.Item.Identifier).ToArray();
            Check(descOutItems[0] == "iron_ore" && descOutItems[descOutItems.Length - 1] == "iron_sword",
                "Clicking OUT heading in Advanced mode sorts items descending by consumption");
            netButton.onClick.Invoke();
            panel.SetViewMode(LedgerPanel.ViewMode.Basic);
            Check(panel.Frame.Find("NetHeading").GetComponentInChildren<TMP_Text>().text.EndsWith(" v", StringComparison.Ordinal),
                "NET sort selection persists when switching from Advanced to Basic");
            panel.SetViewMode(LedgerPanel.ViewMode.Adv);
            incomeButton.onClick.Invoke();
            panel.SetViewMode(LedgerPanel.ViewMode.Basic);
            Check(!panel.Frame.Find("NetHeading").GetComponentInChildren<TMP_Text>().text.Contains("v")
                && !panel.Frame.Find("NetHeading").GetComponentInChildren<TMP_Text>().text.Contains("^"),
                "Switching from Advanced IN sort to Basic clears hidden column sort");
            panel.SetSort(0);
        }

        void VerifyPerUnitWindows(LedgerPanel panel)
        {
            panel.SetViewMode(LedgerPanel.ViewMode.Adv);
            panel.SelectTab(0);
            string[][] expectedLabels =
            {
                new[] { "1s", "10s", "30s" },
                new[] { "1m", "5m", "30m" },
                new[] { "1h", "12h", "24h" }
            };
            int[][] expectedSeconds =
            {
                new[] { 1, 10, 30 },
                new[] { 60, 300, 1800 },
                new[] { 3600, 43200, 86400 }
            };
            int[] chosenOptions = { 2, 1, 2 };
            var controls = panel.Frame.Find("Controls");

            for (int unit = 0; unit < 3; unit++)
            {
                panel.SetTimeUnit((LedgerPanel.TimeUnit)unit);
                for (int i = 0; i < 3; i++)
                {
                    string text = controls.Find("Window" + i).GetComponentInChildren<TMP_Text>().text;
                    Check(text == expectedLabels[unit][i],
                        "Window button " + i + " displays " + expectedLabels[unit][i] + " for " + (LedgerPanel.TimeUnit)unit);
                }
                panel.SetWindow(chosenOptions[unit]);
            }

            for (int unit = 0; unit < 3; unit++)
            {
                panel.SetTimeUnit((LedgerPanel.TimeUnit)unit);
                Check(panel.WindowSeconds == expectedSeconds[unit][chosenOptions[unit]],
                    "Switching back to " + (LedgerPanel.TimeUnit)unit + " preserves its selected observation window");
                string expectedSuffix = unit == 0 ? "/Sec" : unit == 1 ? "/Min" : "/Hr";
                Check(panel.Frame.Find("NetHeading").GetComponentInChildren<TMP_Text>().text.Contains(expectedSuffix),
                    "Column heading reflects rate unit " + expectedSuffix);
            }
        }

        void VerifySmoothingAndRefresh(LedgerPanel panel)
        {
            panel.SetSmoothing(0);
            var rollingLabel = panel.Frame.Find("Controls/RollingToggle").GetComponentInChildren<TMP_Text>();
            Check(rollingLabel.text == "Smooth: OFF" && panel.StatusText.Contains("smoothing: OFF"),
                "Smoothing starts OFF when disabled");

            for (int s = 2; s <= 5; s++)
            {
                panel.CycleSmoothing();
                Check(panel.SmoothingSamples == s
                    && rollingLabel.text == "Smooth: " + s
                    && panel.StatusText.Contains("smoothing: " + s + " samples"),
                    "CycleSmoothing advances to " + s + " samples");
            }

            panel.CycleSmoothing();
            Check(panel.SmoothingSamples == 0 && rollingLabel.text == "Smooth: OFF",
                "CycleSmoothing wraps from 5 samples back to OFF");

            panel.SetSmoothing(30);
            Check(panel.SmoothingSamples == 30 && rollingLabel.text == "Smooth: 30",
                "Configured sample counts up to 30 display without a seconds suffix");

            panel.SetRefresh(10);
            var refreshLabel = panel.Frame.Find("Controls/RefreshInterval").GetComponentInChildren<TMP_Text>();
            Check(panel.RefreshSeconds == 4 && refreshLabel.text == "Refresh: 4s" && panel.StatusText.Contains("Refresh every 4s."),
                "Refresh interval clamps to 1-4s and updates button and status text");

            var window = new LedgerSampleWindow();
            var row = new LedgerRowData("sample-smoothing", "Smoothing", 0) { GameCurrency = GameCurrency.Mana, Income = 10, Outflow = 0 };
            window.Apply(row, 2, true);
            row.Income = 30;
            window.Apply(row, 2, true);
            Check(row.Income == 20, "Sample-based smoothing averages the configured refresh sample count");

            row.Income = 50;
            window.Apply(row, 2, true);
            Check(row.Income == 40, "Sample-based smoothing evicts the oldest refresh sample");
        }

        void VerifyAdvStabilityWhileOpen(LedgerPanel ownerPanel)
        {
            GlobalLedgerPlugin.Instance.EnableRollingAverage.Value = false;
            double now = 1000;
            var tempBook = new LedgerBook(() => now);
            var anchor = ownerPanel.transform.parent.Find("Anchor").GetComponent<Button>();
            var tempPanel = LedgerPanel.Create(tempBook, anchor);
            var source = new LedgerBookSource("adv-stability-src", "Fixture", "Workshop", "Stability Smelter");
            tempBook.Record(source, GameCurrency.Gold, 0, 50);
            tempBook.RecordItem(source, "iron_ore", 100, 0);
            tempBook.RecordItem(source, "iron_ingot", 20, 0);
            tempPanel.OpenPopup();
            tempPanel.enabled = false;
            try
            {
                tempPanel.SetTimeUnit(LedgerPanel.TimeUnit.Sec);
                tempPanel.SetWindow(0);
                tempPanel.SetViewMode(LedgerPanel.ViewMode.Adv);
                tempPanel.SelectTab(2);
                tempPanel.ToggleRow("item/iron_ore");
                string groupKey = tempPanel.VisibleRows.Single(r => r.Depth == 1 && r.Key.StartsWith("item/iron_ore/", StringComparison.Ordinal)).Key;
                tempPanel.ToggleRow(groupKey);
                string buildingKey = tempPanel.VisibleRows.Single(r => r.Depth == 2 && r.Key.StartsWith(groupKey + "/", StringComparison.Ordinal)).Key;
                tempPanel.ToggleRow(buildingKey);
                tempPanel.ToggleRow(buildingKey + "/costs");
                var costRowTransform = tempPanel.Scroller.content.Cast<Transform>()
                    .Single(child => child.gameObject.activeSelf && child.name == buildingKey + "/costs/currency/0");
                Check(costRowTransform.Find("AssetIcon").GetComponent<Image>().enabled
                    && costRowTransform.Find("NameViewport/Name").GetComponent<TMP_Text>().text == "Gold",
                    "Operating cost rows display both the cost icon and item/currency name");
                tempPanel.SetSort(2);

                var initialOrder = tempPanel.VisibleRows.Where(r => r.Depth == 0 && r.Item != null).Select(r => r.Item.Identifier).ToArray();
                Check(initialOrder.SequenceEqual(new[] { "iron_ore", "iron_ingot" }),
                    "Advanced IN heading ranks higher initial production first");

                tempBook.RecordItem(source, "iron_ingot", 5000, 0);
                tempPanel.ToggleFreeze();
                tempPanel.ToggleFreeze();
                var afterRefreshOrder = tempPanel.VisibleRows.Where(r => r.Depth == 0 && r.Item != null).Select(r => r.Item.Identifier).ToArray();
                decimal ingotIncome = tempPanel.VisibleRows.Single(r => r.Key == "item/iron_ingot").Income;
                Check(ingotIncome == 5020 && afterRefreshOrder.SequenceEqual(initialOrder),
                    "Advanced tab keeps row order locked across live refreshes while open");

                now += 10;
                tempPanel.ToggleFreeze();
                tempPanel.ToggleFreeze();
                var zeroOre = tempPanel.VisibleRows.FirstOrDefault(r => r.Key == "item/iron_ore");
                var zeroIngot = tempPanel.VisibleRows.FirstOrDefault(r => r.Key == "item/iron_ingot");
                var zeroBuilding = tempPanel.VisibleRows.FirstOrDefault(r => r.Depth == 2 && r.Key.StartsWith(groupKey + "/", StringComparison.Ordinal));
                Check(zeroOre != null && zeroOre.Income == 0
                    && zeroIngot != null && zeroIngot.Income == 0
                    && zeroBuilding != null && zeroBuilding.Income == 0,
                    "Advanced tab keeps visible items and expanded sources at 0 instead of removing them while open");

                tempBook.RecordItem(source, "iron_ingot", 300, 0);
                tempBook.RecordItem(source, "iron_ore", 10, 0);
                tempPanel.ToggleFreeze();
                tempPanel.ToggleFreeze();
                tempPanel.SetSort(0);
                tempPanel.SetSort(2);
                var reSortedOrder = tempPanel.VisibleRows.Where(r => r.Depth == 0 && r.Item != null).Select(r => r.Item.Identifier).ToArray();
                Check(reSortedOrder.SequenceEqual(new[] { "iron_ingot", "iron_ore" }),
                    "Clicking a column heading in Advanced tab re-sorts the locked list");

                now += 10;
                tempBook.RecordItem(source, "iron_ingot", 50, 0);
                tempPanel.ClosePopup(false);
                tempPanel.OpenPopup();
                tempPanel.RefreshNow();
                var reopenedOrder = tempPanel.VisibleRows.Where(r => r.Depth == 0 && r.Item != null).Select(r => r.Item.Identifier).ToArray();
                Check(reopenedOrder.SequenceEqual(new[] { "iron_ingot" }),
                    "Closing and reopening the ledger clears expired pinned Advanced items");
            }
            finally
            {
                if (tempPanel) UnityEngine.Object.Destroy(tempPanel.gameObject);
            }
        }
    }
}
