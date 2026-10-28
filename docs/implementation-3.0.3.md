出版註記：本報告以下保留最初本機實作階段的範圍與證據。使用者後續已指示上板，3.0.3 進入 GitHub CI 與發佈流程；實際執行紀錄以對應提交的 Actions／Release 為準。六項正規化及完整 Phase 3 仍為 MODEL DEFINITION GAP，部署不改變此結論。

# 3.0.3 本機實作報告：Phase 3 部分完成

主規格：桌面 `# Worklog Phase 3 — FullEvidence100.txt`。補充：`# Worklog Stock Analysis V3.txt`。本次只在本機實作；未提交／上傳 Git、未呼叫 GitHub、未部署或修改線上資料庫。

**PHASE 3 IMPLEMENTATION = PARTIAL**

**PHASE 3 MODEL DEFINITION GAP = 六項 Evidence normalization 尚未完整定義。**

已完成的框架與測試不代表六項模型已驗證，也不代表 Phase 3 = PASSED。

## A. Baseline

- 功能基準：2.0.2，已驗證提交 `5f271fb7a089c54c4a622027310cef426b3143c8`。
- End SHA：無；本次未建立 Git commit。Branch：未建立新分支。本機舊 Git 指標未重設。
- 受測版本：3.0.3 本機工作檔；測試紀錄與變更前雜湊存於 `artifacts/phase303-*`。
- 規格優先序：Phase 3 > V3。V3 的候選／研究方向不能覆蓋 Phase 3 的 STOP 條件；未進入 Research/OOS/Promotion，也未宣告正式模型凍結。

## B. Existing Components Inventory

| File | Primitive / 分類 | 重用判定 | Legacy 分數語意重用 |
|---|---|---|---|
| Services/TechnicalIndicators.cs | MA5/20/60、Wilder RSI14、SMA-seeded EMA 與 MACD12/26/9；VERIFIED_PRIMITIVE | tests/FinMindChecks 驗證上升／下降／平盤、warmup、缺值重啟；可在定義確定後原樣呼叫，這些期間不自動成為 V3 參數 | NO |
| Services/SwingCalculator.cs | prior-20 high/low/均量、量比、法人加總與連買；REUSABLE_PRIMITIVE（與舊評分耦合） | prior window 排除當日的原則已驗證；不可直接呼叫 Calculate 再將其 Score 或 Points 轉成新 Evidence | NO |
| Services/MarketRadarScoreCalculator.cs | prior-volume mean、量比；REUSABLE_PRIMITIVE（區域函式） | 可研究抽離數學部分，尚未改動既有檔案 | NO |
| 同上及 SwingCalculator.cs | 分段加分、罰分、70／75 門檻；LEGACY_SCORE_ONLY | 凍結，不移植到新模型 | NO |
| Services/FinMindStockService*.cs、Models/StockSwingData.cs | 收盤、成交量、法人來源／日期；資料輸入 | 歷史交易日期不能當 AvailableAt；未找到足以直接證明所有歷史發布時間的欄位／流程 | NO |
| WorkJournal.MarketData/OfficialParser.cs、MarketDataGateway.cs、SourceCache.cs | 官方資料解析、來源備援、快取；資料基礎 | 沿用現有隔離模組；快取取得時間不冒充歷史發布時間 | NO |
| Models/V2/Foundations.cs、Services/V2/FoundationService.cs | 快照、版本、參數、Universe、Benchmark、PIT；V2 FOUNDATION | 本次讀取服務直接重用 IPitQueryService，不另建一套快照 | NO |
| V3 文件中的 slope/ROC/ExcessReturn/percentile/ATR/volatility 等 | NEW_CANDIDATE | 可作後續研究方向；本次沒有編造 calculator 或預設參數 | NO |

本次未新增第二套 MA、RSI、MACD，也未抽改 Legacy 程式。重用指標的可行性已盤點，正式六項 Evidence 尚未呼叫它們計分。

## C. 已完成 FullEvidence100 外層

`FullEvidenceModel.AggregationVersion = FullEvidence100.Aggregation.v1` 僅表示固定加總定義，不是已凍結的完整模型版本。

| Evidence | Weight |
|---|---:|
| Trend | 25 |
| Momentum | 15 |
| VolumePrice | 15 |
| Institutional | 20 |
| RelativeStrength | 15 |
| RiskQuality | 10 |

`IFullEvidenceScoreCalculator` 是無 DB／網路／時間依賴的純加總器，接受已正規化的六項契約。要求六個 code 各出現一次、有效狀態、非空 ReasonCode／FeatureVersion／NormalizationVersion，且數值位於 [0,1]；錯誤輸入拒絕，不 clamp、不猜缺少項目。

只有 Available 有正規化值及 WeightedPoints。其他狀態的 canonical normalized value／points 為 null；原始診斷數值應留在 FeatureValue／來源事實，不能混成有效分數。AvailableWeight 不足 100 時 TotalScore=null、Incomplete；不產生部分分數冒充完整分數。

