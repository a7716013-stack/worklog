# Worklog 3.0.8 — Phase 4E implementation

## SOURCE EVIDENCE

StartSHA: `6c908d1d8b089c3ed15c9d6e8a69e5a3653874ae` (3.0.7). Release branch: `release/3.0.8-phase4e`.
Source specification: desktop `# Worklog Phase 4E.txt`, sections 0–100. Version 3.0.8 implements the acquisition/review/component-approval/baseline tools described below. **The Phase 4E exit criterion has NOT been achieved.**

New code is in `AcquisitionContracts`, `EvidenceAcquisitionStore`, `RealDataApproval`, `RealBaselineStore` and CLI `AcquisitionCommands`. Existing Candidate-v1 mathematics, parameters, outer weights, ResearchEngine mathematics, legacy calculators and production scoring are preserved. No database migration or website write API is introduced. The existing database free-tier/billing configuration is unchanged.

- Sections 3–9: independently identified acquisition tasks, controlled TWSE/TPEx HTTPS requests, eight explicit task states, attempt records, run input/output digests, failed response records, raw bytes, metadata and SHA256. Each run is reserved before network access. Failed acquisition retries use a new run identity. Captured does not imply Verified. Review history uses sequence/digest checks and CreateNew, with explicit Supported/Unsupported/Ambiguous claims.
- Sections 10–17: six availability precision values and an explicit `ConservativeUpperBound-v1` resolver. Supplied source-supported bounds are required even for session/day rules; no implicit 17:00 or market-close substitution. Unknown remains blocked. The resolver is a review tool, not an automatic converter of unreviewed provider records into PIT facts; original Phase 4D fact guards still apply.
- Sections 18–38: temporal identity facts, historical universe reconstruction and unknown-denominator reporting; benchmark observation and corporate-action temporal contracts; existing historical benchmark mapping, calendar and price policies are reused. Explicit price-basis compatibility utility rejects raw/adjusted version mismatches. Source-specific parsing/adjustment policies are not invented.
- Sections 39–48: per-component required domains and fact authorities, five independent domain statuses, exact-input dataset review, component receipts and full receipts only for all six components with weight 100. Bindings include the actual policy/membership/calendar digest, evidence review digest and current registry revision. Evidence coverage must include feature-lookback and outcome-session scope. Candidate-v1 requires a known historical membership even for individual time-series components; this existing contract is preserved. Institutional coverage is not required to approve Trend, Momentum or VolumePrice. Missing Institutional cannot create a full-model receipt.
- Sections 49–89: a separate baseline runner freezes approved feature/evidence rows before label evaluation, verifies label source facts and approved calendar, invokes existing Candidate-v1/ResearchEngine, and stores component/feature distributions, IC summaries, quantiles, correlations, quarter summaries, and full-model ablations only for full approval. Component-only output never emits FullEvidence100. Baseline identity binds parameters/protocol/approval and supports deterministic replay. No OOS selection, calibration, threshold, promotion or trading entry is enabled.
- Sections 90–97: deterministic `v3-real-data-contract` CI, independent of all live providers, joins the same-SHA gate. Real captures/reviews are operator-controlled outside CI.

The baseline completion gate is deliberately conservative: complete scoped evidence, authorized labels, actual IC/RankIC pairs and populated configured quantiles are required. Insufficient samples report BLOCKED_BY_DATA, never VALIDATED. It does not invent an investment-performance threshold or minimum number of years/stocks. Formula compilation/tests alone do not establish real baseline completion.

## EXECUTION EVIDENCE

Local full regression: `artifacts/phase308-regression.json`; all recorded exit codes are zero. Includes build, new real-data contracts, prior Authority/PIT/Research/Candidate/Legacy/SQL/Foundation/Evidence checks, CLI synthetic execution and replay. The new lane uses simulated transport and clearly labeled fabricated authority metadata to exercise approval, including the real-entry guard; it is not real-source research evidence.

Additional final targeted checks and unchanged-core hashes are recorded in `artifacts/phase308-final-verification.json`. Same-SHA remote CI and deployment details are written in the release verification artifact after release; do not infer their result from source existence.

Local startup: `Start-WorkJournal308.ps1`, port 5198. Schedulers are disabled locally. Evidence captures and research artifacts are excluded from the Azure web package. Existing Pkcs 9.0.11 NU1902/NU1903 warnings are unchanged.

