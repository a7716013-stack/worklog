# 工作日誌 WorkJournal

繁體中文個人工作日誌網站，採 ASP.NET Core MVC / .NET 10、EF Core 10、SQL Server Express 和 Bootstrap 5.3.8。

## 開啟與執行

1. 用 Visual Studio 2026 開啟 `WorkJournal.sln`。
2. 將 `WorkJournal.Web` 設為啟始專案，選擇 `http` 啟動設定，按 Ctrl+F5。
3. 瀏覽 http://localhost:5180 。

也可在專案根目錄開啟 PowerShell：

```powershell
dotnet tool restore
dotnet run --project src/WorkJournal.Web --launch-profile http
```

或執行 `Start-WorkJournal.ps1`。若 5180 連接埠正由已啟動的這個網站使用，直接開啟網址即可；要從 Visual Studio 偵錯前，先停止原先的開發伺服器。

## 已有功能

- 新增、查看、編輯及刪除工作日誌；刪除前有確認頁。
- 日期、工作項目、專案／客戶、工時、進度、工作內容及後續待辦。
- 關鍵字、日期區間、進度篩選；每頁 10 筆。
- 篩選結果的紀錄數、總工時、完成數與進行中數。
- 繁體中文表單驗證；工時 0–24 小時，介面以 0.25 小時為單位。
- CSRF 防偽保護、Razor HTML 編碼、伺服器端欄位白名單。
- Bootstrap 響應式頁面；所有前端資源均存於專案內。
- 獨立 SQL Server 資料庫及 EF Core Migration，保留建立與更新時間。

## 專案結構

```text
WorkJournal.sln
src/WorkJournal.Web/
  Controllers/WorkLogsController.cs  # 網頁路由、表單流程、篩選與統計
  Models/WorkLog.cs                  # 日誌資料與驗證
  Models/WorkStatus.cs               # 日誌進度
  Models/WorkDateRangeAttribute.cs   # 前後端一致的日期驗證
  Data/JournalDbContext.cs           # EF Core 資料庫映射
  Data/Migrations/                   # 資料庫版本管理
  ViewModels/WorkLogIndexViewModel.cs # 列表、查詢與分頁呈現
  Views/WorkLogs/                    # 列表、編輯、詳細與刪除頁
  Views/Shared/_Layout.cshtml        # 共用導覽與版面
  wwwroot/css/site.css               # 網站樣式
  wwwroot/js/worklog-validation.js   # 日期欄位驗證
  appsettings.json                  # 連線及記錄設定
tests/smoke_test.py                  # HTTP 功能測試
tests/browser_test.py                # Edge 瀏覽器測試
```

這是簡潔的 MVC 起始架構：Controller 透過 DI 取得 DbContext，將查詢結果傳給 ViewModel / Razor View。
若增加複雜業務規則，可再抽出 Services；目前避免只有轉呼叫的多餘 Repository。

## 資料庫

已在本機 `.\\SQLEXPRESS` 建立 `WorkJournal` 資料庫並套用 InitialCreate。
SSMS 使用 Windows 驗證連線。
設定在 `src/WorkJournal.Web/appsettings.json` 的 `ConnectionStrings:JournalDatabase`。
資料庫不會在每次啟動時自動修改或塞入示範資料。

另一台電腦第一次使用：

```powershell
dotnet tool restore
dotnet ef database update --project src/WorkJournal.Web
```

修改資料模型後：

```powershell
dotnet ef migrations add DescribeChange --project src/WorkJournal.Web --output-dir Data/Migrations
dotnet ef database update --project src/WorkJournal.Web
```

正式資料庫更新前請先備份並檢查 Migration。

## 驗證

啟動網站後，在根目錄執行：

```powershell
python tests/smoke_test.py
```

測試建立唯一標記的日誌，檢查 CRUD、日期及狀態驗證、CSRF、HTML 轉義、篩選、總工時及分頁，最後刪除測試資料。不要對正式網站執行測試。
瀏覽器測試需要 Playwright Python 套件及本機 Edge，測試指令為 `python tests/browser_test.py`，截圖輸出到 `artifacts/`。

## 目前範圍與後續擴充

