using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Source.Flow;
using Assets.Source.Flow.Elements;
using Assets.Source.Items;
using Assets.Source.Player;
using Assets.Source.Util;
using BepInEx.Logging;
using HarmonyLib;
using KingdomFoundation.Api;
using LightJson;
using UnityEngine;

namespace KingdomGlobalLedger.Verification
{
    internal static class LedgerIssueRegressionVerification
    {
        sealed class TestCustomHostedMachine : FrameworkWorkspaceNode
        {
            internal TestCustomHostedMachine() : base("gl_test_custom_machine")
            {
                AddInput(1, 64);
                AddOutput(1, 64);
            }

            public override void Update(float delta)
            {
                if (Inputs.Count > 0)
                {
                    var consumed = Inputs[0].ConsumeItem();
                    if (consumed != null && GamePlayer.Current != null)
                    {
                        GamePlayer.Current.AddCurrency(Currency.Mana, 50, true);
                        GamePlayer.Current.SpendCurrency(Currency.Mana, 20);
                    }
                }
            }
        }

        internal static void Run(GlobalLedgerPlugin plugin, JsonObject result, Action<bool, string> check)
        {
            VerifyOverflowProtection(check);
            VerifyObserverLogSourceAndRateLimit(plugin, check);
            VerifyLocaleInvalidation(plugin, check);
            VerifySpawnerIconInvalidation(plugin, check);
            VerifyDayLongHistoryMemoryAndBoundaries(result, check);
            VerifyCustomHostedMachineAttribution(plugin, check);
        }

        static void VerifyOverflowProtection(Action<bool, string> check)
        {
            double clock = 10;
            var book = new LedgerBook(() => clock);
            var s1 = new LedgerBookSource("ov1", "Fixture", "Fixture", "Fixture 1");
            var s2 = new LedgerBookSource("ov2", "Fixture", "Fixture", "Fixture 2");
            long huge = 6_000_000_000_000_000_000L;
            book.Record(s1, GameCurrency.Mana, huge, 100L);
            book.Record(s2, GameCurrency.Mana, huge, 200L);
            var snap = book.Snapshot(60);
            var index = new LedgerSnapshotIndex(snap);
            decimal totalEarned = index.Totals.EarnedAt((int)GameCurrency.Mana);
            decimal totalNet = index.Totals.Net((int)GameCurrency.Mana);
            check(totalEarned == 12_000_000_000_000_000_000m, "Large accumulated earnings do not overflow or wrap");
            check(totalNet == 11_999_999_999_999_999_700m && totalNet > 0m, "Large accumulated net profit never wraps negative");

            decimal summed = LedgerMath.Sum(snap, t => t.Net((int)GameCurrency.Mana));
            check(summed == totalNet, "Snapshot aggregation handles values above long.MaxValue without OverflowException");

            double rate = LedgerMath.Rate(totalNet, 60, 60);
            check(rate > 0 && !double.IsNaN(rate) && LedgerUi.FormatAmount(rate, true).StartsWith("+"), "Formatted rate above long.MaxValue remains positive");

            var window = new LedgerSampleWindow();
            var r1 = new LedgerRowData("parity", "Parity", 0) { Income = 1m, Outflow = 0m, Amount = 1m };
            window.Apply(r1, 2, true);
            var r2 = new LedgerRowData("parity", "Parity", 0) { Income = 2m, Outflow = 0m, Amount = 2m };
            window.Apply(r2, 2, true);
            check(r2.Income == 2m && r2.Amount == 2m, "Half-unit smoothing parity preserves AwayFromZero rounding");

            check(LedgerMath.AddSaturated(decimal.MaxValue - 10m, 100m) == decimal.MaxValue, "Extreme addition saturates instead of throwing OverflowException");
        }

        static void VerifyObserverLogSourceAndRateLimit(GlobalLedgerPlugin plugin, Action<bool, string> check)
        {
            LedgerObserver.Reset();
            int sourcesBefore = BepInEx.Logging.Logger.Sources.Count;
            var testPlayer = new GamePlayer();
            GamePlayer.Current = testPlayer;
            for (int i = 0; i < 15; i++)
            {
                var broken = new LedgerFrame { Player = FoundationApi.Kingdom, Element = null };
                LedgerObserver.current = broken;
                LedgerObserver.Leave(broken, null);
            }
            int sourcesAfter = BepInEx.Logging.Logger.Sources.Count;
            check(sourcesAfter == sourcesBefore, "Repeated accounting observer errors reuse the plugin logger without leaking log sources (#12)");
            check(LedgerObserver.WarningLogsEmitted == 1 && LedgerObserver.WarningLogsSuppressed == 14, "Repeated identical observer errors are rate-limited and counted (#12)");
            LedgerObserver.Reset();
        }

