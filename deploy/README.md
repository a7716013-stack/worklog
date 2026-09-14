# Azure 工作日誌測試環境

測試網址：https://worklog-a7716013-test.azurewebsites.net

## 資源

| 項目 | 設定 |
| --- | --- |
| 資源群組 | rg-worklog-test |
| 地區 | East Asia |
| 網站 | worklog-a7716013-test |
| App Service 方案 | plan-worklog-test-windows-free / Windows F1（Free） |
| 執行環境 | .NET 10 / Production |
| 資料庫伺服器 | sql-worklog-a7716013-test.database.windows.net |
| 資料庫 | WorkJournal / Azure SQL Serverless 免費額度 |
| 超過資料庫免費額度 | AutoPause，不自動選擇超額計費 |
| 網站時區 | Taipei Standard Time |

可用額度與服務限制以 Azure 訂用帳戶的即時狀態為準；F1 有 CPU 與資源限制，適合測試。
資料庫可休眠，初次開啟可能較慢；應用程式已啟用 SQL 暫時性錯誤重試。

## 存取

目前網站尚未加入使用者登入，因此以 App Service 的 TestNetwork IP 規則限制測試來源。
Cloudflare 公開入口已開放外部網路，詳見 [Cloudflare 說明](cloudflare/README.md)。Azure 另允許帶有專用驗證值的 Cloudflare 來源；SCM 使用獨立的管理者 IP 限制。
更換網路後，請在 Azure 入口網站的網站「網路 / 存取限制」更新 TestNetwork 規則。
新增測試者時，新增其明確的來源 IP 規則；不要把目前的單人日誌當成已有帳號隔離的系統。

SQL 防火牆只保留網站出口 IP；Migration 使用的本機臨時規則會在初始化後移除。
網站使用 worklogapp 資料庫使用者，只有資料讀寫角色；結構變更需另外使用管理員。
密碼存在 App Service 連線字串設定，未加入 Git。
本機初始化憑證備份以 Windows DPAPI 加密，位於使用者的 .azure/worklog-test-credentials.clixml，僅限原 Windows 帳戶解密。

## 再次發佈

先使用具有該訂用帳戶權限的 Azure 帳戶登入，再於專案根目錄執行：

```powershell
az login
./deploy/Publish-Azure.ps1
```

腳本會建立 Release 套件並更新現有測試網站，不會自動改動資料庫結構。
若新增了 EF Core Migration，應先備份測試資料，透過具備結構變更權限的連線執行 Migration，再發佈程式。
避免將資料庫密碼寫入程式碼、Git 或命令列文字。

## 檢查

```powershell
python tests/smoke_test.py https://worklog-a7716013-test.azurewebsites.net
python tests/browser_test.py https://worklog-a7716013-test.azurewebsites.net
```

瀏覽器測試需要 Playwright 與 Edge。測試會新增並清除測試日誌；只對測試環境執行。
本機 SQL Server 資料未複製到 Azure；Azure 使用獨立資料庫。

## 參考

- https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer
- https://learn.microsoft.com/en-us/azure/app-service/app-service-ip-restrictions
