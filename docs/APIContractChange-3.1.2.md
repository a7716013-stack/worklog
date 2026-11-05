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

OpenAPI：[openapi-v1.json](openapi-v1.json)；現有 50 個 path。