目前是單人、本機開發版本，尚未加入登入、使用者資料隔離、角色權限、附件與審核流程。
若多人同時編輯同一筆日誌，最後儲存的內容會覆蓋先前內容；多人版建議加入 RowVersion 並處理並行衝突。
已建立 Azure 測試環境，設定與再次發佈方式見 [Azure 部署說明](deploy/README.md)。Azure 直接入口保留來源限制；Cloudflare 公開入口已開放外部網路。目前尚未加入登入功能，任何知道公開網址的人都能讀寫日誌。

IIS 發佈的基礎步驟：

```powershell
dotnet publish src/WorkJournal.Web -c Release -o artifacts/publish
```

部署端需啟用 IIS 與 .NET 10 Hosting Bundle；應用程式集區設定為「沒有受控碼」。
目前 SQL 連線使用 Windows 驗證；IIS 身分與登入 Windows 的開發者不同，部署時需另外授予專用身分適當的資料庫權限。
`AllowedHosts` 現在只允許 localhost 與 127.0.0.1，部署時應設定實際網域，並以環境變數覆寫連線字串。
`TrustServerCertificate=True` 為本機開發設定；正式環境應使用可信任 SQL Server 憑證。
HTTPS 模式可在信任開發憑證後使用；Production 會將 HTTP 重新導向 HTTPS。
Cloudflare 已部署，詳見下方公開網址。

## 參考文件

