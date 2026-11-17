# Production Acceptance Toolkit — 3.2.3 / Phase 4N
基準 SHA 215ad7e947df02eb49eef4fe37e6207a1001a7f5。實作 SHA、資料 digest 見 OperationsReport-Phase4N.json。正式 A/B 驗收 BLOCKED：沒有已授權且隔離的測試帳號。不建立帳號，不保存 Cookie/Token，不操作其他會員資料。

使用兩個獨立瀏覽器工作階段及人工作業；不得提供登入憑證給自動化工具。先記錄環境、release SHA（system/health）、UTC、匿名 A/B 代號、檢查代碼、預期／實際狀態、PASS/FAIL/BLOCKED、去識別證據摘要 SHA256、獨立 reviewer、重驗規則。禁記 email、會員/訂單真實 ID、請求 header、Cookie、Token、密碼、敏感截圖或原始回應。摘要本身不是可信驗收，須有受控的獨立審查回執。

| 檢查 | 操作與通過條件 |
|---|---|
| GOOGLE_OAUTH | 人工正常登入專用帳號、拒絕/取消授權皆安全返回；不記 code/token |
| MEMBER_AB_PAPER_PORTFOLIO | 僅用 A/B 自己的 fixture portfolio，互相不可見資金/持倉；不得送真實交易 |
| PAPER_ORDER_IDOR | A/B 僅使用對方事先同意的測試 order；跨 owner 讀寫均拒絕且無狀態改變 |
| WATCHLIST_STRATEGY_RECOMMENDATION | A/B 名單、策略與建議不可交叉洩漏或變更；逐項檢查 |
| ANONYMOUS_API_MVC | API 401 ProblemDetails、受保護 MVC 302 到登入、無敏感 response |
| LOGOUT_COOKIE_REVOCATION | A 登出後，在原工作階段或記憶體中的舊請求驗證失效；不得匯出 Cookie；B 不受影響 |
| SESSION_EXPIRATION | 等待受控短效測試 session 到期，受保護 API 401，頁面清除管理資料；不得修改正式 session 設定 |
| CSRF | 專用測試資料寫入缺/錯 CSRF 均拒絕，合法同源正常；不得用他人資料 |
| RETURN_URL | 外部/協定相對 ReturnUrl 不得導向外站；合法本機路徑可返回 |
| PUBLIC_MARKET_QUOTES | 匿名基本行情可讀且保留原契約；需會員分析仍拒絕匿名；供應商故障標503而非偽造行情 |

匿名 smoke：`deploy/Verify-Phase4M.ps1 -BaseUrl <url> -ExpectedVersion 3.2.3 -ExpectedSha <release-sha> -Output <new-report>`。此腳本沿用既有七項 GET 檢查，不等同 OAuth/A/B/CSRF/完整行情驗收。
安全記錄模板：`ProductionAcceptanceRecord-Phase4N.json`。僅保存去識別摘要於受控目錄，人工 reviewer 確認後另存不可覆寫回執。Fixture 成功與 Verified Live 分開；正式驗收未完成維持 BLOCKED。

<!-- binding -->
Source commit: 215ad7e947df02eb49eef4fe37e6207a1001a7f5
Data: Phase4I-v1 9A33F33040B97AC61FAB1BCBA4021C182F59D9A12A33E3F1F14BAC2F881815BF; Phase4J-v1 FD958D5B386DD103DE7B51F3105E4D010CF494A77EBF97C9EC223708192000C6; Phase4M-v1 4720DDF7889DAAECB62E4098D07C5914EBC7E3023601187BF96B3DFCA000EEC9
