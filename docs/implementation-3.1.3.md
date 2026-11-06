# Worklog 3.1.3 — Phase 4I / 100 分模型真實驗證與 API Phase C

依桌面 `# Worklog v3.1.3 — Phase 4I  100 分模型.txt`，以 main `cd0254aeb85298218d2d03efa443d36b4fbc2ff2` 為基礎。使用者明確授權本機、Git 及線上發布；不啟用正式交易、付費資源、資料庫修改或模型 promotion。實作前完成 [Audit-3.1.3.md](Audit-3.1.3.md) 的 Gap Matrix、Dependency Graph、API Reuse Matrix。

## PR-1 至 PR-5 階段

PR-1 稽核與計數；PR-2 官方證據及適用性；PR-3 共用 PIT/readiness/sample workflow 與測試；PR-4 API 與網站；PR-5 完整回歸、報告與發布。各階段保留獨立 commit，最後以一個整合 PR 的相同 SHA CI 發布。

## 20 與 21 的修正

凍結 dataset 包含 20 筆 Price 與 1 筆 Institutional，共 21 筆解析後 facts；21 個 checksum 均唯一、無完全重複。法人資料與同日價格是不同類型，不能當重複刪除。原要求是 2330 在 2026-09-30 的一個研究觀測；PIT 合格 0、核准研究樣本 0、核准 Dataset 0。未知歷史母體分母仍為 null。前版文字報告及工程摘要混用 20，3.1.2 JSON 的 RawRealRows=21 是全部 facts，這次以顯式 counters 澄清，保留原始歷史收據不竄改。

## Authority 證據工作

沿用 EvidenceAcquisitionStore、AuthorityResolutionLedger、AuthorityResolutionStore、EvidenceValidationWorkflow 及 ComponentDatasetStore。新增檢視並擷取三份官方資料，優先對應 Availability 的 HistoricalPublication、SourceIdentity、RevisionBehavior：

