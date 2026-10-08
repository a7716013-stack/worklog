# 從虛擬交易與波段名單加入模型驗證（1.3.2）

在虛擬交易「持有部位」與「委託紀錄」，以及波段分析每張自選卡片新增「加入績效模型驗證」。成功後前往「績效與模型驗證」，自動選擇我的追蹤、該股票代號及快照建立後次日開盤基準，定位分數分桶驗證。

採用現有雷達 V1 模型，不混用波段評分。使用最新可用市場收盤行情，單獨取得該標的歷史資料，即使未列入雷達每日候選池也可加入。有當日既有快照時沿用；重複加入或不同入口加入不覆寫快照、不重設追蹤起日。已停止的個人追蹤可重新啟用。

這是加入模型樣本與個人追蹤，不是回填當初虛擬單的分數或重新模擬其成交，也不把先前委託追認為此快照的推薦交易。股票沒有可靠收盤行情時拒絕加入；歷史資料不足時保留不完整評分，不虛構完整分數。未成熟績效由既有更新流程處理，維持待更新。

## 安全與範圍

- POST 與既有 antiforgery；登入者由 claims 取得。
- 波段来源必須在該帳號的 StockWatchlistItems；虛擬交易來源必須有該帳號的 PaperOrder。
- 寫入前在現有 SQL application lock 交易內再次核對名單；唯一日／股票與個人追蹤約束沿用。
- 只建立／重用 MarketRadarRecommendation 與個人追蹤；不新增每日公開選股，不更改訂單、現金、持倉或交易連結。
- 既有 FollowAsync 與新入口共用 SaveFollowAsync，以保留權限、去重與不可變快照規則。
- 來源失敗／逾時顯示安全訊息並返回原功能頁；不回傳 exception stack。
- 無 migration。版本 1.3.2，隨本版發布至 GitHub 與正式站。

## 驗證

- Build：PASS，0 warnings / 0 errors。
- PortfolioEnrollmentChecks：18 PASS（非候選股票、來源／代號驗證、跨帳號、並發去重、評分不足、私人样本、現金／訂單不變、重新追蹤、模型顯示及來源失敗）。
- MarketRadarTrackingChecks：既有 35 PASS。
- MarketRadarChecks：218 PASS。
- PaperTradingChecks：56 PASS。
- performance_enrollment_browser_test.py：PASS（波段與持倉／委託按鈕、CSRF、未登入、不同帳號、個人模型導向、390px 手機版、無 JS 錯誤）。
- 所有測試寫入獨立隨機名稱 SQL 資料庫，不操作使用者既有委託。

## 檔案

- Services/MarketRadarRecommendationService.cs：來源歸屬、單股快照、共用追蹤儲存。
- Controllers/StockAnalysisController.RadarTracking.cs：AddPerformanceValidation POST。
- Views/StockAnalysis/PaperTrading.cshtml、Swing.cshtml、_PerformanceValidationButton.cshtml：加入入口。
- wwwroot/js/swing.js：含 CSRF 的卡片表單。
- tests/MarketRadarTrackingChecks/PortfolioEnrollmentChecks.cs、Program.cs；tests/performance_enrollment_browser_test.py。

MarketRadarScoreCalculator、PaperTradingService／Calculator、Calendar sync 與 WorkLog CRUD 未修改。
