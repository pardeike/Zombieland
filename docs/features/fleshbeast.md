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
Anomaly cascade and every resulting map change becomes part of normal gameplay.

RimWorld continues to own heart growth, mini-growth, disconnected-field
recalculation, fleshbeast composition, emergence positions, assault behavior,
effects, sounds, letters, and camera behavior. Zombieland supplies the attacker
classification that allows the heart to recognize zombie-caused destruction.

## Flesh-family Targets

The flesh family comprises `Fleshmass_Active`, plain `Fleshmass`, the fleshmass
heart, nerve bundles, fleshmass spitters, flesh sacks, fleshbulbs, and other
installed heart-owned buildings belonging to the same organism.

The added local target is a spawned, damageable `Fleshmass_Active` cell whose
`CompFleshmass.source` is a live, spawned `Building_FleshmassHeart` on the same
map. This is a sourced active-flesh target.

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
  tank, and suicide branches may therefore accept it, while the ordinary
  `DoorsOnly` extension adds only sourced active flesh.

Every accepted flesh-family target also passes the existing
`CanSmashBuilding` checks. `OnlyColonists` therefore requires the building to
belong to the player faction and excludes heart-owned flesh. `OnlyHumans` and
`Everything` use their existing building-faction behavior.

Ordinary zombies reach the flesh check through the existing `smashMode` and
agitation rules. Tank, suicide, and former colonist zombies reach their category
check through their existing exceptions, including when `smashMode` is
`Nothing`.

Damage from an attack that was triggered by another target resolves through the
existing damage system. The Anomaly response uses the actual killing instigator
and live heart source described below.

## Zombie Attack Flow

### Ordinary zombies

When the ordinary local `CanSmash` scan runs, a sourced active-flesh cell may
participate in the randomized adjacent candidate scan. This candidacy applies
in the default `DoorsOnly` flow as well as the existing generic building flow.

The ordinary zombie uses the existing one-attack static job and returns to its
normal stumble, tracking, rage, or wander control flow after the attack.

### Tank zombies

When a tank zombie's existing route or smash branch selects an edifice, an
sourced active-flesh cell may be accepted by the shared flesh eligibility gate.
The tank then uses its existing smash behavior.

When the tank category is off and the current wall route points into a
flesh-family building, the gate returns no smash target. The tank's existing
movement fallback may choose another valid move when one is available. A tank
without another valid move remains blocked and retries through its normal
stumble and rage ticks until its route or surroundings change.

### Suicide zombies

When the suicide-zombie building scan finds a flesh-family building, the shared
flesh eligibility gate determines whether that building may trigger the
existing arming behavior. An armed suicide zombie and its explosion use the
existing damage behavior.

### Former colonist and other special zombies

Former colonist zombies and the remaining special zombie variants use their
existing control-flow branches. When one of those branches considers a
flesh-family building, the shared flesh eligibility gate determines whether it
may be selected.

## Anomaly Response

`CompFleshmass.Notify_Killed` receives one additional attacker classification.
When the actual killing instigator belongs to the Zombieland zombie faction and
the destroyed cell has a live spawned heart source, the source grower receives
one call to `Notify_FleshmassDestroyedByPlayer` for that destroyed cell.

The classification applies to actual root deaths and actual cascade-child
deaths. Each destroyed cell credits the heart stored in that cell's
`CompFleshmass.source`. Vanilla continues to classify player-faction and
null-faction kills through its installed path.

The heart's installed 125-200 destruction threshold determines when a response
occurs. The killed position supplies the center of the installed 30-cell
emergence search. Each responding fleshbeast uses a random valid active-flesh
emergence candidate from that search and joins the installed assault lord.

These rules allow the response to emerge into the chewing group, elsewhere on
the same field, on nearby active flesh, or on a touching field. The resulting
fleshbeasts use the player's existing Anomaly targeting and zombie-hostility
settings.

## Pacing

This design deliberately adopts the cadence produced by the existing zombie
attack flows, Anomaly cascade, 125-200 destruction threshold, response, and
heart growth. The combined runtime scenario records root deaths, destroyed
cells, and responses over time as gameplay evidence. Release acceptance uses
correct accounting, stable control flow, acceptable performance, and clean
logs.

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
flesh-triggered suicide arming for every zombie category.

### Category assignment

Every zombie that reaches a building-smash candidate gate belongs to one
attack-setting category for this feature. Category assignment uses this order:

1. A tank or suicide zombie uses **Tank and suicide zombies attack**.
2. A former colonist zombie or another special zombie uses **Former colonist
   and other special zombies attack**.
3. Every remaining zombie uses **Ordinary zombies attack**.

The second category includes the current toxic splasher, miner, electrifier,
dark slimer, and healer variants. Albino zombies retain their existing
non-smashing control flow and do not reach the flesh-family gate. Child zombies
use the ordinary category. A future special zombie that uses a building-smash
flow joins the second category unless it receives its own explicit category.

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

The response classification is based on the actual killing instigator. This
keeps the heart's biological reaction correct for every zombie-caused death,
including damage that resolves after target selection and incidental damage
from an existing attack.

### Player-facing help

The settings use these help meanings:

- **Ordinary zombies attack:** Allows ordinary zombies to choose adjacent
  sourced active fleshmass during their normal smash decisions and preserves
  their existing `AnyBuilding` behavior for the rest of the flesh family.
- **Tank and suicide zombies attack:** Allows tank zombies to smash active
  fleshmass and other flesh-family buildings on their route and allows suicide
  zombies to arm because of flesh-family buildings.