- [ASP.NET Core MVC 與 EF Core](https://learn.microsoft.com/en-us/aspnet/core/data/ef-mvc/intro?view=aspnetcore-10.0)
- [ASP.NET Core 表單防偽](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)

## Cloudflare 公開網址

https://worklog.ork-ournal.workers.dev

部署及存取設定見 [Cloudflare 說明](deploy/cloudflare/README.md)。

## FinMind 台股行情

股票分析頁輸入台股代號（例如 2330、0050、00679B），透過伺服器端 FinMindStockService 取得 TaiwanStockInfo 與 TaiwanStockPrice。
價格為最近 365 日內最新可用交易日的日行情，不是盤中即時價格。畫面顯示交易日期，成交量單位為股，價格單位為新台幣元。
漲跌幅 = spread / (close - spread) × 100；無效或缺少的數值顯示「—」。
行情快取 5 分鐘、股票基本資料快取 24 小時，失敗結果快取 1 分鐘。已接入估值、月營收與技術指標；完整財務報表尚未接入。

目前已驗證免 Token 查詢可用。若需使用帳戶額度，可在伺服器環境設定 `FinMind__Token`，應用程式以 Authorization Bearer 標頭送出。不要將 Token 寫入 Git 或前端。
Azure 部署時可在 App Service 應用程式設定加入同名環境變數。

服務測試：`dotnet run --project tests/FinMindChecks`
官方文件：https://finmind.github.io/tutor/TaiwanMarket/Technical/

### 基本面與技術指標

- 基本面使用 TaiwanStockPER（近 90 日最新本益比、淨值比與殖利率）及 TaiwanStockMonthRevenue（近 16 個月）。估值日期與營收歸屬月份分別顯示。
- 月營收以元讀取，畫面換算為億元；年增／月增分別比較去年同月／上月，缺少比較期或基期為零時顯示 —。
- 技術指標使用近 365 日未還原日收盤價：MA5/20/60、Wilder RSI14、MACD(12,26,9)。EMA 以首段平均初始化；柱狀值為 DIF 減訊號線，未乘 2。
- RSI 全持平定為 50；單邊上漲為 100、單邊下跌為 0。不足資料不計算；無效收盤值之後重新累積樣本。除权息與分割會影響未還原價格。
- 基本面 API 失敗不影響已取得的行情與技術指標，ETF 缺少公司估值或營收時顯示不適用提示。
- 驗證：`dotnet run --project tests/FinMindChecks`；另已使用台積電、鴻海、ETF 實測搜尋切換及手機版面。

### ETF 分析

股票分析依 FinMind 商品分類是否包含 ETF 切換分析區塊，不以代號前綴判斷。個股保留本益比、股價淨值比、殖利率與營收；ETF 顯示淨值、折溢價、基金規模、配息／配股及持股。兩者保留技術指標。

- 淨值與折溢價：證交所 ETF e添富最近 30 日資料，折溢價只使用與淨值同日期的官方數值。
- 基金規模：富邦 ETF 採官方基金資產頁並附資料日期；其他發行商採證交所資產規模，來源未提供日期時明確標示。
- 持股：目前串接富邦官方基金資產頁，依權重排序，僅列股票、不混入期貨或合計。其他發行商與非股票持倉尚未串接，畫面明確提示。
- 配息／配股：FinMind TaiwanStockDividend，近三年最多六筆有除息／除權日期紀錄，同日修訂取最新公告。配股顯示來源金額，不推算股數；基金分割不視為配股。
- 缺少或暫時無法取得的資料顯示 —，不補零；資料來源部分失效仍保留其他可用資料。搜尋結果快取五分鐘。

驗證：`dotnet run --project tests/FinMindChecks`；啟動本機網站後，以已安裝 Playwright 的 Python 執行 `tests/etf_test.py`，可驗證 006208、0050、2330 的區塊切換及手機版排版。

發布慣例：使用者說「上傳」時，同時提交至 GitHub 並部署至線上網站；只有明確指定單一目的地時才僅更新該處。

### 波段雷達（1.1.5）

入口 /StockAnalysis/Swing。輸入上市櫃代號或名稱，再按加入追蹤；搜尋不會自動加入。最多追蹤 20 檔，清單存於目前瀏覽器 localStorage（workjournal.swing.watchlist.v1），不共用到其他瀏覽器或網域；沒有預設追蹤股票，也不需要資料庫 Migration。

評分為趨勢 35、動能 20、突破 15、成交量 10、外資／投信 20，共 100 分。風險為跌破 MA20 扣 15、黑 K 實體至少 3% 且量比大於 2 扣 15；結果限制為 0～100。正向條件詳見畫面的評分拆解。完整來源缺漏時不提供總分、不重新正規化，只列已知分數與可評權重。≥75 且無風險標為優先研究；50～74 為持續觀察。排序僅針對使用者追蹤清單，並非全市場每日前 20 名掃描。

資料為 FinMind TaiwanStockPrice、TaiwanStockInstitutionalInvestorsBuySell。外資採 Foreign_Investor、投信採 Investment_Trust，各為 buy−sell 股數，畫面除以 1000 顯示張數，不把 Foreign_Dealer_Self 重複加到外資。5／20 日窗口依股票行情交易日對齊，缺一日即留空；連買最多看 20 日。突破與均量比較皆排除當日。日行情快取 5 分鐘，回測快取 30 分鐘；目前非即時報價。MA、RSI 與 MACD 沿用未還原價格計算。

回測讀取近五年外加 150 日暖機資料，只用當日以前的訊號。完整分數 ≥75 後下一交易日開盤買入；收盤跌破 MA20 或持有滿 20 交易日，次日開盤賣出。一次一筆多單、全額投入、允許零股數量；假設雙邊手續費各 0.1425%、賣出稅費個股 0.3%／ETF 0.1%，無滑價。報酬未含股利，不是總報酬回測，未維護法人歷史公告時間。遇無效開收盤或單日價格跳動超過 25% 時不提供績效；此保護不代表已完整處理所有企業行動。期間末部位以收盤估值，勝率與平均報酬僅統計已平倉，最大回撤使用每日資產曲線。

測試：dotnet run --project tests/FinMindChecks；啟動本機網站後，使用安裝 Playwright 的 Python 執行 tests/swing_browser_test.py。瀏覽器測試用獨立情境儲存追蹤資料，不影響使用者的追蹤清單。


### 虛擬交易 / Paper Trading（1.1.6）

入口 `/StockAnalysis/PaperTrading`。在個股分析查到股票後按「虛擬買進」，或在波段詳細分析按「建立虛擬單」，會帶入股票代號。股票分析內提供個股、波段、虛擬交易三個頁籤；上方工作日誌／股票分析仍為兩個直接切換按鈕。

這是模擬交易，不會送出真實證券委託。沒有券商交易 API，也沒有登入與多使用者隔離；所有訪客共用一個 SQL Server 帳戶。初始資金由 `PaperTrading:InitialCash` 指定，預設 1,000,000 元，只在第一次建立帳戶時讀取。

- 市價：由伺服器取得 FinMind 最新可用已完成日行情，以 Close 立即模擬成交。台灣時間 14:00 以前排除當日資料，行情日明列於持倉／成交表格，不是盤中即時報價。
- 限價：建立時記錄 SubmittedTradeDate；EligibleFromTradeDate 為 max(行情日, 台灣建立日) 的下一日。只有後續已完成交易日的 Low ≤ 買進限價、High ≥ 賣出限價才成交，成交價固定使用委託限價。未觸價維持 Pending。
- 限價需要按「檢查待成交委託」才會評估最新一根可用日 K；不回補離線期间所有日 K，不依賴背景服務。LastEvaluatedTradeDate 防止重複評估同根已完成日 K。這只是 OHLC 模擬，無法知道盤中成交順序。
- 等待單不保留現金或股票；建立及實際成交前都檢查資金／股數。不足會記錄 Rejected 與原因，不支援放空。數量以整數股計，支援零股；單筆上限 100,000,000 股，限價 0.0001～1,000,000 元。
- 買進成本包含手續費。新平均成本＝(原剩餘成本＋成交金額＋買進手續費)÷新股數。部分賣出平均成本不變；完全賣出保留零股歷史部位，成本歸零。
- 已實現損益＝賣出成交金額－賣出手續費－模擬交易稅－賣出股數的原成本。未實現損益＝目前市值－剩餘成本；未扣未來賣出費用。總資產＝現金＋市值，總損益＝總資產－初始資金，報酬率＝損益÷對應成本×100%。
- `PaperTrading` Options 集中設定 CommissionRate=0.001425、CommissionDiscount=1、MinimumCommission=20、StockSellTaxRate=0.003、EtfSellTaxRate=0.001。手續費＝max(最低費用, 成交金額×費率×折扣)；買賣都收，稅費僅賣出。ETF 延用來源商品分類。這些只是模擬參數，不保證對應最新法規或個別商品實際稅制。
- 金額採 decimal；成交金額、手續費與稅費各四捨五入至兩位（AwayFromZero）。SQL 金額 precision 為 decimal(28,10)，平均成本保留十位，額外 CostBasis 保留成本總額，最後一筆全賣時沖銷剩餘成本，避免尾差。
- 行情失敗不影響現金、持股、成本與歷史紀錄。無報價欄位顯示「—」；部分估值缺漏時總資產、總損益及報酬率也顯示「—」並提示不完整，不能將缺價視為零元股價。
- 在頁面底部展開「重設虛擬帳戶」，勾選確認後再次確認對話框，才以帶防偽 token 的 POST 清除虛擬委託、成交、持倉並恢復原初始資金；不影響 WorkLogs。重設會輪替帳戶 Generation，舊表單不能重送。

資料庫沿用 JournalDbContext / JournalDatabase，新增 PaperTradingAccounts、PaperOrders、PaperTrades、PaperPositions。啟動新版前執行：

```powershell
dotnet ef database update --project src/WorkJournal.Web
```

Migration：`20260922212853_AddPaperTrading`。只增加虛擬交易表、FK、索引與檢查條件；不刪除／重建既有 Migration 或 WorkLogs。正式環境套用前依既有流程備份。此次本機開發不會自動部署 Azure 或更新雲端資料庫。

每次成交都以 EF Core transaction 同時更新 Order、Trade、Position、Cash；SQL transaction-owned sp_getapplock 跨程序序列化帳戶修改，搭配 ClientRequestId 唯一索引與成交序號唯一索引。行情呼叫在取得鎖之前完成；資料庫重試前清除追蹤並重新讀取資料。瀏覽器不能指定成交价、狀態、費用或 AccountId。

本版限制：單一共用帳戶、無部分成交、不模擬滑價與漲跌停／成交量、未自動處理股利／分割／減資、無離線逐日回放。模擬結果不代表真實成交或未來績效。

測試：

```powershell
dotnet run --project tests/PaperTradingChecks -c Release
dotnet run --project tests/FinMindChecks -c Release
# 另開終端執行固定行情＋獨立 SQL 的測試網站（localhost:5187）：
dotnet run --project tests/PaperTradingChecks -c Release -- --serve
# 已安裝 Playwright 的 Python：
python tests/paper_trading_browser_test.py http://localhost:5187
python tests/smoke_test.py http://localhost:5187
python tests/swing_browser_test.py http://localhost:5187
```

PaperTradingChecks 建立本次專用 `WorkJournalPaperChecks_<GUID>` 資料庫，正常結束會移除它，不使用本機 WorkJournal 資料；需要本機 SQL Express 與建立測試 DB 權限。使用 Ctrl+C 正常結束測試主機；強制終止可能留下帶 GUID 的測試 DB，勿將其當作正式資料庫刪除。瀏覽器虛擬交易測試要求隔離主機標頭，拒絕在一般網站重設帳戶。

詳見 [1.1.6 實作與驗收紀錄](docs/releases/1.1.6.md)。
