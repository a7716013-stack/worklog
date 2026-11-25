# Migration and rollback

Audit: MarketRadarRecommendation already stores SnapshotJson, ResearchJson, ScoreVersion, SourceUpdatedAt, score/part columns and daily selection ranks. Strategy signals have existing model-specific persistence. Reuse SnapshotJson for additive metadata; no schema migration is required, and no production database change is performed.

Rollback uses Legacy default routing or the prior application binary. Existing score columns and SnapshotJson properties remain compatible; older readers ignore Scoring. No Candidate rank or signal must be undone. Local guest bootstrap applies existing migrations only to explicitly named WorkJournal_LocalGuest_331; regular and production database configuration is rejected for guest startup.
