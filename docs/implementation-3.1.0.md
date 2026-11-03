# Worklog 3.1.0 — Phase 4F-3 + API Phase A

## A. API Engineering Evidence

建立 WorkJournal.Api 獨立 executable，抽出 JournalHosting 共用註冊；API controller 不直接讀寫 DbContext，不複製評分或經濟邏輯。
此版部署採 API host 同時掛載既有 MVC UI（Api:HostWebUi）；可設定為獨立 API host。
未把 Application/Domain 大規模搬離 Web assembly，亦未新增付費主機。

提供 20 個 GET 路由：health/readiness、市場 radar/industries、模型 metadata/缺值回應、authority、研究與使用者隔離的 paper 查詢。
明確 DTO 與 mapper，decimal/date/null 語意固定；一致分頁、ProblemDetails、X-Trace-Id、權限 policy、可設定限流。
既有 Cookie 登入沿用，API 401 不導向頁面；保留 Web CSRF、TrustedOriginMiddleware、HTTPS/HSTS、Same Origin。
OpenAPI 由 controller/DTO 產生；`docs/api-v1.md` 記錄契約與邊界。

API Phase A 選擇文件 §45 允許的唯讀範圍。§46 所列未來 Paper POST 及 §84 寫入測試延後；沒有公開寫入、reset 或治理操作。
相同冪等 key 不同 payload 的既有 service gap 明確保留，未宣稱解決。
ProductionRateLimit=UNRESOLVED；配置存在才啟用，未任意發明線上限額。

## B. API Execution Evidence

本機完整回歸 23 個步驟通過，涵蓋研究模型、PIT/authority/closure、SQL economic、CLI run/replay/OOS。
新增 `tests/ApiContractChecks` 執行 HTTP、正式 Cookie challenge、錯誤碼、可信代理、分頁、四種 market、上游失敗、取消、MVC 相容、模型 null、authority 0、研究 NOT_RUN、SQL Alice/Bob 隔離、OpenAPI compatibility 與破壞性變更注入測試。
確切計數及 OpenAPI digest 見 `ApiV1ContractSnapshot-3.1.0.json` 與 `engineering-3.1.0.json`。

本機登入頁、CSS、JS、health/readiness 均 HTTP 200；health 顯示 3.1.0。
新增 `api-contract` CI，連同 14 個既有工作組成 15 jobs 的 same-SHA release gate。
此文件在提交前撰寫，CI/deploy 最終狀態以該 commit 的 GitHub Actions、v3.1.0 release 與本機 `artifacts/release3100-verification.json` 為準，不在提交前宣稱線上成功。

依賴還原仍回報既有 transitive `System.Security.Cryptography.Pkcs 9.0.11` NU1902/NU1903 警告；未在本次 API 改造中擴大升級依賴。
未變更線上 DB migration、Azure SQL 計費選項或 Google OAuth 設定。

## C. Authority Evidence

`TrendClaimClosurePlan-3.1.0.json` 包含每一 claim 的 required/existing evidence、候選官方來源、時序/修訂覆蓋、阻擋原因與下一步；優先 availability、revision、identity、universe、calendar、price basis。
再次保存並檢視證交所個股日成交查詢及歷史交易資料服務頁面。兩者仍無法建立歷史原始發布與修訂可重建性，保留 UNPROVEN，而非推論 claim 為 false。
執行紀錄：`phase4f3-real-execution-3.1.0.json`。資料範圍未縮減，未觀察 outcome 後挑選樣本。

| Trend claims | 數量 |
| --- | ---: |
| Required | 20 |
| Supported | 0 |
| Partially supported | 4 |
| Unknown / Unproven | 16 |
| Conflicting | 0 |

## D. Real Dataset Evidence

再次執行既有完整 evaluator 與核准阻擋測試；真實資料仍 raw=20、eligible=0、approved authority domains=0、approved datasets=0。
新增 FIRST_REAL_PIT_ELIGIBLE_OBSERVATION 中間 receipt：只有 archived lineage、identity/calendar/universe/availability/revision/basis 全部通過時才寫入，immutable once-only；不能替代 dataset receipt 或開啟 baseline。
隔離模擬 fixture 驗證正向與防覆寫流程，不計入真實里程碑。
authority/research API 明確是版本化稽核快照，不是 live approval store，不暴露 archive 實體路徑。

## E. Research Evidence

| 狀態 | 結果 |
| --- | --- |
| Phase 4F-3 claim closure | PARTIAL / BLOCKED |
| FIRST_REAL_PIT_ELIGIBLE_OBSERVATION | NO |
| FIRST_APPROVED_REAL_COMPONENT_DATASET | NO |
| FIRST_REAL_COMPONENT_BASELINE | NO |
| Real IC / RankIC / Quantile / Ablation | NOT_RUN；API metric=null |
| V3 promotion | NOT_PROMOTED |
| V3Threshold | UNRESOLVED |
| Phase 5 | LOCKED |

Candidate-v1 公式、六個外層權重、Legacy Radar/Swing 及 Paper economic 規則保持。
AvailableWeight < 100 → FullEvidence100 Missing；沒有 Missing→0 或權重重分配。
API contract PASS 不代表 authority/dataset/model 核准；下一階段仍需補齊歷史權威證據。
