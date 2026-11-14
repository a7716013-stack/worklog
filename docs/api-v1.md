## 3.2.2 — 排程與監控

新增 GET /api/v1/etl/operations，沿用會員授權、ReadPolicy、no-store；無新增寫入或核准端點。詳 [契約變更](APIContractChange-3.2.2.md)。

## 3.2.1 — ETL 與會員存取

六個新唯讀 ETL/provider API，分析工作與私人 JSON 採明確會員授權。基本行情仍公開。詳見 [契約變更](APIContractChange-3.2.1.md) 與 [路由矩陣](RouteAccessMatrix.md)。

## Phase D — 3.1.4

新增 sources、acquisition-plan、acquisition-attempts/{taskId}、research/components/{component}/readiness；既有 dataset readiness 增加 builds。詳見 [APIContractChange-3.1.4.md](APIContractChange-3.1.4.md)。

# API Phase C — Worklog 3.1.3

五個新增唯讀資源，重用已存在的 authority、eligibility、coverage、provenance 與研究契約；不公開 approval、promotion 或交易寫入。

| GET route | Policy | Result |
|---|---|---|
| `/api/v1/authority/closure-queue?page=1&pageSize=50&domain=Availability` | ApiAuthorityRead | 20 個 domain:claim 唯一鍵、priority、scoped requirement、source/reviewer decisions、歷史適用性，分頁及 domain 篩選。 |
| `/api/v1/authority/claims/{id}/evidence` | ApiAuthorityRead | ClaimRequirement、raw identity、retrieval/review timestamp、原始 review digest、blocking reasons。建議使用 queue 回傳的 domain:claim id。 |
| `/api/v1/research/datasets/{id}/readiness` | ApiResearchRead | DatasetReadiness、明確計數口徑、六組件獨立核准及研究樣本狀態。 |
| `/api/v1/research/runs/{id}/validation-summary` | ApiResearchRead | Trend sample policy、各 fold/partition/horizon 樣本檢查、nullable metrics/IC/correlation/stability。 |
| `/api/v1/research/runs/{id}/replay-status` | ApiResearchRead | 發布檔讀取完整性與受控匯出時原始證據／baseline 重播，兩者分開標示。 |

保留既有 `/authority/claims`、claim detail、`/research/eligibility-summary`、dataset coverage、PIT observation eligibility、run provenance 及 metrics。新 path 不改舊 DTO。SourceIdentity 在兩個 domain 存在，無 domain 的新 evidence 查詢回 400/AMBIGUOUS_CLAIM_ID；原有 claim detail 行為保留相容。不存在 404、未登入 401、錯誤頁碼/domain 400、報告遺失/損壞/與 validation snapshot 不一致 503/DATA_NOT_READY。沿用 ProblemDetails、X-Trace-Id、ReadPolicy、同來源 Cookie 與使用者隔離。

所有新增狀態為明確字串；嵌入既有 DTO 的 enum 保留原數字格式（eligibility 0=BLOCKED、1=READY_FOR_CONTROLLED_BASELINE；ResearchPartition 0=Train、1=Validation、2=Oos）。NOT_RUN 與 INSUFFICIENT_SAMPLE 的 metrics/IC/correlations/stability/outputDigest 皆 null，不能解讀為零。未核准資料為 NOT_RUN；INSUFFICIENT_SAMPLE 必須已核准且經受控評估。

Readiness 計數：totalFacts=21、priceFacts=20、institutionalFacts=1、benchmarkFacts=0、uniqueFacts=21、duplicateFacts=0、requestedObservations=1、eligibleObservations=0、approvedResearchSamples=0。這些是解析後 source facts，不宣稱為原始 CSV 實體行數。舊 RawRealRows=21 仍是 totalFacts alias。additionalRevisions 只計算同 provider/type/instrument/date 的額外 observed fact versions，不能當成已證明的 historical RevisionBehavior。缺少母體分母維持 null。

目前正式發布的 run id 為 REAL_BASELINE_V1（尚未執行計畫）。受控成功 run 使用其實際 GUID，需經審查發布新的 Phase4I snapshot，不因 HTTP 查詢自動執行研究。舊 planned-run provenance 不冒充新 run。Evidence VERIFIED_AT_EXPORT 記錄匯出時 raw/review/approval 校驗，VERIFIED_ON_READ 只表示當次發布報告 hash 與資料一致性檢查；HTTP 不接觸內部 archive。完整 baseline 重播只有真實已執行且 replay 成功才能標示 VERIFIED_AT_EXPORT。

