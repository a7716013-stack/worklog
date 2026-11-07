# Worklog 3.1.4 — Phase 4J

以 `dc0f7634c176c97b6efc7268e3161112b9655eba`（3.1.3 main）為基準。先完成 Audit-3.1.4.md 的能力、來源缺口、API 重用與依賴圖，再實作歷史證據供應流程。

## 實作

- Application 新增 Phase4JProjection、Phase4JWorkflow、Phase4JAcquisition 與 AcquisitionSafety。來源目錄從現有封存證據／reviewer receipts 投影，不建立第二套 Authority Registry。
- 13 個來源版本、43 個按 Claim/Subject/Source/Version 去重的工作，涵蓋 20 項 Claims；23 筆可綁定版本的既有取得紀錄。舊版失敗且沒有版本的紀錄保留在原 Ledger，不猜測版本或混入新計畫。
- CLI 可匯出／重播計畫、依固定 TaskId 取得官方證據，以及記錄來源撤銷／替代影響。原始下載、hash、封存及 reviewer receipts 全沿用 EvidenceAcquisitionStore / AuthorityResolutionLedger。
- 下載限制同時涵蓋重試、配額、併發、大小、整體逾時、禁止 redirect 與 DNS 私有位址。購買、登入、聯絡供應商留在 MANUAL_ACTION_REQUIRED，未執行這些外部動作。
- Dataset Build Report 共用六構面框架；Trend 回報現有 PIT 結果，其餘五構面沒有獨立建置／核准時保留 null 與阻擋原因。21 facts 不轉成 21 observations，也不替未知資料補零。
- 來源版本變動與受控 invalidation 產生重新評估要求；既有 AuthorityResolutionStore 的 PreviewBaseline / Baseline 入口檢查此狀態。原有 dataset approval/revocation 與七道 PIT Gate 保持原義。
- API 新增 4 個唯讀資源，相容擴充既有 dataset readiness；ResearchValidation 經正式 API 顯示來源、工作／歷程、下一步與六構面建置狀態。Web/JS 不增加計分運算。

## 重用與研究界線

AuthorityResolutionStore、ComponentDatasetStore.Current、HistoricalDatasetBuilder、七道 PIT Gate、Phase4IWorkflow、TrendSample-v1、RealBaselineStore 全重用。受控研究仍走既有 `--phase4i-run`，未核准 NOT_RUN，核准但不足 INSUFFICIENT_SAMPLE，只有核准且滿足樣本門檻才封存真實 baseline。各 fold 的 Train/Validation、horizon、日期、橫斷面、outcomes、quantiles、IC/RankIC 及 OOS 分離規則不變。

Candidate-v1 19 特徵與權重 25/15/15/20/15/10 不變。FormulaDigest `81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB`。沒有新的真實歷史發布／修訂證明；實際 20 價格 + 1 法人 facts、1 requested observation、0 eligible、0 approved datasets/samples。20 Claims 歷史範圍尚未關閉。Baseline NOT_RUN、V3Threshold UNRESOLVED、NOT_PROMOTED、Phase5 LOCKED。

## 操作

本機 `Start-WorkJournal3140.ps1`，http://localhost:5206/ResearchValidation。CLI usage 可用無參數的 `--phase4j-export` 查看；新增：

```text
--phase4j-export request resolution evidence authority closure compiled-sha new-parent new-report
--phase4j-replay parent report
--phase4j-acquire parent report resolution evidence task-id legacy-task operator new-output
--phase4j-invalidate parent report resolution source-id source-version REVOKED|SUPERSEDED|VERSION_MISMATCH
```

匯出前重讀現有 authority 與 raw archives。取得不代表核准；取得後仍需既有 reviewer CLI。Invalidation 只阻擋，不提供自動解除或核准途徑；重新審核來源、範圍和獨立 dataset approval 後才建立新的受控研究流程。HTTP 只校驗已發布快照，無法即時讀取本機離線資料夾；新撤銷狀態須重新匯出／發布。

## 驗證與交付

回歸涵蓋既有 Formula Golden、Legacy、Paper Trading、PIT、未知 universe、price basis、revision 與 dataset revocation。新增來源版本／報告竄改、任務去重、供應商 timeout/quota/retry、大小、跨程序鎖、SSRF／DNS、來源失效與資料不存在分離、API auth/isolation/errors/null 與 OpenAPI 相容檢查。瀏覽器測試使用實際 Razor/JS 與已匯出 DTO，驗證 13 來源、20 Claims、工作分頁／取得紀錄與未建置提示；不繞過正式登入。

完整執行結果與變更檔案記錄在 engineering-3.1.4.json；發布須通過最終相同 SHA CI，並檢查線上 health/readiness 與靜態資源版本。既有 Pkcs 相依套件警告仍需另案處理。此次未修改正式 DB 結構、帳單、交易或模型 Promotion。
