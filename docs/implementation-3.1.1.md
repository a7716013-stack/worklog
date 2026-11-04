# Worklog 3.1.1 — Phase 4G 與市場分析效能修正

## 範圍與 API 優先

依使用者要求，先核對桌面 `# Worklog Phase 4G.txt` 的 §0–144，實作公式研究基礎設施、共用運算抽離及市場排行效能改善。根目錄 AGENTS.md 加入 API 規劃、契約與建置優先規則。正式 HTTP 契約沿用 `/api/v1`，先定義服務與 DTO，再切換網站呼叫。

## 運算抽離

- `WorkJournal.Domain`：既有 V2／Evidence／Research 契約、Candidate-v1、FullEvidence100、TechnicalIndicators、LegacyRadar100、ResearchEngine／Metrics／Math／Outcome／Protocol，以及新增公式身分、診斷及治理規則。
- `WorkJournal.Application`：市場排行協調、供應商介面及 RadarJobs 背景工作。新模組不引用 Web assembly。
- CLR namespace 暫時保留原名稱，避免既有 CLI／EF／序列化消費端不必要的破壞；實體檔案與編譯 assembly 已移出 Web。
- Web 保留 EF/PIT 準備、資料供應商 adapter、歷史 authority archive、ResearchStore、登入與畫面；Api → Web 暫時仍承載這些既有 hosting/storage 相依。這次不是整個網站及所有歷史儲存服務的全面搬移。

## Phase 4G 對照

| 文件範圍 | 實作與限制 |
|---|---|
| §1–40、55–63 | Candidate-v1 原有權重 25/15/15/20/15/10 與六組公式不變；19 項 raw/normalized feature 診斷、飽和、逐日分布、相關矩陣；未發明 v2、anchor、lookback 或 production threshold。 |
| §41–44 | Train-only calibration receipt 驗證、明確 historical universe 與時間範圍，禁止跨 validation 邊界。Receipt 是研究紀錄，不會自動更改參數或升級模型。 |
| §45–59 | Evidence／FullEvidence validation contracts、逐日 Pearson/Spearman、同值同 quantile、年度／季度穩定性、固定 Full + 六種 ablation。不將剩餘權重重新換算 100。 |
| §31–35、52–54 | Partial IC helper 與 future volatility/drawdown/downside/liquidity stability label helper；future labels 不傳入計分器。缺完整 future sessions 回 null。Regime 與真實 risk outcomes 仍須 PIT sidecar 與核准資料；不產生虛構研究結果。 |
| §64–82 | 假說先於評估、明確多指標比較、KEEP_V1 合法、CandidateFreezeReceipt 與 calibration receipt；controlled CLI 使用 CreateNew 保存。既有 ResearchStore 的 approval、purge/embargo、development selection、OOS 與 replay 防護保留。新 receipt 不構成 production promotion，也不取代既有 OOS 授權。 |
| §83–95、109–110、122–129 | Feature／Evidence／candidate／hypothesis／calibration／comparison／ablation／correlation／stability／freeze／promotion／research-package 正式 API；列表分頁，套件回 references。公式、資料、baseline、研究、promotion 分開。 |
| §96–108 | FormulaDigest 由內嵌的既有公式 source manifest 決定，統一 LF 後 canonical hash；ParameterChecksum 沿用已凍結算法。Canonical compute 回 CalculationIdentity 與各 feature raw/normalized trace。低階 aggregate 明確標為 formula-only。 |
| §111–121、130–144 | 持續查核 authority；沒有 approved dataset 就停止真實 baseline。保留 NOT_RUN、NOT_PROMOTED、V3Threshold UNRESOLVED、Phase5 LOCKED。工程測試不代表預測能力驗證。 |

研究 API 讀取版本化、清除敏感資訊的 release snapshot，不是可即時核准資料的公開管理介面。未存在的 hypothesis/comparison/calibration 回空列表或 404；未執行的研究回 `status=NOT_RUN,value=null`。尚無真實 comparison／freeze／promotion，不偽造資料來填滿端點。新增核准資料時，必須沿受控研究 archive 流程執行、驗證並發布對應報告；API 遇到未支援的新 approval 狀態會 fail closed。

## 市場排行

3.1.0 診斷基準：基本排行約 3.37 秒取得 2,313 筆；整批 analyze 等待 55 秒仍無回應，測試端停止。主要瓶頸是全域分析排隊、資料快取鎖持有期間下載，以及整批完成才顯示。

3.1.1 行為：

- SourceCache 改成每個 SHA256 資料鍵的跨程序檔案鎖，相同資料合併下載；不同鍵可繼續。原有 provider-wide quota、reserve、cooldown 與最小間隔保持不變。
- 新增 bounded hosted worker：佇列 8、最多保留 64 工作、最多 4 檔並行、單檔 35 秒、整批含排隊 180 秒、工作保留 15 分鐘。這些是操作限額，不是模型參數。
- 相同市場／日期／LegacyRadar100／五分鐘 freshness window 重用工作，結果另帶實際行情／事件摘要 DataVersion。不同 process 的 job 不共享，但 source cache 的同鍵鎖仍可跨程序重用。
- 先顯示基本行情，再每 2 秒查詢進度與部分結果。未完成結果按股票代碼顯示且標示非最終排行；已完成仍可能有缺項，缺值不補零、不換算百​​分。
- POST 建立工作回 202 + Location + Retry-After；來源慢不阻塞建立工作／進度查詢。資料取得失敗、逾時與 interrupted 都有明確狀態。
- Job 存於記憶體；服務重啟後舊 id 回 404，網站提示重新整理，不假裝背景仍在執行。離開頁面停止輪詢，不取消其他使用者共用工作。
- 原同步 API/MVC response shape 保留，analyze 新增 45 秒總上限。正式 Api host 網站走新 API；仍以舊 Web executable 啟動時保留 MVC 相容模式。新版 launcher 使用 Api host，port 5203。

外部來源與額度仍可能使冷快取分析不完整。背景工作與進度回傳解決無限等待，不承諾所有供應商都在固定秒數提供完整資料。失敗工作在同一 freshness window 可被重用，稍後刷新進入新工作；成功取得的歷史資料保留快取。

## 安全、測試與發布

Research API 沿用登入／claims；compute、aggregate、建立排行工作皆要求 antiforgery token。市場工作只保存公開行情。沒有新增交易寫入、authority 公開 mutation、計費設定或資料庫 migration。

驗證涵蓋：既有 Candidate-v1／LegacyRadar100／LegacySwing100 golden、0/50/57/100 HTTP aggregation、canonical trace replay identity、missing/constant IC、飽和／時序、ablation、calibration leakage、假說／freeze 順序、快取 single flight／不同鍵隔離、CSRF、job coalescing、舊 OpenAPI 相容與瀏覽器進度。

完整執行結果與真實 authority 查核結果另記於 `engineering-3.1.1.json` 與 `phase4g-real-execution-3.1.1.json`；發布採相同 commit SHA 的 CI、編譯與部署後 health/readiness 驗證。現有 Pkcs 相依套件的 NuGet advisory 警告需另案升級驗證，這版不隱藏建置警告。
