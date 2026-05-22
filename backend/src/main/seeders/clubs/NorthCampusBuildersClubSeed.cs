using backend.main.features.clubs;
using backend.main.features.clubs.posts;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class NorthCampusBuildersClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "north-campus-builders",
        Name: "North Campus Builders",
        Description: "A campus club for students building products, ventures, and side projects through hands-on sessions, mentor feedback, and founder community.",
        ClubType: ClubType.Academic,
        Theme: "student founders and product builders",
        Tone: "ambitious, practical, and collaborative",
        ClubImage: "https://placehold.co/1200x800?text=North+Campus+Builders",
        Email: $"hello.builders{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1103",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/north-campus-builders",
        Location: "Downtown Campus Innovation Hall",
        City: "Toronto",
        MaxMemberCount: 420,
        OwnerEmail: $"builders.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"builders.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"builders.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["startup", "product", "builders"],
        Posts:
        [
            new("Welcome to {club}", "{club} exists for students building real things. Come as you are, bring the rough draft, and keep moving.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 45, 20, 180),
            new("How We Run Build Nights", "Build nights are practical: short kickoff, focused work block, and a fast debrief so momentum stays high.", PostType.Announcement, true, SeedClubAuthorRole.Manager, 41, 14, 146),
            new("Mentor Office Hours Format", "Office hours work best when you arrive with one concrete bottleneck, one key question, and one next step in mind.", PostType.General, false, SeedClubAuthorRole.Manager, 36, 9, 101),
            new("Volunteer Help Needed for Demo Setup", "We need a few extra hands to manage room flow, timers, and check-in before the next demo night.", PostType.Poll, false, SeedClubAuthorRole.Volunteer, 31, 5, 77),
            new("Builder Resource Thread", "Share templates, decks, research tools, and founder reads that actually helped you make progress this semester.", PostType.General, false, SeedClubAuthorRole.Owner, 27, 12, 115),
            new("Pitch Night Expectations", "Pitch nights are sharp but supportive. We care about clarity, audience fit, and honest next-step feedback.", PostType.Event, false, SeedClubAuthorRole.Manager, 22, 8, 96),
            new("Member Win: Shipped This Week", "A quick celebration thread for launches, pilot tests, waitlists, and quiet progress that deserves some credit.", PostType.General, false, SeedClubAuthorRole.Volunteer, 17, 7, 88),
            new("Pinned: Community Standards", "Be specific, be generous, and be honest. We want ambitious conversations without performative noise.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 13, 16, 153),
            new("Breakfast Forum Roll Call", "If you’re coming to the next breakfast forum, reply with the problem you’re currently untangling so tables can be grouped well.", PostType.Poll, false, SeedClubAuthorRole.Manager, 8, 6, 74),
            new("Why We Keep Things Practical", "The culture of {club} is built around execution. Fewer speeches, more reps, better questions, stronger follow-through.", PostType.General, false, SeedClubAuthorRole.Owner, 4, 11, 97)
        ],
        PublicSeries:
        [
            new("Founder Sprint Lab", "Week {number} of the sprint lab helps {club} members sharpen priorities, test assumptions, and keep shipping momentum at {venue}.", EventCategory.Workshop, CampusInnovationVenues, ["startup", "sprint"], 10, 1, 7, 22, 2, 44, 5, false, 4, 5),
            new("Pitch Practice Night", "A structured pitch rehearsal for the {theme} community with peer notes, timed demos, and live feedback at {venue}.", EventCategory.Networking, CampusInnovationVenues, ["pitch", "feedback"], 10, 3, 7, 23, 2, 62, 0, false, 3, 0),
            new("Mentor Office Hours", "Small-group office hours in week {number} connect founders with experienced operators around growth, hiring, and execution.", EventCategory.Academic, MentorVenues, ["mentorship", "office-hours"], 10, 5, 7, 20, 2, 24, 10, false, 2, 0),
            new("Demo and Debrief", "{club} showcases in-progress products, then closes with a grounded debrief on learnings, pivots, and next bets.", EventCategory.Conference, MentorVenues, ["demo", "product"], 10, 7, 7, 21, 2, 80, 0, false, 5, 0),
            new("Builder Breakfast Forum", "A relaxed breakfast table for student operators comparing roadmaps, traction experiments, and workflow systems.", EventCategory.Networking, CampusInnovationVenues, ["breakfast", "community"], 10, 9, 7, 15, 2, 36, 8, false, 3, 0)
        ],
        PrivateSeries:
        [
            new("Core Team Program Review", "An internal planning review for {club} covering mentor outreach, speaker logistics, and sponsor follow-ups at {venue}.", EventCategory.Academic, [new SeedVenueDefinition("Builders War Room", "Innovation Hall Seminar Room", 43.6629, -79.3957)], ["staff", "planning"], 5, 6, 14, 19, 1, 12, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> CampusInnovationVenues =
    [
        new("Innovation Hall", "Innovation Hall", 43.6629, -79.3957),
        new("Design Lab 204", "Campus Design Lab 204", 43.6644, -79.3982),
        new("Founder Lounge", "Founder Lounge", 43.6650, -79.3960)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> MentorVenues =
    [
        new("Engineering Commons", "Engineering Commons Hall", 43.6670, -79.3980),
        new("Campus Cafe", "Campus Cafe", 43.6620, -79.3990),
        new("Startup Atrium", "Innovation Atrium", 43.6660, -79.3970)
    ];
}
