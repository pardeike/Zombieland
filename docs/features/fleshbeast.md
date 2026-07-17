# Fleshmass Collision Design

## Status And Purpose

This is the current design contract for Zombieland's interaction with RimWorld
Anomaly fleshmass hearts. It defines a small extension to the existing zombie
smash flow and connects zombie-caused flesh destruction to Anomaly's existing
biological response.

The intended player experience is an unpredictable collision between two
existing threats. Zombies may open routes, draw attacks, lose numbers, redirect
new zombie spawns, or release additional fleshbeasts toward the colony. The
player can exploit the result, intervene in it, or control which zombie groups
participate through settings.

This document is a design contract. Implementation and live runtime evidence
will be recorded in the repository's patch-audit and test owners.

## Gameplay Contract

Heart-grown active fleshmass participates in Zombieland's existing local smash
decisions. A participating zombie may select the flesh when its current control
flow reaches a building-smash opportunity.

The attack uses the zombie's existing static-attack job, verb, damage, warmup,
cooldown, hit chance, interruption behavior, and target reconsideration. The
selected flesh receives ordinary damage. A killing hit runs the installed
Anomaly cascade, whose 4-8-cell range kills that many additional eligible flesh
cells when enough are connected, and every resulting map change becomes part
of normal gameplay.

RimWorld continues to own cascade selection and destruction, heart growth,
mini-growth, field-connectivity bookkeeping, fleshbeast composition, emergence
positions, assault behavior, effects, sounds, and camera behavior. Zombieland
supplies the attacker classification that allows a live grower to recognize
zombie-caused destruction and may narrowly suppress the response letter as
described below.

## Flesh-family Targets

The flesh family comprises `Fleshmass_Active`, plain `Fleshmass`, the fleshmass
heart, nerve bundles, fleshmass spitters, flesh sacks, fleshbulbs, and other
installed heart-owned buildings belonging to the same organism.

The added local target is a spawned, damageable `Fleshmass_Active` cell whose
`CompFleshmass.source` passes an explicit
`Building_FleshmassHeart { Spawned: true }` type check and is on the same map.
This is a sourced active-flesh target.

Every building candidate receives one complete classification:

- A building outside the flesh family continues through the current branch's
  existing building rules.
- A flesh-family building is unavailable for deliberate target selection and
  flesh-triggered suicide arming when the attacking zombie's category checkbox
  is off.
- A sourced active-flesh target gains this additional candidacy when the
  category checkbox is on.
- Another flesh-family building follows the current branch's existing ordinary
  building rules when the category checkbox is on. The generic `AnyBuilding`,
  tank, and suicide branches may therefore accept a damageable member, while
  the ordinary `DoorsOnly` extension adds only sourced active flesh.

The fleshmass heart is part of the family for classification but is never an
attack or arming target. Its installed definition has no hit points, is not
destroyable, and fails the existing `CanSmashBuilding` checks. Zombies therefore
cannot destroy the heart through this feature. A tank whose route enters the
heart footprint remains blocked and retries through its existing behavior for
unsmashable edifices until its route or surroundings change.

Every accepted flesh-family target also passes the existing
`CanSmashBuilding` checks. `OnlyColonists` therefore requires the building to
belong to the player faction and excludes heart-owned flesh. `OnlyHumans` and
`Everything` use their existing building-faction behavior. In unmodded Anomaly,
the heart and its growth belong to `Faction.OfEntities`, so `OnlyColonists`
makes all three flesh-attack category settings no-ops.

Zombies that are neither tank, suicide, nor former colonist reach the flesh
check through the existing `smashMode` and agitation rules. Former colonists
bypass the ordinary early `Nothing` and agitation checks, but they still need an
existing candidate branch: `DoorsOnly` or `AnyBuilding`. Under `Nothing`, a
former colonist selects no building. Tank and suicide zombies retain their
existing candidate branches under `Nothing`.

Damage from an attack that was triggered by another target resolves through the
existing damage system. The Anomaly response uses the actual killing instigator
and live grower source described below.

## Zombie Attack Flow

### Ordinary zombies

When the ordinary local `CanSmash` scan runs, a sourced active-flesh cell may
participate in the randomized adjacent candidate scan. This candidacy applies
in the default `DoorsOnly` flow as well as the existing generic building flow.

