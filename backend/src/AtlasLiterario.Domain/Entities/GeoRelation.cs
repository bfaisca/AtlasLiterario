namespace AtlasLiterario.Domain.Entities;

internal static class GeoRelation
{
    public const decimal ManualConfidence = 1m;

    public static decimal ValidateConfidence(decimal confidence) =>
        confidence is < 0m or > 1m
            ? throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.")
            : decimal.Round(confidence, 2);
}
