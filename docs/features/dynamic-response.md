# Dynamic Group Response To Zombies

## Status

- **State:** Design ready for implementation; no runtime behavior has changed yet.
- **Scope:** Non-player faction pawns, with separate policies for friendly and enemy groups; Adaptive evaluation requires a RimWorld `Lord`.
- **Primary goal:** Let a provoked group use ranged weapons against a manageable local zombie threat, while keeping a cautious melee-only response when the group lacks the strength to survive the extra engagement.
- **Evidence boundary:** This document owns the intended behavior and implementation contract. Static patch evidence belongs in `TEST_PATCH_AUDIT.md`; runtime results belong in `TEST_COVERAGE.md` and `TEST_SCENARIOS.md`.

## Decision Summary

Add two independent three-state settings:

| Relationship to the player | Default | Purpose |
| --- | --- | --- |
| Friendly or neutral group | `Adaptive` | Visitors normally avoid deliberate combat, but a capable group answers a small zombie attack with normal ranged combat. |
| Enemy group, including raiders | `Full` | Preserve the existing default in which enemies try their best to fight zombies; players may opt into `Adaptive` or `Minimal`. |

Both `Adaptive` policies call one shared, lazily evaluated Lord-level function. The function asks four coarse questions:

1. Does the pawn belong to a Lord?
2. Has a nearby Lord member recently taken a zombie melee hit?
3. How many nearby Lord members are currently capable ranged shooters?
4. How many relevant zombies are near the harmed member?

The answer is cached as `(Minimal or Full, evaluation tick)` per Lord for 120 ticks. No attack hooks, downed hooks, equipment hooks, timers, saved state, or custom group state machine are required.

The initial heuristic is:

```text
confidence = capableShooters * 2 / max(1, zombiePressure)

enter Full when confidence >= 1.25
remain Full while confidence >= 0.85
otherwise use Minimal
```

Ordinary relevant zombies contribute `1` pressure; tanky zombies contribute `3`. The evaluator scans a fixed 16-cell danger radius and only runs once per 120 ticks for an actively queried Lord.

## Player-Facing Behavior

### Response policies

`Minimal`, `Adaptive`, and `Full` describe whether the group deliberately treats zombies as combat targets. They do not directly assign jobs or force weapon use.

| Policy | Hostility toward eligible zombies | Expected behavior |
| --- | --- | --- |
| `Minimal` | Off | The group ignores zombies as deliberate targets. A pawn attacked in close melee may still use RimWorld's ordinary immediate melee-threat reaction, normally a limited melee response. |
| `Adaptive` | Decided per Lord | The group begins in `Minimal`. After a recent zombie hit, it switches to `Full` only when its nearby capable shooters are strong enough for the nearby zombie pressure. |
| `Full` | On | The group continuously treats eligible zombies as hostile and lets normal RimWorld combat AI choose ranged or melee jobs, positions, and targets. |

“Full” does not mean “always shoot.” Existing weapon availability, line-of-sight, target validity, electric-zombie compatibility, attack-mode category gates, and vanilla combat decisions still apply. “Minimal” likewise does not install a custom melee-only job; it leaves the existing close-melee reaction intact by keeping zombies out of deliberate hostility and target selection.

### Relationship split

The friendly and enemy policies are deliberately independent:

- **Friendly** means a non-player, non-animal pawn whose faction is not hostile to the player. Allied and neutral visitors share this setting.
- **Enemy** means a non-animal pawn whose faction is hostile to the player. Raiders are the main case.
- Player pawns keep their existing behavior.
- Animals keep the existing `animalsAttackZombies` behavior.
- Anomaly-specific hostility overrides remain authoritative and are evaluated before this policy.
- Factionless pawns and unsupported callers resolve to `Minimal`. A Lord-less pawn also resolves to `Minimal` when its selected policy is `Adaptive`; a fixed `Full` enemy policy continues to work without a Lord so the existing enemy behavior is preserved.

The shared evaluator does not merge the two settings. A friendly Lord can be `Adaptive` while an enemy Lord is `Full`, or vice versa. Only a relationship whose setting is `Adaptive` pays the evaluation cost.

### Interaction with “What do zombies attack?”

The existing `AttackMode` remains a category gate. The new response policy answers whether an otherwise eligible outside group fights zombies; it does not broaden which pawn categories participate.

For example:

- A human friendly under `OnlyHumans` can use its friendly response policy.
- A human enemy under `OnlyColonists` remains excluded by the existing category rules even if the enemy response policy is `Full`.
- A friendly or enemy mechanoid remains subject to the existing `Everything` requirement.

