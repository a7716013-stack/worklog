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
本版未部署至 IIS 或 Cloudflare。公開部署前先完成身份驗證與資料權限。

IIS 發佈的基礎步驟：

```powershell
dotnet publish src/WorkJournal.Web -c Release -o artifacts/publish
```

部署端需啟用 IIS 與 .NET 10 Hosting Bundle；應用程式集區設定為「沒有受控碼」。
目前 SQL 連線使用 Windows 驗證；IIS 身分與登入 Windows 的開發者不同，部署時需另外授予專用身分適當的資料庫權限。
`AllowedHosts` 現在只允許 localhost 與 127.0.0.1，部署時應設定實際網域，並以環境變數覆寫連線字串。
`TrustServerCertificate=True` 為本機開發設定；正式環境應使用可信任 SQL Server 憑證。
HTTPS 模式可在信任開發憑證後使用；Production 會將 HTTP 重新導向 HTTPS。
Cloudflare 登入、DNS 與 Tunnel 設定屬於後续部署步驟。

## 參考文件

- [ASP.NET Core MVC 與 EF Core](https://learn.microsoft.com/en-us/aspnet/core/data/ef-mvc/intro?view=aspnetcore-10.0)
- [ASP.NET Core 表單防偽](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)

