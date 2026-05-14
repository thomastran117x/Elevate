using backend.main.features.clubs;
using backend.main.features.clubs.posts;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class SummitTrailSocietyClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "summit-trail-society",
        Name: "Summit Trail Society",
        Description: "An outdoor recreation club for urban hikers and trail-curious members who want guided practice, route confidence, and shared weekend adventure.",
        ClubType: ClubType.Sports,
        Theme: "urban hiking and trail fitness",
        Tone: "steady, outdoorsy, and welcoming",
        ClubImage: "https://placehold.co/1200x800?text=Summit+Trail+Society",
        Email: $"hello.summit{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1102",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/summit-trail-society",
        Location: "Evergreen Brick Works",
        City: "Toronto",
        MaxMemberCount: 220,
        OwnerEmail: $"summit.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"summit.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"summit.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["hiking", "outdoors", "trail"],
        Posts:
        [
            new("Welcome to {club}", "{club} is for people curious about {theme} without needing expert gear or prior trail experience.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 45, 14, 132),
            new("How Our Hikes Are Structured", "Expect clear route briefings, pace check-ins, and a supportive rhythm that makes the outdoors feel accessible.", PostType.General, true, SeedClubAuthorRole.Manager, 41, 11, 104),
            new("Gear Basics for New Members", "Bring water, layers, and shoes you trust. We’ll take care of the route notes and the group pacing.", PostType.General, false, SeedClubAuthorRole.Volunteer, 37, 8, 90),
            new("Trail Etiquette We Actually Use", "Give space on narrow sections, communicate stops early, and keep the group feeling calm and predictable.", PostType.Announcement, false, SeedClubAuthorRole.Owner, 33, 7, 88),
            new("Volunteer Interest Check", "We’re looking for members who can help with attendance check-ins and beginner support during weekend treks.", PostType.Poll, false, SeedClubAuthorRole.Volunteer, 28, 4, 63),
            new("Brick Works Route Preview", "This week’s route has gentle elevation, lots of tree cover, and good regroup points for first-timers.", PostType.Event, false, SeedClubAuthorRole.Manager, 23, 10, 98),
            new("Why We End with a Debrief", "The short post-hike debrief helps us swap tips, check energy levels, and learn what made the route work.", PostType.General, false, SeedClubAuthorRole.Owner, 18, 6, 75),
            new("Pinned: Weather and Safety Policy", "If conditions change, we’ll post early and move to the backup plan rather than force a bad route day.", PostType.Announcement, true, SeedClubAuthorRole.Manager, 13, 13, 124),
            new("Community Photo Thread", "Drop your favorite skyline, ravine, or trail shots from recent meetups. The little details make the season memorable.", PostType.General, false, SeedClubAuthorRole.Volunteer, 8, 5, 70),
            new("Weekend Trek Check-In", "If you’re joining the next trek, tell us whether you want the easy group or the steady-climb group so we can pace well.", PostType.Poll, false, SeedClubAuthorRole.Manager, 4, 4, 66)
        ],
        PublicSeries:
        [
            new("City Trail Starter", "{club} runs week {number} of its starter hike series with pacing tips, gear basics, and navigation practice from {venue}.", EventCategory.Sports, ValleyVenues, ["beginner", "hike"], 10, 1, 7, 15, 3, 32, 0, false, 4, 0),
            new("Hill Climb Conditioning", "A {tone} conditioning block that mixes stair climbs, incline walking, and recovery drills near {venue}.", EventCategory.Fitness, UrbanClimbVenues, ["conditioning", "fitness"], 10, 3, 7, 23, 2, 26, 5, false, 2, 0),
            new("Saturday Ridge Trek", "The week {number} ridge trek keeps {club}'s {theme} calendar adventurous while staying friendly to mixed ability levels.", EventCategory.Sports, ValleyVenues, ["weekend", "trek"], 10, 5, 7, 14, 4, 42, 0, false, 5, 0),
            new("Camp Skills Primer", "Members learn wayfinding, packing logic, and group etiquette in this practical outdoor workshop at {venue}.", EventCategory.Workshop, UrbanClimbVenues, ["skills", "workshop"], 10, 7, 7, 18, 2, 24, 10, false, 3, 0),
            new("Golden Hour Nature Walk", "A relaxed evening trail walk through week {number} of the season, designed for conversation, photos, and easy movement.", EventCategory.Social, ValleyVenues, ["walk", "nature"], 10, 9, 7, 20, 2, 36, 0, false, 4, 0)
        ],
        PrivateSeries:
        [
            new("Trail Crew Safety Briefing", "Internal staff planning for {club} covering first-aid roles, attendance plans, and fallback routes at {venue}.", EventCategory.Volunteer, [new SeedVenueDefinition("Summit Planning Room", "Brick Works Planning Room", 43.6843, -79.3657)], ["staff", "safety"], 5, 6, 14, 22, 1, 10, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> ValleyVenues =
    [
        new("Evergreen Brick Works", "Evergreen Brick Works Trailhead", 43.6843, -79.3657),
        new("Crothers Woods North Gate", "Crothers Woods North Gate", 43.6908, -79.3528),
        new("Beltline Forest Entrance", "Kay Gardner Beltline Trail Entrance", 43.6892, -79.4056)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> UrbanClimbVenues =
    [
        new("Casa Loma Stairs", "Casa Loma Stair Circuit", 43.6780, -79.4094),
        new("Riverdale Hill", "Riverdale Park East Hill", 43.6717, -79.3570),
        new("Baldwin Steps", "Baldwin Steps", 43.6765, -79.4037)
    ];
}
