# Phase 4M API 變更（3.2.2）
新增 operations/readiness、research/trend/readiness 的會員唯讀 API；新增 authority/workbench、authority/acquisition-evidence 的 Administrator 唯讀 API。全部 /api/v1、ReadPolicy、no-store，管理 queue 沿用 PageQuery/PageDto，匿名 401、會員管理操作 403、快照不可用 503。沒有公開核准或排程寫入。
完整 DTO 與 scope 由 ApiDocument 反射產生 OpenAPI；契約相容測試沿用，網站透過同源 Cookie 取得。共用投影、scope/COI、readiness、備份與執行 gate 均在 Application；Web 只呈現，CLI 重用核心。Api→Web hosting 既有相依仍存在，沒有宣稱全面抽離完成。
Phase4M-v1 綁定來源 SHA、Phase4I/4J digests、formula digest、sample policy 與 export time；不把 export/retrieved time 當 PublishedAt。Candidate-v1/19 features/25-15-15-20-15-10、七 PIT gates、六獨立構面核准與 TrendSample-v1 不變。舊 HTTP DTO 沒有破壞性變更。
