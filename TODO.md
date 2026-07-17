# TODO

## Harmony patch consolidation

- [ ] Consolidate the remaining exact duplicate Harmony targets into one patch
  class per method while preserving priorities, conditional contamination
  behavior, transpilers, state, and finalizers. Verified candidates:
  `Game.FinalizeInit`, `Pawn_FilthTracker.Notify_EnteredNewCell`, `Pawn.Kill`,
  `Pawn_HealthTracker.DropBloodFilth`, `Thing.TakeDamage`, `Thing.Ingested`,
  `ThingMaker.MakeThing`, `Pawn_PathFollower.StartPath`,
  `AttackTargetFinder.BestAttackTarget`,
  `Verb_LaunchProjectile.TryCastShot`, `Projectile.Launch`, and
  `Projectile.ImpactSomething`. Same-name overloads are intentionally excluded.
