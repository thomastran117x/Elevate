using backend.main.features.clubs;
using backend.main.features.clubs.posts;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class RhythmExchangeCollectiveClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "rhythm-exchange-collective",
        Name: "Rhythm Exchange Collective",
        Description: "A cultural music and dance club where members learn social movement basics, share playlists, and build confidence through guided practice nights.",
        ClubType: ClubType.Cultural,
        Theme: "music and social dance exchange",
        Tone: "lively, expressive, and beginner-friendly",
        ClubImage: "https://placehold.co/1200x800?text=Rhythm+Exchange+Collective",
        Email: $"hello.rhythm{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1108",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/rhythm-exchange-collective",
        Location: "Dundas West Music Row",
        City: "Toronto",
        MaxMemberCount: 310,
        OwnerEmail: $"rhythm.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"rhythm.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"rhythm.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["music", "dance", "rhythm"],
        Posts:
        [
            new("Welcome to {club}", "{club} is for people who want to explore {theme} without needing perfect technique on day one.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 45, 18, 151),
            new("What Beginner-Friendly Means Here", "We slow things down, explain transitions clearly, and keep partner rotations kind and predictable.", PostType.Announcement, true, SeedClubAuthorRole.Manager, 40, 12, 130),
            new("Playlist Drop Thread", "Share a song that makes you want to move. We use member picks to keep the room feeling alive and collective.", PostType.General, false, SeedClubAuthorRole.Volunteer, 35, 9, 87),
            new("Volunteer Ask for Check-In Crew", "We’d love a few extra people to help greet newcomers and keep the first fifteen minutes feeling easy.", PostType.Poll, false, SeedClubAuthorRole.Volunteer, 30, 5, 69),
            new("Live Band Night Recap", "The last live band social had beautiful energy and a lot of brave first dances. Thanks for making it feel open.", PostType.Event, false, SeedClubAuthorRole.Manager, 25, 8, 95),
            new("Pinned: Floor Etiquette", "Respect the shared floor, rotate kindly, and remember that confidence grows fastest in a room without ego.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 20, 14, 140),
            new("Movement Practice Tip", "If timing feels hard, simplify your footwork and listen for the groove before trying to add style.", PostType.General, false, SeedClubAuthorRole.Owner, 16, 7, 76),
            new("Partnerwork Workshop Sign-Up", "Tell us if you’re joining the next workshop and whether you want fundamentals, musicality, or turn-pattern reps.", PostType.Poll, false, SeedClubAuthorRole.Manager, 11, 6, 71),
            new("Member Playlist Spotlight", "This week’s community playlist has warm-up tracks, social-floor picks, and a few quiet gems for the ride home.", PostType.General, false, SeedClubAuthorRole.Volunteer, 7, 6, 74),
            new("Why We Teach Through Joy", "The fastest way to stay in the scene is to make practice feel good. That’s a big part of the {club} philosophy.", PostType.General, false, SeedClubAuthorRole.Owner, 4, 9, 84)
        ],
        PublicSeries:
        [
            new("Groove Basics Night", "{club} teaches week {number} of its foundation series with simple movement, partner rotation, and a {tone} host flow at {venue}.", EventCategory.Music, RhythmVenues, ["dance", "beginner"], 10, 1, 7, 22, 2, 50, 10, false, 4, 0),
            new("Live Band Social", "A social dance night with live music at {venue}, designed to keep the {theme} community welcoming and energetic.", EventCategory.Music, RhythmVenues, ["live-music", "social"], 10, 3, 7, 23, 3, 72, 12, false, 6, 3),
            new("Partnerwork Workshop", "Members practice timing, partner connection, and movement clarity in this structured studio workshop at {venue}.", EventCategory.Workshop, StudioVenues, ["partnerwork", "workshop"], 10, 5, 7, 21, 2, 34, 15, false, 3, 0),
            new("Playlist Exchange Session", "A social listening night where members bring songs, share musical context, and expand the club's community soundtrack.", EventCategory.Cultural, StudioVenues, ["playlist", "listening"], 10, 7, 7, 20, 2, 28, 0, false, 2, 0),
            new("Movement Jam Evening", "A freer-form community jam that mixes guided rounds and open-floor energy for dancers at different comfort levels.", EventCategory.Party, RhythmVenues, ["jam", "movement"], 10, 9, 7, 22, 3, 60, 8, false, 5, 0)
        ],
        PrivateSeries:
        [
            new("DJ and Host Runthrough", "Internal staff practice for {club} covering cue sheets, volunteer roles, and room transitions at {venue}.", EventCategory.Music, [new SeedVenueDefinition("Rhythm Control Booth", "Rhythm Control Booth", 43.6497, -79.4358)], ["staff", "production"], 5, 6, 14, 18, 1, 10, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> RhythmVenues =
    [
        new("Dundas Social Hall", "Dundas Social Hall", 43.6497, -79.4358),
        new("West End Listening Room", "West End Listening Room", 43.6518, -79.4432),
        new("Junction Dance Loft", "Junction Dance Loft", 43.6652, -79.4701)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> StudioVenues =
    [
        new("Rhythm Practice Studio", "Rhythm Practice Studio", 43.6468, -79.4306),
        new("College Street Movement Room", "College Street Movement Room", 43.6551, -79.4212),
        new("Bloor Rehearsal Hall", "Bloor Rehearsal Hall", 43.6648, -79.4297)
    ];
}