計算全程 decimal，沒有額外中間或最終四捨五入量化；保留 decimal 算術結果。尚未新增顯示介面／顯示精度政策。

## D. Normalization / Model Definition Gap

| Evidence | Existing inputs / Legacy logic | Missing definition | Possible options（僅 V3 候選，不採用） | Status |
|---|---|---|---|---|
| Trend | 均線、價格、prior breakout；Legacy 是布林加分 | MA120／結構定義、斜率期間、正規化、子權重 | Alignment／price position／slope／structure | UNRESOLVED |
| Momentum | RSI、MACD、Signal；Legacy RSI50–70 加分 | RSI 曲線、MACD 尺度分母、ROC 期間、子權重 | 分段 RSI、價格或波動尺度 MACD、ROC | UNRESOLVED |
| VolumePrice | prior 均量、量比、突破；Legacy 量比分段分數 | RVOL 期間、量價確認、突破正規化、子權重 | RVOL／confirmation／breakout distance | UNRESOLVED |
| Institutional | Foreign／Trust／Dealer 欄位及部分加總；Legacy 正負買超加分 | 分母、投資人權重、期間權重、持續性、可靠 AvailableAt | 股數/成交量、金額/成交值、股數/流通股數 | UNRESOLVED |
| RelativeStrength | Benchmark version/mapping 結構已存在 | 報酬期間、基準資料接線、歷史母體、ties／singleton／missing 排名規則、期間權重 | ExcessReturn + historical percentile | UNRESOLVED |
| RiskQuality | 價格／量資料；Legacy 有風險罰分 | 波動標準差定義、ATR 平滑方式、回撤／流動性、期間、方向曲線與子權重 | Volatility／drawdown／ATR%／liquidity | UNRESOLVED |

不能推導正式選擇的原因：既有測試證明指標計算及舊模型行為，未證明其對新模型的正規化、子權重或研究效果。V3 明確把這些標示為候選／UNRESOLVED。RSI/100、LegacyPoints/MaxPoints、等權子項、固定 20/60/120 日等都未擅自採用。

NormalizationDefinition 目前只表示方法／版本／參數／狀態，沒有執行任意公式。ZScore 本身不保證 [0,1]，需另定轉換；不能因列在方法 enum 就當成合法 normalized evidence。

**定義缺失不是資料缺失。** 檢查服務回傳 MODEL_DEFINITION_GAP，不偽裝成 Missing Evidence，也不填入中性值。

## E. PIT / Preparation

`IFullEvidencePreparationService.PrepareAsync(FullEvidenceRequest)` 必須明確傳 InstrumentId、TradeDate、DataCutoff、DatasetSnapshotId、UniverseSnapshotId、VersionSetId、ParameterSetId。

檢查參照已 Finalized、日期／cutoff／資料及 Universe 版本一致、Instrument 在該歷史研究 Universe 中為 Eligible、指定 BenchmarkVersion 與 Mapping 在當時有效。Universe eligibility 不變成策略資格或交易決策。

透過 Phase 2 IPitQueryService 取出 AvailableAt <= cutoff 的資料。未知及未來發布資料排除；Stale／Invalid／Missing 等來源狀態保留。依 Phase 2，這是市場當時可取得的語意，不等於本系統當時已實際持有。

限制：Phase 2 BenchmarkMapping 目前有有效期間但沒有發布／首次已知時間；本次可證明「指定版本不被較新版本取代」，不能宣稱已證明所有回填 mapping 都符合當時可知性。完整 RelativeStrength 前仍須定義 mapping known-at 或等價不可變決策證據。未為此擅自改寫 Phase 2 schema。

尚未定義股票／基準 raw payload schema、修訂值選取及同日重複來源優先序；因此不從 JSON 猜測資料來計分。

## F. Trace

新增 Evidence／Feature／Normalization 契約保留版本、原因、來源、snapshot 與 cutoff；加總結果保留每項輸入、權重與貢獻，回傳的 components 為唯讀集合。

目前沒有新的完整評分服務、Score DecisionTrace 或 persisted score。Preparation 的 TotalScore 與 DecisionTraceId 固定 null；不將缺口檢查冒充已完成經濟決策。未建立第二套 tracing system。正式 Evidence→Score→既有 DecisionTrace 的整合待六項定義確定後完成。

## G. Persistence / Changed Files

無新增 migration／資料表／DB 約束，無本機業務資料庫或線上資料庫升級。SQL 測試僅在 GUID 暫存 DB 套用既有 migrations，最後清除。正式 score SQL unique/FK/retry/concurrency/rollback 驗證尚未進入，不能標示完成。

新增程式與測試：

- `Models/Evidence/EvidenceContracts.cs`
- `Services/Evidence/FullEvidenceScoreCalculator.cs`
- `Services/Evidence/FullEvidencePreparationService.cs`
- `tests/V2EvidenceChecks/V2EvidenceChecks.csproj`
- `tests/V2EvidenceChecks/Program.cs`

