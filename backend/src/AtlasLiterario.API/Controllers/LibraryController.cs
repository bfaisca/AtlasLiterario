using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.Library;
using AtlasLiterario.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace AtlasLiterario.API.Controllers;

[Route("api/library")]
[Tags("Library")]
public sealed class LibraryController(ILibraryService libraryService) : ApiControllerBase
{
    /// <summary>Paged, filterable list of the user's library.</summary>
    /// <param name="q">Title/author.</param>
    /// <param name="status">Repeatable (status=Read, status=Reading).</param>
    /// <param name="countryIso">ISO alpha-3 (used with mapType; without it, matches either map).</param>
    /// <param name="mapType">authors | origins</param>
    /// <param name="genre">Genre (exact).</param>
    /// <param name="minRating">1–5.</param>
    /// <param name="geoMissing">any | origin | author | both — books with no stored origin country and/or no author citizenship (unknown, not "none").</param>
    /// <param name="sort">title | author | createdAt | finishedDate | rating | publicationYear</param>
    /// <param name="order">asc | desc</param>
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">Default 20, max 100.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<LibraryItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResult<LibraryItemDto>>> List(
        [FromQuery] string? q,
        [FromQuery] ReadingStatus[]? status,
        [FromQuery] string? countryIso,
        [FromQuery] string? mapType,
        [FromQuery] string? genre,
        [FromQuery] int? minRating,
        [FromQuery] string? sort,
        [FromQuery] string? order,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? geoMissing,
        CancellationToken ct) =>
        Ok(await libraryService.ListAsync(
            new LibraryQuery(q, status, countryIso, mapType, genre, minRating, sort, order, page, pageSize, geoMissing), ct));

    /// <param name="id">UserBook id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<LibraryItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<LibraryItemDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await libraryService.GetAsync(id, ct));

    /// <summary>Adds a book: <c>bookId</c> (catalog) or <c>source</c> (imported from Google Books / Open Library).</summary>
    [HttpPost]
    [ProducesResponseType<LibraryItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<ActionResult<LibraryItemDto>> Add(AddToLibraryRequest request, CancellationToken ct)
    {
        var item = await libraryService.AddAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<LibraryItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<LibraryItemDto>> Update(Guid id, UpdateLibraryItemRequest request, CancellationToken ct) =>
        Ok(await libraryService.UpdateAsync(id, request, ct));

    /// <summary>Removes the item from the library (the catalog book is kept).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await libraryService.DeleteAsync(id, ct);
        return NoContent();
    }
}
