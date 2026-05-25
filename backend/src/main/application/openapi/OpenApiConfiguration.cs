using System.Text.Json.Serialization.Metadata;

using backend.main.application.security;
using backend.main.shared.responses;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace backend.main.application.openapi
{
    /// <summary>
    /// Registers and customizes the application's generated OpenAPI document.
    /// </summary>
    public static class OpenApiConfiguration
    {
        private const string BearerSchemeName = "bearerAuth";
        private static readonly HashSet<string> CsrfProtectedAuthPostPaths =
        [
            "/api/auth/login",
            "/api/auth/signup",
            "/api/auth/verify",
            "/api/auth/verify/otp",
            "/api/auth/google",
            "/api/auth/google/code",
            "/api/auth/microsoft",
            "/api/auth/oauth/complete",
            "/api/auth/device/verify",
            "/api/auth/refresh",
            "/api/auth/logout",
            "/api/auth/forgot-password",
            "/api/auth/change-password"
        ];

        public static IServiceCollection AddAppOpenApi(this IServiceCollection services)
        {
            services.AddOpenApi(OpenApiDocumentMode.DocumentName, options =>
            {
                options.CreateSchemaReferenceId = CreateSchemaReferenceId;
                options.ShouldInclude = description =>
                {
                    var normalizedPath = NormalizeRelativePath(description.RelativePath);
                    if (normalizedPath.StartsWith("/openapi/", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    var includePrefix = Environment.GetEnvironmentVariable(
                        OpenApiDocumentMode.IncludePrefixEnvironmentVariable
                    );

                    if (string.IsNullOrWhiteSpace(includePrefix))
                    {
                        return true;
                    }

                    if (includePrefix.StartsWith('='))
                    {
                        return string.Equals(
                            normalizedPath,
                            includePrefix[1..],
                            StringComparison.OrdinalIgnoreCase
                        );
                    }

                    if (includePrefix.Contains('*'))
                    {
                        var fragment = includePrefix.Replace("*", string.Empty);
                        return normalizedPath.Contains(fragment, StringComparison.OrdinalIgnoreCase);
                    }

                    return normalizedPath.StartsWith(includePrefix, StringComparison.OrdinalIgnoreCase);
                };
                options.AddDocumentTransformer(ApplyDocumentTransformAsync);
                options.AddOperationTransformer(ApplyOperationTransformAsync);
            });

            return services;
        }

        public static IEndpointRouteBuilder MapAppOpenApi(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapOpenApi(OpenApiDocumentMode.JsonRoutePattern).ExcludeFromDescription();
            endpoints.MapGet(
                OpenApiDocumentMode.YamlRoutePattern,
                async Task<IResult> (
                    string documentName,
                    IServiceProvider services
                ) =>
                {
                    var json = await GenerateOpenApiJsonAsync(services, documentName);
                    var yaml = OpenApiYamlSerializer.ConvertJsonDocumentToYaml(json);
                    return Results.Text(yaml, "application/yaml");
                }
            ).ExcludeFromDescription();

            return endpoints;
        }

        private static Task ApplyDocumentTransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext _,
            CancellationToken __
        )
        {
            document.Info = new OpenApiInfo
            {
                Title = "EventXperience Backend API",
                Version = OpenApiDocumentMode.DocumentName,
                Description =
                    "Generated API reference for the EventXperience backend. Responses use the shared ApiResponse envelope unless noted otherwise."
            };
            document.Servers = [];

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description =
                    "Send the access token in the Authorization header as `Bearer {token}` for protected API routes."
            };

            document.Tags =
            [
                new OpenApiTag
                {
                    Name = "auth",
                    Description = "Authentication, session, CSRF, verification, and password recovery flows."
                },
                new OpenApiTag
                {
                    Name = "clubs",
                    Description = "Club management, discovery, membership, reviews, staff, and club content."
                },
                new OpenApiTag
                {
                    Name = "events",
                    Description = "Event CRUD, search, drafts, invitations, registrations, analytics, and assets."
                },
                new OpenApiTag
                {
                    Name = "payments",
                    Description = "Stripe checkout, payment retrieval, refunds, and webhook processing."
                },
                new OpenApiTag
                {
                    Name = "users",
                    Description = "User-centric resources such as followed clubs, reviews, and registrations."
                },
                new OpenApiTag
                {
                    Name = "admin",
                    Description = "Administrative endpoints for moderation, user management, and reindex operations."
                }
            ];

            return Task.CompletedTask;
        }

        private static Task ApplyOperationTransformAsync(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            CancellationToken _
        )
        {
            var relativePath = NormalizeRelativePath(context.Description.RelativePath);
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;

            ApplyOperationId(operation, context);
            ApplyTags(operation, context, relativePath);
            ApplyAuthorization(operation, metadata);
            ApplyStandardErrors(operation, metadata);
            ApplyCsrfDocumentation(operation, relativePath);
            ApplySpecialHeaders(operation, relativePath);
            ApplySpecialResponses(operation, relativePath);
            ApplyOperationDescriptions(operation, relativePath);

            return Task.CompletedTask;
        }

        private static void ApplyOperationId(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context
        )
        {
            var controller = context.Description.ActionDescriptor.RouteValues.TryGetValue(
                "controller",
                out var controllerName
            )
                ? controllerName
                : "api";
            var action = context.Description.ActionDescriptor.RouteValues.TryGetValue(
                "action",
                out var actionName
            )
                ? actionName
                : context.Description.HttpMethod ?? "operation";

            operation.OperationId = $"{SanitizeToken(controller)}_{SanitizeToken(action)}";
        }

        private static void ApplyTags(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            string relativePath
        )
        {
            var controller = context.Description.ActionDescriptor.RouteValues.TryGetValue(
                "controller",
                out var controllerName
            )
                ? controllerName?.ToLowerInvariant()
                : null;

            var tagName = relativePath switch
            {
                var path when path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase) => "auth",
                var path when path.StartsWith("/api/payments", StringComparison.OrdinalIgnoreCase) => "payments",
                var path when path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase) => "admin",
                var path when path.StartsWith("/api/events", StringComparison.OrdinalIgnoreCase) => "events",
                var path when path.StartsWith("/api/clubs", StringComparison.OrdinalIgnoreCase) => "clubs",
                var path when path.StartsWith("/api/users", StringComparison.OrdinalIgnoreCase)
                    && controller is "useradmin" => "admin",
                var path when path.StartsWith("/api/users", StringComparison.OrdinalIgnoreCase) => "users",
                _ => controller ?? "api"
            };

            operation.Tags = [new OpenApiTag { Name = tagName }];
        }

        private static void ApplyAuthorization(
            OpenApiOperation operation,
            IList<object> metadata
        )
        {
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                return;
            }

            if (!metadata.OfType<IAuthorizeData>().Any())
            {
                return;
            }

            operation.Security ??= [];
            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    [
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = BearerSchemeName
                            }
                        }
                    ] = []
                }
            );
        }

        private static void ApplyStandardErrors(
            OpenApiOperation operation,
            IList<object> metadata
        )
        {
            operation.Responses ??= new OpenApiResponses();

            AddResponseIfMissing(
                operation,
                "400",
                "Bad request, validation failure, or CSRF failure.",
                typeof(ApiResponse<object?>)
            );
            AddResponseIfMissing(
                operation,
                "500",
                "Unexpected server error.",
                typeof(ApiResponse<object?>)
            );

            if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
            {
                AddResponseIfMissing(
                    operation,
                    "401",
                    "Authentication is required or the access token is invalid/expired.",
                    typeof(ApiResponse<object?>)
                );
                AddResponseIfMissing(
                    operation,
                    "403",
                    "The authenticated user does not have permission to perform this action.",
                    typeof(ApiResponse<object?>)
                );
            }
        }

        private static void ApplyCsrfDocumentation(OpenApiOperation operation, string relativePath)
        {
            if (!CsrfProtectedAuthPostPaths.Contains(relativePath))
            {
                return;
            }

            operation.Parameters ??= [];
            if (!operation.Parameters.Any(p =>
                    p.In == ParameterLocation.Header
                    && string.Equals(p.Name, CsrfConfiguration.CsrfHeaderName, StringComparison.OrdinalIgnoreCase)))
            {
                operation.Parameters.Add(
                    new OpenApiParameter
                    {
                        Name = CsrfConfiguration.CsrfHeaderName,
                        In = ParameterLocation.Header,
                        Required = true,
                        Description =
                            $"CSRF token obtained from `/api/auth/csrf`. Browser clients must send the `{CsrfConfiguration.CsrfCookieName}` cookie and mirror its value in this header.",
                        Schema = new OpenApiSchema { Type = "string" }
                    }
                );
            }
        }

        private static void ApplySpecialHeaders(OpenApiOperation operation, string relativePath)
        {
            operation.Parameters ??= [];

            if (string.Equals(relativePath, "/api/payments/{eventId}", StringComparison.OrdinalIgnoreCase))
            {
                operation.Parameters.Add(
                    new OpenApiParameter
                    {
                        Name = "Idempotency-Key",
                        In = ParameterLocation.Header,
                        Required = false,
                        Description = "Optional client-generated idempotency key used to safely retry checkout session creation.",
                        Schema = new OpenApiSchema { Type = "string" }
                    }
                );
            }

            if (string.Equals(relativePath, "/api/payments/webhook", StringComparison.OrdinalIgnoreCase))
            {
                operation.Parameters.Add(
                    new OpenApiParameter
                    {
                        Name = "Stripe-Signature",
                        In = ParameterLocation.Header,
                        Required = true,
                        Description = "Stripe webhook signature header used to validate the raw request payload.",
                        Schema = new OpenApiSchema { Type = "string" }
                    }
                );
            }

            if (string.Equals(relativePath, "/api/auth/api/refresh", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relativePath, "/api/auth/api/logout", StringComparison.OrdinalIgnoreCase))
            {
                AddHeaderIfMissing(
                    operation,
                    HttpUtility.RefreshTokenHeaderName,
                    "Optional refresh token header for API-token clients. If omitted, the request body value is used."
                );
                AddHeaderIfMissing(
                    operation,
                    HttpUtility.SessionBindingHeaderName,
                    "Optional session-binding header for API-token clients. If omitted, the request body value is used."
                );
            }
        }

        private static void ApplySpecialResponses(OpenApiOperation operation, string relativePath)
        {
            if (string.Equals(relativePath, "/api/auth/verify", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relativePath, "/api/auth/device/verify", StringComparison.OrdinalIgnoreCase))
            {
                AddResponseIfMissing(operation, "302", "Redirects to the frontend verification screen when a token query parameter is supplied.");
            }
        }

        private static void ApplyOperationDescriptions(OpenApiOperation operation, string relativePath)
        {
            if (string.Equals(relativePath, "/api/auth/csrf", StringComparison.OrdinalIgnoreCase))
            {
                operation.Description =
                    "Returns the CSRF token used by protected browser-oriented auth POST endpoints.";
            }
            else if (string.Equals(relativePath, "/api/auth/refresh", StringComparison.OrdinalIgnoreCase))
            {
                operation.Description =
                    "Browser-cookie session refresh. Requires the CSRF header plus the refresh cookies issued during login.";
            }
            else if (string.Equals(relativePath, "/api/auth/api/refresh", StringComparison.OrdinalIgnoreCase))
            {
                operation.Description =
                    "API-token session refresh. Supply refresh and session-binding tokens through headers or the request body.";
            }
            else if (string.Equals(relativePath, "/api/payments/webhook", StringComparison.OrdinalIgnoreCase))
            {
                operation.Description =
                    "Consumes Stripe webhook events using the raw request payload and the `Stripe-Signature` header.";
            }
        }

        private static void AddHeaderIfMissing(OpenApiOperation operation, string name, string description)
        {
            if (operation.Parameters!.Any(p =>
                    p.In == ParameterLocation.Header
                    && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = name,
                    In = ParameterLocation.Header,
                    Required = false,
                    Description = description,
                    Schema = new OpenApiSchema { Type = "string" }
                }
            );
        }

        private static void AddResponseIfMissing(
            OpenApiOperation operation,
            string statusCode,
            string description,
            Type? responseType = null
        )
        {
            if (operation.Responses.ContainsKey(statusCode))
            {
                return;
            }

            var response = new OpenApiResponse
            {
                Description = description
            };

            if (responseType != null)
            {
                response.Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = CreateSchemaReference(responseType)
                    }
                };
            }

            operation.Responses[statusCode] = response;
        }

        private static OpenApiSchema CreateSchemaReference(Type type) =>
            new()
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.Schema,
                    Id = GetSchemaReferenceId(type)
                }
            };

        private static string CreateSchemaReferenceId(JsonTypeInfo jsonTypeInfo) =>
            GetSchemaReferenceId(jsonTypeInfo.Type);

        private static string GetSchemaReferenceId(Type type)
        {
            var nullableType = Nullable.GetUnderlyingType(type);
            if (nullableType != null)
            {
                return GetSchemaReferenceId(nullableType);
            }

            if (type.IsArray)
            {
                return $"{GetSchemaReferenceId(type.GetElementType()!)}Array";
            }

            if (type == typeof(object))
            {
                return "Object";
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            var genericName = type.Name[..type.Name.IndexOf('`')];
            var genericArguments = string.Join(
                "And",
                type.GetGenericArguments().Select(GetSchemaReferenceId)
            );

            return $"{genericName}Of{genericArguments}";
        }

        private static string NormalizeRelativePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return "/";
            }

            return "/" + relativePath.TrimStart('/');
        }

        private static string SanitizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "operation";
            }

            return string.Concat(value.Where(char.IsLetterOrDigit));
        }

        private static async Task<string> GenerateOpenApiJsonAsync(
            IServiceProvider services,
            string documentName
        )
        {
            var providerType =
                Type.GetType(
                    "Microsoft.Extensions.ApiDescriptions.IDocumentProvider, Microsoft.AspNetCore.OpenApi"
                )
                ?? Type.GetType(
                    "Microsoft.Extensions.ApiDescriptions.OpenApiDocumentProvider, Microsoft.AspNetCore.OpenApi"
                )
                ?? throw new InvalidOperationException(
                    "The OpenAPI document provider type could not be resolved."
                );

            var provider = services.GetService(providerType)
                ?? throw new InvalidOperationException(
                    "The OpenAPI document provider service is not registered."
                );

            var generateMethod = provider.GetType().GetMethod(
                "GenerateAsync",
                [typeof(string), typeof(TextWriter), typeof(OpenApiSpecVersion)]
            ) ?? throw new InvalidOperationException(
                "The OpenAPI document provider does not expose the expected GenerateAsync overload."
            );

            using var writer = new StringWriter();
            var task = generateMethod.Invoke(
                provider,
                [documentName, writer, OpenApiSpecVersion.OpenApi3_0]
            ) as Task ?? throw new InvalidOperationException(
                "The OpenAPI document provider did not return a Task."
            );

            await task;
            return writer.ToString();
        }
    }
}
