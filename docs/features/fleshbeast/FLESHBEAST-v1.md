# Fleshmass Displacement Design

## Summary

This note captures a proposed Zombieland interaction for RimWorld Anomaly
fleshmass hearts. The goal is not to let zombies solve the Anomaly event. The
goal is to make a player unable to safely ignore a heart forever while the
colony sits behind walls.

Preferred design: zombies may attack active fleshmass around a heart. Their
attacks displace flesh instead of destroying the problem. Removed flesh gives
the heart stored expansion points. Periodically, preferably in visible hourly
pulses, the heart spends those points through its normal growth logic, biased
away from recently damaged cells. This can expose the heart locally, but it can
also make the fleshmass grow elsewhere and keep the encounter chaotic.

The result should feel like a Zombieland event colliding with Anomaly biology:
zombies chew into the flesh, the flesh withdraws and resurges somewhere else,
and fleshbeasts that emerge are then handled by the existing Anomaly hostility
settings.

## Current Constraints

- `FleshmassHeart` is a vanilla Anomaly building, not a pawn. Current Zombieland
  Anomaly targeting mostly classifies pawns such as ghouls, shamblers, entities,
  fleshbeasts, and the nociosphere.
- The heart is not a normal combat target. Vanilla defines it as non-destroyable
  through ordinary hit points and routes real defeat through neural-lump
  analysis plus the heart destruction interaction.
- Existing Zombieland evidence currently treats this as nominal: zombies disrupt
  nearby fleshbeast pawns but do not attack the heart building itself.
- Vanilla fleshmass already has a growth model based on growth points,
  contiguous fleshmass cells, tendrils, special mass, nerve bundles, spitters,
  and fleshbeast response.
- Zombieland already has a precedent for living cell systems in the symbiant:
  cell sets, connectedness checks, visible motion, shrink/regrow behavior, and
  delayed relocation/reseed behavior.

## Design Goals

- Prevent passive containment: a heart left outside should not remain a static,
  harmless background object while zombies exist on the map.
- Preserve Anomaly's objective: zombies must not directly kill the heart or skip
  the analysis/interact path.
- Preserve Zombieland chaos: zombie pressure should create openings, new danger,
  moving fronts, and battlefield surprises.
- Keep ordinary fleshbeast hostility policy intact: when fleshbeasts emerge from
  the flesh, existing settings decide whether zombies attack them, whether they
  attack zombies, or whether they mostly ignore each other.
- Avoid microscopic per-cell trickle effects. Hourly or otherwise chunky growth
  pulses should make displacement visible and understandable.
- Avoid making this a precise player tool. Players may exploit openings, but
  they should not be able to reliably use zombies as clean fleshmass miners.

## Final Proposal

Add a Zombieland-owned "fleshmass displacement" layer for active Anomaly
fleshmass connected to a `FleshmassHeart`.

Zombies can attack active fleshmass cells on or near the reachable perimeter of
the heart's connected fleshmass. Each successful attack adds local pressure. When
pressure breaks a cell, the cell is removed or wounded, and the heart receives
stored expansion points. The removed cell is recorded in a recent-wound memory.

Every in-game hour, the heart spends accumulated expansion points in a visible
growth pulse. The pulse should use vanilla-style growth as much as possible:
expand from the heart's existing contiguous flesh, pick plausible tendril/core
growth cells, and keep the natural Anomaly feel. The Zombieland change is that
growth should strongly avoid cells near recently removed flesh, so the mass
appears to withdraw from zombie pressure and surge elsewhere.

This means many zombies can displace a lot of flesh. Do not hard-cap the core
effect too tightly by default. The intended balance is not "a small safe trickle";
it is "a lot of zombies can visibly move the infection front." If limits are
needed later, prefer soft controls such as decay, terrain validity, available
growth sites, hourly batching, and diminishing quality of bad growth sites
rather than a strict low per-day cap.

## Mechanical Flow

1. Heart discovery
   - Detect active `FleshmassHeart` instances on the current map.
   - Track their connected active fleshmass cells or use vanilla contiguous mass
     information where available.
   - Do not make the heart itself a normal `Tools.Attackable` target.

2. Zombie target selection
   - Zombies consider active fleshmass cells only when they are already near the
     heart field or when heart-pressure scent has pulled them toward the area.
   - Prefer perimeter cells, damaged cells, or cells between the zombie and the
     heart.
   - Do not let all fleshmass on the whole map become a high-priority global
     target. The behavior should be local and pressure-driven.

3. Attack result
   - A zombie attack does not apply ordinary building damage to the heart.
   - A zombie attack adds pressure to the attacked active fleshmass cell.
   - Once pressure reaches a threshold, that local flesh retracts, is destroyed,
     or becomes a temporary wounded state.
   - Retraction grants displacement/expansion points to the owning heart.
   - The removed cell and nearby cells are added to recent-wound memory.

