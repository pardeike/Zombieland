# Fleshmass Displacement Design: V3

## Status And Scope

This is the current design contract for a future Zombieland and Anomaly
interaction. It does not claim that the mechanic has been implemented or
live-tested. [`FLESHBEAST-v2.md`](FLESHBEAST-v2.md) is retained as the
superseded V2 snapshot, while [`FLESHBEAST-v1.md`](FLESHBEAST-v1.md) preserves
the original rationale and broader alternatives.

The intended fantasy remains unchanged: in the appropriate game mode, zombies
chew into outer active fleshmass; the mass withdraws; and equivalent active
fleshmass later appears elsewhere from the same heart. V3 narrows the contract
where V2 could have turned that displacement into rapid permanent cleanup:

- A chew reserves replacement capacity *before* it can retract a cell. A full
  queue rejects the chew; it never evicts an older unit or removes a new cell.
- Every committed relocation unit records the exact cell it retracted. That
  origin is excluded from its replacement and from all placements in the pulse
  that resolves it.
- A chew is a dedicated 240-tick action, not a local check that may retract a
  cell on every `JobDriver_Stumble` tick.
- V3 deliberately scopes anti-containment pressure to `Everything`. It does
  not claim to change the normal `OnlyHumans` default or create a hidden
  environmental-attack exception in that mode.

The feature remains pressure and map change, not an alternate heart-kill path.
The player still owns neural-lump analysis and the heart-overload objective.

## V3 Decisions At A Glance

| Concern | V3 contract | Result |
| --- | --- | --- |
| A full queue | Count pending units and in-flight reservations against one fixed capacity. If no slot exists, do not start or complete a chew. | Retraction cannot outrun replacement credit. |
| A blocked frontier | Keep a committed unit queued when its bounded placement search finds no legal destination. Do not expire, evict, or consume it merely because placement is currently impossible. | Blocked space stalls further chewing instead of becoming a deletion sink. |
| The chewed cell | Store `originCell` on every relocation unit and snapshot all unspent origins at each pulse. No placement in that pulse may use a snapshot origin. | A pulse cannot restore the breach it is resolving or swap another fresh breach closed. |
| Idle Stumble ticks | Start a single timed, interruptible chew action after ordinary pawn targeting declines. It completes once after 240 game ticks. | One zombie cannot retract one cell per tick. |
| Default attack mode | `OnlyHumans` remains unchanged and has no chewing. The environmental-pressure promise is explicitly an `Everything`-mode promise. | Players do not receive a surprise targeting-policy change. |

## Current Constraints

- `FleshmassHeart` is an Anomaly building, not a pawn. It must not be added to
  normal zombie attack targeting or treated as a hit-point combat target.
- In RimWorld 1.6, ordinary removal of `Fleshmass_Active` can run
  `CompCascadeOnDestroyed` and retract several connected cells. Normal damage
  or `Kill()` is not an acceptable representation of one chew.
- After neural-lump analysis, reaching the heart is enough to start the vanilla
  heart-destruction interaction. A zombie-cleared touch ring would be a
  repeatable bypass of the final encounter.
- `CompGrowsFleshmassTendrils` spends vanilla growth points every few ticks and
  can use them for outputs other than active fleshmass. It cannot provide an
  hourly, one-for-one relocation pulse.
- Vanilla contiguous-mass traversal follows adjacency without treating
  `CompFleshmass.source` as an ownership boundary. Touching fields from two
  hearts must remain separate in this feature.
- The normal `ZombieAttackTargetIndex` and attackability path are pawn-oriented.
  Adding every active-fleshmass cell to them would widen both targeting
  semantics and scan/pathing cost unnecessarily.
- The current standard zombie `ZombieBite` tool has a four-second cooldown.
  At 60 game ticks per second, V3 uses its 240-tick duration as the normal
  melee-equivalent chew cadence; special zombie types do not accelerate it.

## Design Goals And Non-Goals

### Goals

- In `Everything`, prevent a heart left outside from remaining a wholly static,
  harmless background object while zombies are actually beside its outer mass.
- Preserve the Anomaly objective: zombies never damage, overload, or create the
  final approach to the heart.
- Preserve a one-cell/one-unit model. Each completed retraction has exactly one
  live, owner-correct replacement credit until that credit succeeds or the
  heart itself ceases to exist.
- Keep the effect legible: successful replacements happen in visible hourly
  pulses instead of as a hidden per-tick trickle.
- Make constrained space a brake. A blocked field may hold bounded pending
  displacement, but it cannot accept unbounded new retractions or release an
  unlimited burst when space later opens.
