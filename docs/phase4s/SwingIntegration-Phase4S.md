# Swing integration

SwingCalculator and SwingBacktester moved byte-for-byte to Domain with their original namespace. Application IUnifiedStockScoringService is shared by API and transitional MVC. FinMindStockService remains the legacy data/cache provider; its acquisition behavior is unchanged outside the explicitly isolated guest host. Candidate has no path to that live provider.

GET /api/v1/scoring/swing?symbol=2330 returns SwingAnalysis with the existing summary, seven strategies and chart plus a versioned comparison. Swing UI now uses this API. Legacy score/strategies/risk warnings remain authoritative for their existing purpose. Missing Candidate components remain null and the fixed six outer weights are not redistributed.

GET /api/v1/scoring/backtest/{symbol} returns a Legacy result through the existing cache/gateway guard alongside Candidate model identity and NOT_RUN/NOT_PROMOTED statuses. Application Backtest also offers a provider-free comparison seam for CLI/tests. Candidate performance is never inferred from Legacy or fixtures.