This ordering preserves existing attack-category semantics while replacing the coarse friendly/enemy combat-response choice.

## Current Behavior To Preserve

The design relies on existing RimWorld and Zombieland behavior instead of recreating it.

### Minimal response is already supplied by RimWorld

RimWorld's `Verb_MeleeAttack.TryCastShot` records the attacker as the target pawn's melee threat. `JobGiver_ReactToCloseMeleeThreat` can then issue a short `AttackMelee` reaction. This works even when the factions are not generally hostile, which is why friendly visitors currently defend themselves in melee without committing the group to ranged combat.

The feature must not replace that reaction, assign group combat jobs, or mutate faction relations.

### Full response already has a ranged targeting path

Zombieland's `AttackTargetFinder.BestAttackTarget` postfix contains a friendly ranged fallback. It is currently ineffective for zombies while `HostileTo` returns false. Once an eligible pawn resolves to `Full`, the existing vanilla target search, Zombieland target filters, and ranged fallback should perform the combat work.

The implementation must verify that this path reaches a zombie beyond the normal nine-cell friendly candidate filter but within weapon range. The initial implementation should not relax the nine-cell filter: the ordinary candidate path covers nearby zombies, and the existing postfix is intended to cover the longer-range fallback. Change the distance filter only if runtime evidence proves that combined path is still capped.

### Existing downstream target rules remain authoritative

Do not duplicate these rules in the group-confidence policy:

- Electric zombies are already removed per verb when the weapon cannot harm them.
- Suicide bombers and tanky zombies already receive target-scoring priority.
- Downed, roped, confused, emerging, albino, destroyed, and dead zombies already have specialized target handling.
- Weapon range, line-of-sight, reachability, reservation, and vanilla job selection remain downstream decisions.

The policy estimates whether committing the group is sensible; it does not choose the exact target or weapon.

## Runtime Model

### Types

Use two concepts with distinct names:

```csharp
enum ZombieResponsePolicy
{
    Minimal,
    Adaptive,
    Full
}

enum GroupResponseMode
{
    Minimal,
    Full
}
```

`ZombieResponsePolicy` is persisted in settings. `GroupResponseMode` is the transient result returned to hostility and targeting callers. The runtime result is deliberately binary: `Adaptive` is a way to choose a current mode, not a third combat state.

The final names may follow local naming conventions, but code must preserve this distinction.

### Public resolver

The main entry point should express both policy selection and evaluation:

```text
ModeFor(pawn):
    if pawn is null, unspawned, map-less, factionless, player-controlled,
       animal, or covered by a separate hard override:
        return Minimal or defer to the existing owner

    policy = friendlyZombieResponse or enemyZombieResponse
             based on the pawn faction's relation to the player

    if policy == Minimal:
        return Minimal
    if policy == Full:
        return Full

    return AdaptiveModeFor(pawn)
```

Call sites that already handle player pawns, animals, or Anomaly overrides may call the adaptive resolver after those branches rather than duplicating every guard. There must nevertheless be one canonical relationship-to-policy helper so targeting patches do not drift apart.

### Adaptive resolver

```text
AdaptiveModeFor(pawn):
    lord = pawn.GetLord()
    if lord is null:
        return Minimal

    if cache[lord] exists and now - cache.evaluatedAtTick < 120:
        return cache.mode

    previous = cache[lord].mode if present, otherwise Minimal

    anchor = most recently harmed eligible member of lord.ownedPawns where:
        member is spawned on pawn.Map
        member.mindState.meleeThreat is Zombie
        now - member.mindState.lastHarmTick < 600

    if no anchor exists:
        cache(lord, Minimal, now)
        return Minimal

    shooters = count capable shooters in lord.ownedPawns
               within 24 cells of anchor
    if shooters == 0:
        cache(lord, Minimal, now)
        return Minimal

    pressure = weighted relevant zombies from map TickManager.allZombiesCached
               within 16 cells of anchor

    confidence = shooters * 2 / max(1, pressure)
    threshold = previous == Full ? 0.85 : 1.25
    mode = confidence >= threshold ? Full : Minimal

    cache(lord, mode, now)
    return mode
```

All early `Minimal` outcomes should be cached. The implementation should use simple loops and squared distances in this hot path, avoiding LINQ allocations and square roots.

### Why the harmed member is the anchor