4. Point collection
   - Points accumulate between growth pulses.
   - More zombie attacks mean more points. This should scale naturally with
     zombie count; a large horde should be able to create a large visible shift.
   - Points should decay only if testing shows old pressure makes the heart
     behave strangely after the battle is over.

5. Hourly growth pulse
   - Once per in-game hour, if the heart has collected points, it spends a batch.
   - The pulse should be visible: several cells can appear during one pulse,
     rather than a barely noticeable one-cell-per-few-ticks trickle.
   - The pulse uses vanilla-style growth selection, but filters or downweights
     cells near recently removed flesh.
   - If the normal growth algorithm cannot find valid cells away from pressure,
     it may eventually fall back to less ideal cells rather than losing all
     points. The fallback should still avoid instantly replacing the exact cell
     that zombies just removed.

6. Exposure
   - If enough flesh around the heart is displaced, the heart can become locally
     reachable or visible.
   - This is an opportunity, not a win condition.
   - If the player has not completed Anomaly analysis, reaching the heart should
     still not allow victory. The player may gain access to nerve bundles,
     defenders, or a dangerous battlefield state instead.

7. Defense mechanics
   - When the heart's normal mechanics spawn fleshbeasts, spitters, or other
     defenders, do not special-case their zombie hostility here.
   - Existing Anomaly/Zombieland hostility settings apply:
     - zombies may attack fleshbeasts or ignore them according to zombie attack
       mode and Anomaly targeting settings;
     - fleshbeasts may attack zombies or ignore them according to reverse
       hostility settings;
     - this design only controls flesh displacement, not pawn hostility policy.

## Growth Bias

The important tweak is not a custom growth algorithm from scratch. The heart
should still feel like Anomaly's heart. The bias should be small and targeted:

- Maintain recent-wound records per heart: cell, tick, and maybe pressure amount.
- When choosing growth cells, reject cells inside a short "fresh wound" radius.
- After the fresh window expires, allow those cells again but with reduced
  weight for a longer time.
- Prefer growth from existing connected flesh, not teleporting isolated blobs.
- Prefer directions that move the mass away from the local zombie attack front.
- If all good cells are blocked, let the vanilla-style algorithm degrade
  gracefully rather than discarding the pulse.

The visible outcome should be: zombies chew one side open, the mass pulses and
pushes elsewhere.

## Suggested Starting Values

These are tuning anchors, not final balance:

- Zombie attacks needed to displace one ordinary active flesh cell: 2 to 4.
- Tanky, burning, electrifier, or other special zombie modifiers: optional later.
- Recent-wound hard exclusion radius: 4 to 6 cells.
- Recent-wound hard exclusion duration: 1 to 3 in-game hours.
- Soft avoidance duration: 6 to 12 in-game hours.
- Growth pulse cadence: 1 in-game hour.
- Points spent per pulse: all accumulated points, or a large fraction such as
  70% to 100%.
- Exact-cell replacement delay: always longer than the hard exclusion window.

Avoid starting with a strict low maximum such as "only 5 displaced cells per
day." That would undercut the point of letting a zombie horde create a major
map event. If runaway behavior appears in testing, tune the cost and growth
validity before adding a hard cap.

## Variants Inside The Same Idea

### Equal Displacement

Each removed flesh cell creates one expansion point. The heart eventually grows
one replacement cell elsewhere.

Pros:
- Clean mental model.
- Less runaway growth.
- Strong "the flesh moved" feeling.

Cons:
- May be too fair if players learn to use zombies as excavation.
- Needs good growth bias to avoid instant replacement.

### Displacement With Biological Interest

Each removed flesh cell creates one replacement point, and sustained zombie
attacks create occasional bonus points.

Pros:
- Stronger anti-stall pressure.
- A large zombie horde becomes scary instead of helpful.
- Supports the fiction that zombie violence feeds the organism.

Cons:
- Higher runaway risk.
- Needs softer brakes from terrain, valid growth sites, or delayed pulses.

### Wounded Flesh Before Retraction

Zombie attacks mark flesh as wounded first. Wounded flesh is weaker, maybe
temporarily passable or visually changed. If pressure continues, it retracts and
feeds the hourly pulse.

Pros:
- More readable.
- More time for player intervention.
- Better visual story.

Cons:
- More state and rendering work.
- More cases to save/load and clean up.

### Exposure Pulse

If zombie displacement clears enough cells around the heart, the heart enters a
short exposed/convulsing state. The next hourly pulse may then overgrow
elsewhere or birth defenders.

Pros:
- Creates dramatic openings.
- Makes the player want to act during the chaos.

Cons:
- Can accidentally become too helpful for players who already finished analysis.
- Needs strict "not a win condition" handling.

### Defender-Biased Pulse

Instead of spending all displacement points on cells, the heart can spend some
points on its normal defensive outputs: fleshbeasts, spitters, or nerve-bundle
related activity.

Pros:
- Keeps zombies fighting monsters rather than flesh forever.
- Escalates ignored battles into interesting danger.

