using backend.main.infrastructure.elasticsearch;
using backend.main.shared.utilities.logger;

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Analysis;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace backend.main.features.clubs.search
{
    public class ClubSearchService : IClubSearchService
    {
        private const string IndexName = "clubs";

        private readonly ElasticsearchClient? _client;
        private readonly ElasticsearchCircuitBreaker _circuitBreaker;
        private readonly ElasticsearchHealth _health;
        private readonly SemaphoreSlim _indexLock = new(1, 1);
        private bool _indexEnsured;

        public ClubSearchService(
            ElasticsearchCircuitBreaker circuitBreaker,
            ElasticsearchHealth health,
            ElasticsearchClient? client = null)
        {
            _circuitBreaker = circuitBreaker;
            _health = health;
            _client = client;
        }

        public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_indexEnsured)
                return;

            var client = GetRequiredClient();

            await _indexLock.WaitAsync(cancellationToken);
            try
            {
                if (_indexEnsured)
                    return;

                var exists = await _circuitBreaker.ExecuteAsync(
                    () => client.Indices.ExistsAsync(IndexName),
                    $"{IndexName} index existence check");

                if (!exists.Exists)
                {
                    await _circuitBreaker.ExecuteAsync(
                        () => client.Indices.CreateAsync(IndexName, c => c
                            .Settings(s => s
                                .NumberOfShards(1)
                                .NumberOfReplicas(1)
                                .Analysis(a => a
                                    .TokenFilters(tf => tf
                                        .EdgeNGram("autocomplete_filter", e => e
                                            .MinGram(2)
                                            .MaxGram(20)
                                        )
                                    )
                                    .Analyzers(an => an
                                        .Custom("autocomplete_index", ca => ca
                                            .Tokenizer("standard")
                                            .Filter(["lowercase", "autocomplete_filter"])
                                        )
                                        .Custom("autocomplete_search", ca => ca
                                            .Tokenizer("standard")
                                            .Filter(["lowercase"])
                                        )
                                    )
                                )
                            )
                            .Mappings(m => m
                                .Properties<ClubDocument>(p => p
                                    .IntegerNumber(f => f.Id)
                                    .Text(f => f.Name, t => t
                                        .Analyzer("english")
                                        .Fields(ff => ff
                                            .Keyword("keyword", k => k.IgnoreAbove(256))
                                            .Text("autocomplete", ac => ac
                                                .Analyzer("autocomplete_index")
                                                .SearchAnalyzer("autocomplete_search")
                                            )
                                        )
                                    )
                                    .Text(f => f.Description, t => t
                                        .Analyzer("english")
                                        .Fields(ff => ff
                                            .Text("autocomplete", ac => ac
                                                .Analyzer("autocomplete_index")
                                                .SearchAnalyzer("autocomplete_search")
                                            )
                                        )
                                    )
                                    .Keyword(f => f.ClubType)
                                    .Text(f => f.Location, t => t
                                        .Analyzer("english")
                                        .Fields(ff => ff
                                            .Keyword("keyword", k => k.IgnoreAbove(256))
                                            .Text("autocomplete", ac => ac
                                                .Analyzer("autocomplete_index")
                                                .SearchAnalyzer("autocomplete_search")
                                            )
                                        )
                                    )
                                    .Keyword(f => f.WebsiteUrl)
                                    .Keyword(f => f.Phone)
                                    .Keyword(f => f.Email)
                                    .IntegerNumber(f => f.MemberCount)
                                    .DoubleNumber(f => f.Rating)
                                    .Boolean(f => f.IsPrivate)
                                    .Date(f => f.CreatedAt)
                                    .Date(f => f.UpdatedAt)
                                )
                            )
                        ),
                        $"{IndexName} index creation");

                    Logger.Info("Elasticsearch index 'clubs' created.");
                }

                _indexEnsured = true;
            }
            catch (ElasticsearchServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ElasticsearchUnavailableException(
                    "Failed to verify the Elasticsearch clubs index.",
                    ex);
            }
            finally
            {
                _indexLock.Release();
            }
        }

        public async Task DeleteIndexAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = GetWritableClientOrNull();
            if (client == null)
                return;

            try
            {
                await _circuitBreaker.ExecuteAsync(
                    () => client.Indices.DeleteAsync(IndexName),
                    $"{IndexName} index deletion");
                _indexEnsured = false;
            }
            catch (Exception ex)
            {
                throw new ElasticsearchUnavailableException(
                    "Failed to delete the Elasticsearch clubs index.",
                    ex);
            }
        }

        public async Task IndexAsync(ClubDocument document, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = GetWritableClientOrNull();
            if (client == null)
                return;

            await EnsureIndexAsync(cancellationToken);

            try
            {
                await _circuitBreaker.ExecuteAsync(
                    () => client.IndexAsync(document, i => i.Index(IndexName).Id(document.Id)),
                    $"{IndexName} document indexing");
            }
            catch (Exception ex)
            {
                throw new ElasticsearchUnavailableException(
                    $"Failed to index club document {document.Id}.",
                    ex);
            }
        }

        public async Task DeleteAsync(int clubId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = GetWritableClientOrNull();
            if (client == null)
                return;

            try
            {
                await _circuitBreaker.ExecuteAsync(
                    () => client.DeleteAsync(IndexName, clubId),
                    $"{IndexName} document deletion");
            }
            catch (Exception ex)
            {
                throw new ElasticsearchUnavailableException(
                    $"Failed to delete club document {clubId}.",
                    ex);
            }
        }

        public async Task BulkIndexAsync(IEnumerable<ClubDocument> documents, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = GetWritableClientOrNull();
            if (client == null)
                return;

            await EnsureIndexAsync(cancellationToken);

            try
            {
                var response = await _circuitBreaker.ExecuteAsync(
                    () => client.BulkAsync(b => b
                        .Index(IndexName)
                        .IndexMany(documents)
                    ),
                    $"{IndexName} bulk indexing");

                if (response.Errors)
                    Logger.Warn($"Bulk index had errors: {response.ItemsWithErrors.Count()} items failed.");
            }
            catch (Exception ex)
            {
                throw new ElasticsearchUnavailableException(
                    "Failed to bulk index club documents.",
                    ex);
            }
        }

        public async Task<ClubSearchResult> SearchAsync(ClubSearchCriteria criteria)
        {
            var client = GetRequiredClient();

            await EnsureIndexAsync();

            var from = (criteria.Page - 1) * criteria.PageSize;

            try
            {
                var response = await _circuitBreaker.ExecuteAsync(
                    () => client.SearchAsync<ClubDocument>(s =>
                    {
                        s.Index(IndexName)
                            .From(from)
                            .Size(criteria.PageSize)
                            .Query(q => q
                                .Bool(b =>
                                {
                                    b.Filter(BuildFilters(criteria));

                                    if (!string.IsNullOrWhiteSpace(criteria.Query))
                                    {
                                        b.Must(m => BuildTextQuery(m, criteria.Query!, criteria.SortBy));
                                    }
                                })
                            );

                        ApplySort(s, criteria);
                    }),
                    $"{IndexName} search");

                var hits = response.Hits
                    .Where(h => h.Source != null)
                    .Select(h => new ClubSearchHit(h.Source!.Id))
                    .ToList();

                return new ClubSearchResult(hits, (int)response.Total);
            }
            catch (ElasticsearchServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ElasticsearchUnavailableException(
                    "Elasticsearch search failed for clubs.",
                    ex);
            }
        }

        private ElasticsearchClient GetRequiredClient()
        {
            if (_client != null)
                return _client;

            if (!_health.IsConfigured)
                throw new ElasticsearchDisabledException(
                    "Elasticsearch is disabled because ELASTICSEARCH_URL is not configured.");

            if (_health.Failure != null)
                throw new ElasticsearchConfigurationException(
                    "Elasticsearch is configured but failed to initialize.",
                    _health.Failure);

            throw new ElasticsearchUnavailableException("Elasticsearch client is unavailable.");
        }

        private ElasticsearchClient? GetWritableClientOrNull()
        {
            if (_client != null)
                return _client;

            if (!_health.IsConfigured)
                return null;

            if (_health.Failure != null)
                throw new ElasticsearchConfigurationException(
                    "Elasticsearch is configured but failed to initialize.",
                    _health.Failure);

            throw new ElasticsearchUnavailableException("Elasticsearch client is unavailable.");
        }

        private static void BuildTextQuery(QueryDescriptor<ClubDocument> m, string query, ClubSortBy sortBy)
        {
            var multiMatch = new Action<MultiMatchQueryDescriptor<ClubDocument>>(mm => mm
                .Query(query)
                .Fields((Fields)new Field[]
                {
                    (Field)"name^4",
                    (Field)"name.autocomplete^2.5",
                    (Field)"description^2",
                    (Field)"description.autocomplete^1",
                    (Field)"location^1.5",
                    (Field)"location.autocomplete^0.75"
                })
                .Type(TextQueryType.BestFields)
                .Fuzziness(new Fuzziness("AUTO"))
            );

            if (sortBy == ClubSortBy.Relevance)
            {
                m.FunctionScore(fs => fs
                    .Query(qq => qq.MultiMatch(multiMatch))
                    .Functions(fn => fn
                        .FieldValueFactor(fvf => fvf
                            .Field(d => d.MemberCount)
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

        private static Action<QueryDescriptor<ClubDocument>>[] BuildFilters(ClubSearchCriteria criteria)
        {
            var filters = new List<Action<QueryDescriptor<ClubDocument>>>
            {
                f => f.Term(t => t.Field(d => d.IsPrivate).Value(false))
            };

            if (criteria.ClubType.HasValue)
            {
                var clubTypeValue = criteria.ClubType.Value.ToString();
                filters.Add(f => f.Term(t => t.Field(d => d.ClubType).Value(clubTypeValue)));
            }

            return filters.ToArray();
        }

        private static void ApplySort(SearchRequestDescriptor<ClubDocument> s, ClubSearchCriteria criteria)
        {
            switch (criteria.SortBy)
            {
                case ClubSortBy.Newest:
                    s.Sort(so => so
                        .Field(f => f.CreatedAt, fs => fs.Order(SortOrder.Desc))
                        .Field(f => f.Id, fs => fs.Order(SortOrder.Asc)));
                    break;
                case ClubSortBy.Members:
                    s.Sort(so => so
                        .Field(f => f.MemberCount, fs => fs.Order(SortOrder.Desc))
                        .Field(f => f.CreatedAt, fs => fs.Order(SortOrder.Desc))
                        .Field(f => f.Id, fs => fs.Order(SortOrder.Asc)));
                    break;
                case ClubSortBy.Rating:
                    s.Sort(so => so
                        .Field(f => f.Rating, fs => fs.Order(SortOrder.Desc).Missing("_last"))
                        .Field(f => f.MemberCount, fs => fs.Order(SortOrder.Desc))
                        .Field(f => f.CreatedAt, fs => fs.Order(SortOrder.Desc))
                        .Field(f => f.Id, fs => fs.Order(SortOrder.Asc)));
                    break;
                case ClubSortBy.Relevance:
                default:
                    s.Sort(so => so
                        .Score(new ScoreSort { Order = SortOrder.Desc })
                        .Field(f => f.CreatedAt, fs => fs.Order(SortOrder.Desc))
                        .Field(f => f.Id, fs => fs.Order(SortOrder.Asc)));
                    break;
            }
        }
    }
}
