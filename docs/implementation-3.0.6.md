# Worklog 3.0.6 — Phase 4B 實作報告

本版交付歷史來源封存、PIT 資料集建構、研究轉接、候選登錄與證據報告。真實官方樣本已取得；歷史公布時間、修訂證明、股票池與基準仍不足，因此第一個「核准真實 PIT 研究」里程碑目前是 **BLOCKED_BY_DATA**，不是已完成模型驗證。

## SOURCE

- 依據桌面文件 `# Worklog Phase 4B.txt`；保留既有 Candidate v1、Phase 4 ResearchEngine 與 Legacy 計算。
- `Models/HistoricalResearch` 保存 raw fact、原始回應、凍結可得時間政策、歷史身分／股票池／基準／交易日曆與價格政策契約。
- `IHistoricalResearchDatasetBuilder` 只處理身分、修訂、PIT 篩選與快照；沒有評分、參數搜尋或 future outcomes。
- 時間政策分 Known、DerivedFromVerifiedRule、Unknown。Unknown 永不進入 PIT；TradeDate 不代替 AvailableAt。政策須有來源、review、凍結時間、適用期間與時區；事實須有公布及修訂證明。
- 重複 source record/revision 阻擋；同一時點的衝突修訂阻擋；多版本僅選 cutoff 前最後一個可驗證版本。原始 bytes SHA256 與 fact digest 都驗證。
- 使用歷史股票池與對應日期的 benchmark mapping；不能回填今天的名單。下市涵蓋不足會列 `SURVIVORSHIP_BIAS_RISK`。
- 價格政策明確記錄 Raw／Adjusted／Total-return 的選擇、PriceBasisVersion 與 corporate-action policy。此版不提供未經驗證的公司行動調整器；需要外部審查過的資料與政策。現有轉接器接收 TWD、股數單位 OHLCV／成交額；基準可使用相同契約的證券序列，純指數另需明確單位契約轉接。
- 完整度分 Price、Volume、Institutional、Benchmark、Universe、Date、Instrument，分母使用提供的歷史股票池與交易日曆。無法建立分母時 coverage=null，不視為零缺失或完整覆蓋。
- `HistoricalDatasetStore` 以 staging→final directory 保存 request、result、manifest；不覆寫 finalized snapshot；重播重新建構並比較摘要。
- `ResearchCandidateRegistry` 追加不可覆寫的候選登錄，保留 REJECTED 候選。修改參數／公式需新身分；本版不實作自動候選演化。
- `EvidenceValidationReport` 接回既有報告，補 IC 中位數／標準差／正值比、年度分布、消融 RankIC 差、六分項 Formula/Data/Research/OOS 狀態、來源身分與候選／參數／檢視指標數量。數量範圍是指定 research store 的已封存報告及 registry，不宣稱跨所有研究的全域終身計數。失敗執行的 staging/failure artifacts 保留。
- 新增 `v3-pit-dataset`，納入相同 commit SHA 的 Golden Master gate。原有 CI 工作保留。

## EXECUTION

本機已執行完整回歸：build、V3 PIT dataset、V3 Research、V3 Model、MarketRadar、FinMind、LegacySwing75、PaperTrading、StrategyCenter、MarketRadarTracking、V2 Foundation、V2 Evidence，以及舊研究 CLI 的執行／重播／scorecard／selection／OOS 流程。

- 45 項新 PIT 檢查：重複／未知時間／未來修訂／現行股票池與基準隔離／價格基準混用／checksum／持久化／重播／候選不可覆寫／共享模型／未來標籤隔離／OOS 隔離／報告來源驗證。
- 原有 78 項 Research、104 項 Candidate、64 項 Evidence、45 項 Foundation 檢查及 Legacy／SQL 回歸通過。
- 本機證據：`artifacts/phase306-regression.json`、`artifacts/phase306-*.log`。PIT CLI 與真實來源重播證據：`artifacts/phase306-pit-verification.json`。
- 既有 Pkcs 9.0.11 套件 NU1902／NU1903 警告仍存在；本次沒有變更套件版本。
- 沒有資料庫 migration；新增資料集及研究檔案位於操作者指定的離線目錄。發布包不包含原始來源封存、研究輸出、使用者 secrets 或本機資料庫。

## RESEARCH

已擷取官方 TWSE `STOCK_DAY`：2330、2026-09，共 20 筆；`T86`：2330、2026-09-30，共 1 筆四群組彙整後法人事實。保留原始 bytes、來源網址、ObservedAt／RetrievedAt、parser version 與 SHA256。

| 原始回應 | SHA256 |
| --- | --- |
| STOCK_DAY | `C556FB917676D81615B5C8E133C5F36DAE52FBA2EFCFC8B9AB196C86EF1506EC` |
| T86 | `0D2533EEF03F4E7160D21404D5FD449EE53A8B663AFC1A278B98C09D7BA4724E` |

