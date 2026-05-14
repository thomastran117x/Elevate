namespace backend.main.seeders;

public static class SeedCatalogConstants
{
    public const string SeedEmailDomain = "@seed.eventxperience.test";
    public const string SeedWebsiteHost = "seed.eventxperience.test";
    public const string SeedEventTag = "seed:thematic";

    public static string ClubSeedTag(string slug) => $"seed:club:{slug}";
}
