-- Thin lowered-Lua wrapper around the companion-owned benchmark. The companion
-- keeps test-mode restoration inside a C# try/finally that ignores operation
-- cancellation during cleanup.
--
-- Parameters:
--   saveName              required save name without .rws
--   spawnCount            0 for an existing dense fixture, otherwise 1..200
--   spawnX/spawnZ         center for newly spawned normal zombies
--   spawnRadius           spawn search radius
--   cameraX/cameraZ       camera cell kept away from the fixture
--   durationMs            real-time duration of each speed sample
--   forceRequestedSpeed   normally false; true suppresses forced slowdown
--   runContracts          normally true only for a disposable empty save

rb.assert(params.saveName ~= nil, "params.saveName is required.")

local spawnCount = params.spawnCount or 0
local spawnX = params.spawnX or 10
local spawnZ = params.spawnZ or 10
local spawnRadius = params.spawnRadius or 18
local cameraX = params.cameraX or 240
local cameraZ = params.cameraZ or 240
local durationMs = params.durationMs or 2500
local forceRequestedSpeed = params.forceRequestedSpeed or false
local runContracts = params.runContracts
if runContracts == nil then
  runContracts = true
end

local benchmark = rb.call("zombieland/zombie_ticking_benchmark", {
  saveName = params.saveName,
  spawnCount = spawnCount,
  spawnX = spawnX,
  spawnZ = spawnZ,
  spawnRadius = spawnRadius,
  cameraX = cameraX,
  cameraZ = cameraZ,
  durationMs = durationMs,
  forceRequestedSpeed = forceRequestedSpeed,
  runContracts = runContracts
})

rb.assert(benchmark.result.success == true, "Zombie ticking benchmark failed.")
return benchmark.result