- [TWSE 資訊服務](https://www.twse.com.tw/zh/products/information/information.html)：服務分類不能證明指定 STOCK_DAY 資料版本的原始發布時間。
- [TWSE 問答集](https://www.twse.com.tw/zh/products/information/qa.html)：提供歷史資料取得途徑，未提供該批資料原始發布與修訂紀錄。
- [TWSE 網路資訊商店說明](https://www.twse.com.tw/zh/products/dataeshop.html)：商品說明未將已取得的免費查詢 facts 綁定至歷史發布／修訂版本。

三份原始內容均實際下載、hash 驗證並審查，新增三份 reviewer receipt 與 NOT_FOUND/UNPROVEN search receipt。其餘既有 scoped evidence/reviews 重播沿用，沒有將舊來源冒充本次重新取得。沒有購買資料或聯繫第三方。全部 20 claims 仍列出 Subject、Instrument、Coverage、OfficialSource、RawDigest、RetrievedAt、HistoricalAvailableAtProof、RevisionEvidence、Reviewer、ReviewedAt、Decision、BlockingReasons。

目前不能把服務說明、checksum、目前下載時間解讀為歷史發布或修訂證明，沒有新增 Claim approval。需求矩陣、priority queue、source applicability、reviewer decisions 與 unresolved report 由同一 validated snapshot 投影，沒有另建 authority registry。

## PIT、研究樣本與模型

七道 PIT gates 沿用原實作，逐筆保留 unknown、AvailableAt=null 及 blockers。沒有用 TradeDate、RetrievedAt 或 Reviewer 時間填補 AvailableAt。20/21 facts 不等於研究觀測或 outcomes，未取得核准不能執行 baseline。

新增 Phase4IWorkflow：先驗證當前核准與撤銷、subject/request/report 綁定，再呼叫既有 RealBaselineStore.Preview 重用 authority/closure、label lineage、PIT、development-only 與 ResearchEngine 計算。預覽不建立 baseline 檔案或里程碑；樣本不足只輸出 INSUFFICIENT_SAMPLE 的評估收據，metric/IC/quantile/correlation/stability 全為 null。

研究樣本政策 `TrendSample-v1` 與模型參數分開：每個 protocol fold 的 Train、Validation 與 horizon，至少 20 個互異有效日期、每日 max(10, quantiles) 檔且有可定義 IC/RankIC、200 組 outcomes，所有 configured quantiles 非空，既有完整 feature/outcome gate 亦須通過。這是版本化研究執行門檻，不是顯著性或預測能力宣告；不根據結果自動降低門檻。邊界、少一筆、重複日期、常數橫斷面及空 quantile 已測試。

達門檻才執行原 baseline、原 manifest/approval replay，再發布其真實結果。Correlation/年季穩定性沿用引擎；只有 Trend 時跨組件 correlation 可為空集合，不能捏造其他五構面結果。Development/OOS 保持分離；本階段 baseline 不執行 OOS，後續須使用原有 selection receipt/OOS 流程。本版未宣稱已有真實足量 baseline 或 OOS 結果。

Controlled CLI：`--phase4i-export`、`--phase4i-run`、`--phase4i-replay`。匯出要求 SHA 符合 compiled SourceRevisionId；報告 SHA 指向評估程式 commit，部署 SHA 可另包含產生後的報告。report replay 是發布資料完整性校驗，原始 evidence 與 baseline replay 在受控 export/run 使用既有 stores。原有 CLI/API 與 replay receipts 保持相容。

Candidate-v1、19 features、Trend25/Momentum15/VolumePrice15/Institutional20/RelativeStrength15/RiskQuality10、Missing!=Zero、AvailableWeight<100 時 FullEvidence100=null 保留。FormulaDigest 不變：`81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB`。不生成 candidate-v2、不調 threshold、不 promotion、不解鎖 Phase5。Trend approval 不自動核准其他五構面。

## API 與網站

新增五個正式 GET：authority closure-queue、claim evidence、dataset readiness、run validation-summary、replay-status。詳細 DTO、權限、分頁、錯誤、null/enum 與相容性見 [APIContractChange](APIContractChange-3.1.3.md)。沿用 eligibility-summary、provenance、dataset coverage 與逐筆 PIT API，沒有重複計算引擎或公開治理寫入。

ResearchValidation 頁新增資料／樣本計數、Trend 進度、20 claims queue、可點選來源明細、七道 PIT 結果、重播時間；支援更多觀測分頁及失效 session/timeout 訊息。瀏覽器只取得 API、呈現欄位，不重算模型。

新規則及協調放在 Application，改動的 RealBaselineStore/AuthorityResolutionStore 本來已在 Application；沒有新增 Web 計算邏輯。Api 對 Web hosting/EF 的過渡相依仍在，未宣稱全部拆分。

## 真實狀態與剩餘缺口

21 facts = 20 price + 1 institutional；1 requested observation；0 eligible；0 approved datasets；REAL_BASELINE_V1=NOT_RUN、metrics=null、V3Threshold=UNRESOLVED、V3 Promotion=NOT_PROMOTED、Phase5=LOCKED。上述由受控 evaluation 輸出，不由網站填入假研究結果。模型工程測試和隔離核准 fixture 不能算真實里程碑。

仍缺該歷史範圍的 publication/version/revision proof、完整 universe/identity/calendar 及 price-basis authority。取得可稽核證據後再逐項受控審查，通過七道 gate 並有足量可用樣本才進行第一個真實 Trend baseline；其他組件各自處理。NuGet Pkcs 9.0.11 的既有弱點警告仍存在，本版未變更依賴。

## 驗證與交付檔案

API 最終 254 checks；authority/sample 97 checks（隔離 fixture）；完整 25 個本機建置／回歸步驟包括 legacy golden、Paper Trading、market、research/OOS、PIT、核准與撤銷、CLI replay。瀏覽器以發布 DTO fixture 測試 21/20/1 計數、6 構面、20 claims、明細點擊、分頁邊界、登入過期；不是繞過正式登入。最終部署另驗證本機／線上版本 SHA、DB readiness、靜態腳本及 API 權限，GitHub Actions 提供 same-SHA CI。

實際異動檔案與測試結果見 [engineering-3.1.3.json](engineering-3.1.3.json)；原始來源／審查見 [AuthorityClosureReport](AuthorityClosureReport-3.1.3.json)、[EvidenceRequirements](EvidenceRequirements-3.1.3.json)、[HistoricalSourceApplicability](HistoricalSourceApplicability-3.1.3.json)；觀測與計數見 [PITEligibilityReport](PITEligibilityReport-3.1.3.json)、[DatasetReadiness](DatasetReadiness-3.1.3.json)；模型與來源 SHA 見 [ModelValidationStatus](ModelValidationStatus-3.1.3.json)。最後發布 SHA、CI 與部署紀錄保存在 artifacts/release3130-verification.json，並可由 GitHub release/main 查核。
