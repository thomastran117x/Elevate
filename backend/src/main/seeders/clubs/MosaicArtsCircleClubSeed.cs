using backend.main.features.clubs;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class MosaicArtsCircleClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "mosaic-arts-circle",
        Name: "Mosaic Arts Circle",
        Description: "A cultural arts club for sketching, gallery conversation, creative critique, and community art nights that feel welcoming rather than intimidating.",
        ClubType: ClubType.Cultural,
        Theme: "community visual arts",
        Tone: "reflective, generous, and creatively curious",
        ClubImage: "https://placehold.co/1200x800?text=Mosaic+Arts+Circle",
        Email: $"hello.mosaic{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1107",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/mosaic-arts-circle",
        Location: "Queen Street East Arts District",
        City: "Toronto",
        MaxMemberCount: 230,
        OwnerEmail: $"mosaic.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"mosaic.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"mosaic.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["art", "sketching", "gallery"],
        PublicSeries:
        [
            new("Sketchbook Social", "A hosted sketch night for {club} that keeps the {theme} vibe accessible with prompts, playlists, and table mixing at {venue}.", EventCategory.Arts, ArtVenues, ["sketching", "social"], 10, 1, 7, 22, 2, 26, 12, false, 2, 2),
            new("Gallery Walk and Talk", "Members gather for week {number} of a slow-looking gallery walk with conversation prompts and easy reflection at {venue}.", EventCategory.Cultural, GalleryVenues, ["gallery", "walk"], 10, 3, 7, 19, 2, 30, 0, false, 3, 0),
            new("Colour Study Workshop", "A practical studio session at {venue} focused on color, composition, and playful experimentation for artists at every level.", EventCategory.Workshop, ArtVenues, ["painting", "workshop"], 10, 5, 7, 21, 2, 22, 14, false, 2, 2),
            new("Artist Crit Night", "{club} hosts a supportive critique table where creators share in-progress work and receive specific, generous feedback.", EventCategory.Arts, GalleryVenues, ["critique", "feedback"], 10, 7, 7, 23, 2, 20, 8, false, 2, 0),
            new("Community Mural Meetup", "A collaborative planning and making session tied to the season's mural ideas, hosted in a {tone} group format.", EventCategory.Cultural, ArtVenues, ["mural", "community"], 10, 9, 7, 18, 3, 34, 0, false, 3, 0)
        ],
        PrivateSeries:
        [
            new("Exhibit Install Planning", "Internal staff planning for {club}'s pop-up displays, volunteer shifts, and materials checklists at {venue}.", EventCategory.Cultural, [new SeedVenueDefinition("Mosaic Storage Studio", "Mosaic Storage Studio", 43.6532, -79.3619)], ["staff", "install"], 5, 6, 14, 18, 1, 10, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> ArtVenues =
    [
        new("Mosaic Studio Room", "Mosaic Studio Room", 43.6532, -79.3619),
        new("Queen East Maker Gallery", "Queen East Maker Gallery", 43.6572, -79.3601),
        new("Distillery Sketch Hall", "Distillery Sketch Hall", 43.6506, -79.3595)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> GalleryVenues =
    [
        new("The Image Loft", "The Image Loft", 43.6547, -79.3772),
        new("AGO Learning Space", "AGO Learning Space", 43.6536, -79.3925),
        new("Harbour Gallery Walk", "Harbour Gallery Walk", 43.6426, -79.3862)
    ];
}
