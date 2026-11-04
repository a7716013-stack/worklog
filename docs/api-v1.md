# WorkJournal REST API v1 — 3.1.1

`WorkJournal.Api` 是獨立 ASP.NET Core executable，引用現有 Web service assembly。
API controller 只做 HTTP 驗證與 DTO mapping；Candidate-v1、研究計算、排行評分已抽離至 Domain，排行協調與背景工作位於 Application。既有 hosting、EF 及 authority archive 仍有 Web 相依。

## 3.1.1 新增契約

| 路由 | 用途 |
|---|---|
| GET `/api/v1/market/radar/snapshot` | 有界每日完整行情，供網站跨市場／產業篩選；25 秒取得上限。 |
| POST `/api/v1/market/radar/jobs?market=all&date=YYYY-MM-DD` | CSRF 保護，202 + Location + Retry-After: 2；共用公開行情分析，無個人資料。 |
| GET `/api/v1/market/radar/jobs/{id}` | QUEUED/RUNNING/COMPLETED/TIMED_OUT/FAILED/INTERRUPTED、completed/total、部分結果、版本與錯誤碼。未知／重啟後 id 為 404。 |
| GET `/api/v1/research/features[/{featureId}]` | 19 項特徵定義與版本，列表分頁。 |
| GET `/api/v1/research/features/{featureId}/validation` | Feature 驗證資源；未執行為 NOT_RUN/value=null。 |
| GET `/api/v1/research/evidence/{evidenceId}/validation` | 六大 Evidence 驗證資源。 |
| GET `/api/v1/research/candidates[/{candidateId}]` | Candidate-v1 身分與分層狀態；無虛構 v2。 |
| GET `/api/v1/research/candidate-comparisons/{comparisonId}` | 受控、版本化 comparison release snapshot；不存在為 404。 |
| GET `/api/v1/research/hypotheses[/{id}]`、`calibrations[/{id}]` | 受控治理紀錄，無 public mutation。 |
| GET `/api/v1/research/runs/{id}/ablations`、`correlations`、`stability?period=year|quarter` | 分頁資源契約；無真實結果時 value=null，非零值。 |
| GET `/api/v1/models/{id}/definition`、`parameters`、`validation` | 版本身分、含明確未解參數的 Candidate-v1 與 full validation。 |
| GET `/api/v1/models/{id}/promotion-status`、`promotion-review`、`freeze-receipt`、`research-package` | 分層狀態或 summary + references，不自動核准／升級。 |
| POST `/api/v1/models/{id}/compute` | 登入 + CSRF，最多 4 MB；顯式 CandidateDataset/InstrumentId/Parameters，回 FormulaDigest、CalculationIdentity、raw/normalized trace。僅公式計算，ProductionEligible=false。 |
| POST `/api/v1/models/{id}/aggregate` | 登入 + CSRF，最多 16 KB；六個顯式 normalized Evidence 的低階聚合。FORMULA_ONLY_NOT_REAL_VALIDATION，不代表合法真實資料 baseline。 |

模型 id 為 `V3EvidenceCandidate-v1`。Feature id 如 `Trend.Alignment`，Evidence id 如 `Trend`。POST 的 token header 是 `RequestVerificationToken`，配合網站同來源 antiforgery cookie。未存在的資源回 404，錯誤沿用 ProblemDetails/code/traceId；queue 滿載回 429。Job 的 ANALYSIS_TIMEOUT、UPSTREAM_UNAVAILABLE、WORKER_STOPPED 是工作內錯誤碼，不偽裝 HTTP 成功研究。

資料及治理查詢沿用 `ApiResearchRead`；排行可匿名，但建立 job 仍須 CSRF。沒有新增公開 authority/calibration/promotion 寫入。既有同步 market/radar 契約保持，analyze 增加 45 秒總期限。新網站採 snapshot/jobs，避免將慢分析放在一個 HTTP 長請求。

## Hosting 與登入

本機與 Azure 使用 API executable 作為入口；`Api:HostWebUi=true`（預設）同時掛載既有 MVC UI，讓原本同來源 Cookie 登入與靜態資源繼續使用。
設為 false 可獨立執行 API host。此版沒有新增 Azure 付費資源，也沒有宣稱 Web/API 已部署為兩個服務。
Web executable 仍可獨立執行既有網站。兩種 host 共用 `JournalHosting.AddServices`。

AuthenticationMode = `ExistingUserSession`，Cookie `__Host-WorkJournal`。未實作公開 Bearer/OAuth API。
既有 Web 登入及 Google callback 路由不變。API 未登入回 401 ProblemDetails，不導向登入頁。
API 讀取權限要求已驗證 session 及 NameIdentifier；使用者只能讀取自己的 Paper 帳戶。
`ApiAuthorityAdmin` 要求 Administrator role，但本階段沒有 authority 寫入端點。
保留 TrustedOriginMiddleware、Production HTTPS/HSTS、MVC antiforgery。
不啟用跨來源 CORS。未來 Cookie 寫入必須加 CSRF 驗證；不能因路徑為 `/api` 而豁免。

## Endpoint catalog

