using backend.main.features.clubs;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class LanternSocialCollectiveClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "lantern-social-collective",
        Name: "Lantern Social Collective",
        Description: "A city social club that organizes low-pressure mixers, hobby meetups, and welcoming nights for people building real friendships in Toronto.",
        ClubType: ClubType.Social,
        Theme: "friendship-building city socials",
        Tone: "warm, easygoing, and host-led",
        ClubImage: "https://placehold.co/1200x800?text=Lantern+Social+Collective",
        Email: $"hello.lantern{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1105",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/lantern-social-collective",
        Location: "Queen West",
        City: "Toronto",
        MaxMemberCount: 340,
        OwnerEmail: $"lantern.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"lantern.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"lantern.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["social", "community", "city-life"],
        PublicSeries:
        [
            new("Rooftop Mixer Night", "A hosted social evening at {venue} where {club} members meet in smaller conversation circles and get easy prompts to connect.", EventCategory.Social, SocialVenues, ["mixer", "rooftop"], 10, 1, 7, 23, 3, 72, 0, false, 6, 0),
            new("Board Game Social", "Week {number} of the game social rotates party games and strategy tables so new members can join without pressure.", EventCategory.Gaming, SocialVenues, ["games", "friendship"], 10, 3, 7, 22, 3, 44, 8, false, 4, 0),
            new("Photo Walk Meet-Up", "A guided city walk through {venue} with casual prompts, easy pair rotations, and plenty of room for spontaneous conversation.", EventCategory.Social, WalkVenues, ["walk", "photography"], 10, 5, 7, 19, 2, 38, 0, false, 3, 0),
            new("Trivia and Tacos", "A playful team trivia night that keeps the {theme} calendar lively with food, humor, and low-stakes competition.", EventCategory.Food, SocialVenues, ["trivia", "food"], 10, 7, 7, 23, 3, 58, 12, false, 5, 2),
            new("Newcomer Welcome Night", "A welcome-oriented social for first-timers with introductions, host support, and relaxed table activities at {venue}.", EventCategory.Social, WalkVenues, ["welcome", "newcomer"], 10, 9, 7, 21, 2, 48, 0, false, 4, 0)
        ],
        PrivateSeries:
        [
            new("Host Team Flow Check", "Internal host prep for {club} covering room setup, greeter rotations, and safety follow-ups at {venue}.", EventCategory.Social, [new SeedVenueDefinition("Lantern House Office", "Lantern House Office", 43.6459, -79.3925)], ["staff", "hosting"], 5, 6, 14, 19, 1, 10, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> SocialVenues =
    [
        new("Lantern House Rooftop", "King Street Rooftop", 43.6459, -79.3925),
        new("Queen West Studio", "Queen West Studio", 43.6474, -79.3938),
        new("King Street Loft", "King Street Loft", 43.6504, -79.3964)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> WalkVenues =
    [
        new("City Hall Plaza", "Nathan Phillips Square", 43.6535, -79.3841),
        new("Trinity Bellwoods Lawn", "Trinity Bellwoods Park", 43.6477, -79.4204),
        new("Stackt Courtyard", "Stackt Market Courtyard", 43.6436, -79.4020)
    ];
}
