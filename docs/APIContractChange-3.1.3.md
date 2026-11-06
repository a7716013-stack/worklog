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
