using backend.main.features.clubs;
using backend.main.features.clubs.posts;
using backend.main.features.events;

namespace backend.main.seeders.clubs;

public sealed class CivicSpeakersForumClubSeed : IClubSeedDefinitionSource
{
    public SeedClubDefinition Definition { get; } = new(
        Slug: "civic-speakers-forum",
        Name: "Civic Speakers Forum",
        Description: "A public speaking and civic dialogue club where members practice persuasive communication, structured debate, and thoughtful community discussion.",
        ClubType: ClubType.Academic,
        Theme: "public speaking and civic dialogue",
        Tone: "thoughtful, sharp, and approachable",
        ClubImage: "https://placehold.co/1200x800?text=Civic+Speakers+Forum",
        Email: $"hello.speakers{SeedCatalogConstants.SeedEmailDomain}",
        Phone: "+1-416-555-1104",
        WebsiteUrl: "https://seed.eventxperience.test/clubs/civic-speakers-forum",
        Location: "St. George Civic Hall",
        City: "Toronto",
        MaxMemberCount: 280,
        OwnerEmail: $"speakers.owner{SeedCatalogConstants.SeedEmailDomain}",
        ManagerEmail: $"speakers.manager{SeedCatalogConstants.SeedEmailDomain}",
        VolunteerEmail: $"speakers.volunteer{SeedCatalogConstants.SeedEmailDomain}",
        ThemeTags: ["speaking", "debate", "civic"],
        Posts:
        [
            new("Welcome to {club}", "{club} is a place to practice speaking with courage, rigor, and a lot more kindness than people expect.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 45, 17, 149),
            new("What a Good Debate Night Looks Like", "Clear claims, active listening, no grandstanding, and moderators who keep the room useful for everyone.", PostType.Announcement, true, SeedClubAuthorRole.Manager, 40, 12, 126),
            new("Speechcraft Prompt of the Week", "This week’s prompt: explain a public issue in plain language to someone who has never followed it closely.", PostType.General, false, SeedClubAuthorRole.Volunteer, 35, 8, 87),
            new("Volunteer Sign-Up for Timers and Greeters", "We need two extra volunteers for room timing, participant check-in, and mic handoff support.", PostType.Poll, false, SeedClubAuthorRole.Volunteer, 30, 4, 69),
            new("How to Give Better Feedback", "Name one strength, one specific improvement, and one question that pushes the speaker to think more clearly.", PostType.General, false, SeedClubAuthorRole.Owner, 26, 10, 93),
            new("Town Hall Listening Notes", "Listening labs matter because good public dialogue depends on more than confidence. It depends on attention.", PostType.Event, false, SeedClubAuthorRole.Manager, 21, 9, 91),
            new("Member Reflection Thread", "What speaking habit are you currently working on: pacing, structure, eye contact, or stronger openings?", PostType.Poll, false, SeedClubAuthorRole.Owner, 17, 7, 73),
            new("Pinned: Room Norms", "We interrupt arguments, not people. We ask questions to clarify, not to posture. That’s the baseline here.", PostType.Announcement, true, SeedClubAuthorRole.Owner, 12, 15, 140),
            new("Open Mic Signup", "Reply if you want a short speaking slot at the next open mic policy forum. New speakers are especially welcome.", PostType.Event, false, SeedClubAuthorRole.Manager, 8, 6, 78),
            new("Why Civic Dialogue Still Matters", "We keep showing up because thoughtful conversation is a skill, and communities are better when more people can do it well.", PostType.General, false, SeedClubAuthorRole.Owner, 4, 9, 85)
        ],
        PublicSeries:
        [
            new("Speechcraft Studio", "Week {number} of Speechcraft Studio gives {club} members a practical speaking rep with coached delivery and concise feedback at {venue}.", EventCategory.Workshop, HallVenues, ["speech", "practice"], 10, 2, 7, 22, 2, 28, 0, false, 3, 0),
            new("Debate Night Assembly", "A live debate round at {venue} exploring current issues with a {tone} format built for clarity, rigor, and respect.", EventCategory.Academic, HallVenues, ["debate", "discussion"], 10, 4, 7, 23, 2, 56, 0, false, 4, 0),
            new("Town Hall Listening Lab", "This dialogue session helps members practice question design, moderation, and active listening through civic scenarios.", EventCategory.Conference, ForumVenues, ["moderation", "listening"], 10, 6, 7, 20, 2, 42, 5, false, 3, 0),
            new("Storytelling for Impact", "A speaking workshop at {venue} focused on narrative structure, confidence, and memorable openings for community leaders.", EventCategory.Workshop, ForumVenues, ["storytelling", "leadership"], 10, 8, 7, 19, 2, 32, 8, false, 2, 0),
            new("Open Mic Policy Forum", "{club} blends prepared remarks and audience questions in a public forum that keeps the {theme} calendar lively and social.", EventCategory.Social, HallVenues, ["open-mic", "policy"], 10, 10, 7, 21, 2, 64, 0, false, 5, 0)
        ],
        PrivateSeries:
        [
            new("Moderator Run of Show", "Internal staff check-ins for {club} covering speaker order, timers, and volunteer cues at {venue}.", EventCategory.Academic, [new SeedVenueDefinition("Forum Control Room", "St. George Civic Hall Green Room", 43.6673, -79.4011)], ["staff", "moderation"], 5, 5, 14, 18, 1, 10, 0, true)
        ]);

    private static readonly IReadOnlyList<SeedVenueDefinition> HallVenues =
    [
        new("St. George Civic Hall", "St. George Civic Hall", 43.6673, -79.4011),
        new("Bloor Forum Room", "Bloor Forum Room", 43.6689, -79.3981),
        new("Council Steps Studio", "Council Steps Studio", 43.6648, -79.3926)
    ];

    private static readonly IReadOnlyList<SeedVenueDefinition> ForumVenues =
    [
        new("Reference Library Theatre", "Reference Library Theatre", 43.6715, -79.3865),
        new("Annex Commons", "Annex Commons", 43.6708, -79.4054),
        new("University Event Loft", "University Event Loft", 43.6655, -79.3988)
    ];
}
