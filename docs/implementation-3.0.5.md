# Worklog 3.0.5 — Phase 4 research infrastructure

依據桌面《Worklog Phase 4 — PIT Research / Calibration / OOS / V3 Model Promotion》實作，2026-10-30。

## 交付範圍

在既有 `V3EvidenceCandidate-v1` 外建立研究層，保留 Candidate、Legacy、FullEvidence100 外層權重與缺值語意。本版可執行明確提供的 PIT 研究快照、產生結果並持久保存與重播；**不宣告 Phase 4 歷史研究完成或模型晉升**。

- StartSHA：`367434df2194b80a33d58a73875f869372d31bc9`。
- Branch：`codex/release-3.0.5`；Version：3.0.5。
- EndSHA、WorkflowRunId、SameSHA：由本次 PR／CI 與 `artifacts/release305-verification.json` 記錄實際結果，不預填 PASS。
- 使用者已授權本機實作、Git 上傳及正式網站上版；部署與模型晉升是不同事件。

## 已建立的研究層

| 項目 | 行為 |
|---|---|
| ResearchRun | RunId、版本、各歷史快照身分、參數校驗碼、protocol、時間區段、輸入／輸出摘要與生命週期 |
| PIT dataset boundary | 每日獨立 CandidateDataset、明確來源證據、歷史 calendar；重用既有 Candidate PIT 阻擋 |
| Outcome labels | 獨立 OutcomeDataset，固定 revision、evaluation cutoff、price basis；不傳入 Candidate calculator |
| IC / RankIC | 每日橫斷面 Pearson／Spearman，再列出平均、中位、標準差及正 IC 日期比例 |
| Quantiles | 每日以 Evidence 分組；半權重 ties 同組，缺 outcome 不改組別；空組維持空值 |
| Distribution | Min、P01/05/10/25/50/75/90/95/99、Max、Mean、StdDev、MissingRate |
| Correlation | 六項 Evidence 與 19 個 normalized subfeatures 的每日相關性矩陣 |
| Ablation | 六項領域及四項 Trend 子特徵移除比較；只做相減、不重新分配權重，另標研究對照 |
| Train/Validation/OOS | 明確 chronological windows；不允許重疊、隨機切分；label end 跨區間即 purge |
| Walk-forward | 支援多個明確 fold；每個 fold 時間順序與 embargo 受驗證，OOS 區段不得互相重疊 |
| Comparison / selection | 開發期 scorecard 比較，不發明單一 promotion score、不自動挑贏家；選擇需理由及時間 |
| OOS lock | 需與 Train/Validation 資料、參數、結果一致的凍結 receipt，且選擇時間早於 OOS 日期 |
| Breakdown | 只接受具版本、AvailableAt 的歷史 Regime／Sector／Liquidity／MarketCap 分組，不補造門檻或分類 |
| Persistence / replay | 原子發布本機 JSON bundle、不得覆寫 Finalized RunId；manifest、output digest 與重新計算逐字比較 |
| Failure | 失敗保存 failure.json，沒有有效觀測則 Rejected；不以 Completed 空結果冒充成功 |

研究數學在 decimal 下運作，不以 UI rounding 參與計算。標準差診斷使用明確的 population 定義。分位數區間採 `MidrankKeepTies-v1`，分布百分位採 `LinearOrderStatistic-v1`。每日 IC 不以整期相關係數替代；無樣本或常數序列的相關係數為 null。

Quantile mean/median 為各日期 quantile 成員的 pooled observation 統計；IC 的期平均則為各有效日期等權。單調性列出有資料的相鄰 quantile 中嚴格改善比例，不設 promotion 門檻。TurnoverProxy 為相鄰日期最高組成員的 `1 - intersection/union`；空最高組時不補零，不宣稱為投資組合換手率。

## Outcome 與防洩漏

`FR_N = Close[t+N]/Close[t]-1`，N 由研究 protocol 提供。max/min 欄位明確命名為 **FutureMaxCloseReturn / FutureMinCloseReturn**，是期間內 future closes 的極值，不猜測盤中 high/low，也不是可執行交易 PnL。

Outcome 來源需固定快照、revision、價格基準，t 的 close 必須等於 PIT Evidence 的基準 close。未知發布時間、無效價格、缺交易日、不同價格基準或跨 partition 的標籤保持 unavailable。

Development-only run 不計算 OOS observations，且不對跨分割邊界的 label 產生數值。SelectionScorecard 不含 OOS metrics，digest 綁定 development 快照與 outcome 來源，並校驗資料可知時間。OOS 可於參數凍結後評估；任何 OOS 結果都不能透過 Compare／Select 介面進入選擇。多 fold 的後續訓練可使用已經成為歷史的前期資料。

凍結 receipt 是可稽核的研究宣告與程式檢查，並非外部簽章／可信時間戳。真實研究仍需審閱操作紀錄，不能靠手寫 receipt 證明從未看過 OOS。

## 資料與研究缺口

本機唯讀盤點 `WorkJournal` 資料庫：`sys.tables` 的 `V2%`／`%Snapshot%` 查詢僅發現 `StrategySignalSnapshots`，沒有 V2 DatasetSnapshot／UniverseSnapshot／DataProvenance 表。這只描述本機，不推論線上資料庫內容。

