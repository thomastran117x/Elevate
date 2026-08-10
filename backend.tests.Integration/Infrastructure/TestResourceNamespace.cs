namespace backend.tests.Integration.Infrastructure;

public sealed record TestResourceNamespace(int Slot)
{
    public int RedisDatabase => Slot;

    public string EventsIndex => $"itest_{Slot}_events";

    public string ClubsIndex => $"itest_{Slot}_clubs";

    public string ClubPostsIndex => $"itest_{Slot}_club_posts";

    public string EmailTopic => $"itest-{Slot}-email";

    public string SmsTopic => $"itest-{Slot}-sms";

    public string EmailStatusTopic => $"itest-{Slot}-email-status";

    public string[] ElasticsearchIndices => [EventsIndex, ClubsIndex, ClubPostsIndex];

    public string[] KafkaTopics => [EmailTopic, SmsTopic, EmailStatusTopic];
}
