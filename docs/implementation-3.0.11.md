# Worklog 3.0.11 — Phase 4F-2 實作報告

本版承接 3.0.10（`adb9f11da92816ecc539704ec8da4ba6681e875c`），實作 Authority Evidence Resolution & First Real Component Approval。發布分支為 `release/3.0.11-phase4f2`；最終 Commit SHA 以 [v3.0.11](https://github.com/a7716013-stack/worklog/releases/tag/v3.0.11) 與同 SHA CI 為準，部署後另產生 `docs/release-3.0.11-status.local.json`。

## 結果與停止條件

已實際處理既有真實證據封存，新增兩份官方網頁擷取及審查，並執行真實 Trend 核准申請。既有 20 筆 TWSE 原始行情保留；本次必要主張共 20 項，0 項完全支持、4 項部分支持、16 項尚未證明，0 項已記錄衝突。合格真實觀測數 0，已核准 authority domain 0，已核准 component dataset 0。

`FIRST_APPROVED_REAL_COMPONENT_DATASET = NO`，`FIRST_REAL_COMPONENT_BASELINE = NO`。符合文件 Final Stop Rule State A：真實處理流程已執行，證據不足阻擋核准並停止研究。沒有建立假憑證、沒有以測試資料代替真實資料，也沒有執行真實 IC、RankIC、Quantile、Ablation 或 OOS。

## 程式內容

- `ResolutionContracts.cs`：證據處理工作清單、取得嘗試、負面搜尋結果、時間／修訂規則、逐筆資格、縮減範圍、核准及里程碑紀錄。
- `AuthorityResolution.cs`：優先處理 availability/revision；使用已審查時間上界判斷 PIT，區分六種 revision semantics、全部 observation states；核對現有結案的版本、範圍及 subject digest，從資格紀錄推導 coverage。未知分母保持 null。
- `AuthorityResolutionLedger.cs`：取得前先寫入 started 紀錄，完成或失敗後保存結果及原始內容摘要；保留查無證據的 UNPROVEN 結果。擷取成功不會核准任何主張。
- `AuthorityResolutionStore.cs`：凍結 `TrendAuthorityDependency-v1`、申請、覆蓋報告、逐筆資格與現有 component receipt；封存證據重播、明確撤銷、首次里程碑及 SHA 綁定基準研究。縮減範圍必須是新申請，保留原申請並限制為證據不足等原因。
- `ResolutionCommands.cs`：提供明確的本機操作入口；不透過定時排程自動核准。

新流程附加於既有 component/PIT/authority gate，沿用其完整檢查。舊版封存格式及既有核准命令維持相容；Phase 4F-2 的新工作使用 `--resolution-*` 指令。這次沒有建立獨立 HTTP API、移出 Web 專案或接入網站評分頁面；API 拆分仍是另外的架構工作，不能以本版發布宣稱已完成。

`SystemKnownReplay` 需要歷史接收紀錄，目前明確阻擋。`MarketAvailableReplay` 仍必須具備公開時間與歷史修訂證據；不能從今天的查詢結果或 RetrievedAt 推定。只保存 EndOfSession 等精度而沒有可證明的時間上界，也不能放行。

## 真實證據與缺口

本次查閱並封存：

1. [TWSE 個股日成交資訊](https://www.twse.com.tw/zh/trading/historical/stock-day.html)：頁面提供歷史查詢及服務起始範圍，未找到目標資料逐筆原始發布時間或歷史修訂重建證明。
2. [TWSE 盤後資訊與歷史交易資料](https://www.twse.com.tw/zh/products/information/history.html)：說明取得相關資料的服務管道，未找到 STOCK_DAY 歷史修訂保留方式及更正發布時間的證明。

兩份審查只支持查詢服務／取得管道，歷史發布與修訂主張保持未證明，沒有因來源為 TWSE 自動核准。既有 3.0.9 結案與 3.0.10 更新後資料 subject 不同，不會直接沿用為本次核准。

原始價量範圍為 2026-09，既有申請研究日期為 2026-09-30；20 筆原始價量不等於 20 筆研究觀測。研究股票範圍、永久識別、實際交易日、價格基準／公司行動等仍須補足目標範圍證據。沒有以已下載的 2330 資料自行定義研究 universe，也沒有因資料少而捏造完整覆蓋。

主要可移植報告為 [真實執行結果](phase4f2-real-execution-3.0.11.json) 及 [工程檢查結果](phase4f2-engineering-3.0.11.json)。原始 bytes、不可覆寫 ledger、核准失敗與測試封存在本機 artifacts；Git 僅包含程式、文件及清理後的報告。

下一步證據工作依序為：取得 STOCK_DAY 原始發布規則／歷史 archive timestamp；查明 correction 與 revision reconstruction；再補足可獨立定義的歷史 universe、永久識別、實際 session 與公司行動範圍。未完成前不擴張行情下載範圍、不執行真實 baseline。

## CLI

以下省略 `dotnet <WorkJournal.Evidence.Cli.dll>`；輸出檔案必須尚不存在。

```text
--resolution-request <component-input> <new-request-file>
--resolution-evaluate <request> <store> <evidence> <authority> <closure> --output <new-report>
--resolution-acquire <task> <official-url> <store> <evidence> <operator> <new-attempt-file>
--resolution-not-found <search-result> <store> <evidence>
--resolution-scope <old-request> <new-request> <store> <reason> <gap-array-file> <reviewer> <at> <new-decision>
--resolution-scope-replay <new-request-id> <store> <new-file>
--resolution-approve <request> <store> <evidence> <authority> <closure> <compiled-code-sha> <new-receipt>
--resolution-replay <approval-id> <store> <evidence> <authority> <closure> <new-file>
--resolution-revoke <approval-id> <store> <evidence> <authority> <closure> <status> <reason> <at>
--resolution-baseline <approval-id> <template> <compiled-code-sha> <store> <evidence> <authority> <closure> --run
--resolution-baseline-replay <run-id> <store> <evidence> <authority> <closure> <new-file>
```

基準研究先重查 live authority 與 receipt 狀態，使用現有 Candidate-v1／ResearchEngine；特徵與 evidence freeze 先於 outcome 計算。資料範圍過小時可完成執行但標示統計不足。沒有通過門檻、調參、反轉訊號或模型升級。歷史研究可在撤銷後重播，新研究必須拒絕。

## 驗證與發布

完整本機回歸涵蓋 build、Legacy、V2、V3、PIT、authority、closure、component、CLI 與既有合成研究／重播，共 23 個步驟。既有合成 OOS 回歸僅驗證保護機制，不是本階段真實 OOS 研究。

新增 `V3AuthorityResolutionChecks` 與 `v3-authority-resolution` CI，涵蓋 exact timestamp、時間區間、未證明規則、修訂類別、時間邊界、各資格狀態、縮減範圍不可變、真實／模擬隔離、完整隔離測試核准、首次里程碑、撤銷及歷史 baseline 重播。測試使用明確的模擬 transport／attestation，測試 store 與真實證據完全分開；測試成功不是實際資料里程碑。

Candidate 計算與 Research Engine 原始檔摘要維持不變。無資料庫 migration，無帳號／密鑰變更，不啟用付費額度；本機入口為 `Start-WorkJournal311.ps1`，預設 http://localhost:5201。部署後驗證線上版本、登入頁及股票查詢；匿名登入頁檢查不代表完成 Google OAuth 與登入後資料庫往返。

NuGet 仍有既有 System.Security.Cryptography.Pkcs 9.0.11 弱點警告，本版沒有藉此更動套件或 Legacy 行為。

## Final Required Status Matrix

| 項目 | 狀態 |
|---|---|
| Version | 3.0.11 |
| CommitSHA | v3.0.11 release target；部署後另記錄本機發布驗證檔 |
| Branch | release/3.0.11-phase4f2 |
| Legacy Golden Master | 發布須同 SHA CI PASS |
| Same-SHA CI | 發布須全部工作 PASS |
| Authority Resolution Infrastructure | 本機已實作，測試結果見工程報告 |
| Availability Authority | BLOCKED：原始發布證據不足 |
| Revision Authority | BLOCKED：歷史修訂證據不足 |
| Instrument Identity Authority | BLOCKED：目標範圍未核准 |
| Historical Universe Authority | BLOCKED：歷史成員與分母未證明 |
| Calendar Authority | BLOCKED：實際 session 完整範圍未證明 |
| PriceBasis Authority | BLOCKED：公司行動／基準未核准 |
| Raw Real Rows | 20 |
| Eligible Real Observations | 0 |
| Approved Authority Domains | 0 |
| Approved Component Dataset Count | 0 |
| FIRST_APPROVED_REAL_COMPONENT_DATASET | NO |
| FIRST_REAL_COMPONENT_BASELINE | NO |
| Real IC | NOT RUN |
| Real RankIC | NOT RUN |
| Real Quantile | NOT RUN |
| Real Ablation | NOT RUN |
| Candidate-v1 Status | FROZEN／RESEARCH CANDIDATE |
| V3FullEvidence100-v1 Status | LOCKED |
| V3Threshold | UNRESOLVED |
| OOS | LOCKED |
| V3 Promotion | LOCKED |
| Phase5 | LOCKED |