The cache is per Lord, so its result must not depend on which member happened to call `HostileTo` first. The most recently harmed qualifying member supplies a deterministic local center for both support and zombie pressure.

This also answers the gameplay question correctly: “Can the nearby part of this group protect the member currently under attack?” It avoids counting a rifle carried by a group member on the other side of the map or zombies unrelated to the current contact.

The resulting mode applies to the Lord as a group until the next evaluation. Very dispersed Lords are an accepted v1 approximation; visiting groups and raids are normally cohesive, while downstream target and weapon checks still constrain what distant members can actually do. A per-member mode or cluster fallback should only be added after a reproducible split-group failure.

## Provocation

### Source signal

Use `Pawn_MindState.lastHarmTick`; do not add Harmony damage or attack hooks.

RimWorld maintains this tick when a pawn receives external violence. A recent tick therefore provides the landed-hit signal that the feature needs and naturally expires without a custom timer. Missed melee swings do not update it.

### Zombie provenance guard

`lastHarmTick` is not zombie-specific: gunfire, another melee attacker, and other external violence can update it. The adaptive check must therefore also require:

```text
member.mindState.meleeThreat is Zombie
```

RimWorld writes `meleeThreat` when the zombie attempts the close-melee attack. Combining the two existing fields cheaply approximates “this member recently suffered a zombie melee hit” without event state.

Do not require `lastMeleeThreatHarmTick` to equal `lastHarmTick`. The melee-threat tick changes on later missed swings; equality would cause a valid 600-tick provocation to disappear after the next miss. Do not call `MeleeThreatStillThreat` either, because its shorter lifetime and current-threat conditions would undermine the intended 600-tick policy window.

This read-only provenance guard has accepted limits:

- A zombie's ranged or unusual damage that does not establish `meleeThreat` may not provoke the group.
- If a different melee threat overwrites `meleeThreat`, a prior zombie hit may age out early.
- If unrelated violent damage lands while the pawn's recorded melee threat is still a zombie, it may refresh the apparent provocation.

These cases are preferable to custom damage-event plumbing in v1. Add explicit provenance state only if runtime evidence shows a common player-visible failure.

### Timing

The provocation window is 600 game ticks, approximately ten seconds at normal simulation speed. Because evaluation is cached for 120 ticks, observed transitions are bounded rather than exact:

- A new landed hit may take up to 120 ticks to switch a previously cached `Minimal` Lord.
- A group may remain `Full` for up to roughly 120 ticks after the 600-tick signal expires.

This is an intentional performance tradeoff. If two seconds of worst-case response latency feels visibly wrong, reduce the cache interval before adding hooks or immediate-recalculation events.

## Capable Shooter Count

Use a headcount, not DPS simulation. A Lord member contributes one shooter when all of these are true:

- Spawned, alive, and not downed.
- On the same map as the anchor and within 24 cells of it.
- Not in a mental state.
- Not contained.
- Capable of violent work.
- Summary health is greater than 25 percent.
- Moving capacity is at least 25 percent.
- Has a primary equipment verb that is ranged and currently usable.

This is a `Tools.ColonistsInfo`-style readiness predicate, not a literal call to `ColonistsInfo`: the existing method supplies the health, movement, containment, mental-state, and life-state pattern, but it does not currently require violent capability or a ranged primary.

Every qualifying shooter has equal weight in v1. Do not add combat power, accuracy, body-part readiness curves, per-weapon DPS, or ammo assumptions. The feature needs a coarse boundary such as “three rifles versus two zombies” rather than a combat simulator.

`Tools.DPS` remains a possible later refinement if a concrete playtest shows that headcount repeatedly misclassifies extreme weapons. Any such change should be one bounded multiplier, not a replacement scoring system.

The zero-shooter result is a hard `Minimal` gate even when no relevant zombie remains in the scan. A group without a usable ranged weapon must never enter `Full` through the confidence formula.

## Zombie Pressure

### Source and radius

Use the current map's `TickManager.allZombiesCached`. If the component is absent, not runtime-ready, or has no usable cache, resolve to `Minimal`.

Scan a fixed 16-cell radius around the provoked anchor using squared distance. This radius deliberately covers the existing nine-cell friendly engagement area plus a margin for zombies likely to join the contact. It avoids iterating weapon verbs or reproducing gunshot-attraction calculations merely to adjust the scan by a few cells.

Do not use `PheromoneGrid.GetZombieCount` for this decision. The object cache allows the evaluator to exclude states that should not consume group confidence and to recognize tanky zombies in the same cheap loop.