The ordinary zombie uses the existing one-attack static job and returns to its
normal stumble, tracking, rage, or wander control flow after the attack.

### Tank zombies

When a tank zombie's existing route or smash branch selects an edifice, a
sourced active-flesh cell may be accepted by the shared flesh eligibility gate.
The tank then uses its existing smash behavior.

When the tank category is off and the current wall route points into a
flesh-family building, the gate returns no smash target. The tank remains
blocked and retries through its normal stumble and rage ticks until its route
or surroundings change. The same existing behavior applies to the indestructible
heart regardless of the category setting.

### Suicide zombies

When the suicide-zombie building scan finds a flesh-family building, the shared
flesh eligibility gate determines whether that building may trigger the
existing arming behavior. An armed suicide zombie and its explosion use the
existing damage behavior. The checkbox gates arming because of flesh; it does
not gate explosion damage after the zombie has armed for any reason.

Zombieland's suicide explosion supplies a null damage instigator. Vanilla
therefore treats flesh killed by the blast, including cascade children, as
player-equivalent destruction and advances the heart response without the new
zombie-faction classification. A bomber armed by an unrelated building can
still produce this collateral response even when all three flesh-attack
checkboxes are off.

### Former colonist and other special zombies

Former colonist zombies and the remaining special zombie variants use their
existing control-flow branches. When one of those branches considers a
flesh-family building, the shared flesh eligibility gate determines whether it
may be selected. Former colonists and other non-tank, non-suicide specials do
not gain a new building branch under `smashMode == Nothing`.

## Anomaly Response

`CompFleshmass.Notify_Killed` receives one additional attacker classification.
When the actual killing instigator belongs to the Zombieland zombie faction and
the destroyed cell has a non-null, spawned source carrying
`CompGrowsFleshmassTendrils`, that grower receives one call to
`Notify_FleshmassDestroyedByPlayer` for the destroyed cell. This deliberately
mirrors vanilla's source and grower-comp checks. It does not require the source
to be a `Building_FleshmassHeart`; that stricter type check belongs only to
deliberate target candidacy, so compatible modded growers keep vanilla response
semantics.

The classification applies to actual root deaths and actual cascade-child
deaths. Each destroyed cell credits the grower stored in that cell's
`CompFleshmass.source`. Vanilla continues to classify player-faction and
null-faction kills through its installed path. In particular, null-instigator
suicide-bomb kills advance the response as player-equivalent kills; the new
classification must neither replace nor double-count that path.

`CompFleshmass.source` is typed as `Thing` and remains referenced after a heart
dies. Vanilla schedules the source heart's active flesh for destruction over
the next 60,000 ticks, using `Destroy(KillFinalize)` rather than kill
notifications. The `source.Spawned` guards therefore reject in-flight zombie
kills after source loss, and the later vanilla field decay produces no ghost
response credits.

The heart's installed 125-200 destruction threshold determines when a response
attempt occurs. The killed position supplies the center of the installed 30-cell
emergence search. If there is at least one active-flesh cell in range with a
standable neighboring cell, each responding fleshbeast uses a random candidate
and joins the installed assault lord.

If no such emergence candidate exists, a completed threshold may produce no
visible response. This is accepted vanilla behavior and requires no special
handling.

When the kill that completes a response can be identified synchronously as a
Zombieland zombie-faction attack or Zombieland suicide explosion, the
implementation should suppress only that response's `ThreatBig` letter. The
fleshbeasts, effects, sounds, and assault still occur. This is a best-effort
initial-version refinement: use only narrow transient call-scope attribution.
If suppression would require persistent attribution, replacing the vanilla
counter, delaying responses, or reimplementing response logic, leave the letter
unchanged in the initial version rather than broadening the implementation.

The initial version adds no response-rate limit. Dense zombie-versus-fleshmass
battles and repeated counterattacks are an intended outcome; pacing can be
revisited only if play-testing shows a real problem.

These rules allow the response to emerge into the chewing group, elsewhere on
the same field, on nearby active flesh, or on a touching field. The resulting
fleshbeasts use the player's existing Anomaly targeting and zombie-hostility
settings.

## Fleshbeast Settings

