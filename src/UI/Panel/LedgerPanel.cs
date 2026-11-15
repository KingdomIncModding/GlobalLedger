using KingdomFoundation.Api;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KingdomGlobalLedger
{
    public sealed class LedgerPanel : MonoBehaviour
    {
        internal void Activate(LedgerRowData row)
        {
            if (row.IsHeader) { ToggleCategory(row.Key); return; }
            selectedKey = row.Key;
            if (row.Expandable) ToggleRow(row.Key);
            else RefreshNow();
        }

        LedgerRowData Add(string key, string label, int depth, GameCurrency currency, decimal amount, bool expandable, decimal income = 0, decimal outflow = 0)
        {
            var row = new LedgerRowData(key, label, depth)
            {
                GameCurrency = currency,
                Amount = amount,
                Income = income,
                Outflow = outflow,
                Expandable = expandable,
                Open = expanded.Contains(key),
                Icon = depth == 0 ? currency.GetIcon() : null
            };
            VisibleRows.Add(row);
            return row;
        }

        LedgerRowData AddItem(string key, string label, int depth, GameItemType item, decimal amount, bool expandable, decimal income = 0, decimal outflow = 0)
        {
            var row = new LedgerRowData(key, label, depth)
            {
                Item = item,
                Amount = amount,
                Income = income,
                Outflow = outflow,
                Expandable = expandable,
                Open = expanded.Contains(key),
                Icon = depth == 0 ? item.GetIcon() : null
            };
            VisibleRows.Add(row);
            return row;
        }

        void BuildBasic()
        {
            if (FoundationApi.Kingdom == null) return;
            if (tab != 2 && AddCategory("currencies", "CURRENCIES", 0)) BuildBasicCurrencies();
            if (tab != 1 && AddCategory("production", "PRODUCTION", 0)) BuildBasicProduction();
        }

        bool AddCategory(string id, string label, int depth)
        {
            string key = "category/" + id;
            bool open = !collapsedCategories.Contains(key);
            VisibleRows.Add(new LedgerRowData(key, label, depth) { IsHeader = true, Expandable = true, Open = open });
            return open;
        }

        void BuildBasicCurrencies()
        {
            var totals = snapshotIndex.Totals;
            int currencyCount = 0;
            foreach (GameCurrency currency in Enum.GetValues(typeof(GameCurrency)))
            {
                if (!currency.IsUnlocked())
                    continue;
                int c = (int)currency;
                bool hasActivity = book.HasCurrencyActivity(c);
                bool isActiveInWindow = totals.Active(c);
                double timeSinceActivity = book.TimeSinceCurrencyActivity(c);
                if (!showAll)
                {
                    if (currency != GameCurrency.Gold && !hasActivity)
                        continue;
                    if (!isActiveInWindow && timeSinceActivity > windowSeconds + 60.0 && currency != GameCurrency.Gold)
                        continue;
                }

                decimal net = totals.Net(c);
                var row = new LedgerRowData("basic/currency/" + c, LedgerUi.CurrencyName(currency), 1)
                {
                    GameCurrency = currency,
                    Amount = net,
                    Income = totals.EarnedAt(c),
                    Outflow = totals.SpentAt(c),
                    Expandable = false,
                    Icon = currency.GetIcon()
                };
                VisibleRows.Add(row);
                currencyCount++;
            }

            if (currencyCount == 0 && !showAll)
            {
                VisibleRows.Add(new LedgerRowData("basic/currency/empty", "No observed currency activity in the recent window. Try a longer observation window.", 1));
            }
        }

        void BuildBasicProduction()
        {
            var totals = snapshotIndex.Totals;
            var items = unlockedItems;
            int count = 0;
            foreach (var item in items)
            {
                string id = item.Identifier;
                bool hasActivity = book.HasItemActivity(id);
                bool isActiveInWindow = totals.ItemActive(id);
                double timeSinceActivity = book.TimeSinceItemActivity(id);
                if (!showAll)
                {
                    // 1. If something hasnt been made DO NOT SHOW IT
                    if (!hasActivity)
                        continue;
                    // 2. If somethings qty hasnt been greater than 0 within the selected time,
                    // and it has been more than a rolling minute (60s) since it was last made, DO NOT SHOW IT.
                    // 3. If something has been made, and value keeps changing: SHOW IT (isActiveInWindow == true).
                    // 4. If something has been made recently, but drops to 0: SHOW IT FOR A ROLLING MINUTE (timeSinceActivity <= windowSeconds + 60.0).
                    if (!isActiveInWindow && timeSinceActivity > windowSeconds + 60.0)
                        continue;
                }

                decimal net = totals.ItemNet(id);
                var row = new LedgerRowData("basic/item/" + id, LedgerUi.ItemName(item), 1)
                {
                    Item = item,
                    Amount = net,
                    Income = totals.ItemProduced(id),
                    Outflow = totals.ItemConsumed(id),
                    Expandable = false,
                    Icon = item.GetIcon()
                };
                VisibleRows.Add(row);
                count++;
            }

            if (count == 0 && !showAll)
            {
                VisibleRows.Add(new LedgerRowData("basic/item/empty", "No observed production in the recent window. Try a longer observation window.", 1));
            }
        }

        void BuildCurrencies()
        {
            if (FoundationApi.Kingdom == null)
                return;
            foreach (GameCurrency currency in Enum.GetValues(typeof(GameCurrency)))
            {
                if (!currency.IsUnlocked())
                    continue;
                int c = (int)currency;
                if (!showAll && !book.HasCurrencyActivity(c) && currency != GameCurrency.Gold) continue;
                string root = "currency/" + c;
                var active = snapshot.Where(t => t.Active(c)).ToArray();
                Add(root, LedgerUi.CurrencyName(currency), 0, currency, LedgerMath.Sum(active, t => t.Net(c)), true, LedgerMath.Sum(active, t => t.EarnedAt(c)), LedgerMath.Sum(active, t => t.SpentAt(c)));
                if (!expanded.Contains(root) && string.IsNullOrEmpty(search))
                    continue;
                if (active.Length == 0)
                {
                    if (!EmitMissingAdvChildren(root))
                        VisibleRows.Add(new LedgerRowData(root + "/empty", "No observed activity in the recent window.", 1));
                    continue;
                }

                foreach (var group in locations.Resolve(active).GroupBy(t => t.GroupKey).OrderBy(g => g.First().GroupName, StringComparer.CurrentCulture).ThenBy(g => g.Key, StringComparer.Ordinal))
                {
                    string groupKey = root + "/" + group.Key;
                    var container = group.First();
                    var containerRow = Add(groupKey, container.GroupName, 1, currency, LedgerMath.Sum(group, t => t.Totals.Net(c)), true, LedgerMath.Sum(group, t => t.Totals.EarnedAt(c)), LedgerMath.Sum(group, t => t.Totals.SpentAt(c)));
                    containerRow.PrefixIcon = container.GroupTarget != null ? LedgerLocations.ContainerIcon() : null;
                    containerRow.Icon = container.GroupIcon;
                    containerRow.Target = container.GroupTarget;
                    RememberAdvChild(root, containerRow);
                    if (!expanded.Contains(groupKey) && string.IsNullOrEmpty(search))
                        continue;
                    foreach (var entry in group.OrderBy(t => t.BuildingName, StringComparer.CurrentCulture).ThenBy(t => t.Totals.Source.Id, StringComparer.Ordinal))
                    {
                        var building = entry.Totals;
                        string buildingKey = groupKey + "/" + building.Source.Id;
                        var buildingRow = Add(buildingKey, entry.BuildingName, 2, currency, building.Net(c), true, building.EarnedAt(c), building.SpentAt(c));
                        buildingRow.Icon = entry.BuildingIcon;
                        buildingRow.Target = entry.BuildingTarget;
                        RememberAdvChild(groupKey, buildingRow);
                        if (!expanded.Contains(buildingKey) && string.IsNullOrEmpty(search))
                            continue;
                        string costs = buildingKey + "/costs";
                        var costsRow = new LedgerRowData(costs, "Operating Costs", 3) { Expandable = true, Open = expanded.Contains(costs) };
                        VisibleRows.Add(costsRow);
                        RememberAdvChild(buildingKey, costsRow);
                        if (!expanded.Contains(costs) && string.IsNullOrEmpty(search))
                            continue;
                        bool any = false;
                        foreach (GameCurrency costCurrency in Enum.GetValues(typeof(GameCurrency)))
                            if (building.SpentAt((int)costCurrency) != 0m && costCurrency.IsUnlocked())
                            {
                                var cost = Add(costs + "/" + (int)costCurrency, LedgerUi.CurrencyName(costCurrency), 4, costCurrency, -building.SpentAt((int)costCurrency), false, 0m, building.SpentAt((int)costCurrency));
                                cost.Icon = costCurrency.GetIcon();
                                RememberAdvChild(costs, cost);
                                any = true;
                            }

                        if (!EmitMissingAdvChildren(costs) && !any)
                            VisibleRows.Add(new LedgerRowData(costs + "/empty", "No observed operating costs", 4));
                    }
                    EmitMissingAdvChildren(groupKey);
                }
                EmitMissingAdvChildren(root);
            }
        }

        void BuildProduction()
        {
            if (FoundationApi.Kingdom == null)
                return;
            int beforeCount = VisibleRows.Count;
            foreach (var item in unlockedItems)
            {
                string id = item.Identifier;
                string root = "item/" + id;
                var active = snapshotIndex.Items.TryGetValue(id, out var itemSources) ? itemSources : new List<LedgerBookTotals>();
                if (IsAdvOpen && active.Count != 0)
                    advStability.PinItem(id);
                if (!showAll && active.Count == 0 && !(IsAdvOpen && advStability.IsPinnedItem(id)))
                    continue;
                AddItem(root, LedgerUi.ItemName(item), 0, item, snapshotIndex.Totals.ItemNet(id), true, snapshotIndex.Totals.ItemProduced(id), snapshotIndex.Totals.ItemConsumed(id));
                if (!expanded.Contains(root) && string.IsNullOrEmpty(search))
                    continue;
                if (active.Count == 0)
                {
                    if (!EmitMissingAdvChildren(root))
                        VisibleRows.Add(new LedgerRowData(root + "/empty", "No observed activity in the recent window.", 1));
                    continue;
                }

                foreach (var group in locations.Resolve(active).GroupBy(t => t.GroupKey).OrderBy(g => g.First().GroupName, StringComparer.CurrentCulture).ThenBy(g => g.Key, StringComparer.Ordinal))
                {
                    string groupKey = root + "/" + group.Key;
                    var container = group.First();
                    var containerRow = AddItem(groupKey, container.GroupName, 1, item, LedgerMath.Sum(group, t => t.Totals.ItemNet(id)), true, LedgerMath.Sum(group, t => t.Totals.ItemProduced(id)), LedgerMath.Sum(group, t => t.Totals.ItemConsumed(id)));
                    containerRow.PrefixIcon = container.GroupTarget != null ? LedgerLocations.ContainerIcon() : null;
                    containerRow.Icon = container.GroupIcon;
                    containerRow.Target = container.GroupTarget;
                    RememberAdvChild(root, containerRow);
                    if (!expanded.Contains(groupKey) && string.IsNullOrEmpty(search))
                        continue;
                    foreach (var entry in group.OrderBy(t => t.BuildingName, StringComparer.CurrentCulture).ThenBy(t => t.Totals.Source.Id, StringComparer.Ordinal))
                    {
                        var building = entry.Totals;
                        string buildingKey = groupKey + "/" + building.Source.Id;
                        var buildingRow = AddItem(buildingKey, entry.BuildingName, 2, item, building.ItemNet(id), true, building.ItemProduced(id), building.ItemConsumed(id));
                        buildingRow.Icon = entry.BuildingIcon;
                        buildingRow.Target = entry.BuildingTarget;
                        RememberAdvChild(groupKey, buildingRow);
                        if (!expanded.Contains(buildingKey) && string.IsNullOrEmpty(search))
                            continue;
                        string costs = buildingKey + "/costs";
                        var costsRow = new LedgerRowData(costs, "Operating Costs", 3) { Expandable = true, Open = expanded.Contains(costs) };
                        VisibleRows.Add(costsRow);
                        RememberAdvChild(buildingKey, costsRow);
                        if (!expanded.Contains(costs) && string.IsNullOrEmpty(search))
                            continue;
                        bool any = false;
                        foreach (var entryCost in building.SpentItems)
                        {
                            decimal spentItem = entryCost.Value;
                            if (spentItem != 0m && unlockedItemById.TryGetValue(entryCost.Key, out var costItem))
                            {
                                var cost = AddItem(costs + "/item/" + costItem.Identifier, LedgerUi.ItemName(costItem), 4, costItem, -spentItem, false, 0m, spentItem);
                                cost.Icon = costItem.GetIcon();
                                RememberAdvChild(costs, cost);
                                any = true;
                            }
                        }

                        foreach (GameCurrency costCurrency in Enum.GetValues(typeof(GameCurrency)))
                            if (building.SpentAt((int)costCurrency) != 0m && costCurrency.IsUnlocked())
                            {
                                var cost = Add(costs + "/currency/" + (int)costCurrency, LedgerUi.CurrencyName(costCurrency), 4, costCurrency, -building.SpentAt((int)costCurrency), false, 0m, building.SpentAt((int)costCurrency));
                                cost.Icon = costCurrency.GetIcon();
                                RememberAdvChild(costs, cost);
                                any = true;
                            }

                        if (!EmitMissingAdvChildren(costs) && !any)
                            VisibleRows.Add(new LedgerRowData(costs + "/empty", "No observed operating costs", 4));
                    }
                    EmitMissingAdvChildren(groupKey);
                }
                EmitMissingAdvChildren(root);
            }

            if (VisibleRows.Count == beforeCount)
                VisibleRows.Add(new LedgerRowData("production/empty", "No observed production in the selected window. Try a longer observation window.", 0));
        }

        public void ClosePopup() => ClosePopup(true);
        public void ClosePopup(bool playSound)
        {
            advStability.Clear();
            if (!gameObject.activeSelf) return;
            if (playSound) FoundationApi.UiAccess.PlaySound(GameUiSound.WindowClose);
            gameObject.SetActive(false);
            OnPopupClosed?.Invoke();
        }

        internal void CollapseAll()
        {
            expanded.Clear();
            collapsedCategories.Add("category/currencies");
            collapsedCategories.Add("category/production");
            ResetScroll();
            RefreshNow();
        }

        internal static LedgerPanel Create(LedgerBook book, Button anchor)
        {
            var root = LedgerUi.Rect(anchor.GetComponentInParent<Canvas>().rootCanvas.transform, "GlobalLedgerDropdown");
            LedgerUi.Fill(root);
            root.gameObject.SetActive(false);
            var result = root.gameObject.AddComponent<LedgerPanel>();
            try { result.Initialize(book, anchor, root); return result; }
            catch { Destroy(root.gameObject); throw; }
        }

        public enum ViewMode { Basic, Adv }
        public enum TimeUnit { Sec, Min, Hour }
        public Action OnPopupClosed;
        internal const float Width = 900, Height = 760, RowHeight = 36;
        internal const float IncomeX = 300, OutflowX = 460, NetX = 620, CellWidth = 144, ViewX = 768;
        LedgerBook book;
        LedgerUi ui;
        Button anchor;
        RectTransform overlay, panel, content, viewport, controls, track, divider;
        TMP_Text status, footer, title;
        TMP_InputField searchInput;
        Button basicButton, advButton, secButton, minButton, hourButton, rollingButton;
        Button allTab, currenciesTab, productionTab, settingsToggle, collapseAll, refreshButton, freezeButton, nameHeading;
        readonly Button[] windowButtons = new Button[3];
        readonly TMP_Text[] columnHeadings = new TMP_Text[3];
        ScrollRect scroll;
        readonly HashSet<string> collapsedCategories = new HashSet<string>();
        readonly HashSet<string> expanded = new HashSet<string>();
        readonly List<LedgerTreeRow> rows = new List<LedgerTreeRow>();
        readonly LedgerRowOrdering ordering = new LedgerRowOrdering();
        readonly LedgerSmoothing smoothing = new LedgerSmoothing();
        readonly LedgerAdvStability advStability = new LedgerAdvStability();
        readonly HashSet<string> advEmittedKeys = new HashSet<string>();
        bool IsAdvOpen => viewMode == ViewMode.Adv && gameObject.activeSelf;

        void RememberAdvChild(string parentKey, LedgerRowData row)
        {
            if (!IsAdvOpen) return;
            advEmittedKeys.Add(row.Key);
            advStability.RememberChild(parentKey, row);
        }

        bool EmitMissingAdvChildren(string parentKey)
            => IsAdvOpen && advStability.EmitMissingChildren(parentKey, advEmittedKeys, VisibleRows, expanded, search);
        LedgerLocations locations => GlobalLedgerPlugin.Instance.Locations;
        internal readonly List<LedgerRowData> VisibleRows = new List<LedgerRowData>();
        IReadOnlyList<LedgerBookTotals> snapshot;
        LedgerSnapshotIndex snapshotIndex;
        GameItemType[] unlockedItems = Array.Empty<GameItemType>();
        readonly Dictionary<string, GameItemType> unlockedItemById = new Dictionary<string, GameItemType>();
        int firstBound = -1;
        Vector2 fittedSize;
        Vector3 fittedAnchor;
        internal int RenderedRowCount => rows.Count;
        readonly Vector3[] anchorCorners = new Vector3[4];
        float nextRefresh, searchAfter;
        string pendingSearch;
        double snapshotElapsed;
        int tab, sortField, refreshSeconds = 2;
        readonly int[] windowIndexes = new int[3];
        int windowIndex => windowIndexes[(int)timeUnit];
        bool showAll, descending, frozen, controlsCollapsed = true;
        string search = "", selectedKey = "", lastLocale = "";
        ViewMode viewMode = ViewMode.Basic;
        TimeUnit timeUnit = TimeUnit.Min;
        static readonly int[][] WindowOptions = { new[] { 1, 10, 30 }, new[] { 60, 300, 1800 }, new[] { 3600, 43200, 86400 } };
        static readonly string[][] WindowLabels = { new[] { "1s", "10s", "30s" }, new[] { "1m", "5m", "30m" }, new[] { "1h", "12h", "24h" } };
        int UnitSeconds => timeUnit == TimeUnit.Sec ? 1 : timeUnit == TimeUnit.Hour ? 3600 : 60;
        int windowSeconds => WindowOptions[(int)timeUnit][windowIndex];
        internal string WindowLabel => WindowLabels[(int)timeUnit][windowIndex];
        internal int RefreshSeconds => refreshSeconds;
        internal int SmoothingSamples => GlobalLedgerPlugin.Instance.EnableRollingAverage.Value ? Mathf.Clamp(GlobalLedgerPlugin.Instance.RollingAverageEntries.Value, 2, 30) : 0;
        float ControlsShift => controlsCollapsed ? 84 : 0;
        internal int SelectedTab => tab;
        internal bool ShowingAll => showAll;
        internal ViewMode Mode => viewMode;
        internal TimeUnit Unit => timeUnit;
        internal int WindowSeconds => windowSeconds;
        internal string StatusText => status ? status.text : "";
        internal RectTransform Frame => panel;
        internal ScrollRect Scroller => scroll;
        internal string SelectedKey => selectedKey;
        internal bool ControlsCollapsed => controlsCollapsed;
        internal bool Frozen => frozen;
        internal double Rate(decimal value) => LedgerMath.Rate(value, windowSeconds, UnitSeconds);

        void Initialize(LedgerBook book, Button anchor, RectTransform root)
        {
            this.book = book;
            this.anchor = anchor;
            overlay = root;
            ui = new LedgerUi(anchor);
            panel = LedgerUi.Rect(root, "GlobalLedgerPanel");
            ui.Background(panel, new Color(.16f, .09f, .09f), true);
            panel.gameObject.AddComponent<FoundationUiInputBlocker>();
            CreateHeader();
            CreateControls();
            CreateTable();
            Fit();
        }

        Button Control(Transform parent, string name, string label, Action action, float x, float y, float width, float fontSize = 17)
        {
            var button = ui.Button(parent, name, label, action, fontSize);
            LedgerUi.Place((RectTransform)button.transform, x, y, width, 32);
            return button;
        }

        void CreateHeader()
        {
            title = ui.Text(panel, "Title", "GLOBAL LEDGER", 24);
            title.color = LedgerUi.Gold;
            LedgerUi.Place(title.rectTransform, 24, 13, 320, 34);
            settingsToggle = Control(panel, "ControlsToggle", "Show Settings", ToggleControls, 450, 14, 200, 15);
            basicButton = Control(panel, "BasicMode", "Basic", () => SetViewMode(ViewMode.Basic), 662, 14, 82, 15);
            advButton = Control(panel, "AdvMode", "Advanced", () => SetViewMode(ViewMode.Adv), 756, 14, 120, 15);
        }

        void CreateControls()
        {
            controls = LedgerUi.Rect(panel, "Controls");
            LedgerUi.Place(controls, 0, 56, Width, 84);
            var rateLabel = ui.Text(controls, "RateLabel", "RATE:", 17);
            LedgerUi.Place(rateLabel.rectTransform, 24, 0, 64, 32);
            secButton = Control(controls, "UnitSec", "Sec", () => SetTimeUnit(TimeUnit.Sec), 96, 0, 60, 15);
            minButton = Control(controls, "UnitMin", "Min", () => SetTimeUnit(TimeUnit.Min), 164, 0, 60, 15);
            hourButton = Control(controls, "UnitHour", "Hr", () => SetTimeUnit(TimeUnit.Hour), 232, 0, 60, 15);
            for (int i = 0; i < windowButtons.Length; i++)
            {
                int option = i;
                windowButtons[i] = Control(controls, "Window" + i, "", () => SetWindow(option), 320 + i * 86, 0, 78, 15);
            }
            freezeButton = Control(controls, "Freeze", "Freeze", ToggleFreeze, Width - 150, 0, 126, 15);
            rollingButton = Control(controls, "RollingToggle", "", CycleSmoothing, 24, 42, 280, 15);
            refreshButton = Control(controls, "RefreshInterval", "", () => SetRefresh(refreshSeconds % 4 + 1), 316, 42, 174, 15);
            var hint = ui.Text(controls, "SmoothingHint", "Smoothing blends recent refreshes", 15);
            LedgerUi.Place(hint.rectTransform, 502, 42, 374, 32);
            searchInput = ui.Search(panel, "Search", ScheduleSearch);
            allTab = Control(panel, "AllTab", "All", () => SelectTab(0), 24, 190, 68, 15);
            currenciesTab = Control(panel, "CurrenciesTab", "Currencies", () => SelectTab(1), 104, 190, 144, 15);
            productionTab = Control(panel, "ProductionTab", "Production", () => SelectTab(2), 260, 190, 144, 15);
            collapseAll = Control(panel, "CollapseAll", "Collapse All", CollapseAll, 700, 190, 176, 15);
        }

        void CreateTable()
        {
            status = ui.Text(panel, "Window", "", 15);
            status.color = LedgerUi.Muted;
            status.fontStyle = FontStyles.Italic;
            divider = LedgerUi.Rect(panel, "TableDivider");
            ui.Background(divider, new Color(.72f, .67f, .57f, .4f));
            nameHeading = Control(panel, "NameHeading", "Tier / Name", () => SetSort(0), 24, 254, 230, 15);
            PlainHeading(nameHeading);
            string[] names = { "IncomeHeading", "OutflowHeading", "NetHeading" };
            int[] fields = { 2, 3, 4 };
            for (int i = 0; i < names.Length; i++)
            {
                int field = fields[i];
                var button = Control(panel, names[i], "", () => SetSort(field), 24 + IncomeX + i * (OutflowX - IncomeX), 208, CellWidth, 15);
                PlainHeading(button);
                columnHeadings[i] = button.GetComponentInChildren<TMP_Text>();
                columnHeadings[i].alignment = TextAlignmentOptions.MidlineRight;
            }
            viewport = LedgerUi.Rect(panel, "TreeViewport");
            ui.Background(viewport, new Color(0, 0, 0, .15f));
            viewport.gameObject.AddComponent<RectMask2D>();
            content = LedgerUi.Rect(viewport, "TreeContent");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0, 1);
            content.sizeDelta = Vector2.zero;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.onValueChanged.AddListener(_ => BindRows(false));
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36;
            track = LedgerUi.Rect(panel, "ScrollTrack");
            ui.Background(track, new Color(0, 0, 0, .28f));
            var handle = LedgerUi.Rect(track, "ScrollHandle");
            LedgerUi.Fill(handle);
            var image = ui.Background(handle, new Color(.65f, .48f, .29f, .85f));
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = image;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            footer = ui.Text(panel, "Footnote", "", 14);
            footer.color = LedgerUi.Muted;
        }

        static void PlainHeading(Button button)
        {
            var image = button.GetComponent<Image>();
            image.sprite = null;
            image.color = Color.clear;
        }

        void Fit() => FitLayout(false);

        void FitLayout(bool force)
        {
            if (!anchor || !panel) return;
            ((RectTransform)anchor.transform).GetWorldCorners(anchorCorners);
            if (!force && fittedSize == overlay.rect.size && fittedAnchor == anchorCorners[0]) return;
            fittedSize = overlay.rect.size;
            fittedAnchor = anchorCorners[0];
            Vector2 point = overlay.InverseTransformPoint(anchorCorners[0]);
            float top = Mathf.Round(Mathf.Max(8, overlay.rect.yMax - point.y + 8));
            float maximumHeight = Height - ControlsShift;
            float minimumHeight = 380 - ControlsShift;
            if (overlay.rect.height - top - 12 < minimumHeight)
                top = Mathf.Round(Mathf.Max(8, overlay.rect.height - minimumHeight - 12));
            float height = Mathf.Floor(Mathf.Min(maximumHeight, overlay.rect.height - top - 12));
            float x = Mathf.Round(Mathf.Clamp(point.x - overlay.rect.xMin, 10, Mathf.Max(10, overlay.rect.width - Width - 10)));
            LedgerUi.Place(panel, x, top, Width, height);
            LedgerUi.Place((RectTransform)nameHeading.transform, 24, 286 - ControlsShift, 230, 28);
            // Match original V1: preserve the native pixel font/art scale and scroll
            // through fewer rows on shorter canvases instead of shrinking the panel.
            panel.localScale = Vector3.one;
            controls.gameObject.SetActive(!controlsCollapsed);
            LedgerUi.Place((RectTransform)searchInput.transform, 24, 148 - ControlsShift, Width - 48, 32);
            LedgerUi.Place((RectTransform)allTab.transform, 24, 190 - ControlsShift, 68, 32);
            LedgerUi.Place((RectTransform)currenciesTab.transform, 104, 190 - ControlsShift, 144, 32);
            LedgerUi.Place((RectTransform)productionTab.transform, 260, 190 - ControlsShift, 144, 32);
            LedgerUi.Place((RectTransform)collapseAll.transform, 700, 190 - ControlsShift, 176, 32);
            LedgerUi.Place(status.rectTransform, 24, 232 - ControlsShift, Width - 48, 44);
            LedgerUi.Place(divider, 24, 280 - ControlsShift, Width - 48, 1);
            for (int i = 0; i < columnHeadings.Length; i++)
                LedgerUi.Place((RectTransform)columnHeadings[i].transform.parent, 24 + (viewMode == ViewMode.Basic && i == 2 ? IncomeX : IncomeX + i * (OutflowX - IncomeX)), 286 - ControlsShift,
                    viewMode == ViewMode.Basic && i == 2 ? NetX + CellWidth - IncomeX : CellWidth, 28);
            LedgerUi.Place(viewport, 24, 318 - ControlsShift, Width - 72, Mathf.Max(1, height - 380 + ControlsShift));
            LedgerUi.Place(track, Width - 40, 318 - ControlsShift, 10, Mathf.Max(1, height - 380 + ControlsShift));
            LedgerUi.Place(footer.rectTransform, 24, height - 48, Width - 48, 28);
        }

        internal void RefreshNow()
        {
            string locale = LedgerLocations.CurrentLocale;
            bool localeChanged = lastLocale != locale;
            if (localeChanged)
            {
                lastLocale = locale;
                locations.Invalidate();
                advStability.Clear();
                smoothing.Clear();
                snapshot = null;
            }
            bool sample = snapshot == null || (!frozen && Time.unscaledTime >= nextRefresh);
            if (sample)
            {
                snapshot = book.Snapshot(windowSeconds);
                snapshotElapsed = book.Elapsed;
                snapshotIndex = new LedgerSnapshotIndex(snapshot);
                nextRefresh = Time.unscaledTime + refreshSeconds;
            }
            UpdateControls();
            VisibleRows.Clear();
            if (tab != 1 && (sample || localeChanged || unlockedItems.Length == 0))
            {
                unlockedItems = LedgerUi.OrderedUnlockedItems().ToArray();
                unlockedItemById.Clear();
                foreach (var item in unlockedItems) unlockedItemById[item.Identifier] = item;
            }
            advEmittedKeys.Clear();
            if (viewMode == ViewMode.Basic) BuildBasic();
            else
            {
                if (tab != 2 && AddCategory("currencies", "CURRENCIES", -1)) BuildCurrencies();
                if (tab != 1 && AddCategory("production", "PRODUCTION", -1)) BuildProduction();
            }
            if (GlobalLedgerPlugin.Instance?.EnableRollingAverage?.Value == true)
                smoothing.Apply(VisibleRows, SmoothingSamples, sample);
            var ordered = ordering.Arrange(VisibleRows, search, sortField, descending, IsAdvOpen ? advStability.OrderRank : null);
            VisibleRows.Clear();
            VisibleRows.AddRange(ordered);
            if (IsAdvOpen) advStability.RecordOrder(VisibleRows);
            BindRows();
            string smooth = SmoothingSamples == 0 ? "OFF" : SmoothingSamples + " samples";
            status.text = (frozen ? "Frozen" : "Live") + ". Last " + WindowLabel + ", smoothing: " + smooth + ". Refresh every " + refreshSeconds + "s."
                + (snapshotElapsed < windowSeconds ? "\nCollecting history: " + (int)snapshotElapsed + "/" + windowSeconds + "s." : "");
            int count = VisibleRows.Count(row => !row.IsHeader && (row.GameCurrency.HasValue || row.Item != null));
            footer.text = count == 0 && search.Length != 0 ? "No matching resources or sources."
                : "Headings sort; Resource resets. Gold: g=gold, s=silver, c=copper.";
        }

        void BindRows(bool force = true)
        {
            int capacity = Mathf.Max(1, Mathf.CeilToInt(viewport.rect.height / RowHeight) + 2);
            int first = Mathf.Clamp(Mathf.FloorToInt(content.anchoredPosition.y / RowHeight), 0, Mathf.Max(0, VisibleRows.Count - 1));
            if (!force && first == firstBound) return;
            firstBound = first;
            content.sizeDelta = new Vector2(0, VisibleRows.Count * RowHeight + 8);
            int count = Mathf.Min(capacity, VisibleRows.Count - first);
            for (int i = 0; i < count; i++)
            {
                var data = VisibleRows[first + i];
                data.Top = (first + i) * RowHeight;
                while (rows.Count <= i) rows.Add(new LedgerTreeRow(this, ui, content, rows.Count));
                rows[i].Bind(data, Width - 72);
            }
            for (int i = count; i < rows.Count; i++) rows[i].Root.gameObject.SetActive(false);
        }

        static void Select(Button button, bool active) => button.GetComponentInChildren<TMP_Text>().color = active ? LedgerUi.Gold : LedgerUi.Paper;
        void UpdateControls()
        {
            Select(basicButton, viewMode == ViewMode.Basic);
            Select(advButton, viewMode == ViewMode.Adv);
            Select(secButton, timeUnit == TimeUnit.Sec);
            Select(minButton, timeUnit == TimeUnit.Min);
            Select(hourButton, timeUnit == TimeUnit.Hour);
            Select(allTab, tab == 0);
            Select(currenciesTab, tab == 1);
            Select(productionTab, tab == 2);
            settingsToggle.GetComponentInChildren<TMP_Text>().text = controlsCollapsed ? "Show Settings" : "Hide Settings";
            bool rolling = GlobalLedgerPlugin.Instance?.EnableRollingAverage?.Value == true;
            rollingButton.GetComponentInChildren<TMP_Text>().text = "Smooth: " + (rolling ? SmoothingSamples.ToString() : "OFF");
            freezeButton.GetComponentInChildren<TMP_Text>().text = frozen ? "Unfreeze" : "Freeze";
            refreshButton.GetComponentInChildren<TMP_Text>().text = "Refresh: " + refreshSeconds + "s";
            collapseAll.gameObject.SetActive(viewMode == ViewMode.Adv);
            nameHeading.interactable = true;
            nameHeading.GetComponentInChildren<TMP_Text>().text = "RESOURCE";
            for (int i = 0; i < windowButtons.Length; i++)
            {
                Select(windowButtons[i], windowIndex == i);
                windowButtons[i].GetComponentInChildren<TMP_Text>().text = WindowLabels[(int)timeUnit][i];
            }
            string[] labels = { "IN", "OUT", "NET" };
            for (int i = 0; i < columnHeadings.Length; i++)
            {
                var button = columnHeadings[i].transform.parent.GetComponent<Button>();
                button.gameObject.SetActive(viewMode == ViewMode.Adv || i == 2);
                button.interactable = true;
                columnHeadings[i].text = labels[i] + "/" + (timeUnit == TimeUnit.Sec ? "Sec" : timeUnit == TimeUnit.Min ? "Min" : "Hr")
                    + (sortField == i + 2 ? descending ? " v" : " ^" : "");
            }
        }

        void ResetScroll() { scroll.StopMovement(); content.anchoredPosition = Vector2.zero; }
        internal void SetWindow(int index) { windowIndexes[(int)timeUnit] = Mathf.Clamp(index, 0, 2); InvalidateWindow(); }
        void InvalidateWindow() { smoothing.Clear(); snapshot = null; RefreshNow(); }
        internal void SetRefresh(int seconds) { refreshSeconds = Mathf.Clamp(seconds, 1, 4); nextRefresh = Time.unscaledTime + refreshSeconds; RefreshNow(); }
        internal void ToggleFreeze() { frozen = !frozen; if (!frozen) nextRefresh = 0; RefreshNow(); }
        void ScheduleSearch(string value) { pendingSearch = value ?? ""; searchAfter = Time.unscaledTime + .15f; }
        internal void SetSearch(string value) { pendingSearch = null; search = value ?? ""; if (search.Length != 0) collapsedCategories.Clear(); ResetScroll(); RefreshNow(); }
        internal void SetSort(int field)
        {
            advStability.ResetOrder();
            if (field <= 1) { sortField = 0; descending = false; }
            else if (sortField != field) { sortField = Mathf.Clamp(field, 2, 4); descending = true; }
            else if (descending) descending = false;
            else { sortField = 0; descending = false; }
            RefreshNow();
        }
        internal void SetDescending(bool value) { advStability.ResetOrder(); descending = value; RefreshNow(); }
        internal void ToggleControls() { controlsCollapsed = !controlsCollapsed; UpdateControls(); FitLayout(true); BindRows(); }
        internal void SelectTab(int value) { tab = Mathf.Clamp(value, 0, 2); ResetScroll(); RefreshNow(); }
        internal void SetTimeUnit(TimeUnit unit) { if (timeUnit == unit) return; timeUnit = unit; InvalidateWindow(); }
        internal void SetViewMode(ViewMode mode)
        {
            if (viewMode == mode) return;
            viewMode = mode;
            advStability.ResetOrder();
            if (viewMode == ViewMode.Basic && sortField != 4) { sortField = 0; descending = false; }
            FitLayout(true); ResetScroll(); RefreshNow();
        }
        internal void CycleSmoothing()
        {
            int current = SmoothingSamples;
            SetSmoothing(current == 0 ? 2 : current >= 5 ? 0 : current + 1);
        }

        internal void SetSmoothing(int samples)
        {
            smoothing.Clear();
            var plugin = GlobalLedgerPlugin.Instance;
            plugin.EnableRollingAverage.Value = samples > 0;
            if (samples > 0) plugin.RollingAverageEntries.Value = Mathf.Clamp(samples, 2, 30);
            RefreshNow();
        }

        internal void ToggleCategory(string key)
        {
            if (!collapsedCategories.Add(key)) collapsedCategories.Remove(key);
            ResetScroll(); RefreshNow();
        }

        internal void ToggleRollingAverage()
        {
            smoothing.Clear();
            var setting = GlobalLedgerPlugin.Instance?.EnableRollingAverage;
            if (setting != null) setting.Value = !setting.Value;
            RefreshNow();
        }
        internal void ToggleRow(string key) { if (!expanded.Add(key)) expanded.Remove(key); RefreshNow(); }
        internal void ToggleShowAll() { showAll = !showAll; RefreshNow(); }
        public void TogglePopup() { if (gameObject.activeSelf) ClosePopup(); else OpenPopup(); }
        public void OpenPopup()
        {
            if (gameObject.activeSelf) return;
            FoundationApi.UiAccess.PlaySound(GameUiSound.WindowOpen);
            nextRefresh = 0;
            gameObject.SetActive(true);
        }
        void OnEnable()
        {
            if (book == null) return;
            if (pendingSearch != null) { search = pendingSearch; pendingSearch = null; }
            transform.SetAsLastSibling(); FitLayout(true); RefreshNow();
        }
        void OnDisable() => advStability.Clear();
        internal void ResetTree()
        {
            advStability.Clear();
            expanded.Clear(); collapsedCategories.Clear(); smoothing.Clear(); snapshot = null; frozen = false; showAll = false;
            search = ""; pendingSearch = null; searchInput.SetTextWithoutNotify(""); ResetScroll();
        }
        void Update()
        {
            var bar = anchor ? anchor.transform.parent?.parent?.parent as RectTransform : null;
            if (!anchor || !anchor.gameObject.activeInHierarchy || !anchor.GetComponentInParent<Canvas>().enabled || (bar && bar.anchoredPosition.y > .5f))
            {
                if (FoundationApi.UiAccess.Root) ClosePopup();
                return;
            }
            if (transform.GetSiblingIndex() != transform.parent.childCount - 1) transform.SetAsLastSibling();
            Fit();
            if (pendingSearch != null && Time.unscaledTime >= searchAfter) { SetSearch(pendingSearch); return; }
            if (!frozen && Time.unscaledTime >= nextRefresh) RefreshNow();
        }
        internal void View(LedgerRowData row) { if (row?.Target != null) GlobalLedgerPlugin.Instance.Navigation.Focus(row.Target); }
    }
}