### Relevant zombies

A zombie contributes pressure only when it is:

- Non-null, spawned, and on the anchor's map.
- Not destroyed or dead.
- Not downed.
- Not emerging.
- Not roped or confused.
- Not albino.
- Within 16 cells of the anchor.

Weights:

```text
ordinary relevant zombie = 1
tanky zombie             = 3
```

No other special-zombie corrections belong in the policy. Electric compatibility and suicide-bomber priority are already handled by the actual weapon/target path. If a `Full` group cannot harm a particular electric zombie, the target filter rejects it per verb; the policy need not predict every member's exact target list.

### No-zombie case

Use `max(1, pressure)` in the denominator. A recently provoked, armed group may therefore remain `Full` briefly after the immediate zombie dies or leaves the 16-cell area. That lets normal targeting finish the local contact and avoids a special zero-count branch. The provocation window and cache age return the group to `Minimal` automatically.

## Confidence And Hysteresis

Constants:

| Name | Initial value | Meaning |
| --- | ---: | --- |
| Cache lifetime | 120 ticks | Maximum age of a Lord result before lazy recomputation. |
| Provocation lifetime | 600 ticks | Recent-harm window. |
| Support radius | 24 cells | Maximum distance from harmed member for a shooter to contribute. |
| Zombie danger radius | 16 cells | Pressure scan radius around harmed member. |
| Zombies per shooter | 2 | Coarse group capacity. |
| Enter threshold | 1.25 | Confidence needed to change from `Minimal` to `Full`. |
| Stay threshold | 0.85 | Confidence needed to remain `Full`. |
| Tanky weight | 3 | Pressure contributed by one tanky zombie. |

The 25-percent health and movement gates are shared readiness rules, not tunable group-confidence coefficients.

Examples:

| Nearby shooters | Nearby pressure | Confidence | New/previously Minimal | Previously Full |
| ---: | ---: | ---: | --- | --- |
| 0 | 1 | hard gate | `Minimal` | `Minimal` |
| 1 | 1 normal | 2.00 | `Full` | `Full` |
| 1 | 1 tanky (weight 3) | 0.67 | `Minimal` | `Minimal` |
| 3 | 2 normal | 3.00 | `Full` | `Full` |
| 3 | 4 normal | 1.50 | `Full` | `Full` |
| 3 | 5 normal | 1.20 | `Minimal` | `Full` |
| 5 | 12 normal | 0.83 | `Minimal` | `Minimal` |

The asymmetric thresholds give a useful dead band at no additional state cost: the previous cached mode is already available. A group does not oscillate every evaluation when one zombie crosses the radius boundary.

The `1` capable shooter versus `1` ordinary zombie result is a deliberate v1 choice: the confidence is `2.0`, so a healthy armed visitor who has already suffered a zombie hit enters `Full`. This favors decisive self-defense over the earlier intuition that one visitor with a pistol should remain melee-only against one zombie. The readiness gates, tanky weight, and provocation requirement still keep an incapable, impaired, unarmed, or disproportionately threatened visitor in `Minimal`. Treat this as an explicit playtest decision; do not silently change it by adding weapon-quality scoring.

## Cache And Performance Contract

### Cache shape

Keep one transient dictionary entry per evaluated Lord:

```text
Lord -> { GroupResponseMode mode, int evaluatedAtTick }
```

No state is serialized. Replace or clear the dictionary at the same map-owned reset boundaries used by `TargetCachePatches` and `Tools.ResetMapOwnedState`, including map shutdown/load transitions and return to entry.

The cache is main-thread only. All intended consumers—hostility, target selection, flee checks, and test diagnostics—currently run on RimWorld's main thread. Do not call `ModeFor` from `ZombieAvoider` or any future worker-thread path without first adding an explicit synchronization or main-thread handoff design.

Do not add:

- Attack-attempt or damage Harmony hooks.
- Member-downed, weapon-lost, or pawn-spawn event handlers.
- A minimum hold timer separate from hysteresis.
- A response-expiry timer.
- Periodic background updates.
- Faction clustering for Lord-less pawns.

`ModeFor` is already called through hostility and targeting gates, so the cache is naturally demand-driven. Settings fixed to `Minimal` or `Full` return before the Lord lookup and scan.

### Complexity

For each active Adaptive Lord, at most once per 120 ticks:

- Loop over its usually small `ownedPawns` list to find an anchor and shooters.
- Loop over `allZombiesCached` for one squared-distance and state check per zombie.

