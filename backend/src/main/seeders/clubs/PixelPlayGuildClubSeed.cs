using backend.main.features.clubs;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class PixelPlayGuildClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "pixel-play-guild",
        Name: "Pixel Play Guild",
        Description: "A gaming club that brings together console, PC, and couch co-op fans for tournaments, learning nights, and good-humored community play.",
        ClubType: ClubType.Gaming,
        Theme: "community gaming nights",
        Tone: "energetic, fair, and welcoming",
        ClubImage: "https://placehold.co/1200x800?text=Pixel+Play+Guild",
        Email: $"hello.pixel{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1109",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/pixel-play-guild",
        Location: "Midtown Arcade District",
        City: "Toronto",
        MaxMemberCount: 360,
        OwnerEmail: $"pixel.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"pixel.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"pixel.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["gaming", "co-op", "arcade"],
        PublicSeries:
        [
            new("Couch Co-Op Carnival", "A rotating co-op night where {club} members jump into easy-entry games, cheer each other on, and swap stations at {venue}.", EventCategory.Gaming, GameVenues, ["co-op", "party-games"], 10, 1, 7, 23, 3, 64, 8, false, 4, 0),
            new("Ranked Friendly Tournament", "Week {number} of the guild tournament keeps competition lively without losing the {tone} community feel.", EventCategory.Gaming, GameVenues, ["tournament", "competitive"], 10, 3, 7, 23, 3, 96, 12, false, 8, 0),
            new("Retro Replay Club", "A community play session that revisits older favorites, shares stories, and keeps the {theme} calendar nostalgic and fun.", EventCategory.Gaming, RetroVenues, ["retro", "nostalgia"], 10, 5, 7, 21, 2, 44, 5, false, 3, 0),
            new("New Player Onboarding", "A host-led gaming night with controls help, low-stakes pairings, and simple rotations so newcomers can settle in quickly.", EventCategory.Gaming, RetroVenues, ["new-player", "learning"], 10, 7, 7, 20, 2, 36, 0, false, 2, 0),
            new("Guild Build and Banter", "Members trade recommendations, watch quick demos, and hang out between matches during this social-first gaming evening.", EventCategory.Social, GameVenues, ["hangout", "community"], 10, 9, 7, 22, 3, 52, 0, false, 4, 0)
        ],
        PrivateSeries:
        [
            new("Bracket and Stream Check", "Internal ops for {club} covering tournament brackets, equipment checks, and host assignments at {venue}.", EventCategory.Gaming, [new SeedVenueDefinition("Guild Control Desk", "Guild Control Desk", 43.7081, -79.3982)], ["staff", "production"], 5, 6, 14, 18, 1, 10, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> GameVenues =
    [
        new("Guild Arena Room", "Guild Arena Room", 43.7081, -79.3982),
        new("Midtown Console Hub", "Midtown Console Hub", 43.7104, -79.3959),
        new("Arcade Social Loft", "Arcade Social Loft", 43.7131, -79.4002)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> RetroVenues =
    [
        new("Retro Cabinet Lounge", "Retro Cabinet Lounge", 43.7059, -79.4014),
        new("LAN Commons", "LAN Commons", 43.7116, -79.4047),
        new("Pixel Library Room", "Pixel Library Room", 43.7072, -79.3928)
    ];
}
