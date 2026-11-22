using Microsoft.Extensions.DependencyInjection.Extensions;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Authors;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Library;
using AtlasLiterario.Application.Countries;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasLiterario.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICountryService, CountryService>();
        services.AddScoped<IBookSearchService, BookSearchService>();
        services.AddScoped<IBookImportService, BookImportService>();
        services.AddScoped<IBookService, BookService>();
        services.AddScoped<IBookCoverService, BookCoverService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<IAuthorService, AuthorService>();
        services.AddScoped<IMapService, MapService>();
        services.TryAddSingleton<IEnrichmentSettings, DefaultEnrichmentSettings>();
        services.AddScoped<RequestBudget>();
        return services;
    }
}