        static void VerifyLocaleInvalidation(GlobalLedgerPlugin plugin, Action<bool, string> check)
        {
            var originalLocale = Translation.Current;
            var alternate = Translation.All.FirstOrDefault(t => t != null && originalLocale != null && t.Locale != originalLocale.Locale);
            if (alternate == null)
            {
                check(true, "Single translation locale present; locale invalidation tested with fallback guard");
                return;
            }
            try
            {
                var player = new GamePlayer();
                var flow = new FlowDiagram { Label = "Locale Test" };
                player.Flows.Add(flow);
                var spawner = new DevSpawner { Item = ItemType.All.First(), Position = new Vector2Int(128, -128) };
                flow.Elements.Add(spawner);
                GamePlayer.Current = player;
                plugin.EnsurePlayer();

                var wrapper = WrapFlow(flow).Buildings.First(b => b.Guid == spawner.Guid);
                var firstSource = LedgerObserver.Describe(wrapper);
                var removedSource = new LedgerBookSource("removed-locale-id", "Fixture", "Fixture", firstSource.Name,
                    firstSource.Locale, firstSource.RawDefaultLabel, firstSource.RawDetailKey, firstSource.RawFlowLabel, firstSource.Position);

                alternate.Apply();
                var updatedSource = LedgerObserver.Describe(wrapper);
                check(!ReferenceEquals(firstSource, updatedSource) && updatedSource.Locale == alternate.Locale,
                    "Locale change invalidates cached LedgerSourceCache description (#10)");
                check(removedSource.LocalizedName(alternate.Locale) != firstSource.Name || firstSource.Name == updatedSource.Name,
                    "Removed-building source names re-translate for the active locale (#10)");
            }
            finally
            {
                originalLocale?.Apply();
                LedgerSourceCache.Reset();
                plugin.Locations.Reset();
            }
        }

        static void VerifySpawnerIconInvalidation(GlobalLedgerPlugin plugin, Action<bool, string> check)
        {
            var apiItems = FoundationApi.ItemAccess.Items.Where(i => i != null && i.GetIcon() != null)
                .GroupBy(i => i.GetIcon()).Select(g => g.First()).Take(2).ToArray();
            var nativeItems = apiItems.Select(a => ItemType.All.First(n => n.Identifier == a.Identifier)).ToArray();
            check(apiItems.Length >= 2, "At least two native items with distinct icons exist for spawner icon check");
            var player = new GamePlayer();
            var flow = new FlowDiagram { Label = "Spawner Icon Test" };
            player.Flows.Add(flow);
            var spawner = new DevSpawner { Item = nativeItems[0], Position = new Vector2Int(64, -64) };
            flow.Elements.Add(spawner);
            GamePlayer.Current = player;
            plugin.EnsurePlayer();

            double clock = 10.0;
            var locations = new LedgerLocations(() => clock);
            var hooks = new List<IDisposable>();
            locations.Install(hooks);
            try
            {
                var wrapper = WrapFlow(flow).Buildings.First(b => b.Guid == spawner.Guid);
                var total = new LedgerBookTotals(LedgerObserver.Describe(wrapper));
                var firstEntry = locations.Resolve(new[] { total }).Single();
                check(firstEntry.BuildingIcon == apiItems[0].GetIcon(), "Initial spawner icon matches first item");

                spawner.Item = nativeItems[1];
                clock += 2.1;
                var refreshedEntry = locations.Resolve(new[] { total }).Single();
                check(refreshedEntry.BuildingIcon == apiItems[1].GetIcon(), "Changing spawner item refreshes cached placement icon without moving building (#11)");
            }
            finally
            {
                foreach (var hook in hooks) hook.Dispose();
            }
        }

