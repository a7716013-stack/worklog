# Phase 4R Architecture Audit

Re-read GitHub main: 4105024fbfce15c98e4dc1ab0526ab88984fe8b3, version 3.2.6. Local tracked blobs match this tree with BOM/line-ending normalization. AGENTS.md and Phase4Q documents reviewed before implementation. Baseline: API/SQL 633 assertions, real runs/rights/scopes/PIT 0, production gates 0/8.

Application already owns CapturePermission, PilotScopeRules/PilotTrust, RealAcquisitionWorker, IsolatedEnvironmentReceipt, AcquisitionRunConsent, ForwardCaptureStore, SourceArchive, EtlPipeline/Scheduler, HistoricalDatasetBuilder, AuthorityResolution/EvidenceResolutionPacket, ComponentDatasetStore and seven PIT gates. Existing Domain Candidate-v1 and TrendSample-v1 remain unchanged. Historical Web namespaces in shared code do not imply a dependency on the Web UI assembly. Existing API host still references Web for transitional host/services; this release adds no formula to Web and does not attempt an unrelated complete migration.

Reuse existing worker and authorization gates. Add source-bound review projections, exact candidate scope, evidence checklists and read-only API consumed by existing ResearchValidation. Scope/rights reviewer comparison needs case-insensitive independence hardening, consistent with environment validation. No new approval authority, acquisition worker, scoring engine or database schema.

Public terms and metadata only were inspected (2026-11-24); no market-data request. STOCK_DAY authorization remains unproven; dataset 11549 daily CSV is a compatibility candidate, not an automatically approved replacement. Existing prior 21 facts cannot prove the new scope. No independent actual environment receipt, consent, authority closure or dataset approval supplied.

User authorizes local 3.2.7 implementation and Git upload; no website deployment, real acquisition, billing change, production database modification or scheduler activation. Use a feature branch/PR so main's automatic release workflow is not triggered.
