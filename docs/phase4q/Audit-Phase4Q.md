# Phase 4Q Architecture Audit

Baseline main f41475dc7412d82fc8eda2bbac4f5a357e105d29 (3.2.5), independently re-read from GitHub. All tracked local blobs match baseline (BOM/line endings normalized). Phase 4P has 590 API/SQL and 127 synthetic identity assertions; real acquisition/rights/scopes/PIT/dataset approvals remain zero, production gates 0/8.

Existing Application modules: CapturePermission, PilotScopeRules, RealAcquisitionWorker, ForwardCaptureStore, EtlPipeline/Scheduler, PilotMarketContextRules and PilotFeatureOutcomeFactory. Domain retains Candidate-v1, 19 features, six component weights and TrendSample-v1. Existing AuthorityResolution/EvidenceResolutionPacket, HistoricalDatasetBuilder and independent ComponentDatasetStore approvals are retained. Namespace names containing Web are historical; the shared project does not reference the Web UI assembly.

Gap: a pinned scope/environment approval does not yet bind an accepted environment test receipt plus an explicit per-run operator authorization. Add these checks to the existing worker, including retry and post-response revalidation. No second acquisition or rights approval system. PLAN/DRY_RUN must remain network/storage-free.

Existing admin ResearchValidation uses no-store read-only API projections. Extend it with one additive review-readiness endpoint; keep older contracts and snapshots intact. Report seven PIT dimensions using existing historical observation fields and real prior fact counts, not invented eligible samples. No database migration or production schema change is needed. API/Web host coupling remains the pre-existing migration boundary; no new computation enters Web.

Rights discovery found TWSE website clauses 6/8 and government dataset 11549 under OGDL 1.0. The monthly STOCK_DAY historical endpoint is not proven to be the same licensed resource. Independent reviewer, exact scope, environment acceptance and per-run authority remain external blockers. Public legal/metadata pages were read; no price endpoint was requested.

<!-- binding -->
Source SHA: da22501cfae8af9f59b39ae2767e6d1fb97ab9af
Input versions: Phase4P-v1 / Phase4I-v1 / HistoricalNormalized-v1 / TrendSample-v1.
Evidence: Engineering audit and implementation; external acceptance remains blocked.
Document content SHA256 (before binding): 2bcef23758ee535f015ebf70ef846939c78d628e2d48193dcb28975ea228ea56
Parent digest: 0E159694507DB4519AFB0A4B883D023CC9E4A2F3306398737CF3539CFC2D587D
Historical input digest: 9A33F33040B97AC61FAB1BCBA4021C182F59D9A12A33E3F1F14BAC2F881815BF
Export digest: 48C90EF529153208B2D4AFF856547722D2920E298681F460792A5210A5043BAA