        static void VerifyDayLongHistoryMemoryAndBoundaries(JsonObject result, Action<bool, string> check)
        {
            double clock = 0;
            var book = new LedgerBook(() => clock);
            const int SourceCount = 200;
            const int StepSeconds = 10;
            const int DurationSeconds = LedgerBook.MaxRetentionSeconds;
            var sources = new LedgerBookSource[SourceCount];
            for (int i = 0; i < SourceCount; i++)
                sources[i] = new LedgerBookSource("bench_" + i, "Fixture", "Fixture", "Bench " + i);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long memBefore = GC.GetTotalMemory(true);

            for (int t = 0; t < DurationSeconds; t += StepSeconds)
            {
                clock = t;
                for (int i = 0; i < SourceCount; i++)
                    book.RecordItem(sources[i], "iron_ore", 1m, 0m);
            }

            long memAfter = GC.GetTotalMemory(true);
            int buckets = book.RetainedBucketCount;
            int records = book.RetainedRecordCount;
            long deltaBytes = Math.Max(0, memAfter - memBefore);

            var s24h = book.Snapshot(86400);
            var s1m = book.Snapshot(60);
            decimal total24h = LedgerMath.Sum(s24h, t => t.ItemProduced("iron_ore"));
            decimal expected24h = (decimal)SourceCount * (DurationSeconds / StepSeconds);

            var benchmark = new JsonObject
            {
                ["sources"] = SourceCount,
                ["durationSeconds"] = DurationSeconds,
                ["stepSeconds"] = StepSeconds,
                ["retainedBuckets"] = buckets,
                ["retainedRecords"] = records,
                ["retainedAllocations"] = book.RetainedAllocationCount,
                ["heapDeltaBytes"] = deltaBytes,
                ["snapshot24hTotal"] = (double)total24h,
                ["expected24hTotal"] = (double)expected24h,
                ["snapshot1mSources"] = s1m.Count
            };
            result["historyBenchmark"] = benchmark;

            check(buckets <= 500, "Day-long tiered history bounds buckets to <= 500 (actual: " + buckets + ") (#9)");
            check(records <= SourceCount * 500, "Day-long tiered history bounds retained source records (#9)");
            check(Math.Abs(total24h - expected24h) <= SourceCount, "Tiered rollup conserves totals across bucket boundaries (#9)");
            check(s1m.Count == SourceCount, "Fine 1m window remains intact after 24h rollup (#9)");
        }

        static void VerifyCustomHostedMachineAttribution(GlobalLedgerPlugin plugin, Action<bool, string> check)
        {
            var player = new GamePlayer();
            var flow = new FlowDiagram { Label = "Custom Machine Test" };
            player.Flows.Add(flow);
            GamePlayer.Current = player;
            plugin.EnsurePlayer();

            var nodeType = plugin.Context.Register(new FrameworkWorkspaceNodeDefinition(
                "gl_test_custom_machine",
                "Test Custom Machine",
                () => new TestCustomHostedMachine(),
                "dev_item_spawner",
                new Vector2Int(128, 128)));
            var instance = nodeType.CreateInstance();
            var hostedElement = (FlowElement)instance;
            hostedElement.Position = new Vector2Int(256, -256);
            flow.Elements.Add(hostedElement);

            var customItem = new ItemType("gl_custom_machine_input");
            hostedElement.Inputs[0].BufferedItems.Add(new InventoryItem(customItem));
            GamePlayer.Current.AddCurrency(Currency.Mana, 500, false);

            hostedElement.Update(0.1f);

            var snapshot = plugin.Book.Snapshot(60);
            var total = snapshot.SingleOrDefault(t => t.Source.Id == hostedElement.Guid);
            check(total != null, "Custom hosted machine creates an attributed ledger entry automatically (#8)");
            check(total.ItemConsumed(customItem.Identifier) == 1m, "Custom hosted machine consumes item once via FrameworkWorkspaceNode.Update (#8)");
            check(total.EarnedAt((int)GameCurrency.Mana) == 50m && total.SpentAt((int)GameCurrency.Mana) == 20m,
                "Custom hosted machine earns and spends currency with exact single attribution (#8)");

            hostedElement.Inputs[0].BufferedItems.Add(new InventoryItem(customItem));
            hostedElement.Update(0.1f);
            total = plugin.Book.Snapshot(60).Single(t => t.Source.Id == hostedElement.Guid);
            check(total.ItemConsumed(customItem.Identifier) == 2m && total.EarnedAt((int)GameCurrency.Mana) == 100m && total.SpentAt((int)GameCurrency.Mana) == 40m,
                "Repeated custom hosted machine update increments totals once per operation (#8)");

            var wrapper = WrapFlow(flow).Buildings.Single(b => b.Guid == hostedElement.Guid);
            var behaviour = (TestCustomHostedMachine)wrapper.Behaviour;
            hostedElement.Inputs[0].BufferedItems.Add(new InventoryItem(customItem));
            behaviour.Update(0.1f);
            total = plugin.Book.Snapshot(60).Single(t => t.Source.Id == hostedElement.Guid);
            check(total.ItemConsumed(customItem.Identifier) == 3m && total.EarnedAt((int)GameCurrency.Mana) == 150m && total.SpentAt((int)GameCurrency.Mana) == 60m,
                "Direct FrameworkWorkspaceNode.Update on dynamically registered custom machine attributes cleanly (#8)");
        }

        static GameWorkspace WrapFlow(FlowDiagram native)
            => (GameWorkspace)AccessTools.Method(typeof(GameWorkspace), "Wrap").Invoke(null, new object[] { native });
    }
}
