# Architecture audit — Phase 4S / 3.3.1

Audited 2026-11-25 before implementation. Source specification is Worklog v3.2.8 Phase 4S Unified; user requested delivery 3.3.1.

main: 4105024fbfce15c98e4dc1ab0526ab88984fe8b3 (3.2.6). PR #25 open/unmerged: e6692f1b184787f72e861c578c25e43adb24e1c7 (3.2.7), used as this branch's parent. Push authorized; no merge/release/deploy.

- MarketRadarScoreCalculator is already Domain. Preserve parts, integer/null semantics, MarketRadarRecommendationService Category >=70, ranking, daily batches and owner checks.
- Move pure SwingCalculator and SwingBacktester unchanged from Web to Domain. Preserve seven strategies, 75 threshold, next-session execution and fees. StockAnalysisController.Swing and FinMindStockService.Swing retain data/chart behavior and use shared adapters.
- Reuse V3CandidateCalculator, CandidateFeatureCatalog and FullEvidenceScoreCalculator without modifying their resources. Candidate FormulaDigest remains 81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB.
- CandidateGovernance, ComponentDatasetStore.Current/Replay and ResearchValidation remain authoritative. No complete approved six-component dataset is connected. Shadow/Preview return BLOCKED_BY_DATA without acquisition. CandidatePrimary rejects. No caller-supplied eligibility boolean or live adapter.
- Existing recommendation SnapshotJson supports additive score metadata. Do not backfill old snapshots. Strategy signal versions and selection remain Legacy. No database migration is needed.
- Backtest comparison keeps model identities separate; Candidate baseline/OOS NOT_RUN, promotion NOT_PROMOTED. Fixture calculations never become real performance.
- Pilot remains review-only/unapproved: 10 stocks, 3 historical months, 30 unique requests, maximum 90 attempts; zero acquisition.

Domain owns score contracts; Application owns adapters/routing; API owns authentication and Administrator research access. Legacy is default/rollback. Local guest requires a separate Development loopback host, isolated database/cookie and synthetic providers. Production cannot enable guest access.
