using backend.main.features.clubs;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class HarbourStridersClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "harbour-striders",
        Name: "Harbour Striders Club",
        Description: "A waterfront running club built around consistent training blocks, low-pressure pacing groups, and a cheerful post-run coffee culture.",
        ClubType: ClubType.Sports,
        Theme: "waterfront running",
        Tone: "encouraging and momentum-building",
        ClubImage: "https://placehold.co/1200x800?text=Harbour+Striders+Club",
        Email: $"hello.harbour{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1101",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/harbour-striders",
        Location: "Toronto Waterfront",
        City: "Toronto",
        MaxMemberCount: 260,
        OwnerEmail: $"harbour.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"harbour.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"harbour.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["running", "waterfront", "fitness"],
        PublicSeries:
        [
            new("Sunrise Run Session", "Week {number} of {club}'s sunrise run series keeps the {theme} routine steady with easy mileage and form cues at {venue}.", EventCategory.Fitness, WaterfrontVenues, ["sunrise", "cardio"], 10, 2, 7, 11, 2, 48, 0, false, 6, 0),
            new("Tempo Thursday", "A {tone} tempo workout for runners building confidence and pacing control along the harbour route near {venue}.", EventCategory.Fitness, WaterfrontVenues, ["tempo", "endurance"], 10, 4, 7, 22, 2, 40, 5, false, 4, 0),
            new("Beginner Run Clinic", "{club} hosts week {number} of its beginner clinic with drills, warmups, and technique coaching at {venue}.", EventCategory.Sports, ParkVenues, ["beginner", "clinic"], 10, 6, 7, 16, 2, 34, 0, false, 3, 0),
            new("Waterfront Strength Lab", "A runner-specific strength session at {venue} focused on durability, posture, and injury prevention for the {theme} season.", EventCategory.Fitness, ParkVenues, ["strength", "mobility"], 10, 8, 7, 18, 2, 30, 8, false, 2, 0),
            new("Community Long Run", "The week {number} long run for {club} brings pace groups together for a scenic loop and relaxed recovery chat after the finish.", EventCategory.Sports, WaterfrontVenues, ["long-run", "community"], 10, 10, 7, 13, 3, 58, 0, false, 5, 0)
        ],
        PrivateSeries:
        [
            new("Crew Route Planning Huddle", "An internal coordination session for {club} volunteers covering marshal placement, route notes, and safety planning at {venue}.", EventCategory.Fitness, [new SeedVenueDefinition("Harbour Studio Loft", "Harbourfront Studio Loft", 43.6409, -79.3810)], ["staff", "operations"], 5, 5, 14, 23, 1, 12, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> WaterfrontVenues =
    [
        new("Harbourfront Trailhead", "Queens Quay Trailhead", 43.6387, -79.3817),
        new("Martin Goodman Trail", "Martin Goodman Trail West Loop", 43.6403, -79.3854),
        new("Sugar Beach Boardwalk", "Sugar Beach Boardwalk", 43.6416, -79.3688)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> ParkVenues =
    [
        new("Coronation Park Track", "Coronation Park", 43.6382, -79.3951),
        new("Trillium Park Lawn", "Trillium Park", 43.6331, -79.4107),
        new("Roundhouse Green", "Roundhouse Park", 43.6413, -79.3860)
    ];
}
