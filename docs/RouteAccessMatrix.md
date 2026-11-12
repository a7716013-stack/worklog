# Route access matrix — 3.2.1

| Route group | Anonymous | Member / ownership |
|---|---|---|
| StockAnalysis Index, MarketRadar, basic MarketRadarData, RadarIndustries; API radar/snapshot/industries | Public | Shared basic market data |
| Swing, Strategies, StrategySignal | Login redirect | Feature.SwingAnalysis; shared strategy signal binding |
| SwingSearch/Data/Backtest, ResearchData, StockRadar | 401 JSON | Feature.SwingAnalysis |
| MarketRadarData / API market/radar analyze=true | 401 JSON | Feature.SwingAnalysis |
| API market/radar/jobs POST and GET status | 401 JSON | Feature.SwingAnalysis; POST CSRF; shared system job |
| Watchlist/SaveWatchlist, FavoriteItems/SaveFavorites, StockMembership/StockPerformance | 401 JSON | Feature.SwingAnalysis; server owner; writes CSRF |
| RadarTracking/Performance/Snapshot, personal tracking writes | Login redirect | Feature.SwingAnalysis; recommendation/owner checks; writes CSRF |
| PaperTrading + Place/Cancel/Refresh/ResetPaperOrder forms | Login redirect | Feature.PaperPortfolio; server owner; writes CSRF |
| PaperQuotes, PortfolioHistory | 401 JSON | Feature.PaperPortfolio; server owner |
| API paper account/orders/positions/trades | 401 JSON | Existing ApiPaperTrade + Feature.PaperPortfolio; server owner |
| ResearchValidation and authority/research/ETL APIs | Page redirect / API 401 | Existing research read policies; no approval authority |
| Account login/Google callback/logout | Existing Identity rules | Local return URLs, state/correlation, logout CSRF |

HTML redirects preserve the requested local return URL. JSON errors never redirect to HTML. Protected responses use no-store. The inventory below records current attributes for every StockAnalysis action, including unchanged public quote/detail helpers. No controller-wide membership attribute was introduced.


### StockAnalysisController.cs

- `Swing`: [Microsoft.AspNetCore.Authorization.Authorize, HttpGet, ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing)]
- `Index`: [HttpGet]

### StockAnalysisController.Favorites.cs

- `FavoriteItems`: [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
- `SaveFavorites`: [Authorize, HttpPost, ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
- `Favorites`: [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
- `ChangeFavorite`: [Authorize,HttpPost]
- `StockMembership`: [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
- `StockPerformance`: [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
- `TrackStock`: [Authorize,HttpPost]

### StockAnalysisController.Insights.cs

- `MarketPulse`: [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
- `PortfolioHistory`: [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Paper, true)]

### StockAnalysisController.MarketRadar.cs

- `RadarIndustries`: [HttpGet]
- `MarketRadar`: [HttpGet, ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
- `MarketRadarData`: [HttpGet, ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)] [WorkJournal.Web.Security.AnalysisAccess]

### StockAnalysisController.PaperQuotes.cs

- `PaperQuotes`: [Authorize, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Paper, true)]

### StockAnalysisController.PaperTrading.cs

- `PaperTrading`: [Microsoft.AspNetCore.Authorization.Authorize, HttpGet, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Paper, false)]
- `PlacePaperOrder`: [Microsoft.AspNetCore.Authorization.Authorize, HttpPost, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Paper, false)]
- `CancelPaperOrder`: [Microsoft.AspNetCore.Authorization.Authorize, HttpPost, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Paper, false)]
- `RefreshPaperOrders`: [Microsoft.AspNetCore.Authorization.Authorize, HttpPost, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Paper, false)]
- `ResetPaperAccount`: [Microsoft.AspNetCore.Authorization.Authorize, HttpPost, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Paper, false)]

### StockAnalysisController.RadarTracking.cs

- `RadarModelValidation`: [FeatureAccess(FeaturePolicies.Swing),HttpGet]
- `RadarTracking`: [FeatureAccess(FeaturePolicies.Swing),HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
- `RadarPerformance`: [FeatureAccess(FeaturePolicies.Swing),HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
- `RadarPaperComparison`: [FeatureAccess(FeaturePolicies.Swing),HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
- `RadarSnapshot`: [FeatureAccess(FeaturePolicies.Swing),HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
- `CaptureMarketRadar`: [FeatureAccess(FeaturePolicies.Swing),HttpPost]
- `UpdateRadarPerformance`: [FeatureAccess(FeaturePolicies.Swing),HttpPost]
- `FollowRadarRecommendation`: [FeatureAccess(FeaturePolicies.Swing),HttpPost]
- `AddPerformanceValidation`: [FeatureAccess(FeaturePolicies.Swing),HttpPost]
- `StopRadarTracking`: [FeatureAccess(FeaturePolicies.Swing),HttpPost]

### StockAnalysisController.Research.cs

- `ResearchData`: [HttpGet] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
- `StockRadar`: [HttpGet] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]

### StockAnalysisController.Strategies.cs

- `Strategies`: [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, false)]
- `StrategySignal`: [HttpGet] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, false)]

### StockAnalysisController.Swing.cs

- `SwingSearch`: [HttpGet] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
- `SwingData`: [HttpGet] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
- `SwingBacktest`: [HttpGet] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]

### StockAnalysisController.Watchlist.cs

- `Watchlist`: [Authorize, HttpGet, ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
- `AddToSwing`: [Authorize, HttpPost]
- `SaveWatchlist`: [Authorize, HttpPost, ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)] [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing, true)]