The Attack settings page gains a fleshbeast subsection immediately after the
existing Anomaly response controls. The subsection is visible when the Anomaly
DLC is active.

The subsection displays these checkboxes:

- **Ordinary zombies attack** — default **on**.
- **Tank and suicide zombies attack** — default **on**.
- **Former colonist and other special zombies attack** — default **on**.

The three values are saved independently. Every combination is valid, and
turning all three off disables deliberate flesh-family target selection and
flesh-triggered suicide arming for every zombie category. It does not suppress
collateral damage from a suicide zombie armed by an unrelated building, and
null-instigator blast kills still use vanilla player-equivalent response
accounting.

### Category assignment

Every zombie that reaches a building-smash candidate gate belongs to one
attack-setting category for this feature. Category assignment uses this order:

1. A tank or suicide zombie uses **Tank and suicide zombies attack**.
2. A former colonist zombie or another special zombie uses **Former colonist
   and other special zombies attack**.
3. Every remaining zombie uses **Ordinary zombies attack**.

The second category includes the current toxic splasher, miner, electrifier,
dark slimer, and healer variants. Albino zombies retain their existing
non-smashing control flow and do not reach the flesh-family gate. The ordered
list wins when traits overlap: child status by itself adds no category, so an
otherwise ordinary child uses the ordinary category, while a child special or
former colonist uses the earlier applicable category. A future special zombie
that uses a building-smash flow joins the second category unless it receives its
own explicit category.

### Setting gate

A single helper provides a result for every building candidate:

1. A non-flesh-family building continues through the current building rules.
2. A flesh-family building requires the attacking zombie's category checkbox.
3. A sourced active-flesh target may use the candidacy defined by this design.
4. Another flesh-family building may use the current branch's existing
   ordinary-building candidacy.

The helper is used by the ordinary adjacent scan, the generic building scan,
the tank route-smash branch, and the suicide arming scan. A successful result
allows the current branch to continue with its existing attack behavior.

### Player-facing help

The settings use these help meanings:

- **Ordinary zombies attack:** Allows ordinary zombies to choose adjacent
  sourced active fleshmass during their normal smash decisions and preserves
  their existing `AnyBuilding` behavior for the rest of the flesh family.
- **Tank and suicide zombies attack:** Allows tank zombies to smash active
  fleshmass and other damageable flesh-family buildings on their route and
  allows suicide zombies to arm because of damageable flesh-family buildings.
  It never makes the heart attackable, and it does not gate collateral from a
  bomb armed for another reason.
- **Former colonist and other special zombies attack:** Allows former colonist
  zombies and other smashing special variants to choose flesh-family buildings
  when their normal smash flow reaches them. It does not give those zombies a
  building-smash branch under `Nothing`.

The subsection also explains that `OnlyColonists` excludes vanilla heart-owned
flesh because it belongs to the Entities faction. The checkboxes remain stored
and editable, but they have no deliberate target-selection effect while that
attack mode is active.

## Saved Settings

`SettingsGroup` stores three booleans, all initialized to `true`:

- `ordinaryZombiesAttackFleshmass`
- `tankyAndSuicideZombiesAttackFleshmass`
- `formerColonistAndSpecialZombiesAttackFleshmass`

The existing settings serialization, cloning, and timeline paths include these
fields in the same way as the other `SettingsGroup` booleans. Between keyframes,
each boolean keeps the lower keyframe's value and changes when the upper
keyframe is reached; duplicating a keyframe copies all three values. Existing
saves and setting profiles receive the default-on values when the fields are
absent.

## Implementation Surface

The implementation consists of:

1. The three `SettingsGroup` booleans.
2. The Anomaly-only settings subsection and English labels/help text, followed
   by the repository's supported localization workflow.
3. One flesh-family classifier and one sourced-active-flesh classifier, with an
   explicit spawned-heart type and same-map check for the latter.
4. One zombie category classifier with the ordered category assignment above.
5. One shared setting-and-target eligibility helper used by every deliberate
   flesh-selection branch.
6. The ordinary `DoorsOnly` adjacent-candidate extension.
7. One Harmony patch on the installed flesh-kill notification path that credits
   actual Zombieland-faction kills through vanilla's non-null, spawned source and
   grower-comp checks without narrowing the source to the vanilla heart type.
