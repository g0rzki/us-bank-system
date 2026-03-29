using Microsoft.OpenApi.Models;

namespace UsBankSystem.Api.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "US Bank System API",
                Version = "v1",
                Description = "REST API for US Bank System — accounts, transfers, cards, BLIK-USD"
            });

            var jwtScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter JWT token (without 'Bearer ' prefix)",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };

            c.AddSecurityDefinition("Bearer", jwtScheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { jwtScheme, Array.Empty<string>() }
            });

            c.TagActionsBy(api => [api.GroupName ?? api.ActionDescriptor.RouteValues["controller"]!]);
            c.DocInclusionPredicate((_, _) => true);
        });

        return services;
    }
}