- Leave ordinary fleshbeast hostility to the existing Anomaly and Zombieland
  settings. This feature controls active-fleshmass displacement only.

### Non-Goals For This Slice

- Changing the default `OnlyHumans` setting or treating non-pawn environment
  objects as human targets.
- Pulling zombies to a heart from elsewhere on the map with scent, a global
  index, or route attraction.
- Letting zombies attack the heart itself, spend vanilla growth points, or
  cause vanilla defenders to emerge as a replacement result.
- Wounded-cell state, directional growth bias, bonus pressure, defender
  spending, or special-zombie speed and yield modifiers.

## First Implementation Slice

Use one owner-scoped map component, provisionally
`FleshmassDisplacementMapComponent`. It holds one `HeartState` for each exact,
spawned `FleshmassHeart` on that map. The record is keyed to the heart's stable
thing identity and is discarded only when that heart is destroyed, despawned,
or no longer belongs to the map.

### Heart State

Each `HeartState` contains the following deliberately small state:

- `pendingUnits`: committed relocation units, each containing `originCell` and
  `enqueueTick`;
- `activeReservations`: temporary capacity-and-cell claims, each containing the
  heart identity, zombie identity, target/origin cell, start tick, and scheduled
  completion tick; and
- `nextPulseTick` plus minimal bookkeeping for the exact heart's active cells.

The capacity invariant is always:

```text
pendingUnits.Count + activeReservations.Count <= MaxRelocationSlots
```

`MaxRelocationSlots` starts at **48 per heart**. Reservations count because an
in-progress chew must not race another chew for the last replacement slot.

There is no separate wound list, pressure accumulator, vanilla growth-point
mutation, defender budget, or special-zombie multiplier. `originCell` is not a
wound system: it is the minimum identity required to keep a unit from restoring
the exact cell that created it.

Committed units are serialized with their `originCell` and `enqueueTick`.
Active reservations are deliberately not serialized. On post-load, clear every
reservation and cancel any matching chew action; no cell has been retracted
before completion, so this loses neither mass nor replacement credit. Also
clear a reservation immediately when its zombie, heart, map, target cell, or
timed action is no longer valid. A stale reservation is never promoted into a
relocation unit.

### Ownership, Eligible Cells, And The Protected Ring

An eligible chew target satisfies every condition below:

- It is a spawned `Fleshmass_Active` on the chewing zombie's current map.
- Its `CompFleshmass.source` is the exact `FleshmassHeart` for this state, not
  merely a contiguous or nearby heart-like object.
- It is on the reachable outer perimeter of that exact owner's field according
  to the local chew search.
- It is outside the protected heart-touch ring.
- It is not already claimed by another active reservation for that heart.

Vanilla contiguous-mass information may help identify likely perimeter cells or
expansion directions, but it is never the ownership authority. Every target,
retraction, and newly placed active cell checks the explicit source heart. This
remains true if two heart fields touch.

The protected ring is the complete one-cell ring around the heart's 3-by-3
footprint, including diagonal cells. It is permanently ineligible for chewing,
retraction, and placement fallbacks, both before and after neural-lump analysis.
Queue pressure, lack of growth space, and attack mode never relax this rule.

Consequently, zombies can displace an outer field but cannot clear the final
fleshmass barrier needed to touch the heart. A player who completes analysis
still has to handle the normal final encounter; zombie-created access cannot
start `CompDestroyHeart`.

## Attack-Mode Contract

Fleshmass is not added to `ZombieAttackTargetIndex`, `Tools.Attackable`, or the
normal global target-acquisition path. The local rule only considers an already
adjacent eligible perimeter cell after normal adjacent pawn targeting has had a
chance to win. It neither pulls zombies across the map nor replaces pawn
priorities.

The existing `attackMode` setting is authoritative:

| Attack mode | Local chewing | Expected player-facing outcome |
| --- | --- | --- |
| `OnlyColonists` | Disabled. | No new building or environment attack; a heart may remain passive. |
| `OnlyHumans` (current default) | Disabled. Fleshmass is not a human pawn. | No new building or environment attack; a heart may remain passive. |
| `Everything` | Enabled only for an adjacent eligible perimeter cell after normal pawn targeting declines and a relocation slot is reserved. | Local zombies can pressure and later displace the outer field; there is no heart scent or global targeting. |

This is an intentional scope decision, not an accidental feature absence.
V3's anti-containment goal applies only to `Everything` games. A later version
may introduce a separately named, opt-in environmental-pressure policy for
other modes, but it must default off, describe its exact mode interaction to
players, and receive its own complete settings-matrix contract. It must not be
silently smuggled into `OnlyHumans` or `OnlyColonists`.

