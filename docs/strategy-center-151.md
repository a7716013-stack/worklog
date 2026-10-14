# Strategy Center 1.5.1

## 已實作

沿用 MVC、FinMind、Radar 計分與 PaperTrading 執行服務。新增共用普通股 Universe、版本化 ScanProfile、可續跑 ScanRun，以及 Radar Benchmark、Trend Breakout、Institutional Momentum、Volume Breakout、SwingCandidate。

上市／上櫃以官方 CFI 識別普通股；排除異常交易、少於 120 日歷史與前 20 日平均成交金額低於 2,000 萬元者。設定由 `StrategyCenter` configuration section 綁定，完整預設值見 Models/StrategyCenter.cs。修改研究門檻必須同時增加 Profile.Version，避免歷史結果混用。

策略共用 Episode 與每日 Evaluation。明確 NotMatched 結束 Episode；資料不足保留 Episode 並標記缺口；法人 A/B/C 等級變化不拆樣本。只有 NEW 產生主要績效樣本。Snapshot 與 Signal 同一 SQL transaction 寫入，以鎖與 unique index 防重；已保存 Snapshot 不可修改。

績效使用交易所交易日曆的 T+1/3/5/10/20 終點，提供訊號收盤及次日開盤兩種基準。缺開盤價不替代，未成熟為 NULL；MFE/MAE 使用完整期間最高／最低價。使用者確認下單後才建立虛擬單，來源 SignalId 與訂單同交易寫入。

入口整合市場分析排行、策略分析、虛擬交易；策略頁提供總覽、訊號、績效比較、版本／產業／Heat／法人等級分組。虛擬交易保留既有操作並加入自選側欄、績效連結及策略來源對照。

## 本機操作

網址：https://localhost:7180/StockAnalysis/Strategies

背景工作啟動後 45 秒執行，此後預設每 15 分鐘續跑；交易日 13:40 後處理當日訊號，其餘時間準備歷史行情。23 點後收集事件資料，休市日也保留事件覆蓋。主機必須持續運作才能收集；此版本未啟用雲端 Always On。

預設 BatchSize=15、RequestsPerHour=180。這是可續跑的請求預算，不代表每天全市場都能掃完。ScanRun 明確顯示 Partial、Pending、API calls、耗時及跳過原因；不能用部分結果宣稱完整市場排名。付費批次行情與分散式工作排程尚未實作。

## 驗證

- 新策略／交易日／資料不足／Episode／SQL rollback 等 41 項檢查通過。
- FinMind、MarketRadar、MarketRadarTracking、PaperTrading、HomeDashboard、InvestmentInsights、AuthCalendar 七組既有檢查通過。Google OAuth 為模擬回應，未重做真實帳號登入。
- 瀏覽器 1440、768、390 寬度及訊號詳情、績效頁通過，無橫向溢出或 JavaScript 錯誤。
- 官方來源實測識別 1,978 檔普通股，單股行情與法人資料可取得；未聲稱完成全市場當日掃描。
- 本機 SQL 已 COPY_ONLY 備份、RESTORE VERIFYONLY 驗證，再套用 Migration；既有資料表筆數維持一致。備份位置與紀錄見 artifacts/local-migration151.json。

## 實際限制與後續工作

1. 初次歷史準備及逐股來源受 API 配額限制，尚未完成全市場當日端到端驗證。
2. 事件資料需要累積完整覆蓋；不足時 Radar 完整分數與依賴該分數的推薦不冒充可用。
3. 除權息／公司行動目前以大幅價格跳動標記參考資料，尚未提供完整公司行動校正。報酬為原始價格研究值，非含費用的可交易回測績效。
4. 績效樣本必須等後續交易日成熟；不回填假訊號或虛構勝率。
5. 尚未提供自訂策略編輯器、完整回測、Institutional Exit 自動出場或自動下單。

發布至既有 GitHub 儲存庫及正式站時，先備份正式資料庫再套用新增 Migration；不啟用 Always On。免費主機休眠時背景工作不會持續執行，網站喚醒後才恢復排程。實際發布結果另記錄於本機 artifacts。
