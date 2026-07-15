# Fleshmass Collision Design: V5

## Status And Purpose

This is the current design contract for a future Zombieland interaction with
RimWorld Anomaly's fleshmass heart. It supersedes
[`FLESHBEAST-v4.md`](FLESHBEAST-v4.md).

The previous versions remain design history:

- [`FLESHBEAST-v1.md`](FLESHBEAST-v1.md) contains the original displacement
  fantasy.
- [`FLESHBEAST-v2.md`](FLESHBEAST-v2.md) introduced source ownership, a protected
  heart area, and a bounded relocation queue.
- [`FLESHBEAST-v3.md`](FLESHBEAST-v3.md) made relocation transactional but became
  a second fleshmass state machine.
- [`FLESHBEAST-v4.md`](FLESHBEAST-v4.md) removed relocation and embraced vanilla
  cascade and regrowth, but coupled the interaction to a blocked-rage hook that
  does not exist in the current zombie control flow.

V5 keeps V4's important simplification: Zombieland does not relocate flesh or
replace Anomaly's biological systems. It makes safe active fleshmass participate
in the existing local building-smash rule, then lets vanilla own damage,
cascade, response, and regrowth.

This is a reviewed design, not implemented or live-tested gameplay. The two open
product and tuning decisions are owned by [`TODO.md`](../../../TODO.md).

## Executive Decision

Use the existing `AnyBuilding` local-smash behavior.

When an ordinary eligible zombie reaches an existing `CanSmash` decision, a
cardinally adjacent `Fleshmass_Active` cell may be selected as the same kind of
local static target as another damageable building, provided that:

- the current settings already allow that zombie to smash arbitrary buildings;
- the cell belongs to a live spawned fleshmass heart;
- the cell is outside the protected heart area; and
- no existing higher-priority action has already ended the decision.

The zombie performs one ordinary `AttackStatic` attack. If the cell dies,
vanilla performs its normal cascade. Every actually destroyed sourced cell
caused by a Zombieland-faction instigator advances that source heart's existing
vanilla destruction-response counter once.

V5 adds no:

- rage-specific flesh rule;
- route-blocker detector;
- global flesh attraction;
- flesh target index;
- dedicated job;
- custom damage;
- relocation or replacement;
- map-wide growth algorithm;
- serialized cooldown in the initial prototype; or
- new player setting before the product decision in `TODO.md` is resolved.

## Review Of The V4 Findings

### Corner-Touch Safety: Valid And Fixed

V4 measured only cardinal distance from the heart footprint. That was
insufficient.

The installed `JobDriver_InteractThing` approaches the heart with
`PathEndMode.Touch`. Installed touch-path logic accepts eight-way adjacency when
corner touch is permitted. The 3-by-3 heart is an edifice that holds a roof, so
its diagonal corner positions are valid touch positions.

A corner-touch position is Manhattan distance 2 from the heart footprint. A
root at footprint distance 10 can therefore use eight additional cardinal
cascade kills to remove that position. V4's distance-10 rule did not prove that
zombies could not create the final interaction approach.

V5 protects the real interaction perimeter. An equivalent conservative numeric
rule for the installed 3-by-3 heart and eight-child maximum cascade is:

> A deliberate root target must be at least Manhattan distance 11 from every
> occupied cell of every spawned heart.

The derivation and required diagonal fixture appear in the protected-core and
verification sections below.

### Blocked-Rage Hook: Valid Criticism, Removed Premise

Rage has no intrinsic relationship with fleshmass or fleshbeasts. V4 used rage
only as a convenient Zombieland-side frequency gate.

That gate was not implementable as described:

- `JobDriver_Stumble.TickAction` calls `RageMove` only when `PossibleMoves`
  contains at least one cell;
- the no-parent `RageMove` path clears `zombie.raging` before calling `Smash`;
  and
- its later blocked-smash path represents crowding on the rage parent cell, not
  proof that a particular adjacent fleshmass cell physically blocks a route.

V5 does not invent route data or a new obstruction predicate to repair that
coupling. It reuses the actual local-smash candidate path. Rage can still affect
a zombie through ordinary Zombieland behavior, but it is neither required nor
interpreted as interest in flesh.

### Aggregate Throughput: Valid Risk, Premature Remedy

V4 did not bound successful root kills across a large front. The concern is
real as a balance question:

- one root death can remove up to eight additional cells;
- every completed static job returns the zombie to normal decisions, allowing
  a later attack;
