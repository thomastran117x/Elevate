using backend.main.dtos.responses.general;

using Microsoft.AspNetCore.Mvc;

namespace backend.main.configurations.application
{
    public static class ApiResponseConfiguration
    {
        public static IServiceCollection AddApiResponseConventions(this IServiceCollection services)
        {
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var details = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value!.Errors
                                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                                    ? "The input was invalid."
                                    : error.ErrorMessage)
                                .ToArray()
                        );

                    return new BadRequestObjectResult(
                        ApiResponse<object?>.Failure(
                            "Validation failed.",
                            "VALIDATION_ERROR",
                            details
                        )
                    );
                };
            });

            return services;
        }
    }
}