With 2,000 cached zombies this is about 16.7 zombie checks per game tick per actively queried Lord when amortized across the cache interval. That is simpler and more predictable than cell-circle enumeration, weapon inspection, or a custom event graph. Runtime benchmarking must still cover multiple simultaneous Lords, but profiling evidence—not speculative complexity—should decide whether later optimization is needed.

Ended Lords may leave a small transient entry until the next map reset. Do not add pruning machinery unless profiling or a long-running-map fixture shows material retention.

## Integration Contract

### New policy owner

Add one focused source file, expected to be roughly 80–120 lines before tests and comments, containing:

- Relationship-to-policy resolution.
- Lord cache entry and reset.
- Adaptive evaluator.
- Capable-shooter predicate.
- Relevant-zombie pressure loop.
- The constants in the table above.

The exact class name is implementation-local; `GroupZombieResponse` is a suitable working name.

### `Tools.IsHostileToZombies`

After preserving the existing Anomaly and animal branches:

- Friendly faction pawn: return whether the friendly policy resolves to `Full`.
- Enemy faction pawn: return whether the enemy policy resolves to `Full`.
- Factionless or Lord-less Adaptive pawn: return false.

This is the principal hostility gate. Player pawns are already excluded by the Harmony callers and must remain unaffected.

### Target-list filtering

In `AttackTargetFinder.GetAvailableShootingTargetsByScore`:

- Preserve the current player, animal, race-category, Anomaly, harmless, roped, confused, distance, melee-distance, spitter, and electric rules.
- For a friendly or enemy pawn, derive `attacksZombies` from its relationship-specific policy result.
- If `attacksZombies` is false, remove zombie candidates.
- If true, apply the existing eligible target filters for that relationship and pawn category.

Do not continue reading the legacy enemy boolean in this path. In particular, a friendly `Adaptive` result must not accidentally depend on the enemy setting.

### `AttackTargetFinder.BestAttackTarget` validator

The current prefix contains a comment that friendlies are handled by the postfix, but friendly pawns fall through into the enemy validator. That validator rejects zombies whenever `enemiesAttackZombies` is false. Therefore changing only `Tools.IsHostileToZombies` is insufficient.

Split the validator explicitly:

- Friendly candidates use the friendly policy result and existing friendly restrictions.
- Enemy candidates use the enemy policy result and existing enemy restrictions.
- Both continue to call the original validator.
- Symbiant and spitter special cases remain unchanged.

Once that cross-coupling is removed, the existing friendly ranged postfix should be allowed to perform its weapon-range fallback. Verify before changing its distance behavior.

### Other consumers of the legacy enemy boolean

Every non-test read of `enemiesAttackZombies` must be classified during implementation rather than mechanically replaced:

| Consumer | Required treatment |
| --- | --- |
| `Tools.SeesZombieAsThreat` / flee filtering | Preserve existing friendly avoidance semantics. For enemy pawns, map fixed policies directly and use the cached mode for `Adaptive`. |
| `GenHostility.IsActiveThreatTo(IAttackTarget, Faction, ...)` | This answers whether a zombie threatens the faction for Lord behavior, exit decisions, and related systems; it does not answer whether one group currently fights back. It has no pawn/Lord context and must never call the Adaptive evaluator. Apply the explicit precedence described below. |
| Albino pressure-source eligibility in `JobDriver_Sabotage` | For a concrete friendly/enemy pawn, use its response mode where the old enemy boolean controlled combat eligibility. Preserve the early “already attacking or approaching” rule and all category gates. |
| Settings-dialog Anomaly “automatic” detail | Show `Allow` for `Full`, `Never` for `Minimal`, and `Mixed` for `Adaptive`, combined with the animal setting as today. This is explanatory UI only and must not override Anomaly policy. |
| Bridge contracts and fixtures | Update them to describe both policies and retain explicit backward-compatibility cases. |

This audit prevents the new setting from producing contradictory combat, flee, storyteller-threat, or albino-pressure behavior.

#### Faction-level active-threat precedence

`GenHostility.IsActiveThreatTo(zombie, faction, ...)` must preserve the distinction between danger and response:

