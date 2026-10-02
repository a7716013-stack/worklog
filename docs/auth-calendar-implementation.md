# Google 登入與行事曆實作紀錄

此文件記錄 Google 登入與行事曆實作，納入 1.1.7。已完成本機憑證匯入及本機 Identity Migration；正式發布步驟與驗證見 [1.1.7 發布紀錄](releases/1.1.7.md)。

## 完成內容

- ASP.NET Core Identity 帳號、Google external login、已驗證 email、頭像／名稱／email／登入／登出導覽。
- 登入只要求基本身分。Calendar consent 分開觸發，採 owned-events 最小合理 scope、offline、PKCE、state/correlation，並驗證同帳號與原登入 session。
- WorkLogsController 全部存取以 server-side user ID 限定；Create 不接受 browser 指派 owner；保留股票分析與 PaperTrading 業務邏輯。
- GoogleCalendarService 提供查詢單筆／當月、建立、修改、刪除、刷新與撤銷授權；Controller 只操作當前帳號。
- access/refresh token 都使用含使用者 purpose 的 Data Protection 加密後存 SQL；不放入 View、JS、LocalStorage 或登入 Cookie，遠端錯誤只記錄一般失敗事件。
- 保留既有月曆，合併本地與 Google 事件；台北時間、全天與跨日、分頁、重複事件展開；Google API 失敗時保留本地日誌。
- TrustedOriginMiddleware 及既有 Worker 的受驗證代理 header 支援公開 origin；私人頁面禁止快取。

## 新增檔案

路徑以 repository 根目錄為基準：

| 位置 | 檔案 |
| --- | --- |
| `src/WorkJournal.Web/Models/` | `ApplicationUser.cs`、`GoogleCalendarConnection.cs` |
| `src/WorkJournal.Web/Security/` | `GoogleAuthSettings.cs`、`ApplicationClaimsFactory.cs`、`AuthenticationRegistration.cs`、`TrustedOriginMiddleware.cs` |
| `src/WorkJournal.Web/Controllers/` | `AccountController.cs`、`GoogleCalendarController.cs` |
| `src/WorkJournal.Web/Services/` | `IGoogleCalendarService.cs`、`GoogleCalendarService.cs`、`CalendarAggregationService.cs` |
| `src/WorkJournal.Web/ViewModels/` | `CalendarEventViewModel.cs`（包含輸入／連線／聚合 ViewModels） |
| `src/WorkJournal.Web/Views/` | `Account/Login.cshtml`、`Shared/_LoginPartial.cshtml`、`GoogleCalendar/Index.cshtml`、`GoogleCalendar/Edit.cshtml`、`GoogleCalendar/Delete.cshtml` |
| `src/WorkJournal.Web/Data/Migrations/` | `20261001091455_AddIdentityAndGoogleCalendar.cs` 及 `.Designer.cs` |
| `tests/AuthCalendarChecks/` | `AuthCalendarChecks.csproj`、`Checks.cs`、`FakeGoogle.cs`、`FixtureFactory.cs`、`FixtureBridge.cs` |
| `tests/` | `auth_calendar_browser_test.py` |
| `docs/` | `google-auth-calendar-setup.md`、`assign-legacy-worklogs.sql`、本紀錄 |

## 修改檔案

- `src/WorkJournal.Web/Program.cs`、`WorkJournal.Web.csproj`
- `Data/JournalDbContext.cs`、`Data/Migrations/JournalDbContextModelSnapshot.cs`
- `Models/WorkLog.cs`、`Controllers/WorkLogsController.cs`、`ViewModels/WorkLogIndexViewModel.cs`
- `Views/Shared/_Layout.cshtml`、`Views/WorkLogs/_Calendar.cshtml`、`wwwroot/css/site.css`
- `tests/FinMindChecks/FinMindChecks.csproj`：把股票測試編譯範圍限定為原有股票服務，避免新 Calendar 服務被 wildcard 意外納入；未改股票邏輯。
- `tests/browser_test.py`、`tests/calendar_test.py`：新增 Logout form 後精確選取日誌 form，並辨識事件來源文字。
- `deploy/cloudflare/worker.mjs`：覆寫受驗證的代理 header。
- `README.md`、`CHANGELOG.md`、`deploy/README.md`、`deploy/cloudflare/README.md`
- `Start-WorkJournal.ps1`：改用 HTTPS profile 以支援 Secure 登入 Cookie。

## NuGet

| 專案 | 新增套件 | 版本 |
| --- | --- | --- |
| Web | Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.12 |
| Web | Microsoft.AspNetCore.Authentication.Google | 10.0.12 |
| AuthCalendarChecks（僅測試） | Microsoft.AspNetCore.Mvc.Testing | 10.0.12 |

Google REST API 使用 HttpClient，不增加 Google SDK 依賴。既有 EF Core Design / SqlServer 10.0.12 保持不變。

## Migration 與資料保留

`AddIdentityAndGoogleCalendar` 新增 Identity 七張表、GoogleCalendarConnections 及 nullable WorkLog.ApplicationUserId / FK / index。所有原 Migration 保留。未指派舊日誌不會對任何帳號公開；用管理員審查後的 SQL 指派，詳見 [設定文件](google-auth-calendar-setup.md#資料庫與舊日誌)。SQL 預設只預覽，沒有 first-login claim。

已生成 `artifacts/add-identity-calendar.sql` 供審查；不自動套用到正式或原本開發資料庫。

## 驗證結果

- Release build：0 errors / 0 warnings。
- AuthCalendarChecks：80 項通過。使用實際 MVC / Identity / OAuth middleware、模擬 Google HTTP、GUID 命名的獨立 SQL 資料庫；涵蓋安全、舊 schema 升級、資料隔離、CRUD、refresh、錯誤回退及代理 origin。
- PaperTradingChecks：43 項 SQL 測試通過；交易資金、委託、成交、持倉及重設行為保持原狀。
- FinMindChecks：行情、ETF、技術指標、波段／回測檢查通過。
- `smoke_test.py`、`browser_test.py`、`calendar_test.py`：CRUD、驗證、防偽、分頁、月曆及桌面／390px 手機通過。
- `auth_calendar_browser_test.py`：模擬登入、連結、事件 CRUD、斷線、登出與手機版面通過。
- `paper_trading_browser_test.py`、`swing_browser_test.py`、`etf_test.py`：既有頁面回歸通過。
- 截圖位於忽略的 artifacts 目錄，例如 `auth-login-desktop.png`、`auth-calendar-connected.png`、`auth-event-mobile.png`。

**沒有使用真實 Google ClientId / ClientSecret，不能宣稱真實 Google OAuth 已成功。** 模擬 Google endpoint 及已登入 bridge 只存在於測試專案，不進入網站發布內容。

## 必須人工完成與下一階段

Google Cloud 建立 Project、啟用 Calendar API、設定 consent / test users / Web OAuth Client / redirect URIs；Development 用 user-secrets，Azure 用環境設定；維持 Data Protection key ring。備份／套用 Migration，確認舊日誌擁有者，再部署並真實登入驗收。

開發啟動：`dotnet run --project src/WorkJournal.Web --launch-profile https`，網址 `https://localhost:7180`。完整命令、callback、Production／Cloudflare 注意事項、兩種 OAuth 流程、Token 管理與安全決策見 [Google 設定文件](google-auth-calendar-setup.md)。

下一階段保留背景雙向同步、WorkLog 自動匯出、webhook／衝突解決、多行事曆／共享行事曆、跨 Google 帳號連結與帳號管理 UI。Paper Trading 仍是原本的共用模擬帳戶，不在本次使用者隔離範圍。
