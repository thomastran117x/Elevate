using backend.main.application.security;

namespace backend.main.seeders;

public static class UserSeedCatalog
{
    public static IReadOnlyList<SeedUserDefinition> All { get; } =
    [
        new($"harbour.owner{SeedCatalogConstants.SeedEmailDomain}", "harbourowner", "Maya Chen", AuthRoles.Organizer),
        new($"harbour.manager{SeedCatalogConstants.SeedEmailDomain}", "harbourmanager", "Jordan Bell", AuthRoles.Organizer),
        new($"harbour.volunteer{SeedCatalogConstants.SeedEmailDomain}", "harbourvolunteer", "Noah Patel", AuthRoles.Volunteer),

        new($"summit.owner{SeedCatalogConstants.SeedEmailDomain}", "summitowner", "Olivia Hart", AuthRoles.Organizer),
        new($"summit.manager{SeedCatalogConstants.SeedEmailDomain}", "summitmanager", "Isaac Romero", AuthRoles.Organizer),
        new($"summit.volunteer{SeedCatalogConstants.SeedEmailDomain}", "summitvolunteer", "Leah Brooks", AuthRoles.Volunteer),

        new($"builders.owner{SeedCatalogConstants.SeedEmailDomain}", "buildersowner", "Avery Singh", AuthRoles.Organizer),
        new($"builders.manager{SeedCatalogConstants.SeedEmailDomain}", "buildersmanager", "Marcus Lin", AuthRoles.Organizer),
        new($"builders.volunteer{SeedCatalogConstants.SeedEmailDomain}", "buildersvolunteer", "Priya Shah", AuthRoles.Volunteer),

        new($"speakers.owner{SeedCatalogConstants.SeedEmailDomain}", "speakersowner", "Sofia Bennett", AuthRoles.Organizer),
        new($"speakers.manager{SeedCatalogConstants.SeedEmailDomain}", "speakersmanager", "Caleb Nguyen", AuthRoles.Organizer),
        new($"speakers.volunteer{SeedCatalogConstants.SeedEmailDomain}", "speakersvolunteer", "Amara Jones", AuthRoles.Volunteer),

        new($"lantern.owner{SeedCatalogConstants.SeedEmailDomain}", "lanternowner", "Ella Morrison", AuthRoles.Organizer),
        new($"lantern.manager{SeedCatalogConstants.SeedEmailDomain}", "lanternmanager", "Henry Kim", AuthRoles.Organizer),
        new($"lantern.volunteer{SeedCatalogConstants.SeedEmailDomain}", "lanternvolunteer", "Zoe Collins", AuthRoles.Volunteer),

        new($"makers.owner{SeedCatalogConstants.SeedEmailDomain}", "makersowner", "Lucas Rivera", AuthRoles.Organizer),
        new($"makers.manager{SeedCatalogConstants.SeedEmailDomain}", "makersmanager", "Grace Turner", AuthRoles.Organizer),
        new($"makers.volunteer{SeedCatalogConstants.SeedEmailDomain}", "makersvolunteer", "Ethan Flores", AuthRoles.Volunteer),

        new($"mosaic.owner{SeedCatalogConstants.SeedEmailDomain}", "mosaicowner", "Nina Laurent", AuthRoles.Organizer),
        new($"mosaic.manager{SeedCatalogConstants.SeedEmailDomain}", "mosaicmanager", "Owen Price", AuthRoles.Organizer),
        new($"mosaic.volunteer{SeedCatalogConstants.SeedEmailDomain}", "mosaicvolunteer", "Ruby Stewart", AuthRoles.Volunteer),

        new($"rhythm.owner{SeedCatalogConstants.SeedEmailDomain}", "rhythmowner", "Mateo Alvarez", AuthRoles.Organizer),
        new($"rhythm.manager{SeedCatalogConstants.SeedEmailDomain}", "rhythmmanager", "Chloe Adams", AuthRoles.Organizer),
        new($"rhythm.volunteer{SeedCatalogConstants.SeedEmailDomain}", "rhythmvolunteer", "Jasmine Reid", AuthRoles.Volunteer),

        new($"pixel.owner{SeedCatalogConstants.SeedEmailDomain}", "pixelowner", "Theo Carter", AuthRoles.Organizer),
        new($"pixel.manager{SeedCatalogConstants.SeedEmailDomain}", "pixelmanager", "Mila Foster", AuthRoles.Organizer),
        new($"pixel.volunteer{SeedCatalogConstants.SeedEmailDomain}", "pixelvolunteer", "Cole Murphy", AuthRoles.Volunteer),

        new($"kitchen.owner{SeedCatalogConstants.SeedEmailDomain}", "kitchenowner", "Hannah Doyle", AuthRoles.Organizer),
        new($"kitchen.manager{SeedCatalogConstants.SeedEmailDomain}", "kitchenmanager", "Daniel Park", AuthRoles.Organizer),
        new($"kitchen.volunteer{SeedCatalogConstants.SeedEmailDomain}", "kitchenvolunteer", "Aisha Khan", AuthRoles.Volunteer)
    ];
}