8. If practical through transient call-scope attribution, one narrow hook that
   suppresses only the `ThreatBig` letter for a response completed by a known
   zombie-caused kill. It adds no saved attribution or other response state.

The feature uses existing attack jobs and vanilla heart state, so the three
settings booleans are its complete saved state. Any response-letter attribution
is transient and is never serialized.

## Verification

### Settings UI

- The subsection appears directly after the Anomaly response controls when
  Anomaly is active.
- All three category checkboxes default on.
- Turning all three category checkboxes off blocks deliberate flesh-family
  selection and flesh-triggered suicide arming for every zombie category while
  leaving unrelated suicide-bomb collateral on the vanilla response path.
- Each category checkbox gates only its assigned zombie group.
- `OnlyColonists` visibly explains why vanilla heart-owned flesh is excluded
  even while the three values remain editable.
- Settings save, reload, copy, reset, import, and export preserve the three
  values through the existing settings paths.
- Keyframe duplication copies the booleans, and interpolation keeps each lower
  keyframe value until the upper keyframe's tick.

### Attack selection

- An ordinary zombie can select adjacent sourced active flesh when its existing
  local smash flow reaches the candidate scan.
- With its checkbox on, an ordinary `AnyBuilding` zombie applies its current
  generic-building behavior to organs, plain flesh, and sourceless active flesh.
  The sourceless case is constructed explicitly by the test fixture because
  official Anomaly growth assigns a source.
- With its checkbox off, an ordinary zombie excludes sourced active flesh,
  organs, plain flesh, and sourceless active flesh while unrelated buildings
  continue through the current rules.
- `DoorsOnly` adds sourced active flesh and applies `CanSmashBuilding`, including
  the `OnlyColonists` faction check; it uses its existing structural behavior
  for the rest of the flesh family.
- A tank zombie can select sourced active flesh through its existing route-smash
  branch when its category is enabled.
- A tank whose routed edifice is rejected by the category gate remains stably
  blocked and retrying until its route or surroundings change, without
  attack-job churn or errors.
- No zombie selects the fleshmass heart as an attack or arming target. A tank
  routed into its footprint remains stably blocked until the route or
  surroundings change.
- A suicide zombie can arm because of sourced active flesh when its category is
  enabled.
- A suicide zombie with its category on may arm because of an organ, plain
  flesh, or sourceless active flesh through the current generic scan; with the
  category off, those flesh-family buildings do not cause arming.
- An explosion armed by another building applies its existing collateral damage
  to flesh-family buildings. The explosion's null-instigator root and cascade
  kills use the vanilla player-equivalent path and advance response progress
  even if all three category checkboxes are off.
- Former colonist zombies and each current smashing special variant follow the
  former-colonist/special category gate when their existing smash flow supplies
  a candidate branch.
- Albino zombies retain their existing non-smashing behavior with either value
  of the former-colonist/special category setting.
- Child specials and child former colonists use their special or former-colonist
  category; child status alone leaves a zombie in the ordinary category.
- `smashMode == Nothing` blocks ordinary, former-colonist, and other non-tank,
  non-suicide special building selection. Tank and suicide zombies retain their
  existing exceptions and corresponding category gate.
- `OnlyColonists` excludes all unmodded heart-owned flesh from every category;
  `OnlyHumans` and `Everything` exercise the enabled flesh candidacy.
- Each successful selection creates the existing attack or arming behavior for
  that zombie type.

### Response accounting

- Nonlethal damage leaves the heart's response progress unchanged.
- Every actual zombie-faction root death credits its stored non-null, spawned
  grower source once.
- Every actual zombie-faction cascade-child death credits its stored non-null,
  spawned grower source once.
- Player-faction and null-faction kills use the vanilla notification path once.
- A null-instigator suicide root or cascade death advances the vanilla response
  exactly once and is not double-counted by the zombie-faction patch.
- A spawned modded source carrying `CompGrowsFleshmassTendrils` keeps vanilla
  response compatibility, while deliberate candidacy still requires a live
  `Building_FleshmassHeart` source.
- After heart loss, in-flight attacks and the scheduled 60,000-tick
  `Destroy(KillFinalize)` decay add no ghost response credits.
