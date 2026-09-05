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

    public async Task<PrintDocument> BuildLoanSlipAsync(int loanId, string generatedByUserName)
    {
        var l = await LoadLoanAsync(loanId);
        var doc = await BaseDocAsync(l.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "ऋण पर्ची";
        doc.TitleEnglish = "Loan Issue Slip";
        doc.QrValue = l.Id;
        doc.Rows = LoanRows(l);
        return doc;
    }

    public async Task<PrintDocument> BuildLoanRecoverySlipAsync(int loanRecoveryId, string generatedByUserName)
    {
        var r = await LoadLoanRecoveryAsync(loanRecoveryId);
        var doc = await BaseDocAsync(r.Loan.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "ऋण वसूली पर्ची";
        doc.TitleEnglish = "Loan Recovery Slip";
        doc.QrValue = r.Id;
        doc.Rows = LoanRecoveryRows(r);
        return doc;
    }

    public async Task<PrintDocument> BuildPaymentSlipAsync(int paymentId, string generatedByUserName)
    {
        var p = await LoadPaymentAsync(paymentId);
        var purchaseIds = await _db.PaymentPurchases.Where(pp => pp.PaymentId == paymentId)
            .OrderBy(pp => pp.PurchaseId).Select(pp => pp.PurchaseId).ToListAsync();
        var doc = await BaseDocAsync(p.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "भुगतान पर्ची";
        doc.TitleEnglish = "Payment Slip";
        doc.QrValue = p.Id;
        doc.Rows = PaymentRows(p, purchaseIds);
        return doc;
    }

    public async Task<PrintDocument> BuildSalePurchaseSlipAsync(int salePurchaseId, string stage, string generatedByUserName)
    {
        var p = await _db.SalePurchases.AsNoTracking().Include(x => x.Item).Include(x => x.Party)
            .Include(x => x.VehicleType).FirstOrDefaultAsync(x => x.Id == salePurchaseId);
        if (p == null) throw new KeyNotFoundException($"SalePurchase {salePurchaseId} not found.");
        stage = stage.ToUpperInvariant();
        if (stage is not ("TARE" or "GROSS")) throw new ArgumentException("Stage must be TARE or GROSS.");
        if (stage == "GROSS" && p.WeighmentStatus != "COMPLETED")
            throw new InvalidOperationException("Gross slip is available only after SalePurchase completion.");
        var doc = await BaseDocAsync(null, generatedByUserName);
        doc.TitleHindi = stage == "TARE" ? "बिक्री/खरीद तौल पर्ची - टेयर" : "बिक्री/खरीद तौल पर्ची - अंतिम";
        doc.TitleEnglish = stage == "TARE" ? "Sale/Purchase Weighment Slip - Tare" : "Sale/Purchase Weighment Slip - Final";
        doc.QrValue = p.Id;
        doc.Rows = SalePurchaseRows(p, stage);
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

    private async Task<Loan> LoadLoanAsync(int loanId)
    {
        var l = await _db.Loans.Include(x => x.Grower).ThenInclude(g => g.Village)
            .Include(x => x.LoanType).Include(x => x.Season)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == loanId);
        if (l == null) throw new KeyNotFoundException($"Loan {loanId} not found.");
        return l;
    }

    private async Task<LoanRecovery> LoadLoanRecoveryAsync(int loanRecoveryId)
    {
        var r = await _db.LoanRecoveries.Include(x => x.Loan).ThenInclude(l => l.Grower).ThenInclude(g => g.Village)
            .Include(x => x.Loan.LoanType).Include(x => x.Loan.Season)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == loanRecoveryId);
        if (r == null) throw new KeyNotFoundException($"Loan Recovery {loanRecoveryId} not found.");
        return r;
    }

    private async Task<Payment> LoadPaymentAsync(int paymentId)
    {
        var p = await _db.Payments.Include(x => x.Grower).ThenInclude(g => g.Village)
            .Include(x => x.PaymentMode).Include(x => x.Season)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == paymentId);
        if (p == null) throw new KeyNotFoundException($"Payment {paymentId} not found.");
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

    private static List<PrintRow> LoanRows(Loan l) => new()
    {
        new("ऋण क्रमांक", "Loan ID", l.Id.ToString()),
        new("किसान कोड", "Grower Code", l.GrowerCode),
        new("किसान का नाम", "Grower Name", l.Grower.GrowerName),
        new("पिता का नाम", "Father's Name", l.Grower.FatherName),
        new("गाँव", "Village", l.Grower.Village.VillageName),
        new("ऋण प्रकार", "Loan Type", l.LoanType.LoanTypeName),
        new("ऋण राशि (₹)", "Loan Amount (Rs)", l.LoanAmount.ToString("F2")),
        new("ऋण तिथि", "Issue Date", l.IssueDate.ToLocalTime().ToString("dd-MM-yyyy HH:mm")),
        new("जारीकर्ता", "Issued By", l.IssuedByUserName),
        new("स्थिति", "Status", l.LoanStatus),
        new("बकाया राशि (₹)", "Outstanding (Rs)", l.OutstandingAmount.ToString("F2")),
    };

    private static List<PrintRow> LoanRecoveryRows(LoanRecovery r) => new()
    {
        new("वसूली क्रमांक", "Recovery ID", r.Id.ToString()),
        new("ऋण क्रमांक", "Loan ID", r.LoanId.ToString()),
        new("किसान कोड", "Grower Code", r.GrowerCode),
        new("किसान का नाम", "Grower Name", r.Loan.Grower.GrowerName),
        new("वसूली राशि (₹)", "Recovery Amount (Rs)", r.RecoveryAmount.ToString("F2")),
        new("वसूली तिथि", "Recovery Date", r.RecoveryDate.ToLocalTime().ToString("dd-MM-yyyy HH:mm")),
        new("वसूली कर्ता", "Recovered By", r.RecoveredByUserName),
        new("बकाया शेष (₹)", "Remaining Outstanding (Rs)", r.Loan.OutstandingAmount.ToString("F2")),
    };

    private static List<PrintRow> PaymentRows(Payment p, List<int> purchaseIds) => new()
    {
        new("भुगतान क्रमांक", "Payment ID", p.Id.ToString()),
        new("अग्रिम क्रमांक", "Advice Number", p.AdviceNumber.ToString()),
        new("किसान कोड", "Grower Code", p.GrowerCode),
        new("किसान का नाम", "Grower Name", p.Grower.GrowerName),
        new("गाँव", "Village", p.Grower.Village.VillageName),
        new("क्रय क्रमांक", "Purchase IDs", purchaseIds.Count == 0 ? "-" : string.Join(", ", purchaseIds)),
        new("कुल क्रय राशि (₹)", "Total Purchase Amount (Rs)", p.TotalPurchaseAmount.ToString("F2")),
        new("ऋण कटौती (₹)", "Loan Deducted (Rs)", p.LoanDeductedAmount.ToString("F2")),
        new("शुद्ध देय राशि (₹)", "Net Payable (Rs)", p.NetPayableAmount.ToString("F2")),
        new("भुगतान माध्यम", "Payment Mode", p.PaymentMode.ModeName),
        new("संदर्भ क्रमांक", "Transaction Ref", p.TransactionRefNumber ?? "-"),
        new("भुगतान तिथि", "Payment Date", p.PaymentDate.ToLocalTime().ToString("dd-MM-yyyy HH:mm")),
        new("भुगतान कर्ता", "Paid By", p.PaidByUserName),
    };

    private static List<PrintRow> SalePurchaseRows(SalePurchase p, string stage)
    {
        var rows = new List<PrintRow>
        {
            new("बिक्री/खरीद क्रमांक", "SalePurchase ID", p.Id.ToString()),
            new("वस्तु", "Item", p.Item.ItemName),
            new("पार्टी", "Party", p.Party.PartyName),
            new("वाहन प्रकार", "Vehicle Type", p.VehicleType.VehicleTypeName),
            new("वाहन क्रमांक", "Vehicle Number", p.VehicleNumber),
            new("चालक", "Driver", p.DriverName),
            new("टिप्पणी", "Remark", p.Remark ?? "-"),
            new("टेयर वजन (क्विंटल)", "Tare Weight (Qtl)", p.TareWeightQuintal.ToString("F2")),
            new("टेयर तिथि/समय", "Tare Date/Time", p.TareDateTime.ToLocalTime().ToString("dd-MM-yyyy HH:mm")),
            new("टेयर ऑपरेटर", "Tare Operator", p.TareByUserName),
            new("स्थिति", "Status", p.WeighmentStatus)
        };
        if (stage == "GROSS") rows.AddRange(new[]
        {
            new PrintRow("सकल वजन (क्विंटल)", "Gross Weight (Qtl)", p.GrossWeightQuintal?.ToString("F2") ?? "-"),
            new PrintRow("अंतिम वजन (क्विंटल)", "Final Weight (Qtl)", p.FinalWeightQuintal?.ToString("F2") ?? "-"),
            new PrintRow("दर", "Rate", p.Rate?.ToString("F2") ?? "-"),
            new PrintRow("राशि (₹)", "Amount (Rs)", p.Amount?.ToString("F2") ?? "-"),
            new PrintRow("सकल तिथि/समय", "Gross Date/Time", p.GrossDateTime?.ToLocalTime().ToString("dd-MM-yyyy HH:mm") ?? "-"),
            new PrintRow("सकल ऑपरेटर", "Gross Operator", p.GrossByUserName ?? "-")
        });
        return rows;
    }
}