- multiple zombies can focus nearby cells; and
- the existing rage warmup patch can divide attack warmup by the local zombie
  count for a raging attacker.

This does not create a core-safety failure in V5 because the protected area is
independent of throughput. It may still create too much outer-field cleanup.

V5 does not assume that a per-heart cooldown or one-root-per-rage state is
needed. Either would add persistent ownership, save/load, and concurrency rules
before the actual rate is known. The first prototype must measure roots and
total destroyed cells per in-game hour. Until that evidence exists, the design
uses the more accurate term **intermittent local damage**, not a guaranteed
rare event.

If measurement shows automatic cleanup, add the smallest effective limiter in
this order:

1. a stateless low attempt chance only when the selected building is active
   fleshmass;
2. a short non-serialized hesitation consistent with existing smash decisions;
3. only if aggregate scaling still defeats both, a per-heart successful-root
   cooldown with explicit save/load behavior.

Do not introduce a permanent stateful limiter merely to satisfy the review in
the absence of a failed runtime rate test.

### `DoorsOnly`: Valid Setting Boundary

The current enum, candidate selection, English text, help, and translations all
agree:

- `Nothing` means no structure smashing;
- `DoorsOnly` means closed doors; and
- `AnyBuilding` means arbitrary nearby buildings.

Making active fleshmass a hidden `DoorsOnly` exception would contradict that
contract. Updating help text would advertise the contradiction rather than
remove it.

V5 therefore enables deliberate flesh attacks only under `AnyBuilding`. Whether
the feature should instead receive a separate opt-in is a product decision in
`TODO.md`, not something to hide in this design.

## Vanilla Systems V5 Reuses

The installed RimWorld 1.6 behavior remains authoritative.

### Damage And Cascade

`Fleshmass_Active` has ordinary hit points. Nonlethal damage does not start a
collapse. When a cell dies, `CompCascadeOnDestroyed`:

- chooses four through eight additional cells as its maximum;
- traverses cardinally adjacent eligible `Fleshmass` and `Fleshmass_Active`;
- forwards the root damage instigator to child kills; and
- marks child kills `PreventCascade`, preventing recursive cascades.

One root kill therefore removes one through nine total cells, normally five
through nine when enough connected flesh exists. Field shape or map boundaries
may end it early.

V5 neither predicts nor reimplements this result.

### Destruction Response

Each destroyed sourced cell notifies its own source grower of lost flesh.
Vanilla separately decrements a destruction-response threshold for qualifying
player-caused destruction. The heart's installed threshold is randomly 125
through 200 cells. At zero, vanilla creates its threat-scaled fleshbeast
response, effects, sound, camera shake, assault lord, and letter, then resets
the threshold.

Normal zombies have the Zombieland faction, which vanilla does not currently
classify as player-caused destruction. V5 extends only that classification. It
does not add a counter or spawn fleshbeasts itself.

### Growth

The heart continues to own all growth points, cycles, mini-growth, tendrils,
thickening, organs, and independent fleshbeast births. A zombie-caused collapse
may regrow locally, grow elsewhere, remain absent, or be followed by another
vanilla output.

V5 guarantees no one-for-one conservation. That uncertainty is intentional.

## Final Player-Facing Behavior

Under settings that do not allow arbitrary building smashing, zombies gain no
new deliberate interaction with fleshmass.

Under `AnyBuilding`, an ordinary zombie that reaches the existing local-smash
decision may select a cardinally adjacent safe outer active-flesh cell. It does
not seek that cell from a distance. It does not know where the heart is except
for the safety rejection performed after a local candidate is found.

The zombie makes one normal attack. Several outcomes are possible:

- the attack misses or deals nonlethal damage;
- later attacks eventually kill the root and open a useful breach;
- the horde moves through the breach;
- the collapse removes an awkward or irrelevant edge;
- vanilla heart growth later reclaims or redirects the field; or
- accumulated destruction produces a fleshbeast counterattack.

The event is allowed to help the player, help the horde, help the heart later,
or accomplish little. V5 does not normalize those outcomes.

## Exact Integration Point

V5 does not add a new call to `JobDriver_Stumble.TickAction` or `RageMove`.

The implementation boundary is the existing local building selection:

1. Existing stumble logic decides whether to call `Smash`.
2. Existing settings, agitation, tracking, rage, special-zombie, and destination
   behavior decide whether that call may inspect buildings.
