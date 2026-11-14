# 初始 Gap Matrix
| 範圍 | Audit 狀態 | 工作 |
|---|---|---|
| Identity/CSRF/owner isolation | Verified（既有 fixture） | 重跑及正式匿名 smoke |
| 正式 A/B 與 OAuth | Blocked | 缺隔離真實帳號，保留人工驗收 |
| 歷史來源及取得安全 | Verified（既有實作） | 投影版本、scope、時間、rights、回執；不擴大下載 |
| 指派/COI/審查稽核 | Partial | 受控 append-only 審查工作台，核對既有回執 |
| 排程/lease/recovery | Verified（既有 fixture） | 保留核心，readiness gate/backup restore |
| 正式磁碟/許可/備份 | Blocked | 本機 fixture 不算正式驗收 |
| Trend readiness | Partial | Source→Authority→PIT→Approval→Sample→Baseline |
| Operations UI | Partial | 準備度、告警與管理 queue |
| 本次同 SHA CI/部署 | Not Implemented | 完成後記錄實際結果 |

## 本次交付狀態
Verified：本機 API/SQL、Identity fixture、管理授權、COI/既有 acquisition operator provenance、append-only 審查/撤銷、備份還原 replay、Disabled/Dry-run gate、API 契約及手機畫面。測試詳 engineering。
Partial：歷史證據取得完成既有封存/43 tasks 投影；缺歷史 publication/revision/rights 的工作仍需外部證據。Operations 為發布快照，沒有即時告警通知。
Blocked：正式 A/B 與真實 OAuth、正式共享磁碟與 provider permission/backup/secret/alert/rollback 驗收、資料集核准與真實 baseline；未啟用 production worker。
GitHub 同 SHA CI 與部署：見發布完成後的 release-verification 報告，不由 fixture 推定。
