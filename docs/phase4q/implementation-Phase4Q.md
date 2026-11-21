# Worklog 3.2.6 - Phase 4Q

## A: rights evidence
Four independent purpose notes cite TWSE website terms clauses 6/8, government dataset 11549 and OGDL 1.0, observed 2026-11-22. They preserve source versions, restrictions, missing reviewer, missing expiry and missing CapturePermission receipts. Note digests cover canonical review metadata, not original page bytes or independent approval. No rights are granted. Dataset 11549's daily CSV resource is not proof that the monthly historical STOCK_DAY endpoint has identical authorization. The next reviewer must archive exact applicable terms and bind existing CapturePermission receipts, with retention/expiry, to exact plan IDs. No paid data, external login or market-data request was performed.

## B: concrete proposal
PilotReviewProposal specifies 50 candidate symbols, common-stock identity still unverified, historical months 2025-06 through 2026-11 inclusive (900 exact monthly requests), evaluation 2026-07-01 through 2026-09-30, target 60 evaluation sessions, 250-session lookback, 20-session outcomes, 2700 total attempts including retries, 1 GiB store, 64 KiB response and proposed 90-day retention. Execution is proposed for 2026-12-01 through 2026-12-08 UTC. Counts of trading sessions remain unverified until historical calendar/universe evidence is supplied; the 50 candidates are not an asserted historical index. All fields require human confirmation, including operator identity and rights references.

--phase4q-draft-scope <compiled-sha> <new-output> deterministically expands the proposal into the existing PilotScope/CapturePlan contracts. It produces no permissions or approvals. Changes require recomputed plan/scope IDs and independently reviewed existing receipts. Rights references are empty and the operator is UNASSIGNED_REQUIRES_HUMAN. The worker still has a 20-attempt/hour ceiling and one exclusive lease; 900 requests must be resumed within approved quotas, not silently expanded or run on a timer. The proposed window and source SHA must be reconfirmed before any future use.

## C/D: existing isolated worker strengthened
RealAcquisitionWorker now additionally requires a pinned IsolatedEnvironmentReceipt and AcquisitionRunConsent. Acceptance binds exact canonical storage root digest, source SHA, environment, operator, independent reviewer, time window, all 17 safety controls and their evidence digests. Consent binds exact scope, run ID, operator, environment acceptance digest, ACQUIRE_ONCE, time window and explicit authorization evidence. They are execution preconditions, not another rights or scope approval system. The existing PilotTrust adds optional EnvironmentDigests/ExecutionDigests; older trust files fail closed for real acquisition and must be independently re-provisioned and their canonical pin updated.

WORKJOURNAL_PILOT_TRUST_DIGEST still pins the external trust file. WORKJOURNAL_PILOT_EXECUTION_FILE supplies the externally provisioned environment/consent bundle to the existing --phase4p-acquisition command. No approval-creation command exists. Root ACLs and trustworthy independent provisioning remain required; hashes are not identity signatures. The worker rechecks expiry/revocation before attempts, after responses and before raw/capture writes, archives immutable execution evidence, and rejects changed consent for an existing run. Existing exact endpoint/DNS/TLS/no proxy/no redirect, quotas, one lock, retries, cancellation, circuit breaker, storage checks, quality quarantine, raw immutability and replay are reused. Fixture injection remains test-only; optional fixture execution evidence exercises the same new gates without real network requests. PLAN/DRY_RUN remain free of downloads and store writes. REPLAY/VALIDATE remain historical read-only verification, not authorization to acquire/use more data.

No real ACQUIRE_ONCE was executed: rights/scope/environment/explicit consent are absent. Capture reports therefore contain zero runs and no fabricated timestamps, digests, lineage or replay success. Upon separately authorized acquisition, existing immutable SourceArchive, CaptureRun, quality and EtlJobReport retain provider/dataset/instrument/trade dates/ObservedAt/RetrievedAt/raw digest/source version/quality/lineage and replay bindings; ObservedAt never becomes original PublishedAt. This release's web export is a reviewed snapshot, not a live scanner of private stores.

## E/F: existing historical review and PIT
Historical evidence remains under existing resolution packets, AuthorityReview and ComponentDatasetStore. Eight priority domains retain UNPROVEN status absent scoped closure. Coverage derives from validated Phase4I facts and existing gate statuses: prior total 21 (20 Price, 1 Institutional), one inspected observation, zero new facts, publication/revision proof, PIT eligibility or approved datasets. Each of seven gates has verified/inspected counts. The proposed pilot denominator is unknown, so prior coverage never implies coverage of 50 stocks or 60 dates. Corporate actions stay a separate historical domain and feed existing price-basis requirements.

Candidate-v1, 19 features, weights 25/15/15/20/15/10, FormulaDigest, TrendSample-v1 (20 valid dates, max(10,quantiles) instruments/date, 200 outcome pairs in every Train/Validation fold) and separate OOS remain unchanged. No baseline/OOS/promotion is invoked; Phase5 stays LOCKED.

## G/H: UI and acceptance
The existing administrator ResearchValidation page adds evidence/scope/acquisition/history/PIT/Trend/production sections through the read-only API. The prior panels remain available. All eight production gates and real Google A/B acceptance remain BLOCKED; fixture identity checks are not real OAuth acceptance. Scheduler remains Disabled. User authorization covers Git push/merge and site deployment for 3.2.6, but not acquiring data, buying licenses, production database changes or enabling ETL.

## Evidence and next operation
Reports separately label source-discovery notes, proposed scope, blocked real work, synthetic fixture/integration results, and verified live release checks. Source SHA, parent/input versions and canonical report digests bind every artifact. The next safe operation is independent review of endpoint-specific rights and the concrete proposal, then accepted isolated environment and exact per-run consent; only after all gates pass can an operator separately authorize the existing ACQUIRE_ONCE. No permission is inferred from code deployment.

<!-- binding -->
Source SHA: da22501cfae8af9f59b39ae2767e6d1fb97ab9af
Input versions: Phase4P-v1 / Phase4I-v1 / HistoricalNormalized-v1 / TrendSample-v1.
Evidence: Engineering audit and implementation; external acceptance remains blocked.
Document content SHA256 (before binding): 97dff961d86c5ba915d018557c88c14639d22c733cac87fccdc41e8689713bc5
Parent digest: 0E159694507DB4519AFB0A4B883D023CC9E4A2F3306398737CF3539CFC2D587D
Historical input digest: 9A33F33040B97AC61FAB1BCBA4021C182F59D9A12A33E3F1F14BAC2F881815BF
Export digest: 48C90EF529153208B2D4AFF856547722D2920E298681F460792A5210A5043BAA
