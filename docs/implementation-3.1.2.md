# Worklog 3.1.2 — Evidence-Driven 實作報告

依桌面 `# Worklog v3.1.2 — Evidence-Driven.txt` 及使用者明確授權實作、本機驗證、Git 與線上發布。實作前以 GitHub main `efde456dbb8b25c1596a61d869ed5ad78d387226` 比對所有既有檔案，無未解決差異。能力、缺口、重複風險及依賴稽核見 [audit-3.1.2.md](audit-3.1.2.md)。

## 已完成的程式變更

`EvidenceValidationWorkflow` 位於 Application，協調既有 AuthorityResolutionStore、AuthorityClosureStore、EvidenceAcquisitionStore、ComponentDatasetStore、RealBaselineStore 及 FormulaDiagnostics。未另建 authority registry、PIT engine 或評分公式。

6 個歷史研究 contracts 移至 Domain、17 個歷史研究 services 及 ResearchStore 移至 Application（共 24 個檔案，搬移時位元組不變）。保留舊 namespace 以維持呼叫相容；namespace 名稱不代表仍在 Web assembly。Domain/Application 不引用 Web。Api 對 Web 的 hosting/EF 相依仍是過渡架構，未宣稱全部剝離。

流程：scoped claims → 原有七道 PIT gates → 獨立 Trend 核准 → baseline eligibility → 既有 baseline/replay → 特徵診斷及假說收據。`--validation-export` 輸出可重播的 readiness/report；`--validation-diagnose` 必須提供目前有效且綁定同一 request、report、parameter checksum 的核准；`--validation-baseline` 沿用 development/OOS、outcome lineage 與 purge 邊界；`--validation-hypothesis` 必須綁定可重播真實 baseline 的 output digest 並提供明確假說。輸出僅 HYPOTHESIS_ONLY，不產生 candidate-v2 或自動 promotion。

Trend 的 feature-only diagnostics 共 4 項，沿用 FormulaDiagnostics 分布、缺值及分位數；未提供 future outcomes 時 IC 等維持 null。沒有另造離群值門檻。Quality、Valuation、Flow、Risk、Catalyst 仍須各自 authority、PIT 及核准，Trend 核准不能代替其他組件；Full Evidence 仍被六組件 gate 阻擋。

新增四個正式唯讀 API、OpenAPI、契約測試與 `/ResearchValidation` 頁；頁面只取得 API 結果、呈現狀態，不在 JavaScript 重算分數。API 契約詳見 [APIContractChange-3.1.2.md](APIContractChange-3.1.2.md)。

## 實際資料與研究結果

20 項 claims 依 HistoricalPublication／Availability SourceIdentity、RevisionBehavior 優先順序重查。共 27 次取得嘗試，20 次成功，7 次原網址重新導向官方不存在頁面；另以官方替代端點補查下市清單與除權息預告。成功取得仍不等於 historical authority。

歷史資料服務說明沒有 frozen subject 的原始發布／修訂紀錄；目前公司查詢與下市清單不能證明完整歷史識別、母體分母及市場轉換；當日日價查詢頁沒有原始發布時間；2026 休市表不能代替完整歷史 session 與緊急休市發布證明；目前除權息預告不能證明 2026-09-30 凍結範圍的價格基準。各項來源 URL、raw SHA256、retrievedAt、scope、review digest、reviewer、ReviewedAt 與 blockers 均存入報告，失敗取得保留為失敗紀錄。

真實 raw rows=20；eligible observations=0；approved datasets=0。未知 expected denominator 保留 null。七道 PIT gates 逐筆報告；未證明 AvailableAt 保留 null，絕不改填 RetrievedAt。REAL_BASELINE_V1=NOT_RUN、ResearchOutputDigest=null、V3Threshold=UNRESOLVED、promotion=NOT_PROMOTED、Phase5=LOCKED。

沒有真實 baseline、OOS 績效、ablation、相關性、穩定性數值，也沒有宣稱新模型改善。隔離測試中的核准、baseline 及撤銷案例均為模擬 fixture，不寫入真實里程碑。

## 模型與安全不變量

Candidate-v1、19 特徵、25/15/15/20/15/10 權重、缺值 null、不重新正規化及 legacy/paper 行為保留。FormulaDigest：`81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB`。參數 checksum 綁定原 frozen dataset。

驗證摘要發布器只接受本版支援的 NOT_RUN/feature-diagnostics 狀態；無有效核准時不發布真實研究結果。受控 CLI baseline/replay 已串接；未來取得真實資料並執行後，完整已完成 baseline 的唯讀發布 snapshot 必須經獨立審查，不能手動把 NOT_RUN 改成 COMPLETED。Candidate hypothesis 收據不等於已接受治理或發布。

沿用驗證、使用者隔離、CSRF、HTTPS、安全錯誤及查詢限流。未公開 archive 路徑、憑證、核准修改端點。發布沿用既有 Azure 設定與資料庫方案。

## 報告與驗證

- [AuthorityClosureReport](AuthorityClosureReport-3.1.2.json)：20 項 claims、27 次取得紀錄、review/raw 身分。
- [PITEligibilityReport](PITEligibilityReport-3.1.2.json)：逐筆七道 gates、時間與阻擋原因。
- [DatasetReadiness](DatasetReadiness-3.1.2.json)：六組件各自資格。
- [ModelValidationStatus](ModelValidationStatus-3.1.2.json)：模型、參數、來源版本與尚未執行狀態。
- [validation-state](validation-state-3.1.2.json)：API 發布與完整性重播的單一報告。
- [engineering](engineering-3.1.2.json)：實際異動檔案與本機回歸結果；GitHub Actions 提供最後 commit 的 same-SHA CI，部署另記錄 package SHA256 與健康檢查。

完整回歸包括 API/Auth、19 features/formula、PIT availableAt 邊界、修訂、歷史成分股／母體、component 獨立 gate、核准／撤銷、baseline lineage、development/OOS、replay、資料隔離、legacy swing、Paper Trading、market radar 及 CLI。新增報告 hash 與重新封裝假狀態的防繞過測試、缺檔 503、MVC 頁面、日期／分頁／null 契約。既有 NuGet Pkcs 9.0.11 弱點警告仍存在，本版未變更套件。

## 剩餘資料缺口與下一階段

取得可證明原始發布時間及版本修訂的歷史資料與授權後，按 subject/scope 完成獨立 review；補足 instrument identity、historical universe、calendar、price basis。通過七道 gates 與 controlled approval 後才執行真實 Trend baseline，再由結果提出 prospective hypothesis。其他五組件獨立完成前不跑 Full baseline、不調 threshold、不開 Phase5。下一階段仍 API 優先，逐步移除 Api 的 Web hosting/EF 相依。
