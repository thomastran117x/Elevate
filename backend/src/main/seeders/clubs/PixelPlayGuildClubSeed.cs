using backend.main.features.clubs;
using backend.main.features.clubs.posts;
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
        Posts:
        [
            new("Welcome to {club}", "{club} is a home for {theme} where new players and longtime regulars can actually enjoy the same room.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 45, 21, 172),
            new("How Tournament Nights Stay Friendly", "We seed brackets clearly, explain formats upfront, and make sure casual players still have a great night.", PostType.Announcement, true, SeedClubAuthorRole.Manager, 40, 13, 145),
            new("Current Co-Op Favorites", "Drop the co-op games that have been working best for conversation, quick onboarding, and big laughs lately.", PostType.General, false, SeedClubAuthorRole.Volunteer, 35, 10, 93),
            new("Volunteer Help for Setup", "We need a couple more hands for controller checks, signage, and newcomer table support before the next event.", PostType.Poll, false, SeedClubAuthorRole.Volunteer, 30, 5, 74),
            new("Retro Replay Recap", "The latest retro night proved again that a good throwback session can carry a whole room on pure charm.", PostType.Event, false, SeedClubAuthorRole.Manager, 25, 8, 89),
            new("Pinned: Community Play Rules", "Respect the queue, coach kindly, and don’t let skill level decide who gets included at the station.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 20, 16, 149),
            new("New Player Thread", "If you’re brand new to the club, tell us what kinds of games you enjoy and we’ll point you to the right tables.", PostType.Poll, false, SeedClubAuthorRole.Owner, 16, 7, 81),
            new("Guild Recommendation Swap", "Share one game you think more members should try and why it fits the room’s vibe.", PostType.General, false, SeedClubAuthorRole.Volunteer, 11, 6, 72),
            new("Tournament Format Preview", "The next bracket night will have shorter rounds, better station rotation, and more breathing room between matches.", PostType.Event, false, SeedClubAuthorRole.Manager, 7, 7, 79),
            new("Why We Care About the Room Feel", "A strong gaming community isn’t just about titles or skill. It’s about whether people want to come back next week.", PostType.General, false, SeedClubAuthorRole.Owner, 4, 9, 88)
        ],
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
