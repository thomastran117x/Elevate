using backend.main.configurations.resource.elasticsearch;
using backend.main.models.documents;
using backend.main.models.enums;
using backend.main.models.search;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
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
                        .Keyword(f => f.Category)
                        .Text(f => f.VenueName, t => t
                            .Analyzer("english")
                            .Fields(ff => ff.Keyword("keyword", k => k.IgnoreAbove(256)))
                        )
                        .Text(f => f.City, t => t
                            .Analyzer("english")
                            .Fields(ff => ff.Keyword("keyword", k => k.IgnoreAbove(100)))
                        )
                        .Keyword(f => f.Tags)
                        .GeoPoint(f => f.LocationGeo)
                        .IntegerNumber(f => f.RegistrationCount)
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

        public async Task<EventSearchResult> SearchAsync(EventSearchCriteria criteria)
        {
            if (!_health.IsAvailable || _client == null)
                throw new InvalidOperationException("Elasticsearch is not available.");

            var from = (criteria.Page - 1) * criteria.PageSize;
            var now = DateTime.UtcNow;
            var hasGeo = criteria.Lat.HasValue && criteria.Lng.HasValue && criteria.RadiusKm.HasValue;
            var sortByDistance = criteria.SortBy == EventSortBy.Distance
                && criteria.Lat.HasValue && criteria.Lng.HasValue;

            var response = await _client.SearchAsync<EventDocument>(s =>
            {
                s.Index(IndexName)
                    .From(from)
                    .Size(criteria.PageSize)
                    .Query(q => q
                        .Bool(b =>
                        {
                            b.Filter(BuildFilters(criteria, now, hasGeo));

                            if (!string.IsNullOrWhiteSpace(criteria.Query))
                            {
                                b.Must(m => BuildTextQuery(m, criteria.Query, criteria.SortBy));
                            }
                        })
                    );

                ApplySort(s, criteria, sortByDistance);
            });

            var hits = response.Hits
                .Where(h => h.Source != null)
                .Select(h => new EventSearchHit(
                    h.Source!.Id,
                    ExtractDistanceKm(h, sortByDistance)))
                .ToList();

            return new EventSearchResult(hits, (int)response.Total);
        }

        private static double? ExtractDistanceKm(Elastic.Clients.Elasticsearch.Core.Search.Hit<EventDocument> hit, bool sortByDistance)
        {
            if (!sortByDistance || hit.Sort == null) return null;

            foreach (var value in hit.Sort)
            {
                if (value.TryGetDouble(out var d) && d.HasValue)
                    return Math.Round(d.Value / 1000.0, 3);
            }
            return null;
        }

        private static void BuildTextQuery(QueryDescriptor<EventDocument> m, string query, EventSortBy sortBy)
        {
            var multiMatch = new Action<MultiMatchQueryDescriptor<EventDocument>>(mm => mm
                .Query(query)
                .Fields((Fields)new Field[]
                {
                    (Field)"name^3",
                    (Field)"tags^2.5",
                    (Field)"venueName^2",
                    (Field)"description",
                    (Field)"city",
                    (Field)"location"
                })
                .Type(TextQueryType.BestFields)
                .Fuzziness(new Fuzziness("AUTO"))
            );

            if (sortBy == EventSortBy.Relevance)
            {
                m.FunctionScore(fs => fs
                    .Query(qq => qq.MultiMatch(multiMatch))
                    .Functions(fn => fn
                        .FieldValueFactor(fvf => fvf
                            .Field(d => d.RegistrationCount)
                            .Modifier(FieldValueFactorModifier.Log1p)
                            .Factor(1.0)
                            .Missing(0.0)
                        )
                    )
                    .BoostMode(FunctionBoostMode.Sum)
                );
            }
            else
            {
                m.MultiMatch(multiMatch);
            }
        }

        private static Action<QueryDescriptor<EventDocument>>[] BuildFilters(
            EventSearchCriteria criteria, DateTime now, bool hasGeo)
        {
            var filters = new List<Action<QueryDescriptor<EventDocument>>>
            {
                f => f.Term(t => t.Field(d => d.IsPrivate).Value(criteria.IsPrivate))
            };

            if (criteria.Category.HasValue)
            {
                var categoryValue = criteria.Category.Value.ToString();
                filters.Add(f => f.Term(t => t.Field(d => d.Category).Value(categoryValue)));
            }

            if (criteria.Tags != null && criteria.Tags.Count > 0)
            {
                var tagValues = criteria.Tags
                    .Select(t => (FieldValue)t!)
                    .ToArray();
                filters.Add(f => f.Terms(t => t
                    .Field(d => d.Tags)
                    .Terms(new TermsQueryField(tagValues))
                ));
            }

            if (!string.IsNullOrWhiteSpace(criteria.City))
            {
                var cityValue = criteria.City.Trim();
                filters.Add(f => f.Term(t => t.Field("city.keyword").Value(cityValue)));
            }

            if (hasGeo)
            {
                var lat = criteria.Lat!.Value;
                var lng = criteria.Lng!.Value;
                var radiusKm = criteria.RadiusKm!.Value;
                filters.Add(f => f.GeoDistance(g => g
                    .Field(d => d.LocationGeo)
                    .Distance($"{radiusKm}km")
                    .Location(GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = lat, Lon = lng }))
                ));
            }

            if (criteria.Status == EventStatus.Upcoming)
            {
                filters.Add(f => f.Range(r => r
                    .DateRange(dr => dr.Field(d => d.StartTime).Gt(now))
                ));
            }
            else if (criteria.Status == EventStatus.Ongoing)
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
            else if (criteria.Status == EventStatus.Closed)
            {
                filters.Add(f => f.Range(r => r
                    .DateRange(dr => dr.Field(d => d.EndTime).Lte(now))
                ));
            }

            return filters.ToArray();
        }

        private static void ApplySort(
            SearchRequestDescriptor<EventDocument> s,
            EventSearchCriteria criteria,
            bool sortByDistance)
        {
            if (sortByDistance)
            {
                var lat = criteria.Lat!.Value;
                var lng = criteria.Lng!.Value;
                s.Sort(so => so.GeoDistance(gd => gd
                    .Field(d => d.LocationGeo!)
                    .Location(new[]
                    {
                        GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = lat, Lon = lng })
                    })
                    .Order(SortOrder.Asc)
                    .Unit(Elastic.Clients.Elasticsearch.DistanceUnit.Meters)
                ));
                return;
            }

            switch (criteria.SortBy)
            {
                case EventSortBy.Date:
                    s.Sort(so => so.Field(f => f.StartTime, fs => fs.Order(SortOrder.Asc)));
                    break;
                case EventSortBy.Popularity:
                    s.Sort(so => so
                        .Field(f => f.RegistrationCount, fs => fs.Order(SortOrder.Desc))
                        .Field(f => f.CreatedAt, fs => fs.Order(SortOrder.Desc))
                    );
                    break;
                case EventSortBy.Relevance:
                default:
                    s.Sort(so => so
                        .Score(new ScoreSort { Order = SortOrder.Desc })
                        .Field(f => f.CreatedAt, fs => fs.Order(SortOrder.Desc))
                    );
                    break;
            }
        }
    }
}
