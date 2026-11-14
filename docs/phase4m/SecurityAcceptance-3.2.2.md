# Phase 4M 安全驗收
Fixture：重用 AuthCalendarChecks --production、ApiContractChecks --sql，涵蓋 Identity/Google handler、A/B ownership、Paper order IDOR、策略/推薦綁定、logout cookie revocation、session expiry、CSRF、ReturnUrl 與公開市場相容性。本次結果見 engineering，執行前不視為通過。
Production Smoke：deploy/Verify-Phase4M.ps1 僅匿名 GET，驗證 MVC redirect、API 401、公開頁面/health/readiness。不得以這項結果宣稱正式登入測試成功。
Production authenticated：BLOCKED_NO_ISOLATED_ACCOUNTS。由使用者在獨立瀏覽器 A/B 登入專用帳號，逐項確認本人的 Paper order、推薦、strategy、watchlist 可讀，另一帳號資源為 404/拒絕；錯誤 CSRF 的寫入拒絕；登出/過期後受保護路由拒絕。勿傳送 Cookie/Token 或代建正式帳號。真實 OAuth 登入需人工執行。
管理工作台沿用 Administrator；一般會員 403、匿名 401。審查寫入僅 controlled CLI，使用受 OS ACL 保護的 roster 對照 Environment.UserName，禁止自審、空理由、舊 head、非既有 scoped receipt。網站不授予角色、不寫正式 DB。
