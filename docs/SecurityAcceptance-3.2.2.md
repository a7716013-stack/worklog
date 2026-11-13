# Security acceptance 3.2.2

實際重現 3.2.1 的登出缺口：登出後重放先前發出的 Cookie 仍可讀取私人 PaperQuotes。測試紀錄 `artifacts/phase3220-auth-before.log` 包含失敗斷言。沿用 Identity SecurityStamp，將驗證間隔設為零，登出旋轉安全戳記後舊 Cookie 立即失效，不另建驗證系統。每個已登入請求會增加 Identity 資料庫驗證；保留 fail-closed 與無快取的撤銷語意。

本機 Development 修正後 127 項通過；最終 Production 設定隔離 fixture 127 項通過，含實際 Identity/Google handler、A/B 私人紀錄與 watchlist、Paper 帳戶隔離、CSRF、local ReturnUrl、no-store、過期登入及舊 Cookie 重放。Google 回應是 fixture，資料庫是每次建立並刪除的專用資料庫，沒有使用正式會員資料。

最終 API + SQL 421 項通過，包含 401/403、Feature Policies、A/B owner isolation、Paper order 與既有 recommendation/strategy binding 回歸。API 執行紀錄 `artifacts/phase3220-api-final.log`，登入紀錄 `artifacts/phase3220-auth-production-final.log`。CI 的 api-contract job 同 SHA 執行 Production 設定登入 fixture；公開網頁與匿名保護端點另在部署後實測。

正式網站雙帳號、真實 Google OAuth、真實會員登入後操作驗收：BLOCKED — 未提供可由使用者協助登入的兩個隔離測試帳號，不能以本機 fixture 取代。下一步由使用者在兩個隔離瀏覽器登入專用 A/B 帳號，依 RouteAccessMatrix 驗證互相不可讀取/操作 Paper order、個人追蹤與推薦，確認登出和 Cookie 重放拒絕；不得提供密碼、Cookie 或 Token。

正式匿名 smoke 的實際結果保存在 release3220-smoke.json；尚未執行前不得視為 PASS。沒有正式資料庫 migration、帳號建立或權限授予。
