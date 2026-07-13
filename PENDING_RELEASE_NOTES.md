# Pending Release Notes

Changes since v5.3.4.0.

## Symbiants

- Hostile humanlike and mechanoid pawns can now choose Zombie Symbiants as ordinary colony targets.
- Melee and projectile attacks now target reachable, exposed slime cells instead of the hidden root cell.
- Ranged attackers can now approach a large Symbiant when its nearest edge is inside their search radius.
- Melee attackers now compare a reachable Symbiant edge fairly against other nearby targets.
- Melee attackers no longer lose their Symbiant attack position when another pawn briefly occupies it.
- Symbiant firing lines now refresh after doors, walls, or tar smoke change.
- Beam and spray weapons no longer choose detached slime cells their effects cannot reach.
- Shots at Symbiant edges retain normal target-size, cover, weather, lighting, and Ideology accuracy modifiers.
- Berserk and other aggressive ranged pawns no longer replace an existing target with the Symbiant.
- Combat Extended projectile attacks can now hit the exposed cells of a Zombie Symbiant.
- Combat Extended attacks no longer leave ordinary injuries or accumulating scars on Symbiants.
- Hostile attacks on a Symbiant now use normal damage processing and drain shared health by the damage dealt.
- Direct attacks on a Symbiant no longer create real injuries on its host.
- The linked pawn's Health tab now shows a compact, harmless history of damage absorbed by the Symbiant.
- Symbiant damage history disappears while the bond is dormant and returns when the bond reactivates.
- Symbiants remain functional until shared health is depleted instead of being downed by ordinary wounds.
- Explosions now damage a Symbiant only once and respect their excluded inner radius.
- Shared health now recovers after one quiet hour by 5% of the missing amount each quiet hour, including during dormancy.
- Symbiant health and info text now correctly explain direct hits, harmless damage echoes, and recovery values in every supported language.
- Symbiant benefit stages now grant their listed Biotech Toxic Environment Resistance.
- Expanding Symbiants now breach constructed walls without destroying coolers or other non-wall buildings.

## Gameplay And Stability

- Missing, unsorted, or invalid time-based settings now repair automatically while preserving valid settings.
- Fallback zombie targeting no longer skips a valid zombie when another candidate fails the attack rules.
- Job-based contamination effects no longer cause errors on animals or other non-draftable pawns.
- Multi-map games now keep player-reachable region data separate for each map.
- Save reloads now clear stale zombie-avoidance, pathing, map, and targeting data.
- Zombie serum purity percentages now match their actual success chance, including exact 10%, 50%, and 100% values.
- Thumper shockwaves no longer damage multi-cell buildings more than once per wave.

## Performance

- Reduced combat-targeting overhead on maps without a Symbiant and avoided duplicate Symbiant edge scans.
- Reduced background work from idle Symbiants, especially when their bonded host is missing.
- Greatly reduced pathfinding allocations when colonists avoid zombies, especially on large maps.
- Zombie-avoidance pathing data is now released safely when avoidance is disabled, a map closes, or a save reloads.
- Paused games no longer continue updating zombie wander paths.
- Reduced repeated zombie scans when colonists move away from nearby danger.
- Improved detailed contamination-overlay performance by scanning only visible map cells.
- Reduced redundant healer-zombie scans without changing target order or healing limits.

## Audio

- Updated and normalized the "Misalignment" soundtrack track.
