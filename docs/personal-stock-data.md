# 個人股票資料（1.2.2）

「波段追蹤」與「虛擬交易」需要登入。追蹤清單存於 `StockWatchlistItems`，虛擬帳戶存於 `PaperTradingAccounts`，兩者以 `ApplicationUserId` 關聯登入者。委託、成交與持倉透過帳戶關聯限定擁有者；伺服器不接受瀏覽器指定擁有者。

每個帳號最多追蹤 20 檔，加入、移除與虛擬交易使用每帳號 SQL 交易鎖避免併發重複或超限。所有寫入需通過防偽權杖驗證。個人頁面與 API 不允許快取。

## 升級

先備份資料庫，再套用 `20261004075535_AddPersonalStockData`，最後部署 1.2.2。此 Migration 保留舊帳戶（Id = 1）及其所有交易資料，擁有者保持空值，因此登入帳號無法讀取或修改舊共用資料。新帳號首次使用虛擬交易時建立獨立帳戶，預設資金 100 萬元。

瀏覽器若仍有 `workjournal.swing.watchlist.v1`，追蹤頁提供手動匯入按鈕。確認後匯入目前登入帳號，股票名稱由伺服器目錄重新確認；成功後才移除舊瀏覽器清單。不得自動將無擁有者的資料指派給第一個登入者。

已有個人股票資料時，Migration 的 Down 會拒絕回復舊結構，避免刪除個人資料。應先備份並制定資料移轉方案。

## 驗證

`tests/PaperTradingChecks` 使用獨立 SQL 資料庫驗證交易與追蹤的併發、持久化及跨帳號隔離；`tests/AuthCalendarChecks` 驗證 HTTP 授權、防偽與帳號隔離，並保留既有登入及行事曆回歸測試。

瀏覽器測試 `tests/swing_browser_test.py`、`tests/paper_trading_browser_test.py` 僅對 `PaperTradingChecks --serve` 啟動的隔離資料庫及固定行情執行，不能用正式網站替代。
