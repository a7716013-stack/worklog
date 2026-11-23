# Worklog 3.2.7 — Phase 4R

## Work packages and architecture
Packages 1-10 follow Audit/GapMatrix. Phase4ROperations in Application composes validated prior evidence and the existing PilotScope/CapturePlan, rather than duplicating rights approvals, acquisition, PIT or formula logic. API and CLI reuse this module. Existing transport/archive/ETL/authority and scoring remain shared; no new Web computation or database migration. Independent scope/rights reviewer checks now reject case-only equality, consistent with existing environment receipt validation.

## Rights decision and compatibility
Public metadata/terms inspected 2026-11-24: https://www.twse.com.tw/zh/terms/use.html (clauses 6/8), https://data.gov.tw/dataset/11549 (metadata 2025-05-01, daily CSV resource), https://data.gov.tw/license (OGDL-1.0). This is source-discovery metadata, not an independent grant or archived raw-page digest. Exact monthly STOCK_DAY coverage remains UNKNOWN for ACQUIRE/STORE/RESEARCH/REDISTRIBUTE. Independent reviewers must bind original applicable evidence, purpose, exact plan IDs, expiry and retention through existing CapturePermission and pinned trust. Review notes alone never authorize a run.

Dataset 11549 daily CSV is an alternative compatibility candidate: recognizable date/instrument/OHLC/volume fields, but different CSV/JSON shape, no proven monthly backfill/version/publication/identity coverage. Existing parser and endpoint allowlist are not changed and no new provider is connected. No market-data URL was fetched. Covered-resource licensing cannot be generalized to a different historical endpoint.

## Candidate pilot, not approved
User requirements: 10 stocks, 3 historical months, 30 unique requests, at most 90 total attempts including retries. Candidate symbols are the first ten of the prior review list: 1101, 1102, 1216, 1301, 1303, 1326, 1402, 1476, 1590, 1605. Proposed months July-September 2026; historical identity/trading calendar unverified. This is not an asserted historical universe or a selection recommendation.

Reuse exact TWSE STOCK_DAY monthly URLs and existing HistoricalNormalized-v1. Draft values: 64 KiB per response, 16 MiB isolated storage including metadata, 90-day retention, UTC execution window 2026-12-01 through 2026-12-08 (exclusive end), unassigned operator, no rights references. All require human review. Storage and response limits are ceilings, not proof of capacity sufficiency. Existing 20-attempt/hour and three-attempt/request limits remain. Scope/plan IDs differ from 4Q; all changes to budgets/operator/time/source/rights require new scope digest and re-review. No old approval is reused.

CLI: --phase4r-draft-scope <compiled-sha> <new-output> exports the existing PilotScope contract. --phase4r-export <phase4q-report> <phase4p-report> <phase4i-report> <compiled-sha> <new-output> validates parent evidence and writes a new immutable review report. Both require matching compiled SHA through existing CheckCode. Neither performs acquisition or produces grants. Only a separately authorized operator can later use existing --phase4p-acquisition ACQUIRE_ONCE after every external gate passes.

## Environment and capture verification
17 safety-control checklists enumerate required actual evidence, with seven additional binding requirements: ACL, storage root, source SHA, environment digest, independent reviewer, expiry/revocation, integrity. All actual acceptance remains BLOCKED; no receipt or per-run consent is created. Existing IsolatedEnvironmentReceipt/AcquisitionRunConsent and externally pinned trust remain the authority. Digests alone are not identity signatures.

Capture verification lists raw digest, immutable version, ObservedAt, RetrievedAt, publication, revision, identity, date, units, quarantine, replay and lineage. No real run exists: counts zero and replay NOT_RUN_NO_REAL_CAPTURE. Existing archive/ETL/quality/replay tests are fixtures/integration evidence, not Live Acquisition PASS. ObservedAt and RetrievedAt never become original PublishedAt.

## Authority, PIT and research
Reuse existing AuthorityReview/EvidenceResolutionPacket/ComponentDatasetStore. Eight priority historical domains are inherited with explicit prior-reviewed-scope labels; new scope stays UNPROVEN without original evidence/reviewer/time range/digest. Seven PIT gates are shown individually; one supported price observation does not yield overall PIT eligibility. Existing 21 facts (20 Price, 1 Institutional) are preserved, new facts 0; new scope denominator unknown.

Acquisition Ready, Dataset Eligible, Research Ready are separate. Ten proposed stocks are not ten eligible daily instruments, and three months do not prove the 250-session feature lookback, per-fold 20 dates/200 outcomes, or independent OOS. Candidate-v1, 19 features, weights 25/15/15/20/15/10, original FormulaDigest and TrendSample-v1 remain unchanged. Baseline/OOS NOT_RUN, NOT_PROMOTED, Phase5 LOCKED. No research thresholds lowered.

## Operation and evidence
Existing admin ResearchValidation adds Phase4R review, environment/consent, capture/replay, authority/PIT and three readiness sections through a secure read-only API. Previous panels remain historical context. Actual production gates remain 0/8; real Google A/B acceptance unavailable. ETL stays Disabled. New local launcher uses port 5211 with existing background scanners disabled.

Reports bind implementation source commit SHA separately from final Git commit containing generated reports, input versions, parent digest, evidence class, UTC generation time and artifact hash. No self-referential commit hash is claimed. Engineering test logs and CI attestations are separate from live capture and production acceptance. Local browser fixture success is not real OAuth acceptance. This task uploads a review branch/PR only; no main merge, GitHub release or website deployment.

Next safe action: independent review of exact endpoint rights and candidate symbols/months/budgets, then real isolated environment acceptance and per-run consent. Development/Git authorization is not authorization for download, paid data, production SQL changes, scheduler activation, baseline/OOS, trading or Phase5.
