# Member access audit — 3.2.1

Scope: all StockAnalysis partial controllers, AccountController, AuthenticationRegistration, current-user contract, paper/portfolio services, radar/strategy controllers, formal APIs, views/fetch and scheduler contexts. Baseline and architecture audit: Audit-3.2.1.md.

Observed: anonymous Swing page redirects, but associated JSON APIs are currently exposed. Radar analysis/jobs and strategies are also exposed. Paper actions require authenticated users, and account/order/reset operations already use current-user account scope. Extend rather than replace this boundary. Shared system strategy signals are accessible to members, not anonymous visitors; personal recommendations/order links require their existing ownership checks.

Required correction: centralized Feature.SwingAnalysis and Feature.PaperPortfolio policies, metadata-based MVC JSON challenges (401/403), no-store for all protected responses, UI expired-session handling and back/forward cache reload, route inventory and cross-user regression checks. Existing local-only ReturnUrl, Google OAuth, CSRF and cookie security must remain compatible. Ordinary login grants no research approval/admin operation.

Basic stock Index, industries and non-analytical market quote snapshots remain public. Analysis, backtests, scan jobs and strategy derivatives are protected by feature, not broad StockAnalysisController authorization.
