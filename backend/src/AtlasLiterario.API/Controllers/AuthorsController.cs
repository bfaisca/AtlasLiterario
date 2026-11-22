using AtlasLiterario.Application.Authors;
using AtlasLiterario.Application.Books;
using Microsoft.AspNetCore.Mvc;

namespace AtlasLiterario.API.Controllers;

[Route("api/authors")]
[Tags("Authors")]
public sealed class AuthorsController(IAuthorService authorService) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AuthorDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<AuthorDetailsDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await authorService.GetAsync(id, ct));

    /// <summary>Catalog books of this author that are in the user's library.</summary>
    [HttpGet("{id:guid}/books")]
    [ProducesResponseType<IReadOnlyList<BookDetailsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<BookDetailsDto>>> GetBooks(Guid id, CancellationToken ct) =>
        Ok(await authorService.GetBooksInLibraryAsync(id, ct));

    /// <summary>Replaces the Citizenship relations (Source = Manual). Requires a book of the author in the library.</summary>
    [HttpPut("{id:guid}/countries")]
    [ProducesResponseType<AuthorDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<AuthorDetailsDto>> UpdateCountries(
        Guid id, UpdateAuthorCountriesRequest request, CancellationToken ct) =>
        Ok(await authorService.UpdateCountriesAsync(id, request, ct));
}