1. Keep the existing null-faction, player-faction, Anomaly-override, and non-Zombieland-target branches unchanged.
2. For a friendly faction, ignore `friendlyZombieResponse` and return the existing `AttackMode`-derived result. A visitor faction may correctly regard zombies as an active threat even while its combat response policy is `Minimal`.
3. For an enemy faction with `enemyZombieResponse == Minimal`, return false. This preserves the old `enemiesAttackZombies == false` behavior exactly.
4. For an enemy faction with `enemyZombieResponse == Full`, pass through to the existing `AttackMode` switch.
5. For an enemy faction with `enemyZombieResponse == Adaptive`, also pass through to the existing `AttackMode` switch. This is a deliberate v1 choice: the faction recognizes real zombie danger even when a particular Lord's current response mode is `Minimal`, and the pawn-level hostility/targeting paths still decide whether that Lord fights back.

Thus the response enum gates the enemy faction-level result only for explicit `Minimal`; it never attempts to synthesize a Lord mode from a faction-only query.

### Reset integration

Clear the Lord cache through the existing centralized map-owned static-state reset path in `Patches_Startup.cs` (`Patches.ResetMapOwnedStaticState`). Do not add an independent lifecycle patch.

## Settings And Migration

### Persisted fields

Replace the runtime enemy boolean with:

```text
friendlyZombieResponse = Adaptive
enemyZombieResponse    = Full
```

Both fields live in `SettingsGroup`, so defaults, per-save values, and timeline keyframes retain independent policies. Like the existing enum settings, a response policy changes at a keyframe boundary rather than being numerically interpolated.

### UI

In the Attack settings section, replace the current enemy checkbox and add the friendly control:

- **Friendly response to zombies:** `Minimal`, `Adaptive`, `Full`
- **Enemy response to zombies:** `Minimal`, `Adaptive`, `Full`

Each option needs translated labels and help text explaining the actual behavior:

- `Minimal`: Ignore zombies as deliberate targets; close-melee self-defense may still occur.
- `Adaptive`: After a recent zombie hit, compare nearby capable shooters with nearby zombie pressure and fight fully only when confident.
- `Full`: Continuously permit normal combat AI to use ranged and melee weapons against eligible zombies.

Avoid wording that promises every pawn will fire or that `Minimal` suppresses vanilla self-defense.

### Backward compatibility

Old settings contain `enemiesAttackZombies` and no response enums. `SettingsGroup.ExposeData` must handle migration per serialized instance:

```text
if enemyZombieResponse is present:
    parse and use it
else if legacy enemiesAttackZombies is present:
    false -> Minimal
    true  -> Full
else:
    use the new default Full

if friendlyZombieResponse is absent:
    use the new default Adaptive
```

Implement this in the existing custom exposure callback, which can inspect the current settings XML node. Serialize only the new enum fields. Do not retain a live duplicate boolean, add a global migration version, or add a one-time save mutator.

The migration must apply to:

- Global/default settings.
- Current save settings.
- Every settings timeline keyframe.

This preserves both meanings of the old checkbox exactly while letting existing installations receive Adaptive friendly behavior. Invalid or unknown new enum text should fall back to the field's declared default and report through the project's existing defensive settings validation conventions.

## Deliberately Excluded From V1

The following refinements are not part of the initial implementation:

- Per-weapon attraction or muzzle-flash radius.
- Loud-versus-quiet weapon policy.
- DPS, accuracy, combat power, health curves, or body-part scoring.
- Three distance bands or “about to join” weights.
- Special-zombie policy bonuses other than tanky pressure `3`.
- Hooks for missed attacks, landed damage, downing, death, equipment loss, or pawn joining.
- Explicit response hold and expiry timers.
- Per-pawn or spatial subgroup caches.
- Lord-less faction clustering.
- Forced jobs or a custom combat Lord job.
- Changes to player, animal, or Anomaly-specific response settings.

Each can be added independently if a named runtime scenario demonstrates a repeatable wrong decision. None should be shipped merely to anticipate a possible edge case.

## Verification Plan

### Implementation staging

The friendly policy and the friendly/enemy validator split are the smallest player-facing slice and may be implemented and proven first. The enemy bool-to-enum conversion, serialized migration, settings UI, Anomaly detail, and broad BridgeTools fixture updates carry most of the regression risk and may follow in a second implementation commit. Both slices remain part of this design and must be complete before the feature is considered finished; staging is a way to isolate failures, not permission to leave the enemy policy half-migrated.

### Static checks

Before runtime testing:

1. Re-audit every touched Harmony target and signature against the current RimWorld 1.6 assembly.
2. Confirm the semantic roles of `Pawn_MindState.lastHarmTick`, `meleeThreat`, `Pawn.GetLord`, and `Lord.ownedPawns` remain as assumed. Explicitly re-audit Zombieland's `Verb_MeleeAttack_TryCastShot_Patch` prefix in `Patches.cs`: a smart-melee bite block skips vanilla `TryCastShot` and therefore suppresses that swing's `meleeThreat` write. The expected result is still “blocked bite equals miss and does not provoke.”
3. Confirm all production reads of `enemiesAttackZombies` are removed or intentionally retained only inside the legacy-load branch.
4. Confirm the new fields participate in cloning, defaults, persistence, and timeline interpolation through the reflection-based settings paths.
5. Add pure/helper tests where practical for thresholds, boundaries, filtering, and migration parsing.

### Runtime contract strategy

Extend the existing targeting-oriented BridgeTools workflow rather than adding a one-hunch tool. The contract should stage controlled Lords and report:

- Selected relationship and configured policy.
- Lord identifier and member count.
- Chosen provocation anchor and harm age.
- Capable shooter count.
- Normal and tank-weighted zombie pressure.
- Previous mode, threshold, confidence, final mode, and cache age.
- Actual selected target/job/verb where a combat observation is required.

Diagnostics should be available only through the test contract or development logging, not emitted continuously in normal play.

### Reusable encounter and survival harness

Build the runtime proof around a reusable prepared map rather than hand-constructing each case. The harness should own two layers:

1. A deterministic base save with a clear encounter area, safe spawn/staging cells, enough open firing space for the nine-cell and full-weapon-range cases, and stable camera coordinates.
2. A high-level asynchronous companion workflow that reloads the base before each independent trial, places a requested zombie composition, triggers a friendly-visitor or enemy-raid arrival through the closest real dev-event/incident path, runs normal game time at requested `Superfast`/3x speed, and returns structured evidence.

The high-level workflow must compose generic RimBridge primitives where they already exist and add Zombieland-specific hooks only for reusable response evidence. It should accept at least:

- Relationship/event kind: friendly visitors or enemy raid.
- Group-size or incident-point control.
- Ordinary and tanky zombie counts, placement center/radius, and deterministic seed.
- Response policy overrides for the trial.
- Warm-up, measurement, and maximum trial ticks.
- Requested normal gameplay speed, defaulting to 3x.
- Repetition count and output path.

Each independent trial must reload the same base save so casualties, injuries, destroyed equipment, zombie state, Lord state, random incidents, and cache history do not leak into the next sample. Fixture creation is separate from measurement; save a verified base once and reuse it.

Add lightweight main-thread instrumentation around the policy owner and relevant combat observations. It may retain bounded per-trial diagnostics, but normal gameplay must pay no unbounded logging or allocation cost. Collect at least:

- Group/Lord identity, relationship, policy, member count, capable-shooter count, and weapons.
- Every response-mode transition with tick, anchor, harm age, pressure, confidence, threshold, and cache hit/miss.
- First zombie attack attempt, first landed zombie harm, first `Full` transition, first ranged shot, and first return to `Minimal`.
- Ranged shots, melee attacks, zombie hits, zombies killed, group members downed/killed, survivors, injuries, and elapsed game ticks.
- Lord/group exit or dispersal when observable.
- Actual sampled time speed, trial completion reason, warning-or-higher logs, and cleanup result.

Return raw per-trial rows plus aggregate survival statistics. At minimum aggregate survival rate, full-group survival rate, mean/median survivors, mean zombies killed, mean time to first ranged response, mode-transition count, and response-stutter count. Define one response stutter as `Full -> Minimal -> Full` during a single uninterrupted contact with relevant zombies still present.

The harness is evidence infrastructure, not an alternative combat AI. It must not directly force pawns to shoot, alter weapon stats, heal participants during a trial, or step private AI methods. Let normal RimWorld at 3x choose jobs and targets after staging. Use deterministic narrow contracts for exact thresholds first, then repeated freer-running trials to observe survival outcomes.

### Required behavior matrix

#### Policy and relationship

- Friendly `Minimal`, `Adaptive`, and `Full` operate independently.
- Enemy `Minimal`, `Adaptive`, and `Full` operate independently.
- Changing the friendly setting does not alter enemies; changing the enemy setting does not alter friendlies.
- Player pawns, animals, Anomaly overrides, factionless pawns, and Lord-less pawns retain their specified behavior.
- Old enemy `false` loads as `Minimal`; old enemy `true` loads as `Full` in defaults, live values, and timeline keyframes.