3. Existing `CanSmash` scans cardinally adjacent candidates in its randomized
   order.
4. When a candidate has the active-fleshmass definition, a V5 eligibility helper
   applies the live-source and protected-core rules.
5. An eligible cell remains a normal `CanSmash` result.
6. Existing `AttackThing` creates `AttackStatic` with one static attack and the
   normal expiry interval.

This is a candidate filter, not a parallel targeting system.

Current source suggests that targetable, damageable active flesh may already pass
the generic `AnyBuilding` filter. The prototype must prove that before adding a
new inclusion path. If it already qualifies, V5 is implemented by narrowing and
safeguarding that existing result, not by making a second way to select it.

Inactive `Fleshmass` is rejected by the V5 helper. The initial implementation
must also ensure that cascade-eligible inactive flesh cannot bypass the core
protection through the generic `AnyBuilding` branch. It may either be excluded
from deliberate zombie smash selection entirely or allowed only outside the
same protected area; the minimal recommendation is to exclude it because V5's
player-facing target is living active growth.

Likewise, heart-associated special organs must not slip through the generic
building branch. The candidate filter must recognize the fleshmass family before
ordinary `AnyBuilding` acceptance: safe `Fleshmass_Active` is the sole deliberate
V5 inclusion, while inactive flesh, nerve bundles, spitters, flesh sacks,
fleshbulbs, and other heart organs are excluded.

## Settings Contract

V5 adds no setting in the initial prototype.

| Existing setting or state | V5 behavior |
| --- | --- |
| `smashMode == Nothing` | No deliberate V5 target. |
| `smashMode == DoorsOnly` | No deliberate V5 target. Closed doors remain the only normal structure category. |
| `smashMode == AnyBuilding` | Allows an eligible active-fleshmass candidate through the existing local building scan. |
| `smashOnlyWhenAgitated == true` | The existing smash flow remains limited to its current agitated/tracking cases and existing special exceptions. V5 adds no bypass. |
| `smashOnlyWhenAgitated == false` | The existing flow may inspect nearby buildings without the agitation restriction; safe active flesh follows that same behavior. |
| `attackMode == OnlyColonists` | Existing `CanSmashBuilding` requires the building to belong to the player. Standard heart flesh therefore fails; V5 does not override the filter. |
| `attackMode == OnlyHumans` | Does not impose the colonist-building filter; `AnyBuilding` may select safe heart flesh. |
| `attackMode == Everything` | Same building result as `OnlyHumans`; creature targeting still differs elsewhere. |
| `ragingZombies` / `zombieRageLevel` | No direct V5 eligibility effect. Rage may still alter existing movement, smash opportunities, and warmup behavior. |
| Zombie eating settings | No effect. This is static building damage, not ingestion. |
| Anomaly targeting overrides | No effect on cell selection. They remain authoritative for pawn hostility after fleshbeasts appear. |

The initial V5 implementation must not change labels or help because it preserves
their current meaning. If a later product decision adds an opt-in, label and help
must be designed together across all active languages.

## Attacker Eligibility

A deliberate V5 attacker must:

- be alive, spawned, able to act, and on the target map;
- belong to the Zombieland zombie faction;
- be in the existing ordinary stumble/local-smash flow;
- reach the existing `CanSmash` building scan;
- satisfy the existing `AnyBuilding`, agitation, destination, and faction gates;
- have no higher-priority action that already returned from the stumble tick;
  and
- be able to use the existing one-attack static job.

V5 adds no rage requirement and no damage multiplier.

For the initial implementation:

- ordinary normal, miner, electrifier, healer, toxic-splasher, burning,
  former-pawn, and child variants may inherit V5 only if they reach the ordinary
  local scan and satisfy the same settings;
- albino and dark-slimer behavior remains unchanged;
- suicide bombers receive no deliberate active-flesh candidate;
- spitters and symbiants receive no new targeting behavior; and
- tanky zombies should be excluded from the initial V5 candidate until their
  route-parent smash path has its own focused test. Their existing building
  exception is not evidence that active-flesh cascade is safe.

Incidental damage from any Zombieland-faction instigator may still count toward
the response when it actually kills sourced flesh.

## Target Eligibility And Selection

A deliberate target must satisfy every condition when selected:

- it is spawned `Fleshmass_Active`;
- it is cardinally adjacent to the zombie;
- it is on the same map;
- it is ordinarily damageable;
- its `CompFleshmass.source` is a spawned `Building_FleshmassHeart` on that map;
- `smashMode` is exactly `AnyBuilding`;
- the existing building/faction filter permits it;
- the attacker is not an excluded special case; and
- it passes the protected-core check against every spawned heart on the map.

