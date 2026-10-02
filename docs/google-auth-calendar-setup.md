# Google 登入與 Google Calendar 設定

此功能沿用 ASP.NET Core MVC / .NET 10、SQL Server 與 JournalDbContext。Google 登入僅取得 `openid profile email`；登入後主動連結行事曆，才另外取得 `https://www.googleapis.com/auth/calendar.events.owned`。此 scope 可讀寫使用者擁有的行事曆事件；應用程式固定操作 Primary，不要求 Drive、Gmail、Contacts 或行事曆設定權限。

## Google Cloud 人工設定

1. 在 [Google Cloud Console](https://console.cloud.google.com/) 建立或選取專案。
2. 在 API Library 啟用 **Google Calendar API**。
3. 在 Google Auth Platform 的 Branding / Audience / Data Access（舊介面為 OAuth consent screen）設定應用名稱、支援信箱、授權網域、首頁與隱私權政策網址。一般個人帳號選 External。
4. 加入基本身分 scopes 及 `calendar.events.owned`。不要加入不相關權限。
5. Testing 狀態下，把實際測試帳號加入 Test users。正式上線前依 Google 當時要求完成發布或敏感 scope 審查。Testing 狀態的離線授權可能有較短期限，授權失效需重新連結。
6. 建立 **Web application** 類型的 OAuth Client。建議開發與正式環境使用不同 Client。
7. 加入下表的 Authorized redirect URIs；大小寫、HTTPS、port 與路徑須完全一致。

| 環境 | 登入回呼 | Calendar 回呼 |
| --- | --- | --- |
| Development（launchSettings.json HTTPS profile） | `https://localhost:7180/signin-google` | `https://localhost:7180/GoogleCalendar/OAuthCallback` |
| Production | `https://YOUR-DOMAIN/signin-google` | `https://YOUR-DOMAIN/GoogleCalendar/OAuthCallback` |
| 現有 Cloudflare 入口（若選作正式入口） | `https://worklog.ork-ournal.workers.dev/signin-google` | `https://worklog.ork-ournal.workers.dev/GoogleCalendar/OAuthCallback` |

`/signin-google` 由 Google middleware 處理，之後轉到 `/Account/GoogleCallback` 建立網站登入。Calendar 回呼也先由 middleware 攔截；直接呼叫 Controller 的同名 action 不會建立授權。

## Development

已下載 Google 的 Web application client JSON 時，可直接安全匯入（不會在命令列列出密鑰）：

```powershell
./Import-GoogleOAuth.ps1 -CredentialsPath "$env:USERPROFILE/Downloads/client_secret_YOUR-CLIENT.json" -ValidateOnly
./Import-GoogleOAuth.ps1 -CredentialsPath "$env:USERPROFILE/Downloads/client_secret_YOUR-CLIENT.json"
```

此匯入腳本限定本機 HTTPS origin，會先檢查兩個 redirect URIs，再透過 stdin 寫入 User Secrets。請保持下載的原始 JSON 在專案外；不要把檔案或 secret 貼到聊天。匯入後重啟網站並用自己的 Google 帳號登入。

在專案根目錄執行（以下 Client 值為佔位文字，不是真實憑證）：

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR-CLIENT-ID" --project src/WorkJournal.Web
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR-CLIENT-SECRET" --project src/WorkJournal.Web
dotnet user-secrets set "Authentication:Google:PublicOrigin" "https://localhost:7180" --project src/WorkJournal.Web
dotnet dev-certs https --trust
dotnet tool restore
dotnet build WorkJournal.sln
dotnet ef database update --project src/WorkJournal.Web
dotnet run --project src/WorkJournal.Web --launch-profile https
```

修改既有資料庫前先備份，確認 ConnectionStrings:JournalDatabase 指向預期的開發資料庫。開啟 `https://localhost:7180`；原本 HTTP profile 保留給公開股票工具，但安全登入 Cookie 只經 HTTPS 傳送。

UserSecretsId 已設定為 `workjournal-google-auth`。不要把 Secret 寫到 appsettings.json、原始碼、Git 或 issue。不要把真實值貼到聊天或測試紀錄。未設定憑證時，登入頁顯示尚未開放；股票分析仍可使用。

## Production / Azure / Cloudflare

在 Azure App Service Configuration 設定環境變數；不要提交帶有真實值的檔案：

| 名稱 | 用途 |
| --- | --- |
| `Authentication__Google__ClientId` | 正式 OAuth ClientId |
| `Authentication__Google__ClientSecret` | 正式 OAuth ClientSecret（可用 Key Vault reference） |
| `Authentication__Google__PublicOrigin` | 完整公開 HTTPS origin，例如 `https://worklog.ork-ournal.workers.dev`，不含子路徑 |
| `Authentication__TrustedProxyKey` | 使用既有 Worker 時，設定為同一個 Cloudflare `ORIGIN_KEY`；直接使用 Azure 網址時不設定 |
| `ConnectionStrings__JournalDatabase` | 正式 SQL 連線，由既有部署安全設定提供 |
| `AllowedHosts` | 明確列出公開網域及 Azure 後端 hostname（分號分隔）；本機預設只允許 localhost / 127.0.0.1 |

Worker 會覆寫專用驗證 header。後端以固定時間比對 shared secret 後，才使用設定好的 PublicOrigin 還原 scheme / host；不信任任意 `X-Forwarded-Host`。部署此次 Worker 變更並同步設定 TrustedProxyKey，否則從 Cloudflare 發起 OAuth 會無法通過 origin 檢查。維持 Azure 原有來源限制，不要把 shared secret 當成登入機制。

先備份 SQL、審查 Migration SQL 並用具備 DDL 權限的管理連線套用，再部署網站。現有 `deploy/Publish-Azure.ps1` 不會自動更新 schema。App 的日常資料庫帳號繼續使用最小讀寫權限。網站程式此次不會自動 Migration，也沒有修改既有正式資料庫。

ASP.NET Core Data Protection 使用固定 application name `WorkJournal`，Cookie 和 Google token 都依賴它的 key ring。Azure App Service 應保留 `%HOME%/ASP.NET/DataProtection-Keys`，確認平台持久儲存及檔案權限；不要在每次發布時刪除 keys。開發 Windows 使用框架的使用者 key store。多 instance 必須共用 key ring 與相同 application name；容器／Linux 需先另外配置持久共享 key ring 與保護金鑰的機制（例如 Key Vault），不要把 key ring 包入 Git 或發布壓縮檔。部署 slot 互換需確認 keys 共用策略。金鑰遺失會使現有 Cookie 和 token 無法解密，需重新登入／連結。

HTTPS / HSTS 用於正式環境。Cookie 為 Secure、HttpOnly、SameSite=Lax，OAuth correlation Cookie 使用框架的跨站回呼設定。Proxy、Azure 與 APM 的記錄應排除 OAuth callback 的 query、Authorization、Cookie 及 token request body；應用程式不記錄 OAuth token，Calendar HttpClient 已關閉 request logger。

## 資料庫與舊日誌

Migration：`20261001091455_AddIdentityAndGoogleCalendar`。

- 新增 Identity 七張 `AspNet*` tables；`AspNetUsers` 額外包含 DisplayName / PictureUrl。
- 新增 `GoogleCalendarConnections`：使用者唯一 FK、Google subject/email、CalendarId、加密 access/refresh token、到期時間、scope、連線旗標、建立／更新時間、rowversion。
- `WorkLogs.ApplicationUserId` 是 nullable FK，並新增 `(ApplicationUserId, WorkDate)` index。
- 保留既有 Migration、WorkLog 資料及所有 PaperTrading tables。WorkLog 使用者關係刪除行為 Restrict，避免刪除帳號時意外連帶刪除日誌。

舊日誌先保持 NULL owner，不對任何登入者顯示，且不自動交給第一個登入的人。資料庫管理員確認真正擁有者已登入建立帳號後，使用 [assign-legacy-worklogs.sql](assign-legacy-worklogs.sql) 明確指定 user ID；腳本預設只預覽，須明確開啟套用。不得提供「任何登入者領取全部舊資料」的公開端點。

產生可審查的增量 SQL：

```powershell
dotnet ef migrations script 20260922212853_AddPaperTrading AddIdentityAndGoogleCalendar --project src/WorkJournal.Web --idempotent --output artifacts/add-identity-calendar.sql
```

## 流程與安全決策

登入：帶 antiforgery 的 POST → Google（identity scopes、state、correlation、PKCE）→ middleware 交換 code / 讀取 userinfo → 必須 verified email → 以 Google subject 尋找或建立 Identity 外部登入 → 發出網站 Cookie。沒有密碼註冊功能，不以同 email 自動合併帳號；returnUrl 只接受 local URL。

Calendar：登入者 POST Connect → state 綁定 user ID / security stamp → Google offline consent → 驗證 scope、Google subject、現存登入 Cookie 與 security stamp → 加密 token 存 SQL。Google 帳號必須與網站登入相同。Calendar consent 不會更換網站身份、也不把 token 放進 Cookie、View、JavaScript 或 LocalStorage。

Token 使用 Data Protection，purpose 含 user ID；access 和 refresh token 都加密。到期前一分鐘刷新；401 重試刷新一次，持續 401 / invalid_grant / 無法解密則清除失效憑證、標記需重新連結。403、429、5xx、網路錯誤及逾時以非阻斷警告呈現，不隱藏本地日誌。進程內有限数量鎖防止重複 refresh，SQL rowversion 防止多 instance 覆蓋較新的連線狀態。

Disconnect 先移除本地憑證，再撤銷 Google grant；遠端撤銷失敗時提示使用者到 Google 帳戶手動移除。Logout 清除網站 Cookie 並更新 security stamp，使舊的 Calendar consent 流程失效。所有日誌查詢與新增都使用 server-side user ID，所有修改 action 使用全域 antiforgery。

Calendar aggregation 保留現有月曆 UI，分辨「日誌」與「Google」。固定以 Asia/Taipei 顯示，支援全天、跨日、重複事件展開與分頁；全天事件的結束日期不包含當日。只读取當月事件，新增／修改／刪除直接呼叫 Google API，不自動複製 WorkLogs。Google API DTO 不直接交給 View。

## 測試

```powershell
dotnet build WorkJournal.sln
dotnet run --project tests/AuthCalendarChecks -c Release
dotnet run --project tests/FinMindChecks -c Release
dotnet run --project tests/PaperTradingChecks -c Release
```

AuthCalendarChecks 使用 GUID 命名的獨立 SQL Express 資料庫，先建立舊 schema 與資料、套用新 Migration，再用真正 MVC / Identity / Google middleware 跑模擬 Google HTTP 回應。涵蓋 Login challenge/callback、基本 scope、PKCE、Cookie、Logout、未登入拒絕、跨帳號 GET/POST、owner overposting、Calendar state、授權 scope、缺失 refresh token、Token 加密與更新、CRUD、分頁、全天／時區、401/403/429/5xx/timeout、Disconnect。結束只刪除該次建立的測試資料庫。

瀏覽器與既有日誌回歸測試可使用只編譯在測試專案的已登入 loopback bridge：

```powershell
dotnet run --project tests/AuthCalendarChecks -c Release -- --serve
# 另一個終端機：
python tests/smoke_test.py http://localhost:5188
python tests/browser_test.py http://localhost:5188
python tests/calendar_test.py http://localhost:5188
python tests/auth_calendar_browser_test.py http://localhost:5188
```

Bridge 使用 synthetic Google 帳號與獨立資料庫，不是正式網站的登入捷徑；僅綁定 localhost。測試完成停止 fixture，讓 finally 清理資料庫。原有公開站無登入的日誌測試不能直接套用到新正式網站，應改用 fixture 或人工真實登入。

**上述模擬測試不能證明真實 Google OAuth 已成功。** 人工驗收需完成 Cloud Console 設定、真實登入、Calendar consent、新增／修改／刪除測試事件、斷線與重新授權，並確認資料庫只保存密文。此次沒有使用真實 Google ClientId / ClientSecret，也沒有真實帳號登入測試。

## 下一階段

本版不包含 WorkLog ↔ Google 背景雙向同步、webhook、衝突解決、多行事曆／共享行事曆選擇、跨 Google 帳號連結、帳號合併或管理後台。股票分析與 Paper Trading 業務邏輯保留原狀；Paper Trading 尚未改為每使用者獨立帳戶。

官方參考：[Microsoft Google 登入](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-10.0)、[Google Web Server OAuth](https://developers.google.com/identity/protocols/oauth2/web-server)、[Calendar scopes](https://developers.google.com/workspace/calendar/api/auth)、[Data Protection 預設設定](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0)。
