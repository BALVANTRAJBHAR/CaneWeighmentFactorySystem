namespace CaneFactory.Infrastructure.Camera;

/// <summary>Canonical storage contract for sanitised Cane/Sale rate-edit evidence.</summary>
public static class RateEditImagePathResolver
{
    public static string ResolveRoot(string? configuredWeighmentPath) =>
        Path.Combine(WeighmentImagePathResolver.ResolveRoot(configuredWeighmentPath), "RateEditImage");

    public static string BuildStem(string transactionType, int transactionId,
        int relatedPartyId, string stage)
    {
        var safeStage = stage.Trim().ToUpperInvariant() switch
        {
            "GROSS" => "GROSS",
            "TARE" => "TARE",
            _ => throw new ArgumentOutOfRangeException(nameof(stage),
                "Rate-edit image stage must be GROSS or TARE.")
        };
        var safeType = transactionType.Trim().ToUpperInvariant();
        if (safeType is not ("CANE" or "SALE"))
            throw new ArgumentOutOfRangeException(nameof(transactionType),
                "Rate-edit transaction type must be CANE or SALE.");
        return $"{relatedPartyId}-{safeStage}-{transactionId}-RATEEDIT";
    }

    public static bool IsUnderRoot(string filePath, string root)
    {
        var allowed = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(filePath);
        return fullPath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase);
    }
}
