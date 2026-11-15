using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Assets.Behaviour.Input;
using HarmonyLib;
using KingdomFoundation.Api;
using LightJson;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingdomGlobalLedger.Verification
{
    internal sealed class LedgerFeedbackVerification
    {
        JsonArray checks;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            checks.Add(name);
        }

        internal void Run(LedgerPanel panel, JsonArray checks, string output)
        {
            this.checks = checks;
            VerifyOrdering();
            VerifySearchInput(panel);
            VerifyLargeItems(panel, output);
        }

        void VerifyOrdering()
        {
            var rows = new[]
            {
                new LedgerRowData("category/currencies", "CURRENCIES", -1) { IsHeader = true, Expandable = true, Open = true },
                new LedgerRowData("currency/0", "Gold", 0) { Income = 1 },
                new LedgerRowData("currency/0/a", "A", 1) { Income = 10 },
                new LedgerRowData("currency/0/b", "B", 1) { Income = 100 },
                new LedgerRowData("currency/0/b/low", "Low", 2) { Income = 2 },
                new LedgerRowData("currency/0/b/high", "High", 2) { Income = 20 },
                new LedgerRowData("category/production", "PRODUCTION", -1) { IsHeader = true, Expandable = true, Open = true },
                new LedgerRowData("item/flux", "Mercurial", 0) { Income = 1000 },
                new LedgerRowData("item/ore", "Iron Ore", 0) { Income = 5000 }
            };
            var ordering = new LedgerRowOrdering();
            var ranked = ordering.Arrange(rows, "", 2, true);
            Check(ranked.Where(row => row.IsHeader).Select(row => row.Key).SequenceEqual(new[] { "category/currencies", "category/production" }), "Numeric sorting preserves category header order");
            Check(ranked.Where(row => row.Depth == 0).Select(row => row.Key).SequenceEqual(new[] { "currency/0", "item/ore", "item/flux" }), "Numeric sorting ranks top-level items within their category");
            Check(ranked[2].Label == "B" && ranked[3].Label == "High" && ranked[4].Label == "Low" && ranked[5].Label == "A", "Sorting ranks sibling sources while retaining their hierarchy");
            Check(ordering.Arrange(rows, "", 0, false).Select(row => row.Label).SequenceEqual(new[] { "CURRENCIES", "Gold", "A", "B", "Low", "High", "PRODUCTION", "Mercurial", "Iron Ore" }), "RESOURCE restores original resource and sibling order");
            var found = ordering.Arrange(rows, "High", 2, true);
            Check(found.Count == 4 && found[0].Label == "CURRENCIES" && found[1].Label == "Gold" && found[2].Label == "B", "Filtered search preserves ancestors without unrelated resources");
            Check(LedgerUi.FormatAmount(1) == "1.00" && LedgerUi.FormatAmount(0) == "0.00" && LedgerUi.FormatAmount(-.001) == "0.00", "Zero and integer rates retain two decimal places without negative zero");
        }

        void VerifySearchInput(LedgerPanel panel)
        {
            // Use the actual native action asset without starting a player-input MonoBehaviour.
            var type = typeof(Assets.Behaviour.Input.PlayerInput);
            var field = AccessTools.Field(type, "_inputInstance");
            var original = field.GetValue(null);
            bool enabled = FoundationApi.UiAccess.IsGameInputEnabled;
            var actions = new InputActions();
            field.SetValue(null, actions);
            try
            {
                FoundationApi.UiAccess.SetGameInputEnabled(true);
                var input = panel.Frame.Find("Search").GetComponent<TMP_InputField>();
                input.onSelect.Invoke("");
                Check(!actions.UI.ToggleItemMenu.enabled && !actions.UI.ToggleResources.enabled, "Search focus disables native I and R menu actions");
                input.onDeselect.Invoke("Iron");
                Check(actions.UI.ToggleItemMenu.enabled && actions.UI.ToggleResources.enabled, "Leaving search restores native menu hotkeys");
                FoundationApi.UiAccess.SetGameInputEnabled(false);
                input.onSelect.Invoke(""); input.onDeselect.Invoke("");
                Check(!FoundationApi.UiAccess.IsGameInputEnabled, "Search does not enable game input that was disabled before focus");
                FoundationApi.UiAccess.SetGameInputEnabled(true);
                input.onSelect.Invoke("");
                panel.ClosePopup(false);
                Check(FoundationApi.UiAccess.IsGameInputEnabled, "Closing the ledger restores input after typing");
                panel.OpenPopup(); panel.enabled = false;
            }
            finally
            {
                actions.Disable(); actions.Dispose();
                field.SetValue(null, original);
                FoundationApi.UiAccess.SetGameInputEnabled(enabled);
            }
        }

        void VerifyLargeItems(LedgerPanel panel, string output)
        {
            var book = GlobalLedgerPlugin.Instance.Book;
            // Thousands of independent source records exercise the actual item-filter path.
            for (int i = 0; i < 2000; i++)
            {
                var source = new LedgerBookSource("stress/" + i, "Fixture", "Workshop", "Stress source " + i);
                book.RecordItem(source, "iron_ore", 10 + i, i % 5);
                book.RecordItem(source, "iron_ingot", i, 2);
            }
            panel.SetViewMode(LedgerPanel.ViewMode.Adv);
            panel.SelectTab(2);
            panel.SetTimeUnit(LedgerPanel.TimeUnit.Min);
            panel.SetWindow(0);
            var timer = Stopwatch.StartNew();
            panel.SetSearch("Stress source");
            Canvas.ForceUpdateCanvases();
            timer.Stop();
            int count = panel.VisibleRows.Count;
            Check(count >= 4000, "Advanced item filtering and source search handle over 4,000 result rows");
            int maximum = Mathf.CeilToInt(panel.Scroller.viewport.rect.height / LedgerPanel.RowHeight) + 2;
            Check(panel.RenderedRowCount <= maximum + 3 && panel.RenderedRowCount < 30, "Thousands of results use only a viewport-sized row pool");
            panel.Scroller.content.anchoredPosition = new Vector2(0, (count - 10) * LedgerPanel.RowHeight);
            panel.Scroller.onValueChanged.Invoke(Vector2.zero);
            Check(panel.Scroller.content.Cast<Transform>().Where(row => row.gameObject.activeSelf).Any(row => row.name == panel.VisibleRows[count - 10].Key), "Scrolling reuses row objects to display the bottom of a large result set");
            panel.SetSearch("");
            Check(panel.VisibleRows.All(row => row.IsHeader || row.Depth == 0 || row.Key.StartsWith("item/iron_ore/")), "Clearing search restores collapsed item branches");
            panel.SetSort(0);
            panel.SetSort(2); panel.SetSort(2); panel.SetSort(2);
            Check(panel.Frame.Find("IncomeHeading").GetComponentInChildren<TMP_Text>().text.IndexOf("v", StringComparison.Ordinal) < 0, "Third numeric heading click restores default ordering");
            var report = new JsonObject();
            report["sourceCount"] = 2000; report["resultRows"] = count;
            report["renderedRowPool"] = panel.RenderedRowCount; report["filterAndCanvasMilliseconds"] = timer.Elapsed.TotalMilliseconds;
            File.WriteAllText(Path.Combine(output, "large-item-filter.json"), report.ToString());
            VerifyValueSpacing(panel);
            panel.SetViewMode(LedgerPanel.ViewMode.Basic);
            panel.SelectTab(0);
            var goldRow = panel.Scroller.content.Cast<Transform>().Single(row => row.name == "basic/currency/0");
            var gold = goldRow.Find("Net/Amount").GetComponent<TMP_Text>();
            Check(gold.text.Contains("mspace="), "Coin rates use explicit denomination letters and equal-width pixel characters");
            Check(goldRow.Find("Net/Coin").GetComponent<UnityEngine.UI.Image>().sprite == LedgerUi.SilverCoin() && goldRow.Find("Net/Denomination").GetComponent<TMP_Text>().text == "s", "Currency rate retains the native denomination icon and a readable letter label");
            VerifyValueSpacing(panel);
        }

        void VerifyValueSpacing(LedgerPanel panel)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var amount in panel.Scroller.content.GetComponentsInChildren<TMP_Text>().Where(text => text.name == "Amount"))
            {
                amount.ForceMeshUpdate();
                Check(!amount.isTextTruncated, "Numeric value fits without clipping: " + amount.text);
                for (int i = 1; i < amount.textInfo.characterCount; i++)
                {
                    var previous = amount.textInfo.characterInfo[i - 1];
                    var current = amount.textInfo.characterInfo[i];
                    if (previous.isVisible && current.isVisible)
                        Check(previous.topRight.x <= current.bottomLeft.x + .1f, "Numeric glyphs retain separation");
                }
            }
            if (panel.Mode == LedgerPanel.ViewMode.Basic)
            {
                var mana = panel.Scroller.content.Cast<Transform>().Single(row => row.name == "basic/currency/4").Find("Net/Amount").GetComponent<TMP_Text>();
                Check(Math.Abs(mana.fontSize - 17) < .1f, "Basic values use the full numeric font size instead of shrinking to a narrow column");
            }
        }
    }
}
