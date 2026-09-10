using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CaneFactory.Infrastructure.Printing;

/// <summary>Company header shared by weighment slips and tabular reports.</summary>
public static class CompanyPrintHeader
{
    public static byte[]? ReadLogo(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllBytes(path) : null;

    public static void Compose(IContainer container, string companyName, string? address, byte[]? logo, byte[]? qr)
    {
        container.Row(row =>
        {
            row.ConstantItem(70).Height(60).Element(e =>
            {
                if (logo != null) e.Image(logo).FitArea();
                else e.Border(1).BorderColor(Colors.Grey.Medium).AlignCenter().AlignMiddle()
                    .Text("LOGO").FontSize(8).FontColor(Colors.Grey.Medium);
            });
            row.RelativeItem().PaddingHorizontal(8).Column(c =>
            {
                c.Item().AlignCenter().Text(companyName).FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
                if (!string.IsNullOrWhiteSpace(address)) c.Item().AlignCenter().Text(address).FontSize(9);
            });
            row.ConstantItem(70).Height(70).Element(e =>
            {
                if (qr != null) e.AlignRight().Image(qr).FitArea();
            });
        });
    }
}
