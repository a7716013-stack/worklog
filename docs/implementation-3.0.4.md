# 3.0.4 本機實作報告

> 發布註記：使用者後續已授權 3.0.4 上版，將以同一提交通過 GitHub CI 後部署。以下保留原本本機實作階段的驗證紀錄；其中「未上傳／未部署／SameSHA 未執行」描述當時狀態。發布不代表 Candidate 已通過 Research／OOS，亦不解除資料與模型缺口。

依據桌面《Worklog V3 — Phase 3 Evidence Model Definition v1》，2026-10-29 實作。
本次只修改本機；沒有 Git commit／push、PR、GitHub workflow 執行、Azure 部署或正式資料庫 migration。

## 版本與範圍

- Start SHA（上一版已驗證部署基準）：`0029ad9db8b5a2c6f900328b847fa618f1b8c179`。
- End SHA：N/A，本機未提交。Branch：現有本機工作目錄，沒有建立遠端分支。
- Application version：3.0.4；CandidateVersion：`V3EvidenceCandidate-v1`。
- 線上版維持 3.0.3。本機預覽 `http://localhost:5194`。
- 六項候選公式已實作，結果始終標記 `RESEARCH_CANDIDATE_NOT_PRODUCTION_VERIFIED`。
- **不宣告 PHASE 3 PASSED，不建立 V3FullEvidence100-v1 正式模型。**

## 結果

| 項目 | 本機狀態 |
|---|---|
| Trend Candidate | 已實作四項特徵、SMA20/60/120、斜率、排除當根的結構範圍 |
| Momentum Candidate | 已實作 RSI 分段插值、MACD/Close、ROC；直接使用既有 TechnicalIndicators |
| VolumePrice Candidate | 已實作 ln(RVOL)、Return×ln(RVOL)、突破距離；prior volume/high 排除當根 |
| Institutional Candidate | 已實作流量比、三期間加權、三群組歷史分位數及明確指定群組的持續性 |
| RelativeStrength Candidate | 已實作股票與歷史基準的三期間超額報酬、歷史股票池分位數 |
| RiskQuality Candidate | 已實作明確指定的標準差／ATR 方法、回撤幅度、流動性分位數 |
| Golden / PIT / Replay | 104 項 V3 檢查通過，含獨立手算 golden、未來修訂、資料缺漏及序列順序 |
| CLI | JSON 輸入／輸出重播與測試程式產出的 trace 逐字相同 |
| Legacy regression | MarketRadar、FinMind、LegacySwing75、PaperTrading、StrategyCenter、RadarTracking 全通過 |
| V2 foundation / evidence | 45 / 64 項既有檢查通過 |
| build | 本機 Release 建置通過 |
| legacy-fast / legacy-sql | 對應本機測試通過；本次未執行 GitHub jobs |
| v2-foundation / v2-evidence / v3-model | 對應本機測試通過；本次未執行 GitHub jobs |
| WorkflowRunId / SameSHA | N/A / NOT RUN，依本機限定指示 |

CI 定義已在本機新增 `v3-model` 並納入同 SHA 聚合檢查，未上傳或觸發。通過這些測試仍不代表 Research／OOS gate 通過。

## 實作介面

- `Models/Evidence/CandidateContracts.cs`：不可變資料列、歷史快照、計算身分、參數與結果契約。
- `Services/Evidence/CandidateMath.cs`：共用 Clamp、Linear、InverseLinear、midrank percentile、分段插值及 decimal log/sqrt/median。
- `Services/Evidence/CandidateFeatureCatalog.cs`：19 項版本化 FeatureDefinition，沿用 3.0.3 契約。
- `Services/Evidence/V3CandidateCalculator.cs`：無 I/O 的候選研究計算器，已註冊 DI。
- `src/WorkJournal.Evidence.Cli`：離線 JSON 執行工具，不啟動 Web host、不連接 provider 或資料庫。
- `tests/V3ModelChecks`：公式、失敗條件、PIT、golden、replay 檢查與合成範例匯出。

3.0.3 的 FullEvidenceScoreCalculator、FullEvidencePreparationService 和既有測試保持原樣。原 preparation 仍是保守的基礎檢查介面；新增 CandidateDataset 是離線研究輸入邊界，沒有將舊 preparation 自動升級成正式資料供應器。

## 參數與資料約束

所有權重、MA 期間、RSI anchors、期間與邊界都在 `V3EvidenceCandidateV1ParameterSet`。文件明定的 Candidate v1 公式受版本檢查保護；改權重或公式不能沿用相同 CandidateVersion。未給定的上下界與期間沒有正式預設值。測試用小期間與 [-1,1] 邊界只存在合成 fixture。

