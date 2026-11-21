# Phase 4P API contract

Existing `/api/v1/data/operations`, `/api/v1/data/captures`, authority, PIT, dataset readiness and operations endpoints remain compatible. The additive `GET /api/v1/data/pilot` supplies the new pilot scope, nine actual counts, historical context and feature/outcome readiness to the existing ResearchValidation page. It requires both ApiResearchRead and ApiAuthorityAdmin, ReadPolicy rate limiting, no-store, and existing 401/403/429/503 ProblemDetails semantics. No POST/approval/acquisition route is exposed.

The response is a validated immutable Phase4P-v1 export. It contains only fixed metadata, counts and blockers: no provider credentials, raw bodies, filesystem locations, arbitrary external reviewer notes or user data. A missing/tampered parent or report fails closed with DATA_NOT_READY. Historical v3.2.4 exports retain their original version and digest; the new pilot endpoint is v3.2.5. OpenAPI is generated from actual controller/DTO definitions and checked for backward compatibility.

RealAcquisitionWorker, PilotScopeRules, PilotMarketContextRules and PilotFeatureOutcomeFactory live in Application; formulas remain in Domain. Web only fetches and renders metadata. No existing formula is copied into a controller or JavaScript. Existing API-to-Web hosting/service references are a pre-existing transition; this release does not claim to complete the whole site's extraction.

Deployment keeps the existing API host, cookie authentication and same-origin UI. No new database tables or migrations, provider secrets, hosted timers or production scheduling are introduced. Runtime version/SHA and both new endpoint authorization and static asset identity are verified after deployment. Production member acceptance remains separate from fixture identity tests.

<!-- binding -->
Implementation source SHA: 8f257043d0575897dc4bea0837fdef0c241d7f27
Input Phase4O digest: 3E8B32C6D3946FA87A477B43D8A4DA951B42C116870066DF14CDC664EE95EC81
Export digest: 0E159694507DB4519AFB0A4B883D023CC9E4A2F3306398737CF3539CFC2D587D
Contracts: Phase4P-v1 / HistoricalNormalized-v1 / TrendSample-v1.