All other heart-associated flesh buildings are deliberate negative candidates,
even if their generic `ThingDef` flags would otherwise pass `AnyBuilding`.

If several buildings are adjacent, keep the existing randomized cardinal scan
and thing-list order. Do not prefer flesh over a door, wall, turret, or other
eligible building. Do not prefer low hit points, a large expected cascade, a
particular source, or the heart direction.

No reservation is added. Several zombies may focus the same cell.

## Protected Core And Interaction Access

The heart, nerve bundles, spitters, flesh sacks, and other special organs are
never V5 targets.

For each spawned 3-by-3 heart, define its conservative interaction perimeter as
all cells immediately adjacent to its occupied rectangle in eight directions,
including the four diagonal corner cells. This is the set from which installed
`PathEndMode.Touch` may permit the final interaction.

A deliberate root is safe only if its cardinal shortest-path distance to every
cell in that interaction perimeter is greater than the installed maximum of
eight additional cascade kills.

For the installed geometry this is equivalent to:

> Minimum Manhattan distance from the root cell to the heart's occupied
> rectangle must be at least 11.

Why:

- a cardinal touch cell is footprint distance 1;
- a diagonal corner-touch cell is footprint distance 2;
- the cascade can remove eight cells beyond the root;
- a root at footprint distance 10 can be eight cardinal steps from a diagonal
  touch cell; and
- a root at distance 11 cannot reach any cardinal or diagonal touch cell within
  eight additional kills.

Check every spawned heart because vanilla cascade does not enforce source
ownership when fields touch.

The number 11 is derived safety geometry, not a balance option. Re-audit it when
heart size, interaction path mode, corner-touch logic, cascade eligibility, or
maximum cascade count changes.

V5 does not intercept player attacks, fire, explosions, or third-party damage.
It guarantees only that a deliberate V5 root cannot create the final interaction
position within one vanilla cascade.

## Attack, Cascade, And Repetition

V5 uses existing `AttackThing` and `AttackStatic` behavior:

- one static attack per job;
- ordinary verb selection;
- ordinary hit chance and damage;
- ordinary warmup and cooldown, including existing Zombieland modifiers;
- ordinary interruption, target loss, death, downing, and despawn behavior; and
- no direct `Kill()`, hit-point rewrite, mining damage, or `PreventCascade` on
  the root.

After the job, the zombie returns to ordinary decisions. V5 adds no automatic
continuation flag. A later attack requires the existing smash flow to select a
candidate again.

When a root dies, vanilla owns the complete cascade. Movement, target selection,
and other actions may naturally win after the field changes.

The prototype intentionally has no aggregate root cooldown. It may not be
described as balanced or rare until the throughput TODO passes.

## Response Accounting

For every actually killed sourced cell:

1. Let vanilla perform its normal source-loss notification.
2. Let vanilla retain its existing null/player-faction response classification.
3. If the actual damage instigator belongs to the Zombieland faction and vanilla
   did not already count it, notify that cell's still-spawned source grower once
   through the existing destruction-response method.

Do not:

- count nonlethal hits;
- predict cascade children;
- award a fixed amount per root;
- double-count player or factionless kills;
- count a cancelled kill;
- create another response threshold;
- call the final fleshbeast response directly; or
- suppress vanilla effects or letters.

If a cascade crosses into another heart's field, each destroyed cell contributes
only to its own actual source.

## Save, Load, Concurrency, And Source Loss

V5 introduces no serialized gameplay state in its first implementation.

- Save/load during an attack uses the existing job behavior.
- Concurrent attackers use ordinary focus-fire sequencing.
- Only the actual killing hit produces the root cascade.
- Each actual destroyed cell passes through the response hook once.
- If the target or source disappears before selection, it is ineligible.
- If an already-issued hit lands on an existing cell, vanilla damage resolves.
- Custom response progress requires the source still to satisfy vanilla's
  spawned-source gate.

If a later throughput fix adds a per-heart cooldown, this section must be
rewritten before that implementation is accepted.

## Edge-Case Contract

