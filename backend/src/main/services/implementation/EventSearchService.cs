using backend.main.configurations.resource.elasticsearch;
using backend.main.models.documents;
using backend.main.models.enums;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace backend.main.services.implementation
{
    public class EventSearchService : IEventSearchService
    {
        private const string IndexName = "events";

        private readonly ElasticsearchClient? _client;
        private readonly ElasticsearchHealth _health;

        public EventSearchService(ElasticsearchHealth health, ElasticsearchClient? client = null)
        {
            _health = health;
            _client = client;
        }

        public async Task EnsureIndexAsync()
        {
            if (!_health.IsAvailable || _client == null) return;

            var exists = await _client.Indices.ExistsAsync(IndexName);
            if (exists.Exists) return;

            await _client.Indices.CreateAsync(IndexName, c => c
                .Settings(s => s
                    .NumberOfShards(1)
                    .NumberOfReplicas(1)
                )
                .Mappings(m => m
                    .Properties<EventDocument>(p => p
                        .IntegerNumber(f => f.Id)
                        .IntegerNumber(f => f.ClubId)
                        .Text(f => f.Name, t => t
                            .Analyzer("english")
                            .Fields(ff => ff.Keyword("keyword", k => k.IgnoreAbove(256)))
                        )
                        .Text(f => f.Description, t => t.Analyzer("english"))
                        .Text(f => f.Location, t => t.Analyzer("english"))
                        .Boolean(f => f.IsPrivate)
                        .Date(f => f.StartTime)
                        .Date(f => f.EndTime)
                        .Date(f => f.CreatedAt)
                        .Date(f => f.UpdatedAt)
                    )
                )
            );

            Logger.Info("Elasticsearch index 'events' created.");
        }

        public async Task DeleteIndexAsync()
        {
            if (!_health.IsAvailable || _client == null) return;
            await _client.Indices.DeleteAsync(IndexName);
        }

        public async Task IndexAsync(EventDocument document)
        {
            if (!_health.IsAvailable || _client == null) return;
            await _client.IndexAsync(document, i => i.Index(IndexName).Id(document.Id));
        }

        public async Task DeleteAsync(int eventId)
        {
            if (!_health.IsAvailable || _client == null) return;
            await _client.DeleteAsync(IndexName, eventId);
        }

        public async Task BulkIndexAsync(IEnumerable<EventDocument> documents)
        {
            if (!_health.IsAvailable || _client == null) return;

            var response = await _client.BulkAsync(b => b
                .Index(IndexName)
                .IndexMany(documents)
            );

            if (response.Errors)
                Logger.Warn($"Bulk index had errors: {response.ItemsWithErrors.Count()} items failed.");
        }

        public async Task<(List<int> Ids, int TotalCount)> SearchAsync(
            string search,
            bool isPrivate,
            EventStatus? status,
            int page,
            int pageSize)
        {
            if (!_health.IsAvailable || _client == null)
                throw new InvalidOperationException("Elasticsearch is not available.");

            var from = (page - 1) * pageSize;
            var now = DateTime.UtcNow;

            var response = await _client.SearchAsync<EventDocument>(s => s
                .Index(IndexName)
                .From(from)
                .Size(pageSize)
                .Query(q => q
                    .Bool(b =>
                    {
                        b.Filter(BuildFilters(isPrivate, status, now));

                        if (!string.IsNullOrWhiteSpace(search))
                        {
                            b.Must(m => m.MultiMatch(mm => mm
                                .Query(search)
                                .Fields((Fields)new Field[] { (Field)"name^3", (Field)"description", (Field)"location" })
                                .Type(TextQueryType.BestFields)
                                .Fuzziness(new Fuzziness("AUTO"))
                            ));
                        }
                    })
                )
                .Sort(s => s.Field(f => f.CreatedAt, fs => fs.Order(SortOrder.Desc)))
            );

            var ids = response.Hits
                .Where(h => h.Source != null)
                .Select(h => h.Source!.Id)
                .ToList();
            return (ids, (int)response.Total);
        }

        private static Action<QueryDescriptor<EventDocument>>[] BuildFilters(
            bool isPrivate, EventStatus? status, DateTime now)
        {
            var filters = new List<Action<QueryDescriptor<EventDocument>>>
            {
                f => f.Term(t => t.Field(d => d.IsPrivate).Value(isPrivate))
            };

            if (status == EventStatus.Upcoming)
            {
                filters.Add(f => f.Range(r => r
                    .DateRange(dr => dr.Field(d => d.StartTime).Gt(now))
                ));
            }
            else if (status == EventStatus.Ongoing)
            {
                filters.Add(f => f.Range(r => r
                    .DateRange(dr => dr.Field(d => d.StartTime).Lte(now))
                ));
                filters.Add(f => f.Bool(b => b
                    .Should(
                        s => s.Bool(inner => inner
                            .MustNot(mn => mn.Exists(e => e.Field(d => d.EndTime)))
                        ),
                        s => s.Range(r => r
                            .DateRange(dr => dr.Field(d => d.EndTime).Gt(now))
                        )
                    )
                    .MinimumShouldMatch(1)
                ));
            }
            else if (status == EventStatus.Closed)
            {
                filters.Add(f => f.Range(r => r
                    .DateRange(dr => dr.Field(d => d.EndTime).Lte(now))
                ));
            }

            return filters.ToArray();
        }
    }
}
