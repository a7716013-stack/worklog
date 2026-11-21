# Worklog 3.2.5 — Phase 4P

Implementation follows the specification's PR-1 to PR-9 dependency order in one integrated release PR. The audit and initial gap matrix precede implementation. Baseline is 5ddefcc6a26f3be4dbd9867eebd8ff8c8e16276f.

## Rights and scope (PR-2/3)

CapturePermission is reused, not replaced. ACQUIRE, STORE, RESEARCH and REDISTRIBUTE are independent. Exact CapturePlan IDs bind provider, dataset, instrument, date, coverage, endpoint, schema, code version and retention. PilotScope additionally binds historical/forward windows, lookback, horizon, request/storage limits, expiry and approved rights digests. A separately trusted PilotApproval binds the entire scope and isolated environment. Requester and reviewer must differ. Revoked, expired, mismatched or untrusted receipts fail closed. Existing credentials and public availability never grant rights.

The proposal is approximately 50 instruments and 60 trading days, with dates and instruments intentionally empty. Candidate-v1's existing long moving-average/lookback requirements still apply; 60 evaluation days alone are not enough historical input. No actual approval is invented. Trust files must be independently provisioned outside the repository; the controlled CLI requires WORKJOURNAL_PILOT_TRUST_DIGEST to match them. This is an operator-controlled filesystem trust boundary, not a new identity provider or an approval-signing service. There is no self-approval command. Updating/revoking trust requires operator re-provisioning of its pinned digest.

## Acquisition (PR-4)

PLAN and DRY_RUN validate with no network/archive writes. ACQUIRE_ONCE uses AcquisitionSafety's safe DNS connection, TLS host validation, disabled proxies/cookies/redirects, timeout and cancellation. Only exact TWSE/TPEx price endpoints are supported. FinMind remains unapproved; market-wide T86 is not accepted by the instrument-scoped worker. Per-request durable reservations enforce cumulative scope quota, existing hourly ceiling, three-attempt retries and a persistent circuit breaker. One file lease serializes all provider work across processes sharing the same isolated root.

Storage checks account for the isolated store, staging files and conservative raw/normalized metadata headroom. An acquisition root must be on a storage system that enforces exclusive filesystem locks; production multi-worker/shared-volume acceptance is still blocked. Same scope/run/plan reuses staged bytes and immutable capture/ETL receipts after restart. A crash before raw publication may require another bounded request; this is not an exactly-once network guarantee.

ForwardCaptureStore retains original bytes, observation/retrieval timestamps, source publication UNKNOWN, checksums and revision history. Schema drift and quality failures are quarantined before Silver. Partial failures retain earlier successful captures. EtlScheduler and EtlPipeline provide existing leases, recovery and lineage; operational dispatch calendars are explicitly not historical calendar evidence. The controlled isolated one-shot path does not enable production EtlProductionGate or any timer. REPLAY and VALIDATE verify raw, capture, quality and ETL bindings without downloads; incomplete work remains incomplete. Historical replay remains available after revocation, but new acquisition/use requires current trust.

## Historical market context (PR-5/6)

Existing permanent identity, symbol history, listing/delisting, market transfer, universe, trading session and corporate-action contracts are reused. Dated context versions supplement industry, suspension/resumption and universe known-at bindings. Queries require exact historical identity, effective intervals, known-at cutoff, reviewed digests and source versions. No current-universe or current-industry fallback exists. Emergency closures and scheduled holidays are market events; suspension is per instrument.

Cash/stock dividends, reduction, splits and price-basis changes retain source revisions and availability. Future-known action information is excluded. Raw prices remain unchanged; this release does not invent adjustment factors or infer that no listed action means complete action coverage. Real historical context ingestion remains blocked on verified source evidence.

## Feature/outcome and Trend preparation (PR-7/8)

The controlled factory revalidates an existing ComponentDatasetStore approval and scoped RESEARCH/STORE rights, matches source archives and dates, rebuilds with HistoricalDatasetBuilder and uses V3CandidateCalculator and ResearchOutcomeCalculator. Outcome versions/cutoffs are separate sidecars and never enter feature inputs. Existing raw/adjusted price-basis/null semantics are retained. Formula digest, Candidate-v1, 19 features and weights 25/15/15/20/15/10 remain unchanged; seven PIT gates and six independent component approvals are not replaced.

Preparation evaluates TrendSample-v1 counts with existing ResearchMetrics: each Train/Validation fold needs 20 usable dates, max(10, quantiles) instruments per date, 200 valid outcome pairs and populated quantiles. Partition overlap and embargo checks use ResearchProtocolRules; labels crossing a partition boundary are purged. Independent OOS remains unexecuted. Insufficient data retains INSUFFICIENT_SAMPLE; preparation is not REAL_BASELINE_V1 execution. No baseline, approval, promotion or Phase5-unlock command is invoked.

## Research Data Center and actual outcome (PR-9)

The existing administrator ResearchValidation page gains a pilot summary using one additive read-only API. Existing capture, rights, authority, PIT, dataset and production views are preserved. Exported snapshots remain explicitly distinct from live tests and synthetic fixtures. All nine new-real/approval/gate counts are zero. Required external actions are independently verified purpose rights, exact human-approved pilot scope, authorized isolated environment, historical market/publication/revision evidence, and production isolated Google A/B accounts plus eight deployment gates.

Build, integration, synthetic fixture, real acquisition and production acceptance evidence are separated in engineering-Phase4P.json and release verification. Actual production scheduler remains Disabled, REAL_BASELINE_V1 NOT_RUN, model NOT_PROMOTED and Phase5 LOCKED. Git/merge/deploy are authorized by the user's current request; data purchase, third-party login, restricted downloads, production database changes and real trades are not performed.

<!-- binding -->
Implementation source SHA: 8f257043d0575897dc4bea0837fdef0c241d7f27
Input Phase4O digest: 3E8B32C6D3946FA87A477B43D8A4DA951B42C116870066DF14CDC664EE95EC81
Export digest: 0E159694507DB4519AFB0A4B883D023CC9E4A2F3306398737CF3539CFC2D587D
Contracts: Phase4P-v1 / HistoricalNormalized-v1 / TrendSample-v1.