| Situation | Required behavior |
| --- | --- |
| `Nothing` | No deliberate active-flesh target. |
| `DoorsOnly` | No deliberate active-flesh target. |
| `AnyBuilding`, ordinary eligible zombie | Safe cardinally adjacent active flesh participates in the existing randomized local scan. |
| Calm zombie with agitation restriction enabled | No new bypass; existing smash flow decides and normally rejects it. |
| Calm zombie with agitation restriction disabled | May attack safe active flesh if the existing `AnyBuilding` scan runs. |
| Raging zombie | No special flesh interest; any attack comes only from the ordinary local-smash path. |
| Valid creature attack, eating action, movement, or other earlier return | Existing action wins before V5 selection. |
| `OnlyColonists` | Neutral heart flesh is rejected by the existing building-faction rule. |
| `OnlyHumans` or `Everything` | Safe heart flesh may qualify under `AnyBuilding`. |
| Inactive `Fleshmass` | Not a V5 target and must not bypass core safety through the generic branch. |
| Active cell merely damaged | No cascade and no response progress. |
| Active root killed | Vanilla cascade runs; actual sourced deaths are counted once. |
| Candidate footprint distance 10 | Rejected because a straight cascade can reach a diagonal touch cell. |
| Candidate footprint distance 11 | Passes the geometric rule, subject to all other gates. |
| Several hearts | Candidate must pass distance 11 for every heart. |
| Cascade crosses touching fields | Vanilla removal remains; per-cell source accounting remains. |
| Several zombies attack one root | Ordinary damage sequencing; one root death and one cascade. |
| Suicide bomber, spitter, symbiant, or initial tanky case | No deliberate V5 target. Incidental damage remains possible. |
| Heart/source missing or despawned | Candidate rejected; no custom response progress. |
| Save/load during attack | Existing job persistence only; no V5 duplication state. |
| Fleshbeasts emerge | Existing Anomaly/Zombieland hostility settings decide pawn relationships. |

## Implementation Surface

The expected first prototype is narrow:

1. Add an active-fleshmass candidate helper used from the existing `CanSmash`
   building selection.
2. Require `AnyBuilding`, an eligible ordinary attacker, exact live heart
   source, and distance 11 from every spawned heart.
3. Exclude inactive cascade-eligible flesh and special heart organs from the
   generic building path; they must not bypass core protection or the intended
   active-cell-only scope.
4. Return the eligible cell through existing `CanSmash`; use existing
   `AttackThing` unchanged.
5. Add a narrow kill-notification postfix or equivalent hook that classifies
   actual Zombieland-faction sourced-flesh deaths for vanilla response progress
   exactly once.
6. Add prototype observation for root kills and total cascade deaths without
   adding a serialized limiter.

Do not modify `RageMove`, add a path planner, or add a map component in the first
prototype.

Before coding, record exact installed members and source-edit locations in the
patch audit. The names in this document are design anchors, not permission to
skip the normal static audit.

## Verification Plan

### Static And Decompiled Contracts

- Reconfirm the heart uses `PathEndMode.Touch` for final interaction.
- Reconfirm corner touch is allowed for the installed heart definition.
- Reconfirm heart size is 3-by-3.
- Reconfirm cascade is cardinal, has at most eight child kills, forwards the
  instigator, and uses `PreventCascade` for children.
- Reconfirm active and inactive flesh definitions and damageability.
- Reconfirm the existing `CanSmash` mode branches and `CanSmashBuilding` faction
  filter.
- Reconfirm `AttackThing` creates a one-static-attack job.
- Reconfirm vanilla response classification still excludes the Zombieland
  faction.

### Focused Runtime Contracts

1. **Settings:** Prove `Nothing` and `DoorsOnly` never select active flesh.
2. **Local scan:** Under `AnyBuilding`, prove a safe adjacent active cell can be
   selected without any target index, scent, or route attraction.
3. **Agitation:** Cover `smashOnlyWhenAgitated` on and off with wandering,
   tracking, and raging ordinary zombies.
4. **Attack modes:** Prove `OnlyColonists` rejects neutral heart flesh while
   `OnlyHumans` and `Everything` follow the documented building behavior.
5. **Damage:** Prove a nonlethal hit changes hit points but not cascade or
   response progress.
6. **Cascade:** Prove a killing hit uses the actual zombie instigator, vanilla
   child range, cardinal traversal, early exhaustion, and no recursive cascade.
7. **Response:** Prove every actual root and child death advances its source
   counter once and that a full threshold produces the unmodified response.
8. **Cardinal core:** Exercise the nearest side-touch chain around the protected
   heart.
