using backend.main.features.clubs;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class NeighbourhoodKitchenTableClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "neighbourhood-kitchen-table",
        Name: "Neighbourhood Kitchen Table",
        Description: "A community food club where members cook, learn, and gather around practical meals, neighborhood hospitality, and shared kitchen confidence.",
        ClubType: ClubType.Other,
        Theme: "community cooking and shared meals",
        Tone: "grounded, generous, and neighborly",
        ClubImage: "https://placehold.co/1200x800?text=Neighbourhood+Kitchen+Table",
        Email: $"hello.kitchen{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1110",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/neighbourhood-kitchen-table",
        Location: "East End Community Kitchens",
        City: "Toronto",
        MaxMemberCount: 210,
        OwnerEmail: $"kitchen.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"kitchen.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"kitchen.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["food", "cooking", "community"],
        PublicSeries:
        [
            new("Weeknight Supper Club", "{club} gathers around a practical shared meal with rotating prep teams and table conversation hosted at {venue}.", EventCategory.Food, KitchenVenues, ["supper", "shared-meal"], 10, 1, 7, 22, 3, 24, 15, false, 2, 3),
            new("Knife Skills and Basics", "A hands-on kitchen workshop for week {number} focused on prep confidence, technique, and approachable recipes.", EventCategory.Workshop, KitchenVenues, ["skills", "kitchen"], 10, 3, 7, 19, 2, 18, 10, false, 2, 0),
            new("Seasonal Pantry Session", "Members explore pantry strategy, flexible home cooking, and ingredient swaps in this {tone} community class at {venue}.", EventCategory.Food, PantryVenues, ["pantry", "seasonal"], 10, 5, 7, 20, 2, 20, 8, false, 2, 0),
            new("Neighbour Potluck Night", "A social potluck designed to make the {theme} calendar feel open, warm, and deeply local.", EventCategory.Social, PantryVenues, ["potluck", "neighbourhood"], 10, 7, 7, 22, 3, 34, 0, false, 3, 0),
            new("Community Bake Table", "Week {number} of the bake table invites members to learn one reliable bake, trade tips, and leave with something to share.", EventCategory.Food, KitchenVenues, ["baking", "learning"], 10, 9, 7, 18, 2, 22, 12, false, 2, 2)
        ],
        PrivateSeries:
        [
            new("Kitchen Ops Reset", "Internal volunteer ops for {club} covering ingredients, cleanup assignments, and allergy planning at {venue}.", EventCategory.Volunteer, [new SeedVenueDefinition("Kitchen Pantry Office", "Kitchen Pantry Office", 43.6685, -79.3438)], ["staff", "operations"], 5, 6, 14, 18, 1, 10, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> KitchenVenues =
    [
        new("East End Community Kitchen", "East End Community Kitchen", 43.6685, -79.3438),
        new("Riverside Prep Room", "Riverside Prep Room", 43.6657, -79.3496),
        new("Danforth Bake Hall", "Danforth Bake Hall", 43.6774, -79.3455)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> PantryVenues =
    [
        new("Neighbourhood Pantry Lab", "Neighbourhood Pantry Lab", 43.6712, -79.3340),
        new("Carlaw Kitchen Studio", "Carlaw Kitchen Studio", 43.6653, -79.3412),
        new("Community Table Room", "Community Table Room", 43.6744, -79.3483)
    ];
}
