# 本機多資料來源（第一階段）

本機 Development 已啟用。1.5.3 上版以 Azure 環境設定啟用正式資料層，無資料庫 Migration。

本機試用將 StrategyCenter、RadarTracking、InvestmentInsights 的自動排程停用，避免啟動後全市場掃描與互動查詢搶用資料預算；手動頁面仍可使用。若要測排程，可在 Development 設定逐項重新啟用。正式環境設定不變。

## 結構與來源

`WorkJournal.MarketData` 是獨立 .NET 函式庫，不依賴 MVC、EF 或網站模型。網站透過 `MarketDataGateway` 取得與既有計算相容的資料；之後可由獨立 API host 承載。

| 資料 | 優先來源 | 備援與限制 |
| --- | --- | --- |
| 上市／上櫃商品清單 | TWSE ISIN 分類清單 | FinMind |
| 上市歷史 OHLCV | TWSE STOCK_DAY 月報 | FinMind |
| 上櫃日常歷史價格 | TPEx tradingStock 月報 | 月報不含鉅額交易，僅保留價格，不將四捨五入張數當成精確股數 |
| 上櫃最近 21 筆成交量及價格 | TPEx dailyQuotes 全市場日報 | 日報完整覆蓋該日資料；較舊月份量保留 null，不補 0 |
| 90 天以內法人資料 | TWSE T86／TPEx dailyTrade 全市場日報 | FinMind；外資不含外資自營商，Dealer 分拆數值僅在完整時合計 |
| 多年法人、超過 730 天的上櫃行情、其他資料集 | FinMind | 共用持久快取、預算與冷卻；無資料時明確拒絕完整回測 |

所有行情均為未還原日資料。不同來源可能因修訂及交易範圍出現差異；波段細節列出實際來源。價格、成交量缺值維持 null，禁止把缺資料當成 0 分或零交易量。

## 降低額度消耗

- 波段、Radar、策略及原有 FinMindStockService 的讀取共用 gateway。
- 上市月份快取不含呼叫端日期區間，重疊查詢直接重用月份。
- 法人及上櫃日行情按「市場＋日期」整批存檔，其他股票重用同一批。
- 一般波段法人只抓最近 45 個日曆日；回測仍要求完整歷史，絕不把近期資料當成五年完整資料。
- 當期／最近七天資料 30 分鐘更新，較舊資料 30 天重驗，商品清單 1 天；FinMind 原始查詢 6 小時。FinMind 的不同日期範圍尚未做區間合併。
- 持久快取使用原子替換；失敗不覆蓋舊檔，不自動把過期資料當成新資料。並行相同快取請求合併。
- FinMind 預設每小時最多 200 次；官方來源合計 240 次，間隔至少 1100ms。這是本機保守預算，不是官方保證的服務額度。
- FinMind HTTP 或 JSON status 402／429 觸發 1 小時冷卻；官方 403／429 觸發 15 分鐘冷卻。預算與冷卻在重啟後保留。

## 啟動與驗證

```powershell
dotnet build src/WorkJournal.Web/WorkJournal.Web.csproj
dotnet run --project tests/MarketDataChecks
dotnet run --project tests/FinMindChecks
# 有網路時：使用官方來源實測上市、上櫃及重複查詢，禁止 FinMind 備援
dotnet run --project tests/MarketDataChecks -- --live
./Start-WorkJournal.ps1
```

本機網址：`https://localhost:7180/StockAnalysis/Swing`。首次需正常 Google 登入。

設定位於 `src/WorkJournal.Web/appsettings.Development.json` 的 `MarketData` 區段。
資料放在 `src/WorkJournal.Web/App_Data/market-data`，已列入 gitignore，不保存任何 Token。
將 `MarketData:Enabled` 設為 false 並重啟，可切回原本資料來源。

首次載入需逐月／逐日建立快取，可能較慢；不要連續重整。歷史缺口採按需補齊，尚未建立排程匯入與後台任務 UI。

## 本機驗證紀錄（2026-10-23）

- `dotnet build WorkJournal.sln -c Release --no-restore` 成功。
- MarketDataChecks 與 FinMindChecks 全數通過。
- `--live` 禁止 FinMind 備援，2330／6488 的官方行情及法人查詢均成功，重複查詢新增外部請求為 0。
- 本機網站 SwingData：2330、6488 均 HTTP 200，行情日期 2026-10-22，MA60、量比及近 20 日法人資料可用。
- 首次完整查詢分別約 59／56 秒；重啟後 2330 約 1.1 秒，官方請求計數 220 → 220。時間取決於既有快取與來源回應，並非效能承諾。
- 既有 `System.Security.Cryptography.Pkcs 9.0.11` 的 NU1902／NU1903 相依套件警告仍存在；本次未升級無關套件。

## 部署前待完成與限制

1.5.3 增加檔案鎖，保護同一檔案系統上的程序及重啟交接。現有 Azure 網站為單一 worker；多實例擴充前仍需驗證共用儲存鎖語意，或改為資料庫／分散式鎖。測試 `--live` 會共用並消耗真實查詢預算。

正式快取設定為 `D:\home\data\worklog-market-data`，不在網站發佈目錄內。套件排除 `bin`、`obj`、本機快取與 `appsettings.Development.json`。正式自動排程維持啟用；背景工作會為互動查詢保留官方 80 次／FinMind 50 次預算。這是應用程式的保守限流，不增加上游實際額度。

長期資料完整度、歷史回測樣本及快取空間仍需持續觀察；多年資料仍有 FinMind 相依。FinMind 僅提高額度不會取代共用快取設計。

官方參考：
- https://openapi.twse.com.tw/
- https://www.twse.com.tw/zh/trading/foreign/t86.html
- https://www.tpex.org.tw/zh-tw/mainboard/trading/major-institutional/3itrade/day.html
- https://finmind.github.io/en/api_usage_count/