## REAL DATA EVIDENCE

Two controlled acquisitions captured six real TWSE query pages. The first benchmark address returned HTTP 307 to a not-found page; this failed attempt was retained and the corrected historical-index address was captured separately. Six source reviews explicitly record unsupported historical authority claims. No review claims to establish historical publication/revision timing from current HTML.

Sources inspected:

- https://www.twse.com.tw/zh/trading/historical/stock-day.html
- https://www.twse.com.tw/zh/trading/foreign/t86.html
- https://www.twse.com.tw/zh/listed/suspend-listing.html
- https://www.twse.com.tw/zh/indices/taiex/mi-5min-hist.html
- https://www.twse.com.tw/zh/announcement/ex-right/twt49u.html
- https://www.twse.com.tw/zh/trading/holiday.html

These are query pages, not complete archived historical records. The corporate-action page describes reference-price formulas, which do not by themselves approve a complete historical adjusted-price policy. Query availability dates do not establish when each historical revision became available. The calendar page alone does not establish all historical sessions and emergency closures.

Raw bytes and immutable reviews are under local `artifacts/phase308-evidence/`; source/review digests and failure evidence are in [real evidence report](phase4e-real-evidence-3.0.8.json). The previously captured 21 historical TWSE facts remain unapproved. The new evaluator actually ran and returned `BLOCKED_BY_AUTHORITY_OR_DATA`: see [dataset gap report](phase4e-gap-3.0.8.json).

## RESEARCH EVIDENCE

Approved real authorities: 0. Approved real component datasets: 0. Approved full datasets: 0. Approved real PIT snapshots: 0.

Real baseline, IC, RankIC, quantiles, ablation, walk-forward and OOS: **NOT_RUN**. Only synthetic/fixture baseline calculation and replay were executed. V3 promotion: NOT_PROMOTED. V3Threshold: UNRESOLVED. Phase 5: LOCKED.

## KNOWN GAPS

The specification's section 98 hard stops apply: historical availability/revisions, denominator, benchmark mapping, corporate-action reconciliation and full calendar evidence are unproven. Sections 99–100 cannot be reported complete. No fabricated timestamp, current membership substitution, missing-to-zero conversion or weight redistribution was used.

This release does not claim complete historical source adapters: listing/symbol/market-transfer/suspension facts, benchmark definitions/observations, supported action types and extraordinary calendar sessions still require actual scoped sources, parsing and review. Unknown action types and unsupported adjustment calculations must remain blocked. The existing PIT builder requires explicit universe and benchmark identities to construct snapshots; component approval removes the all-six coverage requirement, but does not remove those existing identity prerequisites.

The new baseline diagnostics add annual/quarter summaries (Quarter=0 denotes the annual aggregate), per-date IC and correlation on a shared complete population. Custom outcome labels require a separate approved protocol and implementation. ResearchHypothesis is a record contract, not an automated calibration or change-selection workflow. No real baseline findings or hypotheses are manufactured.

Reviewers are local operator attestations, not enterprise authentication or digital signatures. Digests detect accidental changes and bind scope; an operator with unrestricted file-system access can rewrite the store. Independent review and protected backups remain operational responsibilities. Captured raw sources stay local; the Git repository contains audit summaries, not an assertion that external content is freely redistributable.

## CLI

Build `src/WorkJournal.Evidence.Cli` first. Pass these arguments to its executable:

```text
--evidence-acquire <plan.json> <evidence-store>
--evidence-replay <evidence-store> <run-guid>
--evidence-review <review.json> <evidence-store>
--evidence-task-state <evidence-store> <run-guid> <task-guid>
--evidence-task-review <evidence-store> <run-guid> <task-guid> <expected-digest> <status> <ISO-time> <reviewer> <reason> <review-digest-or-dash>
--evidence-export-authority <evidence-store> <review-digest> <phase4d-evidence.json> <authority-store>
--evidence-dataset-digest <submission.json>
--evidence-dataset-evaluate <submission.json> <evidence-store> <authority-store> <new-report.json>
--evidence-baseline <submission.json> <research-template.json> <evidence-store> <authority-store> <baseline-store>
--evidence-baseline-replay <baseline-store> <run-guid>
```

Evidence export, authority approval and dataset review are separate explicit steps. For blocked reviews, store the reason and pursue additional evidence instead of overriding the guards. The web deployment contains the library but does not schedule acquisitions or run research automatically.