Any eventual player-facing setting text or release description must therefore
say that fleshmass displacement is an `Everything`-mode interaction. It must
not imply that a default zombie horde will clear, kill, or normally target a
heart.

## Dedicated Chew: Reservation, Cadence, And Completion

The local hook belongs beside the normal `JobDriver_Stumble` attack decision,
after ordinary pawn targeting has declined. It starts a dedicated timed chew
job or equivalent timed state; it must not use `AttackMelee`, apply normal
building damage, or invoke the generic destruction path.

### Starting A Chew

`TryBeginFleshmassChew(zombie, heart, cell)` is an atomic reservation step. It
may start only when all of these remain true:

1. `attackMode == Everything`.
2. The normal adjacent pawn attack check produced no target for this decision.
3. The zombie is alive, spawned, idle in its normal stumble behavior, on the
   correct map, and adjacent to the selected cell.
4. The selected cell is eligible under the ownership, perimeter, ring, and
   cell-claim rules above.
5. After stale reservations are cleaned, the heart has capacity under the
   combined pending-unit and reservation invariant.

On success, claim the exact cell and one relocation slot, then start a
240-game-tick chew action. The reservation is only a promise of future
capacity; it removes no fleshmass and grants no queue unit. If capacity is full,
the method returns false and the zombie continues normal behavior without a
chew animation, a retraction, or eviction of an older unit.

The 240 ticks are fixed for the first slice: they are one standard four-second
zombie-bite interval. They use game ticks, so fast-forward changes wall-clock
time but not the in-game rate. V3 applies no special-zombie or body-type
modifier. A fresh, continuously eligible zombie can therefore complete at most
10 chews in the first 2,500-tick in-game hour; the Stumble loop must never turn
its repeated local checks into additional removals.

### Interruption And Final Revalidation

The chew action is interruptible. It cancels and releases its reservation, with
no retraction and no relocation unit, if any of the following occurs before
completion:

- the zombie dies, despawns, changes map, becomes unable to act, or leaves
  adjacency;
- the target cell despawns, changes def/source, ceases to be perimeter, enters
  the protected ring, or is claimed/removed by another action;
- the heart is gone or changes map;
- `attackMode` is no longer `Everything`; or
- ordinary pawn targeting takes priority, or the chew job is otherwise
  interrupted by normal zombie behavior.

At the scheduled completion tick, revalidate every start condition again,
including the still-live reservation. Only then invoke the dedicated,
Zombieland-owned one-cell retraction operation. The completion path has these
non-negotiable guarantees:

1. It uses the chewing zombie as the instigator/cause and never relies on a
   no-instigator `Kill()` that vanilla could classify as player-caused.
2. It suppresses or bypasses `CompCascadeOnDestroyed`. One completed chew
   retracts exactly the selected active cell and never the connected neighbours.
3. It performs the required connected-mass cleanup and notification exactly
   once. The implementation pass must identify that vanilla path with
   decompiler evidence rather than assume ordinary destruction is safe.
4. Only after the one-cell retraction succeeds, it atomically converts the
   reservation into one committed relocation unit whose `originCell` is the
   retracted cell. Failed validation or failed removal releases the reservation
   and creates no unit.

The final operation is intentionally not generic environmental sabotage. Its
one-cell accounting, capacity handoff, and interruption behavior are the core
of the mechanic.

## Independent Relocation Queue And Hourly Pulse

Pending units are Zombieland-owned state. They are never written into vanilla
`growthPoints`, and the pulse never calls or overrides `CompFleshmassHeart.Grow()`
as its spending mechanism. Vanilla terrain/connectivity rules may inform the
candidate search, but every success explicitly places one `Fleshmass_Active`
with the tracked heart recorded as its source.

The pulse cadence starts at one in-game hour (`GenDate.TicksPerHour`, currently
2,500 game ticks). For each heart, a pulse may examine at most **12** committed
units and may place at most **12** active cells. Each examined unit receives a
hard-bounded candidate search, initially no more than **64** candidate checks.
Those limits bound both work and later release rate.

For a pulse:

1. Discard state only for a missing/despawned/wrong-map heart. Do not discard a
   valid heart's pending units because they are old or currently unplaceable.
2. Snapshot the `originCell` values of all pending units before resolving any
   unit. These cells are excluded for the entire pulse.
3. Examine pending units in enqueue order, within the 12-unit work limit. For
   each one, look for a valid candidate under the placement rules below.
