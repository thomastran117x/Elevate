using backend.main.features.clubs;
using backend.main.features.clubs.posts;
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
        Posts:
        [
            new("Welcome to {club}", "{club} is a home for {theme} where imperfect projects are part of the fun.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 45, 15, 138),
            new("What Counts as a Project Here", "Loose repairs, paper prototypes, tiny experiments, and unfinished ideas all count. Bring what you’ve got.", PostType.General, true, SeedClubAuthorRole.Manager, 40, 11, 119),
            new("Tool Table Notes", "If you borrow shared tools, reset the station before you leave so the next builder can jump in fast.", PostType.Announcement, false, SeedClubAuthorRole.Volunteer, 35, 7, 83),
            new("Open Bench Sign-Up", "Reply if you need table space, a soldering station, or extra materials for the next build night.", PostType.Poll, false, SeedClubAuthorRole.Manager, 30, 5, 74),
            new("Repair Cafe Recap", "The repair cafe was a reminder that practical making can be social, generous, and surprisingly funny.", PostType.Event, false, SeedClubAuthorRole.Owner, 25, 8, 91),
            new("Pinned: Commons Etiquette", "Ask before rearranging someone’s setup, label shared materials clearly, and leave the bench better than you found it.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 20, 14, 133),
            new("Project Prompt Thread", "What small build would you actually finish in one evening if you had the right nudge from the group?", PostType.Poll, false, SeedClubAuthorRole.Volunteer, 16, 6, 71),
            new("Member Showcase Call", "If you’ve wrapped a small project recently, post a photo and a one-line lesson from making it.", PostType.General, false, SeedClubAuthorRole.Manager, 11, 9, 86),
            new("Craft Social Preview", "The next craft social is casual by design: simple materials, low stakes, and lots of room to talk while you work.", PostType.Event, false, SeedClubAuthorRole.Volunteer, 7, 5, 70),
            new("Why We Like Tiny Wins", "Small finished things keep the momentum alive. {club} is built to make tiny wins feel visible and shared.", PostType.General, false, SeedClubAuthorRole.Owner, 4, 8, 79)
        ],
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