標準差可明確選 Population 或 Sample；ATR 可選 SimpleMean 或 WilderSeededMean；法人持續性必須明確選 Foreign／Trust／Dealer／Combined。這些是顯式的研究選項，沒有宣稱文件已指定其中一個為正式版本。LiquidityPrice 必須明確選 `Close` 才使用 Close×Volume 代理成交金額。

資料必須帶 DatasetSnapshot／UniverseSnapshot／BenchmarkVersion／VersionSet／ParameterSet 識別、內容校驗參考、價格基準版本、TradeDate、DataCutoff。ParameterChecksum 必須匹配實際參數 JSON。結果另外包含 canonical InputDigest；計算識別碼包括資料內容摘要，避免相同外部 ID 下的修改結果被混用。

每個交易日須依明確歷史 calendar 對齊。必要視窗內資料缺少、重複、無效或 stale，均傳遞相應狀態；不跨缺漏計算。未來交易日／晚於 cutoff 才發布的修訂不進入歷史計算。法人 AvailableAt 不明時保持 Missing，不以交易日期代替。

跨股票分位數使用完整指定歷史 universe，包括目標股票；任一必要成員缺少資料就阻擋該 Evidence，不縮小分母。ties 使用文件的半權重公式；單一成員按同公式為 0.5，空集合拒絕。負向風險使用 1−Percentile。Unavailable 的 normalized value 為 null，不補零、中性分數或重分配權重。

## Primitive 分類

- VERIFIED_PRIMITIVE：沿用上一版固定 FullEvidence100 聚合器的已驗證行為；本次不新增正式模型認證。
- REUSED_PRIMITIVE：原 TechnicalIndicators 的 RSI14、EMA12/26/9、MACD。SMA 擴充採相同尾端算術平均定義；原程式與 Golden fixtures 未修改。
- NEW_CANDIDATE：所有新正規化／特徵組合；decimal log/sqrt；ATR 兩種明確研究選項；Close×Volume 流動性代理。
- MODEL_DEFINITION_GAP：各項 Linear 上下界、未指定期間、持續性群組、Variance／ATR 方法、流動性代理須由研究參數明確提供；尚未有核定／校準後的 ParameterSet。
- PIT_DATA_GAP：真實來源 AvailableAt 驗證政策、歷史交易 calendar／完整 cohort、公司行動與價格調整基準、benchmark mapping 的可知時間、實際 snapshot payload adapter 仍需建立與驗證。

目前資料庫 BenchmarkMapping 沒有知識時間欄位，CLI 因此要求明確的 `BenchmarkMappingAvailableAt`；無法提供即阻擋 RS。呼叫者提供的時間、checksum 及 Finalized 標記是輸入契約，不是本工具替外部來源簽發的真實性證明。沒有宣稱已完成真實歷史資料的端到端 PIT 認證。

## 尚未完成的 Gate

ResearchRun 保存／校準、Coverage、IC／Rank IC、quantile monotonicity、turnover、regime／parameter stability、train/validation/OOS 切分與 OOS 驗證皆未進行。這版輸出可序列化 CandidateTrace（含身分、raw features、normalized features、權重與缺口），尚未寫入正式 DecisionTrace 表或建立正式評分 API。

不能取代波段／市場排行評分、不能套用 Radar70／Swing75 到 V3、不能把分數當機率或買進訊號。程式沒有策略、排名、選股、配置或交易串接。

## 本機操作

```powershell
dotnet build WorkJournal.sln -c Release --artifacts-path artifacts/build304
./Start-WorkJournal304.ps1
dotnet run --project tests/V3ModelChecks -c Release --artifacts-path artifacts/build304
```

產生合成範例（不是真實行情）：

```powershell
dotnet run --project tests/V3ModelChecks -c Release --artifacts-path artifacts/build304 -- --export-fixture artifacts/demo304
dotnet run --project src/WorkJournal.Evidence.Cli -c Release --artifacts-path artifacts/build304 -- artifacts/demo304/synthetic-dataset.json artifacts/demo304/synthetic-parameters.json 10000000-0000-0000-0000-000000000001 artifacts/demo304/my-new-trace.json
```

輸出檔必須是新檔名，防止覆蓋先前研究紀錄。Incomplete 仍會寫出帶缺口的有效研究結果。真實資料的 JSON 請依 CandidateDataset 契約建立，禁止將測試參數當 production preset。

本機證據：`artifacts/phase304-regression.json`、`phase304-*.log`、`phase304-baseline.json`、`phase304-verification.json`、`demo304/cli-trace.json`。既有 System.Security.Cryptography.Pkcs 9.0.11 NU1902／NU1903 建置警告仍存在，未在此模型工作中調整套件。
