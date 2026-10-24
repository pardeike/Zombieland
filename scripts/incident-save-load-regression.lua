-- Run against disposable, loadout-matched fixtures prepared as documented in
-- scripts/README.md. Each mode fails on the original scheduler implementation.
rb.assert(params.saveName ~= nil, "params.saveName is required.")
local mode = params.mode or "shortSessions"
local scratch = params.scratchSave or "ZL_Incident_Regression_scratch"
local loaded = rb.call("rimworld/load_game_ready", {
  saveName = params.saveName, readiness = "playable", pauseIfNeeded = true
})
rb.assert(loaded.result.success == true, "Could not load the matching incident fixture.")
local before = rb.call("zombieland/incident_threat_state", { includeSpawnDiagnostics = mode == "failedPlacement" })
rb.assert(before.result.success == true, "Could not read the initial incident state.")

if mode == "shortSessions" then
  for segment = 1, 3 do
    rb.call("zombieland/wait_for_semantic_change", {
      targetKind = "map", condition = "zombieCountChanged",
      maxTicks = 10000, sampleEveryTicks = 1000, maxSamples = 1
    })
    if segment < 3 then
      local saved = rb.call("rimworld/save_game", { saveName = scratch })
      rb.assert(saved.result.success == true, "Could not save the short-session fixture.")
      local reloaded = rb.call("rimworld/load_game_ready", {
        saveName = scratch, readiness = "playable", pauseIfNeeded = true
      })
      rb.assert(reloaded.result.success == true, "Could not reload the short-session fixture.")
    end
  end
  local after = rb.call("zombieland/incident_threat_state", { includeSpawnDiagnostics = false })
  rb.print("short-session incident", after.result.state.incident)
  rb.assert(after.result.state.incident.lastIncident > before.result.state.incident.lastIncident,
    "Reloading short sessions prevented the overdue incident check.")
  return { success = true, before = before.result.state.incident, after = after.result.state.incident }
elseif mode == "reloadDuringWave" then
  for pass = 1, 2 do
    rb.call("zombieland/wait_for_semantic_change", {
      targetKind = "map", condition = "zombieCountChanged",
      maxTicks = 20000, sampleEveryTicks = 1000, maxSamples = 1
    })
  end
  local admitted = rb.call("zombieland/incident_threat_state", { includeSpawnDiagnostics = false })
  rb.assert(admitted.result.state.incident.lastIncident > before.result.state.incident.lastIncident,
    "No wave was admitted before the reload test.")
  local reloaded = rb.call("rimworld/load_game_ready", {
    saveName = params.saveName, readiness = "playable", pauseIfNeeded = true
  })
  rb.assert(reloaded.result.success == true, "Could not reload during the wave.")
  local errors = rb.call("rimbridge/list_logs", { minimumLevel = "error", limit = 1 })
  rb.assert(errors.result.logs[1] == nil, "A wave continued using the discarded map after reload.")
  local fresh = rb.call("zombieland/incident_threat_state", { includeSpawnDiagnostics = false })
  rb.assert(fresh.result.state.zombies.spawning == 0, "Reloading leaked an active zombie-generation count.")
  return { success = true, admitted = admitted.result.state.incident }
elseif mode == "failedPlacement" then
  rb.print("placement spawn mode", before.result.state.settings.spawnHowType)
  rb.assert(before.result.state.spawnDiagnostics.allOverTheMap.validSpotCount == 0,
    "Fixture must have no valid all-over-map spawn spots.")
  rb.assert(before.result.state.incident.parameters.daysStretched > 0,
    "Fixture must contain a saved incident interval.")
  for pass = 1, 2 do
    rb.call("zombieland/wait_for_semantic_change", {
      targetKind = "map", condition = "zombieCountChanged",
      maxTicks = 20000, sampleEveryTicks = 1000, maxSamples = 1
    })
  end
  local after = rb.call("zombieland/incident_threat_state", { includeSpawnDiagnostics = false })
  rb.print("failed-placement incident", after.result.state.incident)
  rb.assert(after.result.state.incident.parameters.incidentSize > 0, "No incident was attempted.")
  rb.assert(after.result.state.zombies.count == 0, "The blocked fixture spawned zombies.")
  rb.assert(after.result.state.incident.lastIncident == before.result.state.incident.lastIncident,
    "Failed placement consumed the incident timestamp.")
  rb.assert(after.result.state.incident.parameters.daysStretched == before.result.state.incident.parameters.daysStretched,
    "Failed placement replaced the pending incident interval.")
  return { success = true, before = before.result.state.incident, after = after.result.state.incident }
else
  rb.fail("Unknown incident regression mode.")
end
