# 風險
- 同版號以 Phase、manifest、SHA 區分，保留舊報告。
- RetrievedAt 不替代 PublishedAt；來源取得不授予 Authority。
- 正式 A/B、OAuth 缺帳號；與 fixture 分開記錄。
- 檔案式協調需共享持久磁碟/OS ACL，未驗證不得啟用正式 worker。
- CLI 操作員為 OS 受控身分，不能把任意 reviewer 字串當成網站授權；HTTP 只讀且限管理者。
- Scoped review 不取代六構面核准、PIT、sample 或 promotion。
- 正式備份/秘密管理缺驗收證據，不能由本機測試推定成功。
