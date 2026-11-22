# TODO

- Reconcile the coverage inventory after 5.4.1: assign `Source/FleshmassCollision.cs`, `Source/Patches_Fleshmass.cs`, `Source/TitleBloodDrip.cs`, and the two `ZombielandBridgeTools.Fleshmass*.cs` files in `coverage/ZL_COVERAGE_INDEX.tsv`, then refresh `coverage/COVERAGE_COMPLETENESS_REPORT.md` so `./scripts/coverage-inventory.sh --consistency-checks` passes again.
- Multiplayer: run the Symbiant abandon confirmation (needs a second colony map) and the decontamination quest in a host plus client session. Then run a session between two real machines, ideally Windows and macOS.