目前未提供核可的完整 PIT 歷史研究輸入、真實來源 availability policy、已校準 ParameterSet、正式 train/validation/OOS protocol、歷史分類／股數證據。因此本版執行的是合成 fixture 對研究基礎設施的驗證，不是市場預測能力研究。

`DataAvailabilityPolicy` 記錄 provider、dataset、版本、有效期間、rule 與證據來源，不會自行從 TradeDate 推定發布時間。外部證據引用由呼叫者提供，程式沒有替資料來源簽發真實性認證。未驗證來源一律不能成為 promotion 證據。

現階段正式研究狀態：

| 領域 | 真實歷史研究狀態 |
|---|---|
| Trend | NOT_ENOUGH_EVIDENCE |
| Momentum | NOT_ENOUGH_EVIDENCE |
| VolumePrice | NOT_ENOUGH_EVIDENCE |
| Institutional | BLOCKED_BY_DATA：歷史發布時間尚未建立可驗證政策 |
| RelativeStrength | NOT_ENOUGH_EVIDENCE：歷史 universe／benchmark 尚待認證 |
| RiskQuality | NOT_ENOUGH_EVIDENCE |

**PromotionStatus = BLOCKED_BY_DATA**。歷史校準、替代 RSI/MACD／法人分母研究、正式 ablation、參數與 regime 穩定性評估及實際 OOS 尚未完成。現有比較介面可接受不同參數研究的 scorecards，但未註冊新公式變體；MA、RSI 曲線或法人分母等變更仍須建立不同 CandidateVersion，不能繞過 3.0.4 的 v1 公式鎖。

缺 Institutional 時總分仍 null、AvailableWeight=80；個別 Evidence 可獨立研究。Full-model ablation 使用完整觀測的配對對照，不用「去掉缺項」產生假百分制總分。

## 持久化與介面

新增 `Models/Research/ResearchContracts.cs`，以及 `Services/Research` 下的 ResearchMath、ResearchProtocolRules、ResearchOutcomeCalculator、ResearchMetrics、ResearchEngine、ResearchStore。ResearchEngine 已註冊 DI。

沿用離線 `WorkJournal.Evidence.Cli`，沒有新增正式研究 HTTP 寫入端點。研究 bundle 的目錄由操作者明確指定；發布網站不會自動跑大型研究、匯入合成資料或變更資料庫。本版不需要 migration。

每個 run 保存：

```text
request.json
research-run.json
research-observations.json
research-metrics.json
evidence-distributions.json
quantiles.json
correlations.json
ablation.json
oos-results.json
development-scorecards.json
promotion-report.json
report.json
manifest.json
```

Input/output digests 使用排序 JSON object keys、正規化 decimal 表示與固定集合順序。為兼容鎖定的 Candidate v1 ParameterChecksum，`request.json` 保留原始 decimal scale；研究摘要仍採 canonical encoding。API 防止覆寫 Finalized bundle；manifest 與完整重播偵測內容修改，但不宣稱為具簽章的防惡意篡改儲存。

生命週期提供 Created → Prepared → Running → Completed，以及 Failed／Rejected。Promoted 雖有明確狀態名稱，但本版沒有自動轉入的路徑；正式晉升與新 Golden fixtures 必須另獲批准。

## 本機操作

```powershell
dotnet build WorkJournal.sln -c Release --artifacts-path artifacts/build305
./Start-WorkJournal305.ps1
dotnet run --project tests/V3ResearchChecks -c Release --artifacts-path artifacts/build305 -- --export-fixture artifacts/demo305
dotnet run --project src/WorkJournal.Evidence.Cli -c Release --artifacts-path artifacts/build305 -- --research artifacts/demo305/synthetic-research-request.json artifacts/research305
```

其餘命令（使用新輸出檔名，保留舊研究紀錄）：

```text
--research-replay <store-root> <run-guid>
--research-scorecard <store-root> <run-guid> <fold> <new-scorecard.json>
--research-select <scorecard.json> <ISO-time> <rationale> --output <new-selection.json>
```

要執行 OOS，請把選擇 receipt 放入新 run 的 `Selections`，並明確設定 `EvaluateOos=true`；OOS run 使用新 ResearchRunId。合成範例僅示範，不得當真實研究或 production preset。

## 驗證與發布紀錄

78 項研究測試涵蓋研究數學 golden、時間切分／embargo、purge、future-label 隔離、當前 universe／benchmark 拒絕、未來 AvailableAt、缺法人無重分配、版本與參數身分、OOS 選擇阻擋、分位數 ties、相關性、消融、分組、持久化、重播、防覆寫及修改偵測。

完整本機執行結果保存於 `artifacts/phase305-regression.json` 與 `phase305-*.log`；既有 V3 model、Legacy fast/SQL、V2 foundation/evidence 全部列入回歸。新增 `v3-research` CI lane，只跑小型 deterministic fixture，既有七個 jobs 全保留並擴充 same-SHA 聚合檢查。

部署完成的實際 SHA／CI run IDs／線上 HTTP 與版號證據保存於 `artifacts/release305-verification.json`；不把研究框架測試 PASS 寫成歷史研究或模型 promotion PASS。原 System.Security.Cryptography.Pkcs 9.0.11 的 NU1902／NU1903 警告仍在，本次未變更套件。
