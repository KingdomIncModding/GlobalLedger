# Global Ledger

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/urakkaamyx/tip)

**Global Ledger 0.1.17 — for Kingdom Inc game version 1.0.14.**

[GitHub releases](https://github.com/KingdomIncModding/GlobalLedger/releases) · [Nexus Mods](https://www.nexusmods.com/kingdominc/mods/19)

Track live currency earnings, operating expenses, and item production across your entire kingdom from a dropdown on the top Mod Bar in Kingdom Inc.

Monitor economy flow in real time without placing physical tracking nodes in every factory line.

## Features

- **Top Mod Bar Dropdown:** Access real-time financial and production data from the top Mod Bar at any time.
- **Basic and Advanced Modes:**
  - **Basic Mode:** Lightweight currency and item overview showing net rates only.
  - **Advanced Mode:** Full hierarchical drill-down by Currency / Item > Container > Building > Operating Costs.
- **Original Pixel Look:** Native pixel font, frames, buttons, resource icons and coin artwork.
- **Independent Rate Units and History:** Switch between `Sec`, `Min`, and `Hr` rate units without losing each unit's selected observation window (`1s / 10s / 30s`, `1m / 5m / 30m`, `1h / 12h / 24h`) inside Settings.
- **Advanced In / Out / Net:** See production and consumption even when a container balances to zero.
- **Search, Tabs, and Collapsible Categories:** Search resources, containers and buildings, including entries under collapsed categories or parents. Filter with the **All**, **Currencies**, and **Production** tabs, or collapse and expand the **CURRENCIES** and **PRODUCTION** category headers directly. Game hotkeys pause while typing in search.
- **Column Sorting & Stable Advanced Lists:** Click **NET** in Basic mode or **IN**, **OUT**, and **NET** in Advanced mode to sort resources within each category (and sources beneath expanded entries) descending, click again to reverse, and click a third time or click **RESOURCE** to reset to the default order. While open in Advanced mode, visible items, expanded sources, and the current row order stay pinned instead of disappearing or jumping between auto-refreshes.
- **Refresh and Freeze:** Rates refresh every 2 seconds by default; Settings lets you cycle `1` to `4` seconds. **Freeze** the display while activity continues to be recorded, then **Unfreeze** to update it.
- **Optional Smoothing:** All rates already average activity over the observation window. Smoothing optionally blends recent display refresh samples without changing the observation window. Cycle `OFF` or `2`–`5` samples via **Smooth** in Settings, or choose `2`–`30` samples in the mod settings.
- **Compact Settings:** Settings starts closed (`Show Settings` / `Hide Settings`). Search, **All / Currencies / Production** tabs, and **Collapse All** (in Advanced mode) remain visible above the table.
- **Multi-Currency Accounting:** Gold rates retain native denomination icons alongside explicit g / s / c labels; Requisition, Mana, Crowns, Medals, and Rings maintain their own units.
- **Container & Workspace Grouping:** Automatically groups production by blueprint containers and workspaces, with container icon stickers and clean names.
- **Live View Navigation:** Click the View button on any container or building row to switch workspaces, pan the camera, and highlight the building.
- **Readable Values:** Equal-width numeric characters use the native font's natural spacing, with thousands separators and two decimal places. Basic has a wide value column; Advanced has wider numeric columns. Gold uses native coin icons with explicit g (gold), s (silver) or c (copper) labels. Long names scroll after a two-second hover.
- **Right Click Fix Integration:** With Right Click Fix active, a short right-click closes the ledger. Escape, Close and the Mod Bar button also dismiss it.

## How the Accounting Works

Rates reflect actual production and financial activity over the selected observation window. Session history is retained for up to 24 hours; activity before the session began is unavailable. Longer windows can make intermittent dungeon rewards easier to assess. The status shows when a full window is still being collected.

- Income and operating expenses are attributed when transactions occur.
- Container collection tray changes are combined with wallet credits so income is recognized when earned without double-counting.
- Pure routing nodes and storage pass-throughs are excluded to ensure true net production measurements.
- In and Out show observed production/earnings and consumption/spending, not portal transfers or estimated demand.
- Gold rates use the largest applicable denomination with two decimal places: for example, 10,398 copper displays as 1.04g. The table legend explains g / s / c; denomination does not depend on icon color. Recorded accounting totals remain exact.

## Requirements

- Kingdom Inc (Windows x64 Mono)
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6) (5.4.23.6 or newer)
- [Foundation](https://www.nexusmods.com/kingdominc/mods/18) (0.14.4 or newer)

## Installation

1. Install BepInEx 5 for Kingdom Inc.
2. Install Foundation (0.14.4 or newer).
3. Extract the release ZIP into your Kingdom Inc game directory.
4. Verify the file exists at `BepInEx/plugins/GlobalLedger/GlobalLedger.dll`.
5. Launch the game and access Global Ledger from the top Mod Bar!

## Compatibility

| Class.Method | Hook Type | Impact |
|---|---|---|
| FlowIO.ConsumeItem | Foundation hook (postfix) | Observes item consumption events across production chains. |
| GamePlayer.AddCurrency | Foundation hook (postfix) | Captures currency income credits from shops, quests, and rewards. |
| GamePlayer.SpendCurrency | Foundation hook (postfix) | Tracks currency spending events for production operating costs. |
| WarProgress.UpdateWar | Foundation execution observer | Observes war effort currency income and progress updates. |
| GameUI.ProcessEscape | Foundation hook (prefix) | Closes the Global Ledger dropdown panel when pressing Escape. |
| GameUI.ProcessCancel | Foundation hook (prefix) | Closes the ledger on a short right-click when Right Click Fix is active; preserves dragging, native right-click actions and modal menus. |
| GameUI.ToggleUI | Foundation hook (postfix) | Synchronizes Global Ledger dropdown visibility with native HUD toggle states. |
| FlowElement lifecycle methods | Foundation execution observer | Dynamically tracks production scopes across building updates and craft operations. |
| FoundationApi.Events.Subscribe | Foundation event subscription | Refreshes workspace, container and recipe grouping after relevant changes. |
| FoundationApi.ExecutionHooks.Subscribe | Foundation execution observer (WorkspaceEdit) | Refreshes grouping after workspace edits. |
| FoundationApi.GameHooks.Subscribe | Foundation hooks (DecorationStyleUpdate, BuildingClearPorts) | Refreshes cached locations and building information after edits. |

## Removal

Close the game and delete `BepInEx/plugins/GlobalLedger/`. Accounting history is session-local and leaves no persistent custom data in your saves.

## Credits and Source

Created by **Urakka** (rockyamyx). [Source code on GitHub](https://github.com/KingdomIncModding/GlobalLedger). Game artwork belongs to its respective owners.

## Support

If you enjoy these Kingdom Inc mods and tools, consider supporting development on Ko-fi!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/urakkaamyx/tip)
