namespace backend.main.features.profile
{
    /// <summary>
    /// The cache key for a user's profile, shared by every service that reads or invalidates it.
    /// </summary>
    /// <remarks>
    /// Versioned: the cached <see cref="User"/> gained <see cref="User.LinkedProviders"/>, and an
    /// entry written before that would deserialize as linking nothing. Bump the version whenever
    /// the cached shape changes in a way old entries cannot represent.
    /// </remarks>
    public static class UserCacheKeys
    {
        public static string For(int userId) => $"user:v2:{userId}";
    }
}
