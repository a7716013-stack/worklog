# Worklog 3.3.1 — Phase 4S Unified

Implements the v3.2.8 Phase 4S specification with the user-requested delivery version 3.3.1, continuing unmerged PR #25. Includes the previously accepted local-only guest entry. No online release/deployment, production database change, real acquisition, approval, ETL enablement or promotion.

Shared adapters reuse both Legacy calculators and Candidate-v1. Swing computation/backtesting is now in Domain, not Web. API contracts, radar/Swing/snapshot presentation, append-only snapshot metadata, routing denial and isolated guest startup are implemented. Web retains data/provider, EF persistence and UI orchestration because those existing dependencies are outside this formula migration; no claim is made that all Web services have moved.

No complete authorized Candidate dataset exists in this release. Shadow and authorized Preview expose blocked readiness; no real score comparison is manufactured. CandidatePrimary rejects even for administrators. CandidateFixture is an internal test/CLI seam and is permanently labeled synthetic. Real baseline/OOS NOT_RUN, promotion NOT_PROMOTED, Phase 5 locked. Seven PIT gates, six independent dataset approvals and TrendSample-v1 thresholds are unchanged.

Pilot remains 10 stocks / 3 historical months / 30 unique requests / max 90 attempts, REVIEW_ONLY_NOT_APPROVED. Acquisition count is zero.

See RegressionReport for actual local test evidence, and PR checks for CI tied to the uploaded head SHA. Local site uses Start-WorkJournal3310.ps1 on localhost:5212 with isolated synthetic data. External-only account functions retain their external requirements.
