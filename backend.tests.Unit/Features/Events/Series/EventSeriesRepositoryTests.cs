using backend.main.features.events;
using backend.main.features.events.series;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events.Series;

public class EventSeriesRepositoryTests
{
    [Fact]
    public async Task GetByIdAsync_ShouldReturnTheSeries_OrNullWhenMissing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var series = await fixture.AddSeriesAsync();

        (await fixture.Repository.GetByIdAsync(series.Id))!.TimeZoneId.Should().Be("Australia/Sydney");
        (await fixture.Repository.GetByIdAsync(9999)).Should().BeNull();
    }

    [Fact]
    public async Task GetByClubAsync_ShouldPageNewestFirst_AndReportTheTotal()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.AddSeriesAsync(createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = await fixture.AddSeriesAsync(createdAt: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var (page, totalCount) = await fixture.Repository.GetByClubAsync(Fixture.ClubId, 1, 1);

        totalCount.Should().Be(2);
        page.Should().ContainSingle().Which.Id.Should().Be(newer.Id);
    }

    [Fact]
    public async Task GetByClubAsync_ShouldIgnoreOtherClubs()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddSeriesAsync();

        var (page, totalCount) = await fixture.Repository.GetByClubAsync(Fixture.OtherClubId, 1, 20);

        page.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOccurrencesAsync_ShouldReturnOccurrencesInScheduleOrderWithImages()
    {
        await using var fixture = await Fixture.CreateAsync();
        var series = await fixture.AddSeriesAsync();

        await fixture.AddOccurrenceAsync(series.Id, index: 2);
        await fixture.AddOccurrenceAsync(series.Id, index: 0, imageUrl: "https://cdn.test/a.png");
        await fixture.AddOccurrenceAsync(series.Id, index: 1);

        var occurrences = await fixture.Repository.GetOccurrencesAsync(series.Id);

        occurrences.Select(o => o.OccurrenceIndex).Should().Equal(0, 1, 2);
        occurrences[0].Images.Should().ContainSingle();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const int ClubId = 4;
        public const int OtherClubId = 5;

        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db
        {
            get;
        }
        public EventSeriesRepository Repository
        {
            get;
        }

        private Fixture(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
            Repository = new EventSeriesRepository(db);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var db = new AppDatabaseContext(
                new DbContextOptionsBuilder<AppDatabaseContext>().UseSqlite(connection).Options);

            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new backend.main.features.profile.User
            {
                Id = 7,
                Email = "organizer@test.local",
                Usertype = "Organizer"
            });

            foreach (var id in new[] { ClubId, OtherClubId })
            {
                db.Clubs.Add(new backend.main.features.clubs.Club
                {
                    Id = id,
                    UserId = 7,
                    Name = $"Club {id}",
                    Description = "A club used by the series repository tests.",
                    Clubtype = backend.main.features.clubs.ClubType.Gaming,
                    ClubImage = "https://cdn.test/clubs/gaming.png"
                });
            }

            await db.SaveChangesAsync();

            return new Fixture(connection, db);
        }

        public async Task<EventSeries> AddSeriesAsync(DateTime? createdAt = null)
        {
            var series = new EventSeries
            {
                ClubId = ClubId,
                Frequency = EventRecurrenceFrequency.Weekly,
                Interval = 1,
                TimeZoneId = "Australia/Sydney",
                FirstOccurrenceLocalStart = new DateTime(2026, 6, 1, 19, 0, 0, DateTimeKind.Unspecified),
                DurationMinutes = 120,
                EndMode = EventRecurrenceEndMode.Count,
                OccurrenceCount = 4,
                CreatedByUserId = 7,
                CreatedAt = createdAt ?? new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = createdAt ?? new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            Db.EventSeries.Add(series);
            await Db.SaveChangesAsync();

            return series;
        }

        public async Task<EventEntity> AddOccurrenceAsync(int seriesId, int index, string? imageUrl = null)
        {
            var occurrence = new EventEntity
            {
                Name = $"Occurrence {index}",
                Description = "An occurrence generated by a recurrence series.",
                Location = "Studio 1",
                ClubId = ClubId,
                LifecycleState = EventLifecycleState.Draft,
                SeriesId = seriesId,
                OccurrenceIndex = index,
                TimeZoneId = "Australia/Sydney",
                StartTime = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc).AddDays(index * 7),
                CurrentVersionNumber = 1
            };

            Db.Events.Add(occurrence);
            await Db.SaveChangesAsync();

            if (imageUrl is not null)
            {
                Db.EventImages.Add(new backend.main.features.events.images.EventImage
                {
                    EventId = occurrence.Id,
                    ImageUrl = imageUrl,
                    SortOrder = 0
                });

                await Db.SaveChangesAsync();
            }

            return occurrence;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
