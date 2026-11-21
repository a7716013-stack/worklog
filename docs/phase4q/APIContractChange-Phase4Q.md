# Phase 4Q API contract

GET /api/v1/data/pilot/readiness returns Phase4QReport to the existing ResearchValidation administrator page. Both ApiResearchRead and ApiAuthorityAdmin, ReadPolicy, no-store and existing 401/403/429/503 ProblemDetails apply. A missing or tampered report or parent fails closed with DATA_NOT_READY. GET does not acquire data or approve anything; POST is 405. Existing Phase 4P and older endpoints retain their contracts and immutable evidence snapshots.

The response contains fixed source-discovery notes, a proposed scope, execution prerequisites, historical-domain coverage, seven PIT dimensions, actual counts and eight production gates. No raw bodies, arbitrary reviewer text, credentials or store paths are returned. Nullable proposed scope denominator means unknown, never zero or 100 percent. Prior inspected observations are explicitly distinct from the proposed 50-stock universe. Sources and report digests bind the export, not an original publication timestamp or grant.

Application owns Phase4QOperations and PilotExecutionRules; Domain formulas are unchanged. CLI and API share the same export/validation functions; the existing acquisition CLI shares RealAcquisitionWorker. Web only renders same-origin authenticated metadata and clears it when hidden or on authorization errors. The existing API hosting reference to Web remains a documented migration boundary; no new Web calculations or database migrations.

OpenAPI is generated from controllers/DTOs and compatibility-tested against the prior snapshot. docs/api-v1.md includes the new route. SourceCodeSha identifies the implementation commit used to export; system/health identifies the deployed release commit containing the exports. Both release CI runs and both websites must match that release SHA.

<!-- binding -->
Source SHA: da22501cfae8af9f59b39ae2767e6d1fb97ab9af
Input versions: Phase4P-v1 / Phase4I-v1 / HistoricalNormalized-v1 / TrendSample-v1.
Evidence: Engineering audit and implementation; external acceptance remains blocked.
Document content SHA256 (before binding): b688c2391db3720ed2b7de7e5373985f6653a52db39305c6d4feb33ad5d79700
Parent digest: 0E159694507DB4519AFB0A4B883D023CC9E4A2F3306398737CF3539CFC2D587D
Historical input digest: 9A33F33040B97AC61FAB1BCBA4021C182F59D9A12A33E3F1F14BAC2F881815BF
Export digest: 48C90EF529153208B2D4AFF856547722D2920E298681F460792A5210A5043BAA
