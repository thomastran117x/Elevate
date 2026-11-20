namespace backend.main.features.clubs.moderation;

public static class ClubModerationActionTypes
{
    public const string Remove = "remove";
    public const string Edit = "edit";
    public const string Pin = "pin";
    public const string Unpin = "unpin";
}

public static class ClubModerationContentTypes
{
    public const string Post = "Post";
    public const string Discussion = "Discussion";
    public const string Reply = "Reply";
    public const string Comment = "Comment";
    public const string Review = "Review";
}

public static class ClubModerationRoles
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Volunteer = "Volunteer";
    public const string Admin = "Admin";
}
