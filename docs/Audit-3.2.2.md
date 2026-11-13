# Phase 4L audit — 3.2.2

GitHub main rechecked before work: `01ee9b02295460a997d1db608b247f5aec442695`; all 633 Git blobs match local files. Read root AGENTS, desktop Phase 4L instruction, 3.2.1 implementation/engineering, ETL architecture/provider capability, access audit/routes/implementation/tests, closure/PIT/readiness/model reports. Prior release has 15/15 PR and main CI jobs; 392 API/SQL assertions and 126 Identity/Google-fixture assertions. These are prior evidence, not new 3.2.2 results.

| Phase | Existing capability | Gap / implementation |
|---|---|---|
| Security | Feature policies, Identity, owner-scoped paper and recommendation services, 401/403, CSRF, no-store | Add production-mode integration acceptance and captured-cookie logout replay; production A/B requires dedicated accounts. Never use another real member's data |
| Scheduler | Immutable EtlPipeline, per-job exclusive filesystem lease, three attempt receipts; Phase4J network quota/transport | Persist scheduled definitions, UTC leases/heartbeats/fencing generations, bounded retry/cancellation, recovery after commit, explicit calendar and provider circuit/quota; controlled worker only |
| Closure | 13 source versions, 43 tasks, 23 version-bound attempts, 20 claims and reviewer receipts | Workflow projection binds requirement, priority, role owner, next action, review/closure receipt and invalidation to existing reports; no alternative approval ledger |
| Monitoring | Six authenticated ETL GET APIs, Phase4J sources/plan, Phase4I PIT/readiness | Existing six DTOs unchanged; one new operations snapshot resource supplies schedules, closure work and alerts missing from those APIs |
| Trend | Seven PIT gates, six independent approvals, TrendSample-v1 and existing baseline guard | Surface existing exact readiness and blockers; retain NOT_RUN/INSUFFICIENT_SAMPLE distinction; no formula change |
| Release | Same-SHA CI, immutable source-bound reports, Azure package checks | 3.2.2 evidence separates fixture execution, real archive ETL, anonymous production probes and unavailable member production acceptance |

Application owns scheduler/closure/operations logic, API serves authenticated read DTOs, CLI runs controlled workers and export; Web only renders API results. Reuse existing EtlPipeline/provider parsers, Phase4J acquisition and reviewer/PIT stores. No hosted scheduler is registered in production. Filesystem durable coordination requires workers to share one volume with exclusive locks and atomic rename; independent hosts without shared storage are unsupported and must not be enabled.

User explicitly authorizes implementing 3.2.2, Git push/merge and deployment, overriding the document's default publication restriction. Production scheduler activation, production DB modification, provider purchases/licensing, trading, dataset approval and promotion remain unauthorized. Production isolated test-account availability was requested; pending or unavailable items must be recorded as BLOCKED, never PASS.

Implementation order: audit → security acceptance → scheduler/recovery → closure → operations UI/API → Trend/regression/release evidence. Historical availability/revision remain unproven: 21 real parsed facts, 1 observation, 0 PIT eligible, 0 approved, baseline NOT_RUN, Phase5 LOCKED.
