# Phase 4O API 契約 — 3.2.4
既有 /api/v1/data/providers 已提供ProviderCapabilities，ETL jobs/quality/lineage、Authority workbench/packets、PIT/dataset/readiness與operations dashboard繼續重用。缺少的分項rights、pilot plan及capture history以兩個管理唯讀契約補足：
- GET /api/v1/data/operations → Phase4OReport：Source SHA/parent digest/export time、三個既有provider policy、四項rights、有限pilot草案、真實成果計數、warehouse、quality/reconciliation狀態。
- GET /api/v1/data/captures → PageDto<CaptureRun>：僅發布的capture metadata，page>=1/pageSize<=200；目前真實history為空，隔離fixture不進發布快照。
兩端點同時要求ApiResearchRead+ApiAuthorityAdmin（Administrator），ReadPolicy、no-store；匿名401、會員403、錯誤分頁400、無資源快照/篡改503 DATA_NOT_READY、限流429；不新增HTTP POST，不能下載/啟用排程/核准資料。無BodyBase64、token、credential或本機路徑，raw archive只供受控本機CLI。
新聚合/權利/擷取/品質/比對運算在Application。CLI重用同一核心；API adapter只讀與驗證發布snapshot。Web為同源Cookie GET展示，15秒逾時，背景頁清空、返回時重新驗證，401/403清空；一般會員不呈現管理section。ResearchValidation沿用既有頁，不建立新dashboard。
SourceCodeSha指產生快照的實作commit，system/health指執行中的release SHA；Schema Phase4O-v1引用Phase4N-v1 digest，後者連結I/J/M與原FormulaDigest。沒有公式/權重/缺值/PIT語意變更。舊DTO不變，OpenAPI由實際Controller/DTO生成並檢查相容。Api→Web hosting/Identity過渡相依仍存在。

<!-- binding -->
Source commit: 4357d1a07e5bbb67c256081925cfdedd737235ad
Parent Phase4N digest: C93F4253A044CC62C57579A236DF63F652B2F3D4BA56E8CEB8648288B1083675
Data contracts: Phase4O-v1 / HistoricalNormalized-v1 / TrendSample-v1. FormulaDigest: 81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB
