namespace WorkJournal.Api.Contracts;
// Additive response fields may be absent on an older compatible API server.
[AttributeUsage(AttributeTargets.Property)]
public sealed class ApiOptionalAttribute:Attribute;