程式：Application 的 Phase4IWorkflow/Phase4IProjection/Phase4IReadService；API adapter 將儲存錯誤轉成穩定錯誤碼。Controller 不讀 DbContext 或供應商，不計算模型。網站沿用 ResearchValidation 並呼叫上述 API 及既有 coverage/provenance。OpenAPI snapshot 見 [openapi-v1.json](openapi-v1.json)。

---

# Worklog API v1 — 3.1.2 契約增量

沿用 ApiResearchRead、ReadPolicy、trace ID 與 ProblemDetails；四個路由皆唯讀，不公開審查或核准寫入功能。

| GET 路由 | 契約 |
|---|---|
| `/api/v1/research/eligibility-summary` | 資料集、六組件獨立狀態、固定權重、阻擋原因、公式與參數 digest、報告 digest。 |
| `/api/v1/research/runs/{runId}/provenance` | `REAL_BASELINE_V1` 是尚未執行的計畫識別；ResearchStatus=NOT_RUN、ResearchOutputDigest=null。回傳 dataset/version set、request/eligibility/source digest、核准及 code SHA。 |
| `/api/v1/pit/observations/{id}/eligibility` | 七道 gates、decision time、nullable availableAt、source versions、authority receipts、blockers。id 可由 coverage 找到。 |
| `/api/v1/research/datasets/{id}/coverage?page=1&pageSize=20&from=2026-09-01&to=2026-09-30` | 資料集全範圍 counts 與日期過濾後的 observation 分頁；日期包含首尾，from>to 為 400。 |

外層 summary.status 為字串。Domain DTO 的 ResearchEligibilityStatus JSON enum 沿用整數：0=BLOCKED、1=READY_FOR_CONTROLLED_BASELINE；未知 enum 拒絕。日期 ISO-8601，缺值 null，不轉 0。未登入 401、未知 id 404、不支援 method 405；遺失或損壞發布報告 503/DATA_NOT_READY。分頁沿用既有 PageQuery 上限與 400 驗證。

既有 `/research/status`、feature/evidence validation 改讀共用驗證報告；只有經核准的 Trend feature diagnostics 才能有值，沒有 future labels 就不宣稱 IC／outcome 已驗證。其他既有路由保留。

`CodeCommitSha` 為產生證據評估時的程式 commit，與 `/system/health` 的部署 commit 分開；後者可能包含產生後的唯讀報告。報告 digest 綁定前者及所有輸出。原始 archive 路徑與憑證不透過 API 傳出。受控發布檔案的 hash 是完整性校驗，並非第三方簽章或外部授權。

OpenAPI：[openapi-v1.json](openapi-v1.json)；現有 50 個 path。以下保留前版契約細節。

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

## Phase 4M（3.2.2）

新增唯讀 GET：`/api/v1/operations/readiness`（OperationsReadinessDto）、`/api/v1/research/trend/readiness`（TrendFactoryReport），沿用 ApiResearchRead、ReadPolicy、no-store。
管理 GET：`/api/v1/authority/workbench`（PageDto<ReviewQueueItem>）、`/api/v1/authority/acquisition-evidence`（PageDto<HistoricalEvidenceCard>），額外要求 ApiAuthorityAdmin/Administrator，page/pageSize 沿用既有驗證。匿名 401、一般會員管理端點 403、缺失或損壞快照 503 DATA_NOT_READY。沒有新 HTTP 寫入或角色授予。
來源模型是綁定 Phase4I/Phase4J digest 的 Phase4M-v1 發布快照。SourceCodeSha 指產生報告的原始碼提交，網站執行版本另見 system/health。沒有以匯出時間當歷史發布時間。運算在 Application；Web 同來源 Cookie 讀取、15 秒逾時，401/403 不保留資料。
審查/撤銷採受控 CLI，指派角色與 OS ACL 明確配置；核准仍需既有 scoped receipt。GET 不取得外部資料、不執行排程、baseline 或 promotion；舊 DTO 保持相容。