4. On success, place exactly one active cell, consume that one unit, and leave
   the snapshot exclusion in force until the pulse ends.
5. If its bounded search finds no legal candidate, retain the unit unchanged.
   It continues to occupy capacity and can be retried in a later hourly pulse.

There is deliberately **no** queue expiry, failed-search consumption, or
oldest-unit eviction in V3. The V2 version of those policies could make a horde
delete more cells than it retained replacement credit for. Here, no-space means
the queue eventually fills and new chews are refused. It is a bounded stalled
displacement state, not a free cleanup path.

The 48-slot cap and 12-successes-per-hour pulse cap still prevent a historical
debt burst: at most 48 committed replacements can wait, and at most 12 can
appear in any one pulse. If an opening remains blocked, they wait rather than
vanish; if it opens, they return visibly over bounded pulses.

## Placement Rules And Origin Exclusion

The pulse uses a Zombieland-side candidate-selection routine. It may use current
vanilla tendril/core rules as a source of terrain and connectivity constraints,
but it must never:

- spend or add vanilla `growthPoints`;
- call a method that may choose a nerve bundle, spitter, fleshbeast, or any
  other normal heart output;
- join fields or transfer a unit across a different heart's source boundary;
- place an isolated cell;
- place a protected-ring cell as a fallback; or
- place on an origin included in the pulse's snapshot.

A candidate is valid only when it is legal for active fleshmass, connected to
the exact source heart's current field, unoccupied in a compatible way, and
outside the protected ring and pulse-origin snapshot. The explicit
`CompFleshmass.source` check is required both before choosing the candidate and
after the new active cell is placed.

The origin rule has two layers:

- A relocation unit may never place on its own `originCell` while it is
  pending.
- No unit resolved in one pulse may place on *any* origin that was pending when
  that pulse began, even if another unit has already succeeded earlier in that
  pulse.

This is intentionally narrower than V1's general wound memory. It prevents an
immediate same-cell flicker and cross-unit breach swapping without introducing
long-lived radius, weighting, or directional state. Once a unit is consumed,
its cell becomes eligible again only in a later pulse, subject to every normal
placement rule and any still-pending origins.

## Player-Facing Outcome

- In `Everything`, a zombie already beside an outer, chewable perimeter cell
  may commit to one visible-duration chew only when replacement capacity exists
  and no adjacent pawn target takes priority. The mass can later reappear as an
  active cell elsewhere from the same heart.
- In `OnlyHumans` and `OnlyColonists`, the mechanic intentionally introduces no
  new building or environment attack. An outdoor heart may remain passive; this
  is the chosen behavior for the first slice, not a hidden defect.
- Zombies cannot attack the heart or chew the protected ring. They never create
  a final-interaction route, before or after neural-lump analysis.
- If the frontier has no legal alternate cell, chewing stops once the bounded
  relocation budget is reserved. A horde cannot continue removing the field
  while replacement is impossible.
- Normal Anomaly heart behavior still controls its own defenders, and existing
  Zombieland/Anomaly hostility settings still control how those pawns and
  zombies treat one another.

## Implementation Boundaries

- Keep the map component owner-scoped. Do not attach generic pressure to a
  combined contiguous-fleshmass collection.
- Keep normal pawn attack selection intact. Local chewing is a separate,
  timed check after normal pawn targeting, not a widening of `Tools.Attackable`.
- Do not begin the timed chew by giving `AttackMelee` or `AttackStatic` a
  `Fleshmass_Active` target. That would reintroduce normal damage/cascade risk.
- Ensure a cell claim and reservation release occur on every job completion,
  interruption, map cleanup, and post-load path.
- Save/load committed units, origins, timestamps, pulse timing, and heart
  identity. On load, validate every record against a spawned heart on its map;
  clear in-flight reservations rather than attempting to resume ambiguous
  half-chews.
- Keep temporary diagnostics out of the feature. A reusable runtime contract
  may expose queue/reservation/origin state, but it must clean up all spawned
  hearts, cells, zombies, and records.

## Verification Plan

### Static And Decompiler Pass

- Confirm the current 1.6 `Fleshmass_Active` destruction and cascade path, then
  identify the exact safe single-cell removal and connected-mass notification
  path with a zombie instigator.
- Confirm the heart's 3-by-3 footprint, the complete adjacent touch ring, and
  `CompDestroyHeart`'s analysis/access behavior.
- Confirm how active cells record `CompFleshmass.source` and how an explicit
  newly spawned active cell is associated with one exact heart.
