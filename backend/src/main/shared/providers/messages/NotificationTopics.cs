using backend.main.application.environment;

namespace backend.main.shared.providers.messages
{
    public static class NotificationTopics
    {
        public static string Email => EnvironmentSetting.EmailTopic;
        public static string Sms => EnvironmentSetting.SmsTopic;
    }
}