9. **Diagonal core:** Put a valid pawn on each corner-touch cell; prove the heart
   interaction can begin there, distance-10 deliberate roots are rejected, and
   distance-11 roots cannot remove a corner-touch cell in one maximum cascade.
10. **Two hearts:** Exercise touching outer fields while both distance checks and
    per-cell source accounting remain correct.
11. **Concurrency:** Focus several zombies on one cell and prove one root death,
    one cascade, and no duplicate response credit.
12. **Save/load:** Save during the attack and prove no duplicate damage or
    accounting after load.
13. **Specials:** Prove excluded attackers do not deliberately select flesh and
    inherited ordinary variants receive no multiplier.

### Throughput Prototype Gate

Measure at least:

- one eligible zombie;
- a small front;
- a medium horde; and
- a dense front at the configured maximum practical local crowd.

For each, record successful roots and total cascade deaths per in-game hour with:

- agitation restriction enabled and disabled;
- no rage and active rage; and
- a thin tendril, broad field, and field that remains obstructive after one
  collapse.

The goal is not to force equal results. The review question is whether the field
is still an encounter after sustained contact or becomes routine automatic
cleanup. Resolve that product judgment in `TODO.md` before adding a limiter.

### Combined Scenario

The reusable final fixture should include:

- an analyzed 3-by-3 heart;
- marked cardinal and diagonal touch cells;
- distance-10 rejection and distance-11 eligible roots;
- thin and broad outer active-flesh shapes;
- a normal horde with natural tracking and rage opportunities;
- competing nearby buildings to exercise ordinary randomized selection;
- enough preloaded vanilla response progress to observe one birth;
- two touching outer fields beyond both protected areas; and
- save immediately before a killing hit.

The player-visible sequence should show local bashing, ordinary damage, a sudden
vanilla collapse, an unpredictable battlefield change, accumulated vanilla
response, existing hostility behavior, later heart growth, and clean
warning-or-higher logs.

## Acceptance Criteria

V5 is ready for implementation beyond a prototype only when:

- all repository documentation names V5 as current;
- `Nothing` and `DoorsOnly` never deliberately target flesh;
- no new rage-only, obstruction, scent, or global-target system exists;
- `AnyBuilding` uses the existing local scan and its existing player-facing
  settings;
- inactive flesh and special heart organs cannot bypass the V5 candidate filter;
- deliberate root distance is at least 11 from every heart footprint;
- both cardinal and diagonal final-interaction positions survive a maximum
  deliberate cascade;
- attacks use ordinary one-attack static jobs and actual instigators;
- nonlethal damage produces no cascade or response progress;
- each actual sourced death contributes at most once to the correct vanilla
  response counter;
- vanilla owns cascade, response, effects, defenders, and growth;
- multiple hearts, concurrent attackers, source loss, and save/load match this
  contract;
- the setting product decision is closed;
- the throughput prototype is reviewed before any limiter is added or omitted;
  and
- the combined scenario has no warning-or-higher regression logs.

## Explicitly Deferred Or Rejected

Do not add during the first prototype:

- a `DoorsOnly` exception;
- a new flesh toggle before the product decision;
- a rage requirement;
- route-blocker inference;
- a successful-root cooldown before measurement;
- a relocation queue or replacement reservation;
- custom growth points or pulses;
- a global flesh index, scent, or attraction;
- a dedicated chew job;
- custom damage, cascade, defenders, effects, or letters;
- special-zombie damage multipliers; or
- direct heart interaction or damage by zombies.

## Final Design Summary

V5 is one narrow extension to an existing candidate filter:

1. Existing zombie logic reaches its normal local building-smash scan.
2. Existing settings must say `AnyBuilding` and permit the attacker.
3. Safe sourced outer `Fleshmass_Active` may appear as a local candidate.
4. Existing `AttackStatic` performs one ordinary attack.
5. A killing hit triggers the complete vanilla cascade.
6. Actual Zombieland-caused deaths advance the existing vanilla response.
7. The distance-11 rule keeps cardinal and diagonal heart-interaction positions
   outside one maximum deliberate cascade.
8. Vanilla growth and defender systems continue in their own time.

Rage is no longer made to mean something about flesh. It remains merely one of
the existing conditions that can change how a zombie moves or attacks. The
fleshmass feature itself is local building smashing plus vanilla biological
consequences.

That is smaller, more implementable, and more faithful to both games' existing
rules than V4's invented blocked-rage subsystem.