- **Former colonist and other special zombies attack:** Allows former colonist
  zombies and other smashing special variants to choose flesh-family buildings
  when their normal smash flow reaches them.

## Saved Settings

`SettingsGroup` stores three booleans, all initialized to `true`:

- `ordinaryZombiesAttackFleshmass`
- `tankyAndSuicideZombiesAttackFleshmass`
- `formerColonistAndSpecialZombiesAttackFleshmass`

The existing settings serialization and cloning paths include these fields in
the same way as the other `SettingsGroup` booleans. Existing saves and setting
profiles receive the default-on values when the fields are absent.

## Implementation Surface

The implementation consists of:

1. The three `SettingsGroup` booleans.
2. The Anomaly-only settings subsection and English labels/help text, followed
   by the repository's supported localization workflow.
3. One flesh-family classifier and one sourced-active-flesh classifier.
4. One zombie category classifier with the ordered category assignment above.
5. One shared setting-and-target eligibility helper used by every deliberate
   flesh-selection branch.
6. The ordinary `DoorsOnly` adjacent-candidate extension.
7. One Harmony patch on the installed flesh-kill notification path that credits
   actual Zombieland-faction kills to the destroyed cell's heart source.

The feature uses existing attack jobs and vanilla heart state, so the three
settings booleans are its complete saved state.

## Verification

### Settings UI

- The subsection appears directly after the Anomaly response controls when
  Anomaly is active.
- All three category checkboxes default on.
- Turning all three category checkboxes off blocks deliberate flesh-family
  selection and flesh-triggered suicide arming for every zombie category.
- Each category checkbox gates only its assigned zombie group.
- Settings save, reload, copy, reset, import, and export preserve the three
  values through the existing settings paths.

### Attack selection

- An ordinary zombie can select adjacent sourced active flesh when its existing
  local smash flow reaches the candidate scan.
- With its checkbox on, an ordinary `AnyBuilding` zombie applies its current
  generic-building behavior to organs, plain flesh, and sourceless active flesh.
- With its checkbox off, an ordinary zombie excludes sourced active flesh,
  organs, plain flesh, and sourceless active flesh while unrelated buildings
  continue through the current rules.
- `DoorsOnly` adds sourced active flesh and applies `CanSmashBuilding`, including
  the `OnlyColonists` faction check; it uses its existing structural behavior
  for the rest of the flesh family.
- A tank zombie can select sourced active flesh through its existing route-smash
  branch when its category is enabled.
- A tank whose category is off uses existing alternative movement when
  available and otherwise remains stably blocked by a flesh-family building,
  without attack-job churn or errors.
- A suicide zombie can arm because of sourced active flesh when its category is
  enabled.
- A suicide zombie with its category on may arm because of an organ, plain
  flesh, or sourceless active flesh through the current generic scan; with the
  category off, those flesh-family buildings do not cause arming.
- An explosion armed by another building applies its existing collateral damage
  to flesh-family buildings. Its kills follow the installed `DamageInfo`
  instigator classification; null-instigator kills use the vanilla path.
- Former colonist zombies and each current smashing special variant follow the
  third category gate.
- Albino zombies retain their existing non-smashing behavior with either value
  of the third category setting.
- `smashMode == Nothing` blocks zombies governed by the ordinary smash-mode
  check, while tank, suicide, and former colonist zombies continue through their
  existing exceptions and the corresponding category gate.
- Each successful selection creates the existing attack or arming behavior for
  that zombie type.

### Response accounting

- Nonlethal damage leaves the heart's response progress unchanged.
- Every actual zombie-faction root death credits its stored live heart source
  once.
- Every actual zombie-faction cascade-child death credits its stored live heart
  source once.
- Player-faction and null-faction kills use the vanilla notification path once.
- A completed threshold produces the installed response, emergence search,
  assault lord, effects, and threshold reroll.
- Touching fields credit destroyed cells by their stored source while emergence
  positions follow the installed regional search.

### Combined runtime scenario

A reusable scenario places ordinary, tank, suicide, former-colonist, and other
special zombies beside thin and broad sourced flesh, an organ, plain flesh,
sourceless active flesh, and an unrelated building. It includes a disabled tank
route through flesh, two nearby or touching heart fields, and response progress
close to its threshold. The scenario cycles the category settings, attack modes,
and smash modes; observes ordinary attacks and explosions; triggers root and
cascade deaths; saves and reloads during an attack; and runs a dense horde
through the field while recording destruction and response cadence.

The completed scenario demonstrates category gating, one-credit-per-death
response accounting, vanilla emergence behavior, continued zombie control flow,
settings persistence, and warning-and-error-clean logs.

## Release Contract

This design is ready for implementation release when:

- the three settings follow the UI, defaults, category, persistence,
  and help-text contracts;
- every deliberate flesh-family selection and flesh-triggered suicide arming
  decision passes through the shared category gate;
- enabled categories retain their existing generic flesh-family behavior and
  gain sourced-active-flesh candidacy, while disabled categories reject
  flesh-family candidates;
- ordinary attacks use the existing local smash and one-attack job flow;
- actual Zombieland-faction flesh deaths credit their stored live heart source
  exactly once;
- the completed threshold produces the installed Anomaly response;
- touching fields, source loss, concurrent attacks, cascades, and save/load
  satisfy the focused verification cases; and
- the combined runtime scenario completes with stable tank fallback, acceptable
  performance, recorded cadence, and clean warning-and-error logs.