Cons:
- Can become pawn spam.
- Needs live balance testing with different zombie counts.

## Pros

- The mechanic prevents stale heart containment without bypassing the Anomaly
  objective.
- It turns zombie presence into a chaotic environmental pressure, not a clean
  advantage.
- It creates visible map change, which is more interesting than hidden damage or
  slow invisible counters.
- It composes with existing Anomaly hostility settings instead of replacing
  them.
- It supports large zombie counts: more zombies cause more displacement and more
  flesh response.
- It gives players tactical openings while preserving risk.

## Cons And Risks

- If growth pulses are too predictable, players can weaponize zombies to carve a
  path to the heart.
- If growth is too strong, the heart may become oppressive on high-zombie maps.
- If every flesh cell is indexed as a zombie target, pathing and attack scans
  can get expensive.
- If recent-wound avoidance is too strict, growth may stall in cramped maps.
- If recent-wound avoidance is too weak, the flesh will visibly replace the same
  cells and the movement fantasy fails.
- If defender births are tied too directly to every removed cell, combat can
  become noisy and exhausting.
- If UI feedback is absent, players may not understand why zombies attacking the
  flesh makes it expand elsewhere.

## Implementation Notes

- Keep this feature separate from the existing pawn-only Anomaly targeting
  categories. This is a heart/fleshmass pressure system, not a generic pawn
  hostility rule.
- Prefer a map component or heart-attached tracker that records per-heart
  displacement state: recent wounds, pending points, next pulse tick, and maybe
  pressure by cell.
- Avoid making `FleshmassHeart` itself a normal attack target.
- Avoid adding all active fleshmass cells to the standard pawn attack target
  index. Use local checks around zombies near heart-connected flesh.
- Prefer a custom zombie job/report such as "chewing fleshmass" or reuse the
  existing attack-static shape only if it can target the cell/thing without
  changing vanilla heart kill semantics.
- Use vanilla `CompGrowsFleshmassTendrils` behavior as the model, but expect
  some private fields/methods to require either reflection, Harmony patches, or
  a Zombieland-side companion algorithm that mirrors the public behavior.
- Save/load all Zombieland displacement state. This feature will be long-lived
  during a map event.
- Add log-clean cleanup to any bridge fixture. Fleshmass state can easily leave
  spawned buildings, defenders, or stale per-heart records behind.

## Testing Strategy

Static/source pass:
- Confirm current RimWorld build still uses `Building_FleshmassHeart`,
  `CompFleshmassHeart`, and `CompGrowsFleshmassTendrils` in the expected shape.
- Confirm the heart still dies only through the vanilla overload path and not
  normal hit-point attacks.
- Confirm active fleshmass cells can be removed without breaking vanilla
  connected-mass cleanup.

Contract/runtime pass:
- Stage a heart with active flesh and a controlled zombie group.
- Verify zombies attack perimeter flesh, not the heart building.
- Verify removed cells create pending expansion points.
- Advance to the next hourly pulse and verify new cells grow away from recent
  wound cells.
- Verify a large zombie group displaces substantially more flesh than a small
  group.
- Verify no direct heart kill occurs, even if zombies expose it.
- Verify spawned fleshbeasts use existing Anomaly hostility settings in both
  directions.
- Verify save/reload preserves pending points, recent wounds, next pulse timing,
  and does not duplicate growth.

Scenario pass:
- Run a passive-containment scenario where the player does nothing and zombies
  pressure the field for several in-game hours.
- Run an active-player scenario where the player exploits a zombie-made opening.
- Run a high-zombie-density scenario to see whether uncapped displacement stays
  fun or becomes runaway.
- Run settings matrix checks for `OnlyColonists`, `OnlyHumans`, `Everything`,
  and Anomaly reverse-hostility overrides.

## Open Design Questions

- Should zombie attacks fully remove a flesh cell, or first create a wounded
  temporary state?
- Should zombie corpses on flesh add bonus points, or should only attacks count?
- Should an exposed heart trigger a special letter, sound, or mote?
- Should special zombies alter displacement pressure, or should v1 keep all
  zombie attacks equal?
- Should growth pulses spend every pending point, or reserve some points for
  defender responses?
- How long should the recent-wound memory last before the heart may reclaim
  that exact ground?

## Recommended V1

Implement the equal-displacement version first:

- zombies attack active fleshmass perimeter cells near a heart;
- enough attacks remove or retract a cell;
- each removed cell adds one pending expansion point to that heart;
- once per in-game hour, the heart spends all pending points in a visible growth
  pulse;
- growth follows vanilla-style expansion but rejects cells near recent wounds;
- no direct heart damage or overload occurs;
- fleshbeasts spawned by normal heart behavior continue to use existing
  Anomaly/Zombieland hostility settings.

After that works, test whether the result needs biological interest, wounded
flesh, or defender-biased pulses. The first version should prove the main
fantasy: zombies do not clear the fleshmass, they shove it around.
