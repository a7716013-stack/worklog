# Recommendation tracking integration

MarketRadarService and RadarJobs call the shared adapter. ReadMapping adds optional scoring to the v1 radar analysis DTO; existing members, ordering, pagination and null semantics stay compatible. The UI displays distinct Legacy and Candidate sections.

MarketRadarRecommendationService attaches versioned scoring when creating a new SnapshotJson. Its existing SourceUpdatedAt is recorded as the Legacy cutoff metadata, not asserted to satisfy Candidate PIT. The existing database transaction, daily batch uniqueness, Rank, Category, personal ownership checks and follow operations are retained. Existing records are never backfilled or recalculated. Old snapshot pages explicitly state comparison metadata was not saved.

The tracking table links to immutable snapshot breakdowns. Candidate contributes no rank, Qualified state, signal or trade. 70 remains Legacy only.
