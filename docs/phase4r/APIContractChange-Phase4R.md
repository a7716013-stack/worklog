# Phase 4R API contract

Additive GET /api/v1/data/pilot/review returns Phase4RReport, a validated source-bound review snapshot. No request body or mutation. Existing /pilot, /pilot/readiness and older API contracts remain unchanged. Existing ResearchValidation consumes the new endpoint with same-origin credentials, no-store and a 15-second timeout. Application constructs and validates all review computations; Web only renders strings using textContent.

ApiResearchRead + ApiAuthorityAdmin, ReadPolicy and no-store are retained. Anonymous 401, non-admin 403, throttling 429, malformed/missing/digest-mismatched snapshot 503 DATA_NOT_READY (no local path or exception leak). POST is 405. No approval, acquisition, trust provisioning, raw download or consent action is exposed. DTO includes candidate URLs but no raw response, private filesystem root, secret or actual external receipt.

Read adapter validates the Phase4Q parent through existing Phase4P/Phase4I chain and recomputes the complete Phase4R projection, so merely resealing a fabricated report is rejected. FormulaDigest, source SHA, input versions, generated UTC timestamp and digest remain visible. Unknown coverage denominator remains null. Parent historical counts are explicitly prior-scope evidence, not 10-stock pilot coverage.

OpenAPI is generated from actual controllers/DTOs and exported by ApiContractChecks. Contract tests cover roles, no-store, corrupt reports, no write route, unchanged policies and page integration. Browser fixtures cover success, 401, 403, 429, 503 and non-admin no fetch. Local publishing only; user explicitly excluded online deployment.

The newly exposed PilotScope storageLimitBytes is documented as integer/int64. Legacy schemas retain their published representation for compatibility; the pre-existing generic Int64 schema issue in older DTOs requires a separately reviewed contract correction and is not silently rewritten here.

<!-- binding -->
Source SHA: 0386a1abd0124859007c4cdc16dd5ec61f8e332e
Input versions: Phase4Q-v1, PilotScope-v1, HistoricalNormalized-v1, TrendSample-v1
Evidence: Engineering audit/implementation; no external acceptance.
Generated UTC: 2026-11-23T19:05:09.1056726+00:00
Content SHA256 (before binding): 0c38a467547e18f726c147151acd746bc4015c3b8ee4e8460c796d7d40cfe904
Parent digest: 48C90EF529153208B2D4AFF856547722D2920E298681F460792A5210A5043BAA
Export digest: D4B985EA47BB8FC406C13E37703DDFD5B6D96CC42CE0339B43C8BD58CB7519AB
