# Phase 4R Architecture Audit

Re-read GitHub main: 4105024fbfce15c98e4dc1ab0526ab88984fe8b3, version 3.2.6. Local tracked blobs match this tree with BOM/line-ending normalization. AGENTS.md and Phase4Q documents reviewed before implementation. Baseline: API/SQL 633 assertions, real runs/rights/scopes/PIT 0, production gates 0/8.

Application already owns CapturePermission, PilotScopeRules/PilotTrust, RealAcquisitionWorker, IsolatedEnvironmentReceipt, AcquisitionRunConsent, ForwardCaptureStore, SourceArchive, EtlPipeline/Scheduler, HistoricalDatasetBuilder, AuthorityResolution/EvidenceResolutionPacket, ComponentDatasetStore and seven PIT gates. Existing Domain Candidate-v1 and TrendSample-v1 remain unchanged. Historical Web namespaces in shared code do not imply a dependency on the Web UI assembly. Existing API host still references Web for transitional host/services; this release adds no formula to Web and does not attempt an unrelated complete migration.

Reuse existing worker and authorization gates. Add source-bound review projections, exact candidate scope, evidence checklists and read-only API consumed by existing ResearchValidation. Scope/rights reviewer comparison needs case-insensitive independence hardening, consistent with environment validation. No new approval authority, acquisition worker, scoring engine or database schema.

Public terms and metadata only were inspected (2026-11-24); no market-data request. STOCK_DAY authorization remains unproven; dataset 11549 daily CSV is a compatibility candidate, not an automatically approved replacement. Existing prior 21 facts cannot prove the new scope. No independent actual environment receipt, consent, authority closure or dataset approval supplied.

User authorizes local 3.2.7 implementation and Git upload; no website deployment, real acquisition, billing change, production database modification or scheduler activation. Use a feature branch/PR so main's automatic release workflow is not triggered.

<!-- binding -->
Source SHA: 0386a1abd0124859007c4cdc16dd5ec61f8e332e
Input versions: Phase4Q-v1, PilotScope-v1, HistoricalNormalized-v1, TrendSample-v1
Evidence: Engineering audit/implementation; no external acceptance.
Generated UTC: 2026-11-23T19:05:09.1056726+00:00
Content SHA256 (before binding): be1d9a845fc8f40d145ddc9002ee7cc7910e7d966d6e51a473dc8e6efeff7e75
Parent digest: 48C90EF529153208B2D4AFF856547722D2920E298681F460792A5210A5043BAA
Export digest: D4B985EA47BB8FC406C13E37703DDFD5B6D96CC42CE0339B43C8BD58CB7519AB