| GET 路徑 | 權限／結果 |
| --- | --- |
| `/api/v1/system/health` | 公開；版本與編譯 SHA，不包含設定秘密 |
| `/api/v1/system/readiness` | 公開；啟動、必要 DB 設定與 SQL 可達性；不探測市場供應商 |
| `/api/v1/market/radar` | 公開；共用 IMarketRadarService；market/date/analyze |
| `/api/v1/market/industries` | 公開；透過 application adapter 沿用分類服務 |
| `/api/v1/evidence/models` | 公開；模型 metadata |
| `/api/v1/evidence/models/{modelVersion}` | 公開；精確模型 ID |
| `/api/v1/evidence/{instrumentId}` | ApiResearchRead；目前 503 DATA_NOT_READY，不輸出虛假分數 |
| `/api/v1/authority/domains` | ApiAuthorityRead；稽核快照 |
| `/api/v1/authority/claims` | ApiAuthorityRead；20 個 claim |
| `/api/v1/authority/claims/{claimId}` | ApiAuthorityRead；claim 範圍、證據 ID、revision、缺口 |
| `/api/v1/authority/coverage` | ApiAuthorityRead；真實資料里程碑與快照 revision |
| `/api/v1/research/runs` | ApiResearchRead；包含具名的 NOT_RUN baseline 計畫 |
| `/api/v1/research/runs/{runId}` | ApiResearchRead；`REAL_BASELINE_V1` 為未執行計畫，不是執行成功紀錄 |
| `/api/v1/research/runs/{runId}/metrics` | ApiResearchRead；NOT_RUN 的所有 metric 均為 null |
| `/api/v1/research/dataset-approvals` | ApiResearchRead；目前空集合 |
| `/api/v1/research/dataset-approvals/{approvalId}` | ApiResearchRead；不存在回 404 |
| `/api/v1/paper/account` | ApiPaperTrade；目前使用者帳戶 |
| `/api/v1/paper/positions` | ApiPaperTrade；目前使用者持倉 |
| `/api/v1/paper/orders` | ApiPaperTrade；目前使用者委託 |
| `/api/v1/paper/trades` | ApiPaperTrade；目前使用者成交 |

Paper 查詢走 IPaperTradingService，保留其首次查詢建立帳戶的既有行為。
沒有 order POST、reset、calibrate、promote 或 authority approve；治理仍由 controlled CLI 執行。
`/api/v1/radar` namespace 保留供未來追蹤功能，第一版市場 radar 位於 `/market/radar`。

## Contract

API v1 與 modelVersion、strategyVersion、parameterSetId 分別版本化。
集合統一 `page=1&pageSize=50`；page 1–1000000，pageSize 1–200。
回傳 `{items,page,pageSize,totalCount}`；市場回應的 quotes/analysis/events 各採相同分頁欄位。
Paper order/trade ID 是刻意定義的字串識別符，不用它當分頁 cursor。
交易日為 yyyy-MM-dd；時間為具 offset 的 ISO 8601；金額保持 decimal。
null 表示缺值或未執行，不能解讀為 0。

失敗回 `application/problem+json`：type/title/status/instance/code/traceId。
所有 API 回應帶 X-Trace-Id；記錄 route template、狀態、耗時與雜湊 subject，不記錄 token、Cookie 或 provider exception payload。
400 VALIDATION_ERROR、401 AUTHENTICATION_REQUIRED、403 FORBIDDEN、404 RESOURCE_NOT_FOUND、429 RATE_LIMITED、503 UPSTREAM_UNAVAILABLE/DATA_NOT_READY、500 INTERNAL_ERROR。
保留 409 CONFLICT/IDEMPOTENCY_CONFLICT/BLOCKED_BY_AUTHORITY、412 STALE_VERSION、422 BLOCKED_BY_DATA。
readiness 未就緒為 503 的明確 ReadinessDto，不是 provider 錯誤。
資料庫檢查最多等待 5 秒；逾時回 not_ready/databaseReachable=false，不讓 SQL retry 長時間拖住 health probe。liveness 不依賴 SQL。

## Model 與研究語意

模型 ID 沿用 `V3EvidenceCandidate-v1`，六項外層權重 25/15/15/20/15/10。
FullEvidence100 = Evidence Strength；不是 probability、expected return、position size、allocation 或 trade instruction。
AvailableWeight < 100 時完整分數 Missing，不重新分配權重。
V3Threshold=null、thresholdStatus=unresolved；不可帶入 Legacy Radar70/Swing75。

authority/research 目前使用隨版本發佈的稽核快照 `Research/authority-state.json`，非即時讀取受控 evidence store。
revision 綁定原始 evaluation digest；run 的 codeCommitSha 為該來源紀錄 SHA，不冒用本次 API 發布 SHA。
後續 approved receipt 的線上展示需更新受控快照；API 不自動核准，也不曝露本機 archive 路徑。
API PASS 與模型核准互不推導；Phase5=LOCKED。

## Rate limits、寫入與版本相容

`Api:RateLimits:{ReadPolicy|WritePolicy|ExpensiveResearchPolicy}:PermitLimit` 與 `WindowSeconds` 可設定固定視窗限流。
未設定時不套用虛構限額，ProductionRateLimit=UNRESOLVED。Write/ExpensiveResearch policy 已註冊，待對應寫入功能開放時使用。

未來經濟寫入需 ClientRequestId/Idempotency-Key；相同 key+相同 payload 回既有結果，相同 key+不同 payload 回409 IDEMPOTENCY_CONFLICT。
現有服務的 payload conflict gap 尚未修改；本版不公開寫入。Timeout≠Rejected、CancelRequested≠Cancelled。
未來可變設定用 ETag/If-Match，舊版本寫入回412 STALE_VERSION；本版不替所有 GET 加 ETag。

Development `/openapi/v1.json`；Production 預設關閉，可用 `Api:OpenApi:Enabled=true` 開放 JSON。未提供 Swagger UI。
CI 由 DTO/controller 產生 OpenAPI，比對 `docs/openapi-v1.json`，檢查 endpoint 移除、required 欄位增加、型別改變、enum 移除。
ApiV1ContractSnapshot 記錄 digest、關鍵 endpoint/schema/enum/problem codes。新增 api-contract 與所有既有 gate 必須同 SHA 通過。
