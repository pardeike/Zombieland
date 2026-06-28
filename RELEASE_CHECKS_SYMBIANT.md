# Symbiant Release Checks

Scope: player-facing Symbiant behavior added or materially changed for the simplified Symbiant release slice.

Baseline:
- Current commit: `f39484d Implement simplified Symbiant feature`.
- Runtime target: paired `RIMWORLD_MOD_DIR` deployment to `Mods/ZombieLand` and sibling `Mods/../BridgeTools/Zombieland`.
- Working save fixture: `SYMBIANT-TEST-MAP`.
- Current stance: broad empirical checks are release gates; narrow edge matrices are follow-up work unless they expose a broad player-facing regression.

## Claims

| Area | Main player-facing claim | Evidence target | Status |
| --- | --- | --- | --- |
| Deployment/startup | Current Symbiant build loads from the local deployed mod path. | `scripts/build-quiet.sh`, GABS start/load | PASS |
| Settings | Player-facing settings are compact: enable/disable and max cells. Lowering max cells caps future growth without deleting an active Symbiant. | `symbiant_settings_contract`, source/UI read | PASS |
| Spawn/discovery | A Zombie Symbiant can be created in a used indoor room, links to a host when available, and reports the expected state. | `symbiant_infestation_state createEvent` | PASS |
| Growth/rendering | Add/expand paths update cell count, selector rect, GPU render metadata, and effect text. | `symbiant_infestation_state expand` | PASS |
| Door slowdown | A Symbiant-covered door cell applies difficulty-scaled movement slowdown through the real path follower. | `symbiant_door_path_cost_contract` | PASS |
| Surgery | Host surgery is visible while linked and uses RimWorld's normal dynamic ingredient-count path for zombie extract plus medicine. | `symbiant_severance_contract` | PASS |
| Selection/UI | Clicking any covered blob cell selects the Symbiant, inspect text is valid, benefits/effects are visible, normal pawn tabs are hidden, and the removed status gizmo is absent. | `click_cell`, `get_selection_semantics`, `list_selected_gizmos` | PASS |
| Save/load/cache | Active/empty Symbiant caches invalidate correctly and repeated load after restart remains playable. | `symbiant_map_cache_contract`, `load_game_ready` | PASS |
| Old-save compatibility | Removed legacy defs are not hidden. Old saves may log missing-def errors but should load non-fatally. | `load_game_ready` on `SYMBIANT-TEST-MAP` | PASS with expected error |
| Cleanup/interruption | Load, restart, map removal, and main-menu/load boundaries do not leave obvious stale active-cache state. | GABS load/restart smoke, cache contract | PASS |

## Evidence

| Result | Area | Operation / evidence | Notes |
| --- | --- | --- | --- |
| PASS | Build/deploy | `./scripts/build-quiet.sh` | Build succeeded with 0 warnings and 0 errors. The script deployed to the configured local RimWorld Mods folder. |
| PASS | XML/translation/static | `xmllint --noout`; translation key/placeholder parity script; stale Symbiant key scan; `git diff --check` | XML clean, all supported language keyed files matched, placeholders matched, and removed legacy keys/settings were absent. |
| PASS | Settings | `op_b71ff072d53740beaa65492c17d6c68b` | Disabled events blocked natural spawn; disabling did not delete an active Symbiant; lowered max acted as a cap. |
| PASS | Surgery | `op_62f6304761dd4856ab7b238e7f5249dc` | Recipe worker class matched; torso operation visible; dynamic extract count was 10 at current difficulty; bond removal succeeded; no extra map extract was manually consumed by the worker. |
| PASS | Door slowdown | `op_ad90e439d0144a768e06a3c51c14b606` | Covered door cell inflated path follower cost to expected difficulty-scaled value. |
| PASS | Map cache | `op_cd933d6f1a0344c195cf85f0a2ca31c3` | Empty-map cache, spawn invalidation, cleanup invalidation, explicit cache reset, and map-pawn exclusion passed. |
| PASS | Event creation | `op_dcf73c3cdec44689b76436c9e2d85531` | Created active linked Symbiant. Reported `maxCells=400`, `technicalMaxCells=4000`, GPU metaball mask active, surgery visible, and current effect/benefit text. |
| PASS | Expansion/render state | `op_4ec9a17a81904fdd9cb510286ddc9440` | Expanded from 1 to 5 cells, maintained selector coverage, effect text, render metadata, and active cell motions. |
| PASS | Click-anywhere selection | `op_f89e3bf86b504f9eb4d011ab4926f4a6` | Clicking non-origin cell `(120,135)` selected the Symbiant rooted at `(119,134)`. |
| PASS | Inspect text and no gizmo | `op_66b3469fc8a84ff4aa5b342638c09143`; `op_13ffd847c5a140439ea61c69bf3caf2e` | Inspect string contained no empty lines and listed size, host, growth, effects, and benefits. `gizmoCount=0`. |
| PASS with expected error | Old-save missing def | GABS attention `attn_17` / `attn_20` | Loading `SYMBIANT-TEST-MAP` logged `Could not load reference to Verse.ThingDef named SymbiantCoagulantPack`. The save loaded playable afterward. This is expected because the legacy def was intentionally removed. |
| PASS | Repeated load after restart | `op_bd8b228709b74079881bc1416edc5c7b`; earlier `op_c550cf129ef64706bf750cee8f97281f` | `SYMBIANT-TEST-MAP` loaded playable and paused after restart. |

## Current Assessment

No release-blocking Symbiant issue was found in the current simplified broad pass on `SYMBIANT-TEST-MAP`.

Known non-blockers:
- The old-save missing `SymbiantCoagulantPack` red error is expected and non-fatal for the tested save.
- The pass used the current Symbiant test map, not a full DLC-heavy matrix.
- No fresh full 4000-cell endurance run was performed after the doc cleanup; rerun stress only after rendering/path/stat/cache changes.
- `go_to_main_menu` caused the test RimWorld process to stop during one smoke attempt; restarting and reloading the test map succeeded. No current Player.log was found at the usual paths to classify the process exit.

## Release Notes

- Normal source-only commits should not include tracked `1.6/Assemblies/ZombieLand.dll` rebuilds.
- Rebuild asset bundles only when shader/material assets change.
- Treat `SYMBIANT_PLAN.md` as the current design handoff and `TEST_COVERAGE.md` as the longer historical evidence ledger.
