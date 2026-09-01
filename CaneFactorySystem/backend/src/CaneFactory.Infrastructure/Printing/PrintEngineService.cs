using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.Infrastructure.Printing;

/// <summary>Builds PrintDocuments from Purchase/CompanyConfig/PrintConfig and dispatches rendering.
/// The single centralized print engine - Payment/Loan/LoanRecovery/SalePurchase/Reports will add
/// their own BuildXxxSlipAsync() here later and reuse the exact same renderers untouched.</summary>
public class PrintEngineService : IPrintEngineService
{
    private readonly AppDbContext _db;
    private readonly IEnumerable<IPrintRenderer> _renderers;

    public PrintEngineService(AppDbContext db, IEnumerable<IPrintRenderer> renderers)
    {
        _db = db; _renderers = renderers;
    }

    public async Task<PrintDocument> BuildGrossSlipAsync(int purchaseId, string generatedByUserName)
    {
        var p = await LoadPurchaseAsync(purchaseId);
        var doc = await BaseDocAsync(p.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "गन्ना क्रय पर्ची - सकल तौल";
        doc.TitleEnglish = "Cane Purchase Slip - Gross";
        doc.QrValue = p.Id;
        doc.Rows = GrossRows(p);
        return doc;
    }

    public async Task<PrintDocument> BuildTareSlipAsync(int purchaseId, string generatedByUserName)
    {
        var p = await LoadPurchaseAsync(purchaseId);
        var doc = await BaseDocAsync(p.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "गन्ना क्रय पर्ची - अंतिम तौल";
        doc.TitleEnglish = "Cane Purchase Slip - Final";
        doc.QrValue = p.Id;
        doc.Rows = GrossRows(p).Concat(TareRows(p)).ToList();
        return doc;
    }

    public PrintDocument BuildTestDocument(string language, string generatedByUserName) => new()
    {
        Language = language,
        TitleHindi = "प्रिंटर परीक्षण पृष्ठ",
        TitleEnglish = "Printer Test Page",
        CompanyName = "Demo Cane Factory Pvt Ltd",
        Address = "Industrial Area, Muzaffarnagar, Uttar Pradesh",
        SeasonName = "2026-27",
        QrValue = 999999,
        GeneratedByUserName = generatedByUserName,
        PrintDateTime = DateTime.Now,
        Rows = new()
        {
            new("किसान का नाम", "Grower Name", language == "hi" ? "श्री रमेश कुमार शर्मा (लंबा नाम परीक्षण)" : "Mr. Ramesh Kumar Sharma (Long Name Test)"),
            new("वाहन क्रमांक", "Vehicle Number", "UP32AB1234"),
            new("सकल वजन (क्विंटल)", "Gross Weight (Qtl)", "123.45"),
            new("दर (प्रति क्विंटल)", "Rate (per Qtl)", "0.00"),
            new("शून्य दशमलव", "Zero Decimal Test", "1"),
            new("वैकल्पिक फ़ील्ड", "Optional Field (blank)", "-"),
        }
    };

    public (byte[] bytes, string contentType, string fileExtension) Render(PrintDocument doc, string target, bool preview)
    {
        var renderer = _renderers.FirstOrDefault(r => r.TargetType == target)
            ?? throw new InvalidOperationException($"No print renderer registered for target '{target}'.");
        return preview ? renderer.RenderPreview(doc) : renderer.RenderFinal(doc);
    }

    private async Task<Purchase> LoadPurchaseAsync(int purchaseId)
    {
        var p = await _db.Purchases.Include(x => x.Grower).ThenInclude(g => g.Village)
            .Include(x => x.VehicleType).Include(x => x.Variety).Include(x => x.Season)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == purchaseId);
        if (p == null) throw new KeyNotFoundException($"Purchase {purchaseId} not found.");
        return p;
    }

    private async Task<PrintDocument> BaseDocAsync(string? seasonName, string generatedByUserName)
    {
        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted) ?? new PrintConfig();
        var company = await _db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        return new PrintDocument
        {
            Language = cfg.Language,
            CompanyName = company?.CompanyName ?? "Cane Factory",
            Address = company?.Address,
            LogoPath = company?.LogoPath,
            SeasonName = seasonName,
            GeneratedByUserName = generatedByUserName,
            PrintDateTime = DateTime.Now
        };
    }

    private static List<PrintRow> GrossRows(Purchase p) => new()
    {
        new("क्रय क्रमांक", "Purchase ID", p.Id.ToString()),
        new("किसान कोड", "Grower Code", p.GrowerCode),
        new("किसान का नाम", "Grower Name", p.Grower.GrowerName),
        new("पिता का नाम", "Father's Name", p.Grower.FatherName),
        new("गाँव", "Village", p.Grower.Village.VillageName),
        new("वाहन क्रमांक", "Vehicle Number", p.VehicleNumber),
        new("वाहन प्रकार", "Vehicle Type", p.VehicleType.VehicleTypeName),
        new("प्रजाति", "Variety", p.Variety.VarietyName),
        new("सकल वजन (क्विंटल)", "Gross Weight (Qtl)", p.GrossWeightQuintal.ToString("F2")),
        new("सकल तिथि/समय", "Gross Date/Time", p.GrossDateTime.ToLocalTime().ToString("dd-MM-yyyy HH:mm")),
        new("तौल कर्ता", "Weighed By", p.GrossByUserName),
        new("दर (प्रति क्विंटल)", "Rate (per Qtl)", p.Rate.ToString("F2")),
    };

    private static List<PrintRow> TareRows(Purchase p) => new()
    {
        new("टेयर वजन (क्विंटल)", "Tare Weight (Qtl)", p.TareWeightQuintal?.ToString("F2") ?? "-"),
        new("टेयर तिथि/समय", "Tare Date/Time", p.TareDateTime?.ToLocalTime().ToString("dd-MM-yyyy HH:mm") ?? "-"),
        new("टेयर कर्ता", "Tare By", p.TareByUserName ?? "-"),
        new("नेट वजन (क्विंटल)", "Net Weight (Qtl)", p.NetWeightQuintal?.ToString("F2") ?? "-"),
        new("कटान %", "Cutting %", p.CuttingPercent.ToString("F2")),
        new("कटान वजन (क्विंटल)", "Cutting Weight (Qtl)", p.CuttingWeightQuintal?.ToString("F2") ?? "-"),
        new("टैक्स %", "Tax %", p.TaxPercent.ToString("F2")),
        new("टैक्स वजन (क्विंटल)", "Tax Weight (Qtl)", p.TaxWeightQuintal?.ToString("F2") ?? "-"),
        new("अंतिम वजन (क्विंटल)", "Final Weight (Qtl)", p.FinalWeightQuintal?.ToString("F2") ?? "-"),
        new("कुल राशि (₹)", "Purchase Amount (Rs)", p.PurchaseAmount?.ToString("F2") ?? "-"),
    };
}
