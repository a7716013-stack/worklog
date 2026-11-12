namespace WorkJournal.Web.Security;
// Application contract: HTTP and explicitly scoped background identities provide implementations.
public interface ICurrentUser { string Id { get; } }
public static class FeaturePolicies
{
    public const string Swing="Feature.SwingAnalysis";
    public const string Paper="Feature.PaperPortfolio";
}
