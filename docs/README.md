# Documentation Map

Use this page to find the current owner of a decision or piece of evidence. Each document should have one clear role; do not create a new Markdown file for a single work session.

## Start Here

| Document | Role | Update when |
| --- | --- | --- |
| [`AGENTS.md`](../AGENTS.md) | Repository-wide working rules and invariants. | A durable development rule changes. |
| [`TODO.md`](../TODO.md) | Small, temporary queue of unresolved work. | Work is deferred or completed; remove finished items. |
| [`PENDING_RELEASE_NOTES.md`](../PENDING_RELEASE_NOTES.md) | Short player-facing changes queued for the next release. | A player-visible or player-relevant change is ready to commit, or a release consumes the queue. |
| [`README.md`](../README.md) | Player-facing overview copied into the built mod. | Player-facing behavior or support information changes. |
| [`ModDescription.md`](../ModDescription.md) | Short shared publishing description. | The public feature summary changes. |

## Development Evidence

| Document | Role | Update when |
| --- | --- | --- |
| [`coverage/README.md`](../coverage/README.md) | Entry point and operating rules for coverage work. | Coverage ownership or workflow changes. |
| [`TEST_COVERAGE.md`](../TEST_COVERAGE.md) | Current coverage matrix and durable evidence ledger. | A feature's evidence or coverage state changes. |
| [`TEST_SCENARIOS.md`](../TEST_SCENARIOS.md) | Reusable player-facing scenario definitions and scenario evidence. | A durable scenario or fixture changes. |
| [`TEST_PATCH_AUDIT.md`](../TEST_PATCH_AUDIT.md) | Harmony target/signature/semantic audit. | A patch target is added, removed, or re-audited. |
| [`coverage/ZL_COVERAGE_INDEX.tsv`](../coverage/ZL_COVERAGE_INDEX.tsv) | Advisory planning and ownership index. | Inventory or row ownership changes. |
| [`coverage/COVERAGE_COMPLETENESS_REPORT.md`](../coverage/COVERAGE_COMPLETENESS_REPORT.md) | Reconciliation summary consumed by coverage checks. | Regenerated or reconciled through the coverage workflow. |

Keep operation IDs and historical runtime results in the evidence ledgers, not in feature design documents or new release snapshots.

## Design And Workflows

| Document | Role | Update when |
| --- | --- | --- |
| [`docs/features/symbiant.md`](features/symbiant.md) | Current Symbiant design invariants and release contract. | The intended feature behavior or release gate changes. |
| [`docs/features/fleshbeast.md`](features/fleshbeast.md) | Current design contract for the proposed Anomaly fleshmass-collision feature. | The intended attack categories, settings contract, response accounting, or release criteria change. |
| [`scripts/README.md`](../scripts/README.md) | Supported project validation and diagnostics, including XML/localization checks, Player.log summarization, soundtrack, asset-bundle, and repeatable runtime-benchmark workflows. | Script behavior, prerequisites, validation criteria, or diagnostic and benchmark evidence shape changes. |
| [`Originals/Soundtrack/README.md`](../Originals/Soundtrack/README.md) | Source soundtrack layout and sync entry point. | Source-folder conventions change. |
| [`Originals/Soundtrack/LOUDNESS_NOTES.md`](../Originals/Soundtrack/LOUDNESS_NOTES.md) | Durable loudness policy and its deliberate exception. | The target, exception, format, or normalization procedure changes. |
| [`1.6/Sounds/music/README.md`](../1.6/Sounds/music/README.md) | Generated runtime music-folder contract. | Runtime loader or folder hints change. |

Unity's `Originals/Effects/Library` directory is generated cache state and is intentionally ignored. Third-party package readmes, changelogs, and licenses inside that cache are not project documentation.

## Maintenance Rules

- Update an existing owner before creating another document.
- Put active follow-up work in `TODO.md`; remove it when finished.
- Put repeatable procedures in the nearest workflow README.
- Put lasting behavior and architectural decisions in a feature document.
- Put runtime proof in the appropriate coverage or scenario ledger.
- Do not preserve chronological work logs when Git history and the current invariant are sufficient.
