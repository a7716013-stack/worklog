# Worklog 3.0.9 — Phase 4F 實作報告

本版完成已提供規格的分項研究入口、逐項主張審查與不可覆寫結案紀錄。真實資料仍為 `BLOCKED_BY_AUTHORITY_OR_DATA`；尚未達成 FIRST_APPROVED_REAL_COMPONENT_DATASET 或 FIRST_REAL_COMPONENT_BASELINE。

## 文件範圍

依據桌面 `# Worklog Phase 4F.txt`，第 0–7 節完整，第 8 節在 `all emergency` 中斷。未推定後續要求已完成。必要主張名稱、SubjectDigest、狀態轉移限制及 CLI 形式為本版實作決策；完整原文補齊後仍需重新比對。

## 已實作

- 首輪明確限定 Trend、Momentum、VolumePrice，可逐項申請；沒有強制等待六分項 FullEvidence100。
- 依交易日曆、歷史識別、歷史 Universe、價量、Availability、公司行動／價格基準順序建立 AuthorityClosureCase。案件保存規格要求的所有欄位，另以 SubjectDigest 綁定確切資料／政策。
- OPEN、EVIDENCE_CAPTURED、UNDER_REVIEW、PARTIALLY_SUPPORTED、APPROVED、REJECTED、BLOCKED、SUPERSEDED 八種狀態。每次修訂採序號、前次摘要、案件摘要與修訂摘要；舊版本不可覆寫。
- 每個 RequiredClaim 獨立核對來源、期間及 review。支持一項不代表整個 domain 核准；衝突、不明、範圍不足均保留阻擋。核准案件只能明確 supersede，不能回寫歷史。
- 分項結案通過後，仍需既有 Phase 4D/E 真實資料核准。baseline 執行前重新讀取目前案件與原始證據，拒絕過時回條；結案回條納入 baseline manifest 與重播檢查。舊版無結案回條封存仍可重播。
- RealDataApproval 改為檢查各分項實際依賴的來源註冊版本；無關 Institutional authority 被 supersede 不再阻擋 Trend。既有 PIT、歷史 Universe、price basis、來源與標籤檢查保留。
- GitHub CI 新增 v3-closure，加入相同 SHA 的總驗收關卡。

Candidate-v1 既有 snapshot identity 仍要求歷史 Universe 與 benchmark identity；這屬文件允許保留的既有 invariant，沒有補假識別或以目前成分股代替歷史 Universe。分項 closure 本身不額外要求 Institutional 或 Benchmark domain 核准。

## 真實證據執行結果

2026-09 目標期間，六份 TWSE 回應成功封存且通過摘要重播。六個案件目前均為 PARTIALLY_SUPPORTED，核准案件與真實資料集數均為 0。review 是具名的操作者內容審查紀錄，並非獨立認證或自動語意驗證。

| Domain | 已有支持 | 尚未支持 |
|---|---|---|
| TradingCalendar | 2026 年預定休市表，包括 9/25、9/28 | 全部臨時休市、完整歷史 sessions 與 horizon |
| HistoricalInstrumentIdentity | 特定終止上市紀錄 | 完整歷史識別、代號變更、轉市場 |
| HistoricalUniverse | 特定終止上市紀錄 | 歷史完整成分、上市／停止交易與分母 |
| HistoricalPriceVolume | 官方來源、欄位單位；回應含 2330 的 20 日價量 | 與既有提交 facts/archive 的逐筆對應、原始修訂識別 |
| Availability | 官方歷史查詢來源 | 每筆歷史發布時間及修訂規則 |
| CorporateActionPriceBasis | 除權息參考價紀錄 | 完整公司行動與相容調整政策 |

除權息回應的部分「最近一次申報資料」欄位為 115 年 10 月 30 日，晚於查詢的 9 月。這些欄位不能直接當作當時可知資料。擷取時間沒有轉成 AvailableAt。新增 PublishedDelistingRows、PublishedReferencePriceRows 與 OriginalRevisionIdentity 主張用以記錄有限支持及缺口，不替代各 domain 的必要主張。

原始回應與完整案件修訂保留於本機 `artifacts/phase309-evidence`、`artifacts/phase309-closures`。Git 中保存來源 URL、摘要、review、最終逐項判定及缺口報告，未納入憑證或原始封存目錄。

- [真實證據與案件](phase4f-real-evidence-3.0.9.json)
- [結案缺口](phase4f-gap-3.0.9.json)
- [資料集核准缺口](phase4f-dataset-gap-3.0.9.json)
- [執行驗證摘要](phase4f-execution-3.0.9.json)

## 操作方式

以下透過 `dotnet run --project src/WorkJournal.Evidence.Cli --` 呼叫；輸出檔必須尚不存在。

```text
--closure-subject <real-submission.json> <ClosureDomain>
--closure-open <case.json> <closure-store> <evidence-store>
--closure-review <review-request.json> <closure-store> <evidence-store>
--closure-export <closure-store> <evidence-store> <case-guid> <new-file>
--closure-evaluate <component-request.json> <closure-store> <evidence-store> <new-file>
--closure-baseline <component-request.json> <template.json> <closure-store> <evidence-store> <authority-store> <baseline-store>
```

先完成案件逐項審查，再完成資料集 review；取得真實核准後才執行 baseline。本版沒有執行真實 IC、RankIC、quantile、ablation 或 OOS，沒有宣稱模型有效。Promotion=NOT_PROMOTED、Threshold=UNRESOLVED、Phase5=LOCKED。

## 驗證與發布

21 項本機建置、契約／Legacy 回歸與 CLI 流程通過。最後增補防止過時結案回條的檢查後，V3ClosureChecks 為 118 項；測試用證據都是合成 fixture，不計入真實核准。V3RealDataChecks 再跑以確認既有 baseline 與分項回歸。計算公式與 Research 核心檔案摘要保持不變。

本版不新增資料庫 migration、不變更既有投資評分或 Azure SQL 計費。NuGet 仍回報既有 Pkcs 9.0.11 NU1902/NU1903 警告。登入頁 HTTP 200 僅證明頁面可達，不代表 Google OAuth 與資料庫登入流程已驗證。

GitHub 相同提交 CI 通過後才更新 main 並部署 Azure。部署識別、套件摘要、線上版本及暫時 SCM 規則清理結果另保留於本機發布狀態紀錄。
