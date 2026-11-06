namespace JP.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Puts every test class marked <c>[Collection("integration")]</c> in the same group:
/// they share one <see cref="IntegrationFixture"/> and run one after the other
/// (they share a database, so they must not run in parallel).
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "integration";
}