官方[每日收盤行情產品說明](https://eshop.twse.com.tw/zh/product/detail/cfec9a1470e448ec91bfde006db361e8)列有多批資料產製時間及不同交易涵蓋範圍；這不能直接證明本次下載歷史 STOCK_DAY 的原始版本。官方[三大法人買賣金額統計產品](https://eshop.twse.com.tw/zh/product/detail/d31c1b9570ae47058ec83a0bb1ffa419)是金額統計，也不能外推成 T86 逐股股數報表的歷史公布／修訂證據。

因此 captured-body revision 只代表下載內容的識別，不冒充交易所歷史版本編號；兩種來源的 AvailableAt 均保持 null、Confidence=Unknown。真實輸入做封存／重播及缺口報告，**沒有執行真實 IC、OOS 或參數最佳化**。合成資料成功走通既有 ResearchEngine，只能證明程式流程與數學實作。

固定外層權重仍為 25／15／15／20／15／10。測試確認法人不可得時 AvailableWeight=80、FullEvidence100=null，其餘可用分項仍可獨立計算。

## KNOWN GAPS

| 類別 | 本次真實研究狀態 |
| --- | --- |
| DATA_GAP | 歷史 source revision／完整研究區間、標籤與經審查 calendar 尚缺；下載摘要不等於當年版本證明。 |
| AVAILABILITY_GAP | STOCK_DAY／T86 歷史公布時間與適用政策尚未核准；Unknown 全部排除。 |
| UNIVERSE_GAP | 未提供涵蓋上市／下市變動的歷史股票池；禁止用今天股票清單反推。 |
| BENCHMARK_GAP | 未提供歷史基準定義、映射及可得時間證據。 |
| CORPORATE_ACTION_GAP | 未提供經審查的公司行動與一致價格基準政策。 |
| CLASSIFICATION_GAP | 歷史產業、規模、流動性 quantile、成交量／報酬／突破狀態等分組定義未提供；框架接受版本化 PIT 分組，不臆造門檻。 |
| FORMULA_GAP | Candidate v1 已實作，真實研究參數仍需明確凍結；測試參數不能自動成為正式參數。 |
| NORMALIZATION_GAP | 正規化方向、飽和／壓縮／跨年分布穩定性尚無真實研究結果；報告提供事實統計，不自行制定驗證門檻。 |
| COVERAGE_GAP | 21 筆來源事實均不符合已驗證 PIT；無核准 ResearchSnapshot，不輸出假回測績效。 |

第一個核准真實 PIT dataset＋歷史股票池＋benchmark＋真實 baseline run 尚未達成。須先補足上述來源證據，再執行既有 CLI。所有六項 Evidence 仍為 research candidate；完整模型 promotion、V3Threshold、買賣／持有／配置規則維持鎖定。

## 使用與重播

```powershell
dotnet build src/WorkJournal.Evidence.Cli -c Release
dotnet run --project src/WorkJournal.Evidence.Cli -- --pit-capture Price 2026-09-01 2330 new-price-capture.json
dotnet run --project src/WorkJournal.Evidence.Cli -- --pit-capture Institutional 2026-09-30 2330 new-flow-capture.json
dotnet run --project src/WorkJournal.Evidence.Cli -- --pit-build reviewed-request.json dataset-store
dotnet run --project src/WorkJournal.Evidence.Cli -- --pit-replay dataset-store SNAPSHOT_N_GUID
dotnet run --project src/WorkJournal.Evidence.Cli -- --pit-register candidate.json registry
dotnet run --project src/WorkJournal.Evidence.Cli -- --pit-research dataset-store SNAPSHOT_N_GUID research-template.json research-store
dotnet run --project src/WorkJournal.Evidence.Cli -- --pit-report research-store RUN_GUID registry new-report.json
```

request 契約見 `HistoricalBuildRequest`；capture 不是已核准的 request。research template 的 Snapshots 必須為空，由驗證過的 archive 填入；參數 checksum、標籤基準、時間切分與 OOS receipt 由原 ResearchEngine 繼續驗證。研究資料夾應放在受控、持久化且非網站靜態目錄，並另做備份；checksum 可偵測內容改變，不能取代外部來源審核或簽章。

本機啟動：`dotnet build WorkJournal.sln -c Release --artifacts-path artifacts/build306` 後執行 `./Start-WorkJournal306.ps1`，預設 `http://localhost:5196`。發布目標為原 GitHub repository 及既有 Azure／Cloudflare 網站；精確 commit、CI run、package SHA 與上線檢查另存本機 release verification artifact。
