# TODO

## Fleshmass collision V5

- [ ] Decide whether deliberate zombie attacks on active fleshmass should remain
  available only under the existing `AnyBuilding` smash mode, as V5 recommends,
  or whether the feature needs a separate explicit opt-in. Do not silently make
  the default `DoorsOnly` mode destroy non-door structures.
- [ ] Before adding any cooldown or persistent limiter, prototype the V5 local
  smash integration and measure successful root kills and total cascade cells
  per in-game hour with small, medium, and dense zombie fronts. Cover
  `smashOnlyWhenAgitated` on/off and raging/non-raging attackers. Add the
  smallest limiter only if the measured interaction becomes automatic outer-field
  cleanup rather than intermittent local damage.
- [ ] Implement and live-validate
  [`docs/features/fleshbeast/FLESHBEAST-v5.md`](docs/features/fleshbeast/FLESHBEAST-v5.md)
  only after the setting decision and throughput prototype are resolved.
