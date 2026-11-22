using AtlasLiterario.API.Filters;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AtlasLiterario.API.Controllers;

[Route("api/books")]
[Tags("Books")]
public sealed class BooksController(
    IBookSearchService searchService, IBookService bookService, IBookCoverService coverService) : ApiControllerBase
{
    /// <summary>
    /// Consolidated search (local catalog + Google Books + Open Library). Nothing is persisted.
    /// A failing source does not fail the search; 502 only when every source fails and nothing local matches.
    /// </summary>
    /// <param name="q">Search term (at least 2 characters).</param>
    /// <param name="type">all | title | author | isbn (an ISBN typed with "all" is searched as ISBN).</param>
    /// <param name="limit">Maximum results (default 20, max 40).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("search")]
    [ProducesResponseType<IReadOnlyList<BookSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<BookSearchResultDto>>> Search(
        [FromQuery] string? q, [FromQuery] string? type, [FromQuery] int? limit, CancellationToken ct) =>
        Ok(await searchService.SearchAsync(q, type, limit, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<BookDetailsDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await bookService.GetAsync(id, ct));

    /// <summary>Manual registration (no external source). Origins given here are stored as Manual/verified.</summary>
    [HttpPost]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<BookDetailsDto>> Create(CreateBookRequest request, CancellationToken ct)
    {
        var book = await bookService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = book.Id }, book);
    }

    /// <summary>Correction of bibliographic data (requires the book in the caller's library).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<BookDetailsDto>> Update(Guid id, CreateBookRequest request, CancellationToken ct) =>
        Ok(await bookService.UpdateAsync(id, request, ct));

    /// <summary>Replaces the origin countries (Source = Manual). Requires the book in the caller's library.</summary>
    [HttpPut("{id:guid}/origin-countries")]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<BookDetailsDto>> UpdateOriginCountries(
        Guid id, UpdateOriginCountriesRequest request, CancellationToken ct) =>
        Ok(await bookService.UpdateOriginCountriesAsync(id, request, ct));

    /// <summary>Reprocesses authors/geography in the external sources. Manual relations are preserved.</summary>
    [HttpPost("{id:guid}/enrich")]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<BookDetailsDto>> Enrich(Guid id, CancellationToken ct) =>
        Ok(await bookService.EnrichAsync(id, ct));

    /// <summary>
    /// Uploads (or replaces) the cover: multipart field <c>file</c>, JPEG/PNG/WebP up to 2 MB, validated by the leading
    /// bytes (name and Content-Type are ignored). Requires the book in the caller's library.
    /// </summary>
    [HttpPost("{id:guid}/cover")]
    [Consumes("multipart/form-data")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(CoverRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = CoverRequestLimit)]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public async Task<ActionResult<BookDetailsDto>> UploadCover(Guid id, CancellationToken ct)
    {
        // The form is read by hand (not model-bound) so that an oversized body becomes 413 cover.too_large, not a generic 400.
        IFormFile? file = null;
        if (Request.HasFormContentType)
        {
            try
            {
                var form = await Request.ReadFormAsync(ct);
                file = form.Files.GetFile("file");
            }
            catch (Exception ex) when (ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
                                       || ex.InnerException is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
                                       || (ex is InvalidDataException
                                           && (ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase)
                                               || ex.Message.Contains("too large", StringComparison.OrdinalIgnoreCase))))
            {
                throw new PayloadTooLargeException("cover.too_large", "A capa deve ter no máximo 2 MB.");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                throw ValidationException.ForField("validation.failed", "file", "Formulário multipart inválido.");
            }
        }

        await using var stream = file?.OpenReadStream();
        return Ok(await coverService.UploadAsync(id, stream, ct));
    }

    /// <summary>Removes the uploaded cover (idempotent).</summary>
    [HttpDelete("{id:guid}/cover")]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<BookDetailsDto>> DeleteCover(Guid id, CancellationToken ct) =>
        Ok(await coverService.DeleteAsync(id, ct));

    /// <summary>
    /// Anonymous: only the image bytes (an &lt;img&gt; cannot send the token). The URL changes with <c>?v=</c> when the
    /// cover changes, hence the immutable cache.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id:guid}/cover")]
    [Produces("image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetCover(Guid id, CancellationToken ct)
    {
        var etags = (Request.GetTypedHeaders().IfNoneMatch ?? [])
            .Select(t => (t.Tag.ToString() ?? string.Empty).Trim('"'))
            .ToList();
        var cover = await coverService.GetAsync(id, etags, ct);

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.GetTypedHeaders().ETag = new EntityTagHeaderValue($"\"{cover.Sha256}\"");
        if (cover.Data is null)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(cover.Data, cover.ContentType); // no file name: no Content-Disposition, never forced download
    }

    private const int CoverRequestLimit = 3 * 1024 * 1024; // 2 MB image + multipart overhead; the exact 2 MB rule is in the service
}