#### Provocation

- An unprovoked Adaptive group remains `Minimal` even when heavily armed.
- A zombie miss alone does not provoke the group.
- A landed ordinary zombie melee hit does provoke it.
- Recent non-zombie violence without a zombie melee threat does not provoke it.
- Provocation ages back to `Minimal` after the bounded 600-plus-cache window.
- A subsequent missed zombie swing does not immediately erase a valid recent landed-hit signal.
- In a long uninterrupted contact where ranged fire prevents every second melee hit, record whether the group expires from `Full` to `Minimal`, stops firing while relevant zombies still approach, is hit again, and returns to `Full`. The v1 design permits this self-correcting stutter, but the harness must quantify it so the 600-tick provocation window or cache lifetime has a concrete tuning signal.

#### Shooter capability

- No capable ranged shooter is always `Minimal`.
- Dead, downed, mentally broken, contained, violence-incapable, badly impaired, slow, melee-only, unusable-weapon, off-map, and out-of-radius members do not contribute.
- A capable shooter exactly at the 24-cell boundary is included; one beyond it is excluded.

#### Pressure and thresholds

- `1` shooter versus `1` normal zombie enters `Full`.
- `1` shooter versus `1` tanky zombie remains `Minimal`.
- `3` shooters versus `2` normal zombies enters `Full`.
- `3` shooters versus `5` normal zombies demonstrates hysteresis: does not enter `Full`, but remains `Full` if already there.
- `5` shooters versus `12` normal zombies resolves `Minimal` from either prior mode.
- Downed, roped, confused, emerging, albino, destroyed, dead, off-map, and out-of-radius zombies contribute no pressure.
- A zombie exactly at 16 cells contributes; one beyond it does not.

#### Combat integration

- A Minimal pawn retains the limited vanilla close-melee defense and does not acquire a deliberate ranged zombie target.
- A Full or confident Adaptive group with usable rifles selects and attacks eligible zombies with normal ranged combat.
- Friendly Full combat works when enemy response is Minimal, proving the validator split.
- Enemy Full behavior after old-`true` migration matches current behavior.
- The friendly ranged fallback reaches an eligible zombie beyond nine cells but within weapon range, or produces evidence justifying a targeted distance-filter change.
- An incompatible electric zombie is rejected downstream without changing the policy mode.
- Existing suicide-bomber/tanky target priority remains intact.
- `AttackMode`, Anomaly, albino-pressure, flee, and active-threat behavior match the integration table above.

#### Lifecycle and performance

- Cache hits avoid member and zombie rescans for 120 ticks.
- Save/load and return-to-entry clear transient Lord entries.
- Settings and timeline policies survive save/load.
- A fixture with roughly 2,000 cached zombies and multiple queried Adaptive Lords shows no meaningful tick-rate regression or unexpected allocations.
- Repeated 3x friendly and enemy encounter matrices reload the same prepared base between trials and produce raw plus aggregate survival evidence without manual intervention.
- The sustained ranged-only contact reports response stutters explicitly rather than treating eventual self-correction as an unconditional pass.
- Build, load, fixture setup, combat observation, save/load, and cleanup produce no new warning-or-higher log signatures.

## Acceptance Criteria

The feature is ready to release when all of the following are true:

- Friendly and enemy response policies are separately configurable and their defaults/migration match this document.
- Adaptive mode uses only the lazy Lord cache, existing mind-state provocation signals, capable-shooter headcount, and fixed-radius zombie pressure described here.
- No custom combat jobs, damage hooks, background timers, or faction-cluster fallback were introduced.
- Strong groups visibly use ranged force against small local zombie contacts after being hit.
- Weak, unarmed, injured, dispersed, or heavily outnumbered groups remain cautious.
- The two settings do not cross-couple in either vanilla target search or the friendly ranged fallback.
- Static patch audit, behavior matrix, save/load migration, performance fixture, and clean-log checks are recorded in the existing evidence owners.

## Tuning Rule After Implementation

Tune only from named wrong-decision scenarios. Change one constant at a time in this order:

1. `Zombies per shooter` for broad confidence bias.
2. Enter/stay thresholds for commitment and oscillation.
3. Support or danger radius for locality errors.
4. Tanky weight for tank-specific misclassification.
5. Provocation lifetime for sustained-contact stutter, then cache lifetime for visible response delay.

Do not add weapon-quality, special-zombie, or event-driven machinery until the simple model fails a repeatable scenario that constants cannot reasonably correct.
