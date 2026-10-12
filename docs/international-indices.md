# 首頁國際指數

首頁桌面版右側顯示七個指數；1100px以下移到主要內容上方，窄手機版以緊湊卡片排列。此功能以1.4.2同版更新發布至GitHub main與正式網站；原v1.4.2標籤保留首次發布提交，最新內容以main為準。

| 指數 | Yahoo代號 | 來源 |
|---|---|---|
| 道瓊工業指數 | ^DJI | [Yahoo Finance](https://finance.yahoo.com/quote/%5EDJI/) |
| S&P 500 | ^GSPC | [Yahoo Finance](https://finance.yahoo.com/quote/%5EGSPC/) |
| 費城半導體指數 | ^SOX | [Yahoo Finance](https://finance.yahoo.com/quote/%5ESOX/) |
| 那斯達克綜合指數 | ^IXIC | [Yahoo Finance](https://finance.yahoo.com/quote/%5EIXIC/) |
| 羅素2000 | ^RUT | [Yahoo Finance](https://finance.yahoo.com/quote/%5ERUT/) |
| 日經225 | ^N225 | [Yahoo Finance](https://finance.yahoo.com/quote/%5EN225/) |
| 香港恆生 | ^HSI | [Yahoo Finance](https://finance.yahoo.com/quote/%5EHSI/) |

後端限定上述七項，讀取Yahoo公開chart日區間資料；使用來源指數報價、報價時間及前收盤，漲跌點數＝目前值−前收盤，漲跌幅＝點數／前收盤×100%。所有數值為指數點位，非可下單商品或期貨、ETF。時間以台北時間顯示，行情可能延遲；休市保留來源最後報價時間。

前端獨立載入，不阻擋首頁工作、行程與台股摘要。伺服器共用5分鐘快取與併發鎖；每項來源失敗不影響其他項，最多保存7天的上次成功資料並明示更新失敗。沒有有效快取時顯示「—」，不填入假報價；429或故障也遵守5分鐘冷卻。瀏覽器可手動更新，分頁可見時每5分鐘自動查詢。Yahoo公開介面可能變更或限流，頁面提供各指數來源連結。

不新增資料庫表、不修改虛擬交易資金、持倉、雷達評分、登入或行事曆。驗證：`dotnet run --project tests/HomeDashboardChecks -c Release`；加入`--serve`後可執行`python tests/international_indices_browser_test.py`。