- When narrow transient attribution is practical, a response completed by a
  known zombie-faction attack or Zombieland suicide explosion suppresses only
  its `ThreatBig` letter while retaining the vanilla beasts, effects, sounds,
  and assault.
- Touching fields credit destroyed cells by their stored source while emergence
  positions follow the installed regional search.

### Combined runtime scenario

A reusable scenario places ordinary, tank, suicide, former-colonist, child,
child-special, and other special zombies beside thin and broad sourced flesh,
an organ, plain flesh, a manually constructed sourceless active-flesh cell, the
heart, and an unrelated building. It includes a disabled tank route through
flesh, a tank route into the heart, two nearby or touching heart fields, and
response progress close to its threshold.

The scenario cycles the category settings, `attackMode`, and `smashMode`;
observes ordinary attacks and explosions armed by flesh and unrelated
buildings; crosses settings keyframes; triggers root and cascade deaths; removes
a source heart during an in-flight attack; saves and reloads during an attack;
and runs a dense horde through the field. A zombie-induced response joins the
battle normally and, when the narrow suppression hook is practical, does so
without its `ThreatBig` letter.

The small isolated targets in the attack matrix prove category and smash-mode
gates without another eligible cell confounding the result. Cascade behavior is
proved separately: the response stage witnesses an ordinary zombie's real
`AttackStatic` against a connected 7x7 sourced field and requires the selected
root plus at least four additional cells to be destroyed. The dense stage then
exercises many live zombies against two adjoining 14x10 sourced fields.

The completed scenario demonstrates category gating, one-credit-per-death
response accounting, continued zombie control flow, settings persistence and
timeline semantics, zombie-versus-flesh battle behavior, and
warning-and-error-clean logs.

## Release Contract

This design is ready for implementation release when:

- the three settings follow the UI, defaults, category, persistence,
  timeline, and help-text contracts;
- `OnlyColonists`, heart immunity, and suicide collateral are described
  accurately to the player;
- every deliberate flesh-family selection and flesh-triggered suicide arming
  decision passes through the shared category gate;
- enabled categories retain their existing generic flesh-family behavior and
  gain sourced-active-flesh candidacy, while disabled categories reject
  flesh-family candidates;
- ordinary attacks use the existing local smash and one-attack job flow;
- former colonists and non-tank, non-suicide specials remain unable to select a
  building under `Nothing`;
- the heart remains unattackable and unarmable through every branch;
- actual Zombieland-faction flesh deaths credit their stored live grower source
  exactly once, without narrowing vanilla-compatible source types;
- player and null-instigator deaths retain the vanilla path exactly once;
- if a completing kill can be identified through narrow transient attribution,
  a zombie-induced response suppresses only its `ThreatBig` letter; inability
  to do so without broader state is documented for play-testing rather than
  blocking the initial release;
- no response-rate limit is introduced in the initial version;
- touching fields, source loss, concurrent attacks, cascades, and save/load
  satisfy the focused verification cases; and
- the combined runtime scenario completes with stable tank blocking and retry,
  the intended dense battle behavior, and clean warning-and-error logs.

## Implementation And Verification Status

Completed on 2026-07-16 against installed Steam RimWorld
`1.6.4871 rev597`, Assembly-CSharp MVID
`967ddb80559449f0a776dafa26a855d1`.

The implementation lives in the shared `FleshmassCollision` helper, the two
narrow Anomaly Harmony patches, the existing zombie building-selection flow,
three settings fields/UI controls, all ten active translations, and two
companion contracts. It retains vanilla player/null-instigator response
handling, adds actual Zombieland-faction credit only for a stored live grower,
uses transient suicide attribution, and introduces no response-rate limit.

The release contract above is satisfied by:

- two final 36/36 direct runs, including an active-Biotech child-body/category
  run;
- the clean-reload five-stage async run covering attacks, response emergence,
  touching fields, source loss, in-flight save/load, settings/timeline
  persistence, and a 120-zombie dense fortress;
- live settings UI verification for all three default-on controls and the
  `Only colonists` note; and
- zero warning-or-higher entries in the final normal and active-Biotech
  post-startup log gates.

Durable operation identifiers, fixture names, stage measurements, and exact
patch member IDs are recorded in `TEST_SCENARIOS.md`,
`TEST_COVERAGE.md`, and `TEST_PATCH_AUDIT.md`.
