using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Common;

public sealed record ExternalSourceRef(ExternalProvider Provider, string ExternalId);
