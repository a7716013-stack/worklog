# Cloudflare 公開工作日誌

網址：https://worklog.ork-ournal.workers.dev

Cloudflare Worker 將請求轉送至 Azure ASP.NET Core MVC 網站，資料仍儲存在 Azure SQL。
網站已開放外部網路，沒有登入功能，任何知道網址的人都能查看及修改日誌。
Worker 停用快取，保留反偽造 Cookie，改寫後端重新導向與 Cookie 網域。

Azure 僅允許原有測試網路，或帶有專用驗證值的 Cloudflare IP 流量。
ORIGIN_KEY 儲存在 Cloudflare Secret，Azure 使用 x-azure-fdid 規則比對，不得提交至 Git。
SCM 使用獨立管理者 IP 限制。輪替密鑰時需同步更新 Azure CloudflareWorker-* 規則。
Cloudflare 官方 IP 範圍變更時需同步更新 Azure 規則。

## 再次部署

```powershell
npm exec --yes --package=wrangler -- wrangler login
npm exec --yes --package=wrangler -- wrangler deploy --config deploy/cloudflare/wrangler.jsonc
```

後端更新使用 deploy/Publish-Azure.ps1；部署 Worker 不會更新 ASP.NET 程式。

## 驗證

```powershell
python tests/smoke_test.py https://worklog.ork-ournal.workers.dev
python tests/browser_test.py https://worklog.ork-ournal.workers.dev
```

採用 Cloudflare 免費額度與 Azure 原有免費方案，未啟用付費升級。
免費方案有限額，資料庫休眠後首次開啟可能較慢。
參考：https://developers.cloudflare.com/workers/platform/limits/
