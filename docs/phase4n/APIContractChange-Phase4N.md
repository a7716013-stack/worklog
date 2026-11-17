# Phase 4N API 契約 — 3.2.3
實作 SHA / 資料版本見 OperationsReport-Phase4N.json；基準 main 215ad7e947df02eb49eef4fe37e6207a1001a7f5。
新增 GET /api/v1/operations/dashboard → OperationsDashboardDto、GET /api/v1/authority/resolution-packets → PageDto<ResolutionPacket>。Phase4M 原 readiness/workbench/acquisition-evidence 缺少具體 deployment evidence、acceptance 與完整 packet；本次兩個管理唯讀契約補足，不複製舊端點。既有會員 API/DTO 保留。
兩端點同時要求 ApiResearchRead + ApiAuthorityAdmin（Administrator）、ReadPolicy、no-store。packets 的 page>=1/pageSize<=200 沿用 PageQuery，無效400、匿名401、一般會員403、快照缺失/損壞503 DATA_NOT_READY、限流429。沒有 HTTP 寫入，不新增 CSRF 寫入面或核准流程。
所有聚合/狀態映射位於 Application Phase4NOperations，CLI/API 共用。API adapter 僅讀取並驗證 source SHA、I/J/M digest、報告 digest 與可重建投影。Web 同源 Cookie、15秒timeout、401/403清空、背景頁清除並於返回時重新取得。不在瀏覽器計算公式/資格。
新 DTO 欄位與 OpenAPI 從實際型別生成，測試相容性、401/403、分頁、no-store、無POST、digest篡改、scope/expiry/可信receipt gate。無資料庫 schema 改動。部署 Api host、保留既有 Api→Web hosting/Identity 過渡相依。
BaselineReadinessDecision 為既有六階段權威狀態的顯示映射：Authority阻塞優先，其次缺資料核准、樣本不足、未執行；既有COMPLETED且充分樣本才顯示READY（可重播既有受控結果，非新的授權）。Candidate-v1、19 features、25/15/15/20/15/10、原FormulaDigest、七PIT gates、六獨立核准不變。TrendSample-v1每fold Train/Validation至少20日期、每日max(10,quantiles)、200 pairs；OOS獨立。缺值不補零、不補造availableAt。

<!-- binding -->
Source commit: 791f71b233d12b35803a034bf4efeab0129e9a89
Data: Phase4I-v1 9A33F33040B97AC61FAB1BCBA4021C182F59D9A12A33E3F1F14BAC2F881815BF; Phase4J-v1 FD958D5B386DD103DE7B51F3105E4D010CF494A77EBF97C9EC223708192000C6; Phase4M-v1 4720DDF7889DAAECB62E4098D07C5914EBC7E3023601187BF96B3DFCA000EEC9
