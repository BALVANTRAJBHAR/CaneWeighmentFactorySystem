namespace CaneFactory.Application.Common;

/// <summary>Masks a mobile number for display in any API response/UI/log - full number is only ever
/// used server-side when actually calling the SMS provider.</summary>
public static class SmsMask
{
    public static string Number(string mobile)
    {
        if (string.IsNullOrEmpty(mobile) || mobile.Length <= 4) return mobile;
        return $"{mobile[..2]}{new string('X', mobile.Length - 4)}{mobile[^2..]}";
    }
}
