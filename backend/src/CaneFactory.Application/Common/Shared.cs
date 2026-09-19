namespace CaneFactory.Application.Common;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ApiError
{
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string[]>? Errors { get; set; }
}

public static class WeightCalculator
{
    public static decimal KgToQuintal(decimal kg) => Math.Round(kg / 100m, 2, MidpointRounding.AwayFromZero);
    public static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static (decimal net, decimal cutting, decimal tax, decimal final, decimal amount) Calculate(
        decimal grossQuintal, decimal tareQuintal, decimal cuttingPercent, decimal taxPercent, decimal rate)
    {
        var net = R2(grossQuintal - tareQuintal);
        var cutting = R2(net * cuttingPercent / 100m);
        var tax = R2(net * taxPercent / 100m);
        var final = R2(net - cutting - tax);
        var amount = R2(final * rate);
        return (net, cutting, tax, final, amount);
    }
}

public static class Validators
{
    public static bool IsMobile(string? v) => !string.IsNullOrWhiteSpace(v) && v.Length == 10 && v.All(char.IsDigit);
    public static bool IsEmail(string? v) => string.IsNullOrWhiteSpace(v) || System.Text.RegularExpressions.Regex.IsMatch(v, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    public static bool IsIfsc(string? v) => !string.IsNullOrWhiteSpace(v) && System.Text.RegularExpressions.Regex.IsMatch(v, @"^[A-Z]{4}0[A-Z0-9]{6}$");
    public static bool IsAadhaar(string? v) => !string.IsNullOrWhiteSpace(v) && v.Length == 12 && v.All(char.IsDigit);
    public static string Norm(string? v) => (v ?? string.Empty).Trim();
    public static string NormUpper(string? v) => Norm(v).ToUpperInvariant();
    /// <summary>Canonical identifier used for all vehicle duplicate checks and storage.
    /// UP32 AB 1234, up32ab1234 and UP32AB-1234 all become UP32AB1234.</summary>
    public static string NormalizeVehicleNumber(string? v) =>
        new string(Norm(v).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
