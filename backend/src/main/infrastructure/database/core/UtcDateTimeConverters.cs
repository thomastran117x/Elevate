using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace backend.main.infrastructure.database.core
{
    /// <summary>
    /// Normalizes <see cref="DateTime"/> values to UTC on the way into PostgreSQL.
    /// <para>
    /// Npgsql maps <see cref="DateTime"/> to <c>timestamp with time zone</c> and throws when the
    /// value's <see cref="DateTimeKind"/> is not <see cref="DateTimeKind.Utc"/>. MySQL's zoneless
    /// <c>datetime(6)</c> accepted any kind, and request payloads still bind
    /// <see cref="DateTimeKind.Local"/> (for offsets such as <c>+11:00</c>) and
    /// <see cref="DateTimeKind.Unspecified"/> (for bare timestamps) values.
    /// </para>
    /// <para>
    /// <see cref="DateTimeKind.Unspecified"/> is treated as <em>already UTC</em> rather than being
    /// run through <see cref="DateTime.ToUniversalTime"/>. Every write path in this codebase uses
    /// <see cref="DateTime.UtcNow"/>, so unspecified values are UTC by convention; converting them
    /// as server-local would silently shift stored instants by the host's offset.
    /// </para>
    /// </summary>
    public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                value => ToUtc(value),
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
        {
        }

        internal static DateTime ToUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// Nullable counterpart of <see cref="UtcDateTimeConverter"/>.
    /// </summary>
    public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                value => value.HasValue ? UtcDateTimeConverter.ToUtc(value.Value) : null,
                value => value.HasValue
                    ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                    : null)
        {
        }
    }
}
