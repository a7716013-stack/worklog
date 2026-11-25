# Local guest / 3.3.1

Run the published API host with Start-WorkJournal3310.ps1. Only http://localhost:5212, Development, integrated local SQLEXPRESS and database WorkJournal_LocalGuest_331 are accepted. The launcher migrates only this explicitly isolated database. The existing WorkJournal database is not migrated or read.

Login offers a CSRF-protected guest button. Each entry/reset creates a fresh Identity owner. Cookie name/application discriminator are distinct from regular members. No existing member can be selected. All requests must have loopback local/remote IPs and localhost Host; forwarded headers cannot grant access. Production or wrong-database guest configuration fails startup.

Synthetic stock catalog, prices, flows and radar quotes support normal member interactions. All HttpClientFactory outbound requests are blocked; Google OAuth is unavailable. Missing external-only facts remain missing. Google calendar synchronization and real/paid services cannot be exercised without their external accounts. Synthetic results are labeled and cannot become real baseline/OOS/promotion. Reset changes workspace ownership without deleting prior sessions' data.

Guest is disabled by default. No production configuration or online hosting changed.