- Confirm the normal growth-point cadence and all possible heart outputs so the
  implementation cannot accidentally reuse them.
- Confirm the local `JobDriver_Stumble` attack order, `ZombieBite` cooldown,
  and attack-mode behavior before placing the timed-chew hook.

### Focused Runtime Contracts

1. **Single-chew cadence and accounting.** Start one eligible normal zombie
   beside one outer cell. Prove zero retractions before tick 240, exactly one
   selected cell at completion, no neighbour cascade, one source-correct unit,
   and no second completion without another full 240-tick action. Advance a
   fresh, continuously eligible zombie through one hour and prove at most 10
   completed chews.
2. **Chew interruption.** Interrupt the action by moving/downing the zombie,
   removing or changing the target, changing the attack mode, and placing an
   ordinary adjacent pawn target. Each case must release its reservation with
   no retraction and no unit.
3. **Capacity-before-retraction.** Fill all 48 slots with committed units and
   reservations, then stage more eligible zombies. Prove no additional chew
   starts or completes, no old unit is evicted, and no additional active cell
   disappears. After one successful pulse placement frees one slot, prove only
   a later new chew may use that one slot.
4. **Origin exclusion.** Chew an outer cell, then make its origin the only
   otherwise valid candidate. Prove the unit remains queued and the origin is
   not restored. Add a different valid candidate and prove the replacement
   appears there. Repeat with two units to prove one pulse cannot use either
   snapshot origin for the other unit.
5. **Source ownership.** Stage two hearts whose fields touch. Chew one cell
   from each and prove each reservation, unit, and later replacement belongs
   only to its exact source heart.
6. **Heart barrier.** Complete neural-lump analysis, put zombies around every
   protected-ring cell, and prove no chew removes the ring or makes
   `CompDestroyHeart` interactable through zombie work.
7. **Attack modes.** Run identical adjacent-zombie fixtures under
   `OnlyColonists`, `OnlyHumans`, and `Everything`. Prove the first two do
   nothing, including the default `OnlyHumans` case; prove `Everything` allows
   only the local timed action; and prove no mode creates a global fleshmass
   target index or scent.
8. **Blocked-frontier pulse.** Fill a valid heart's queue while alternate
   placement is impossible, advance several hours, then open space. Prove
   pending units neither expire nor disappear while blocked, the queue blocks
   additional retractions at capacity, and later placement never exceeds 12
   cells in one pulse.
9. **Pulse isolation.** Queue units and advance an hour. Prove every success is
   one explicit active cell, never a vanilla defender or other heart output,
   and the feature never changed vanilla `growthPoints`.
10. **Save/load.** Save during a timed chew and prove reload cancels it without
    loss or credit. Separately save before a pulse and prove committed origins,
    queue capacity, source ownership, and per-pulse limits survive without
    duplication.

### Combined Scenario Fixture

Use one reusable map fixture containing two nearby heart fields, a protected
analyzed heart, an outer chewable perimeter, an origin-only constrained edge, a
separately valid expansion edge, a queue-capacity setup, and controlled zombies.
The fixture should run the mode matrix, timed chew, hourly pulse, save/load, and
log summary in one sequence. It is the regression surface for coexistence, not
a set of permanent one-off bridge tools.

## Acceptance Criteria For The First Slice

The slice is ready to expand only when all of the following are true in the
current RimWorld build:

- no completed chew removes more or fewer than one active cell;
- no one-tick local check can cause repeated removal; one normal zombie obeys
  the 240-tick chew duration and fresh-hour maximum;
- a retraction cannot occur without a previously reserved relocation slot;
- a full queue never evicts, expires, or consumes an unspent unit and never
  permits another cell to disappear;
- each successful retraction creates one source-correct unit with its exact
  origin persisted;
- no pulse restores a pending origin, including another pending unit's origin
  in the same pulse;
- a blocked placement retains the unit and bounds later release to the
  48-slot/12-per-pulse budget;
- no ordinary destruction cascade or player-caused heart response is triggered
  by the chew path;
- no relocation uses vanilla `growthPoints` or produces defenders;
- the heart-touch ring is never a zombie-chew or fallback-placement target;
- `OnlyColonists` and the default `OnlyHumans` retain their non-environmental
  attack contract, while `Everything` has only the local timed-chew rule; and
- the two-touching-hearts, capacity, origin, and save/load combined fixture is
  log-clean.

Only after this proof should the project revisit a separately opt-in
environmental policy, wound memory, directional bias, defender responses,
special zombies, or attraction behavior.
