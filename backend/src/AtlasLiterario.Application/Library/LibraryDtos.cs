using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Library;

/// <param name="Id">UserBook id.</param>
public sealed record LibraryItemDto(
    Guid Id,
    ReadingStatus Status,
    int? Rating,
    string? Review,
    DateOnly? StartDate,
    DateOnly? FinishedDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BookDetailsDto Book);

/// <summary>Exactly one of <see cref="BookId"/> or <see cref="Source"/> must be provided.</summary>
public sealed record AddToLibraryRequest(
    Guid? BookId,
    ExternalSourceRef? Source,
    ReadingStatus Status,
    int? Rating,
    string? Review,
    DateOnly? StartDate,
    DateOnly? FinishedDate);

public sealed record UpdateLibraryItemRequest(
    ReadingStatus Status,
    int? Rating,
    string? Review,
    DateOnly? StartDate,
    DateOnly? FinishedDate);
