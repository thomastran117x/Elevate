using backend.main.features.clubs;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class WeekendMakersCommonsClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "weekend-makers-commons",
        Name: "Weekend Makers Commons",
        Description: "A social maker club for casual DIY builds, collaborative tinkering, and low-barrier creative nights where people make things together.",
        ClubType: ClubType.Social,
        Theme: "hands-on community making",
        Tone: "playful, practical, and collaborative",
        ClubImage: "https://placehold.co/1200x800?text=Weekend+Makers+Commons",
        Email: $"hello.makers{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1106",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/weekend-makers-commons",
        Location: "West End Workshop District",
        City: "Toronto",
        MaxMemberCount: 250,
        OwnerEmail: $"makers.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"makers.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"makers.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["makers", "diy", "creative"],
        PublicSeries:
        [
            new("Open Bench Build Night", "A drop-in build session at {venue} where {club} members keep projects moving with peer help and shared tools.", EventCategory.Workshop, MakerVenues, ["build", "workshop"], 10, 1, 7, 23, 3, 34, 10, false, 3, 0),
            new("Repair Cafe Session", "Week {number} of the repair cafe invites members to troubleshoot small objects, swap knowledge, and learn practical fixes.", EventCategory.Other, MakerVenues, ["repair", "community"], 10, 3, 7, 20, 2, 28, 0, false, 2, 0),
            new("Paper and Print Lab", "A creative studio night at {venue} exploring cards, posters, zines, and tactile making techniques in a {tone} format.", EventCategory.Arts, PrintVenues, ["print", "paper"], 10, 5, 7, 22, 2, 24, 12, false, 2, 2),
            new("Micro Project Showcase", "{club} members present finished experiments, quick prototypes, and lessons from their recent maker sprints.", EventCategory.Conference, PrintVenues, ["showcase", "prototype"], 10, 7, 7, 21, 2, 40, 0, false, 4, 0),
            new("Weekend Craft Social", "A relaxed community-making social with simple prompts, materials tables, and lots of time to talk while making.", EventCategory.Social, MakerVenues, ["craft", "social"], 10, 9, 7, 18, 3, 36, 8, false, 3, 0)
        ],
        PrivateSeries:
        [
            new("Volunteer Inventory Reset", "Internal volunteer time for {club} to reset supplies, prep kits, and organize the bench layout at {venue}.", EventCategory.Volunteer, [new SeedVenueDefinition("Commons Back Room", "Commons Back Room", 43.6491, -79.4218)], ["staff", "inventory"], 5, 6, 14, 18, 1, 10, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> MakerVenues =
    [
        new("Commons Bench Hall", "Commons Bench Hall", 43.6491, -79.4218),
        new("Dovercourt Maker Loft", "Dovercourt Maker Loft", 43.6513, -79.4305),
        new("Junction Worktable Studio", "Junction Worktable Studio", 43.6657, -79.4709)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> PrintVenues =
    [
        new("Print Lab Annex", "Print Lab Annex", 43.6612, -79.4098),
        new("Paper House Studio", "Paper House Studio", 43.6574, -79.4332),
        new("West End Gallery Room", "West End Gallery Room", 43.6479, -79.4093)
    ];
}
