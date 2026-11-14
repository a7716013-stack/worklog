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