修改：`Program.cs`（DI）、`WorkJournal.Web.csproj`（3.0.3）、`.github/workflows/ci.yml`（僅本機準備）、`README.md`、`CHANGELOG.md`。新增本報告、`docs/releases/3.0.3.md` 與本機啟動腳本。既有模型／Foundation 實作與 Golden expected values 不變。

## H. Tests

`V2EvidenceChecks`：64 PASS，exit 0。

- 精確 0／50／100 及各項貢獻；混合 25+12+9+8+3+0=57。
- 不同輸入排列仍映射正確；超界／重複／未知／不完整契約拒絕。
- Missing／Invalid／Stale／NotApplicable → Incomplete、AvailableWeight=80、TotalScore=null；真實 0 維持 Complete。
- decimal 不提前量化；結果與輸入陣列隔離；保留 feature version。
- 真實 SQL 的 PIT -1／0／+1 秒、未知發布時間、品質傳遞、明確參照重播、新參數與未來基準隔離、研究 Universe 檢查。
- 未來資料測的是「prepared PIT facts 不變」，不是尚未實作的六項 calculator 或完整歷史 score 不變。
- 確認缺口服務沒有寫入 DecisionTrace，也沒有 dirty tracked changes。

執行：

```powershell
dotnet build WorkJournal.sln -c Release --artifacts-path artifacts/build303
dotnet run --project tests/V2EvidenceChecks -c Release --artifacts-path artifacts/build303
# 不需 SQL 的加總契約測試：上述 run 命令最後加 -- --fast
```

完整回歸命令及 exit code 記錄：`artifacts/phase303-regression.json`；逐項 log：`artifacts/phase303-*.log`。結果以實際紀錄為準。

## I. CI

本機已準備 `v2-evidence` job，彙總加入 EVIDENCE_RESULT 與同 SHA 比對。未上傳，因此 GitHub CI 未執行，沒有本次 WorkflowRunId／EndSHA；不得把本機通過稱為 Same-SHA CI PASS。

該 job 驗證已完成的部分，不代表 MODEL DEFINITION GAP 已解除。

## J. Legacy Regression

LegacyRadar100、LegacySwing100、LegacySwing75 維持原程式及 expected values。完整回歸包含 MarketRadarChecks、FinMindChecks、LegacySwing75Checks、PaperTradingChecks、StrategyCenterChecks、MarketRadarTrackingChecks、V2FoundationChecks。

保留原有 Pkcs 9.0.11 NU1902／NU1903 警告；本次未修改依賴套件。

## K. Policy / Final Status

```text
Application Version = 3.0.3 (LOCAL ONLY)
Phase 3 Implementation = PARTIAL
Phase 3 Model Definition = GAP (all six normalizations unresolved)
Phase 3 Overall Validation = NOT PASSED
Same-SHA CI = NOT RUN (user requested local only)
V2Threshold = UNRESOLVED
Strategy Eligibility = NOT IMPLEMENTED
Selection = NOT IMPLEMENTED
Allocation = NOT IMPLEMENTED
Automatic Trading = NOT IMPLEMENTED
Git commit / upload = NOT PERFORMED
Online deployment / migration = NOT PERFORMED
```

後續需先確認六項 Input／Normalization／Version／Parameters／Missing／Stale policy，再接 calculator、score persistence、完整 replay 與 SQL 測試。Phase 3 通過前不進入 Phase 4。

## 本機驗證結果補記

- build = PASS。
- legacy-fast = PASS（MarketRadarChecks、FinMindChecks、LegacySwing75Checks）。
- legacy-sql = PASS（PaperTradingChecks、StrategyCenterChecks、MarketRadarTrackingChecks）。
- v2-foundation = PASS（45 checks）。
- v2-evidence = PASS（64 checks，僅已完成範圍）。
- legacy-golden-master = 本機組成檢查 PASS；遠端 aggregate／Same-SHA CI 未執行。
- 基準 source/test 雜湊確認：既有檔案僅 Program.cs（DI）與 WorkJournal.Web.csproj（版號）變更。
- 本機預覽：http://localhost:5193；登入頁 HTTP 200，版號 3.0.3。
- 啟動方式：`./Start-WorkJournal303.ps1`。預覽停用 Strategy／RadarTracking／InvestmentInsights 背景排程，連線明確指定本機 SQLEXPRESS；沒有自動 migration。可保留既有其他埠的本機網站。
- 第一次 HTTP probe 早於程式完成啟動，得到連線失敗；確認 listening 後重試通過，非程式建置／測試失敗。

完整 Phase 3 狀態仍為 PARTIAL / MODEL DEFINITION GAP。尚未執行的完整六項計算器、歷史 score 防未來資料、score persistence SQL 及同 SHA CI，不列為已通過。
