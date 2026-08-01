-- Reusable Steam/RimBridge entry point for the controlled-loadout Anomaly music
-- integration scenario. The companion accepts either the exact minimal set or
-- Core plus every official DLC, owns predicate setup and verified cleanup, and
-- saves only after every scenario and postcondition succeeds.

local saveName = params.saveName or "ZL_Anomaly_Music_100"
local seed = params.seed or 73101

local entry = rb.call("rimworld/go_to_main_menu", nil)
rb.assert(entry.result.success == true, "Could not return to RimWorld's main menu.")

local started = rb.call("rimworld/start_debug_game_ready", {
  readiness = "visual",
  pauseIfNeeded = true,
  timeoutMs = 120000
})
rb.assert(started.result.success == true, "Could not start the Anomaly music debug colony.")

local scenario = rb.call("zombieland/anomaly_music_replacement_scenario", {
  leaveSettingsAt100 = true,
  seed = seed
})
rb.assert(scenario.result.success == true, "Anomaly music replacement scenario failed.")

local saved = rb.call("rimworld/save_game", { saveName = saveName })
rb.assert(saved.result.success == true, "Could not save the clean 100% music fixture.")

local warnings = rb.call("rimbridge/list_logs", {
  minimumLevel = "warning",
  limit = 50
})

return {
  success = scenario.result.success,
  saveName = saveName,
  entry = entry.result,
  started = started.result,
  scenario = scenario.result,
  saved = saved.result,
  warnings = warnings.result
}
