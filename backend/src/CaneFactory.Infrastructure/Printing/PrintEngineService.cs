using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Services;
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
        doc.Rows = GrossRows(p, doc.Language);
        doc.Images = doc.PrintImages ? await LoadPurchaseImagesForPrintAsync(p.Id, "GROSS") : new();
        return doc;
    }

    public async Task<PrintDocument> BuildTareSlipAsync(int purchaseId, string generatedByUserName)
    {
        var p = await LoadPurchaseAsync(purchaseId);
        var doc = await BaseDocAsync(p.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "गन्ना क्रय पर्ची - अंतिम तौल";
        doc.TitleEnglish = "Cane Purchase Slip - Final";
        doc.QrValue = p.Id;
        doc.Rows = GrossRows(p, doc.Language).Concat(TareRows(p)).ToList();
        doc.Images = doc.PrintImages ? await LoadPurchaseImagesForPrintAsync(p.Id, "GROSS", "TARE") : new();
        return doc;
    }

    public async Task<PrintDocument> BuildLoanSlipAsync(int loanId, string generatedByUserName)
    {
        var l = await LoadLoanAsync(loanId);
        var doc = await BaseDocAsync(l.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "ऋण पर्ची";
        doc.TitleEnglish = "Loan Issue Slip";
        doc.QrValue = l.Id;
        doc.Rows = LoanRows(l, doc.Language);
        return doc;
    }

    public async Task<PrintDocument> BuildLoanRecoverySlipAsync(int loanRecoveryId, string generatedByUserName)
    {
        var r = await LoadLoanRecoveryAsync(loanRecoveryId);
        var doc = await BaseDocAsync(r.Loan.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "ऋण वसूली पर्ची";
        doc.TitleEnglish = "Loan Recovery Slip";
        doc.QrValue = r.Id;
        doc.Rows = LoanRecoveryRows(r, doc.Language);
        return doc;
    }

    public async Task<PrintDocument> BuildPaymentSlipAsync(int paymentId, string generatedByUserName)
    {
        var p = await LoadPaymentAsync(paymentId);
        // PaymentPurchase preserves the final amount at the time of payment. The purchase itself
        // supplies the completed final weight and rate for a single-purchase advice slip.
        var paymentPurchases = await _db.PaymentPurchases.AsNoTracking().Where(pp => pp.PaymentId == paymentId)
            .OrderBy(pp => pp.PurchaseId)
            .Select(pp => new { pp.PurchaseId, pp.Purchase.FinalWeightQuintal, pp.Purchase.Rate, pp.PurchaseAmountAtPayment })
            .ToListAsync();
        var purchases = paymentPurchases.Select(pp => new PaymentPurchasePrintLine(
            pp.PurchaseId, pp.FinalWeightQuintal, pp.Rate, pp.PurchaseAmountAtPayment)).ToList();
        var doc = await BaseDocAsync(p.Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "भुगतान पर्ची";
        doc.TitleEnglish = "Payment Slip";
        doc.IsPaymentDocument = true;
        doc.QrValue = p.Id;
        doc.Rows = PaymentRows(p, purchases, doc.Language);
        if (purchases.Count > 1)
            doc.Tables.Add(PaymentPurchaseTable(purchases));
        return doc;
    }

    public async Task<PrintDocument> BuildPaymentBatchSlipAsync(IReadOnlyCollection<int> paymentIds, string generatedByUserName)
    {
        var ids = paymentIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) throw new ArgumentException("At least one payment ID is required.");

        var payments = await _db.Payments.AsNoTracking()
            .Include(x => x.Grower).ThenInclude(g => g.Village)
            .Include(x => x.Grower).ThenInclude(g => g.Bank)
            .Include(x => x.PaymentMode).Include(x => x.Season)
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted)
            .OrderBy(x => x.AdviceNumber).ToListAsync();
        if (payments.Count != ids.Length)
            throw new KeyNotFoundException("One or more payment records do not exist.");

        var sourceLines = await _db.PaymentPurchases.AsNoTracking()
            .Where(pp => ids.Contains(pp.PaymentId))
            .OrderBy(pp => pp.Payment.AdviceNumber).ThenBy(pp => pp.PurchaseId)
            .Select(pp => new
            {
                pp.PaymentId,
                pp.Payment.AdviceNumber,
                pp.Payment.GrowerCode,
                GrowerName = pp.Payment.Grower.GrowerName,
                pp.PurchaseId,
                pp.Purchase.FinalWeightQuintal,
                pp.Purchase.Rate,
                pp.PurchaseAmountAtPayment
            }).ToListAsync();
        var purchaseLines = sourceLines.Select(line => new PaymentBatchPurchasePrintLine(
            line.PaymentId, line.AdviceNumber, line.GrowerCode, line.GrowerName, line.PurchaseId,
            line.FinalWeightQuintal, line.Rate, line.PurchaseAmountAtPayment)).ToList();

        var doc = await BaseDocAsync(payments[0].Season?.SeasonName, generatedByUserName);
        doc.TitleHindi = "दिनांक-सीमा भुगतान विवरण";
        doc.TitleEnglish = "Date-Range Payment Details";
        doc.IsLandscape = true;
        doc.Rows = new()
        {
            new("भुगतान संख्या", "Payment Count", payments.Count.ToString()),
            new("कुल क्रय राशि (₹)", "Total Purchase Amount (Rs)", payments.Sum(p => p.TotalPurchaseAmount).ToString("F2")),
            new("कुल ऋण कटौती (₹)", "Total Loan Deducted (Rs)", payments.Sum(p => p.LoanDeductedAmount).ToString("F2")),
            new("कुल देय राशि (₹)", "Total Net Payable (Rs)", payments.Sum(p => p.NetPayableAmount).ToString("F2"))
        };
        doc.Tables.Add(PaymentBatchBankTable(payments));
        doc.Tables.Add(PaymentBatchPurchaseTable(purchaseLines));
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
        doc.Rows = SalePurchaseRows(p, stage, doc.Language);
        if (stage == "TARE")
        {
            // A tare slip is printed before the final/gross weight exists.  Show
            // the effective master rate as provisional, but never invent an
            // amount that has not yet been calculated from the final weight.
            var tareDate = p.TareDateTime.Date;
            var rate = await SaleItemRateLookup.CurrentAsync(_db, p.ItemId, tareDate);
            doc.Rows.Add(new PrintRow("वर्तमान दर (अनंतिम)", "Current Rate (Provisional)",
                rate?.ToString("F2") ?? "Sale Rate Master not configured"));
            doc.Rows.Add(new PrintRow("राशि (₹)", "Amount (Rs)", "Calculated after Final Gross"));
        }
        doc.Images = !doc.PrintImages ? new() : stage == "TARE"
            ? await LoadSalePurchaseImagesForPrintAsync(p.Id, "TARE")
            : await LoadSalePurchaseImagesForPrintAsync(p.Id, "GROSS", "TARE");
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
            .Include(x => x.Grower).ThenInclude(g => g.Bank)
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
            Language = string.Equals(cfg.Language, "hi", StringComparison.OrdinalIgnoreCase) ? "hi" : "en",
            CompanyName = string.Equals(cfg.Language, "hi", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(company?.CompanyNameHi) ? company.CompanyNameHi : company?.CompanyName ?? "Cane Factory",
            Address = string.Equals(cfg.Language, "hi", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(company?.AddressHi) ? company.AddressHi : company?.Address,
            LogoPath = company?.LogoPath,
            SeasonName = seasonName,
            GeneratedByUserName = generatedByUserName,
            PrintDateTime = DateTime.Now,
            PrintImages = cfg.PrintImages
        };
    }

    private async Task<List<PrintImage>> LoadPurchaseImagesForPrintAsync(int purchaseId, params string[] stages)
    {
        var expectedCameras = await _db.Cameras.AsNoTracking()
            .CountAsync(c => !c.IsDeleted && c.Status && c.CaptureEnabled);
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            var images = await _db.PurchaseImages.AsNoTracking()
                .Where(i => i.PurchaseId == purchaseId && i.Status && stages.Contains(i.CaptureStage))
                .OrderBy(i => i.CapturedAt)
                .ToListAsync();
            var existing = images.Where(i => File.Exists(i.FilePath)).ToList();
            var expected = expectedCameras * stages.Length;
            if (expected == 0 || existing.Count >= expected || DateTime.UtcNow >= deadline)
                return OrderPrintImages(existing.Select(i => new PrintImage(
                    $"{i.CaptureStage} • {Path.GetFileNameWithoutExtension(i.FilePath)}", i.FilePath)), stages);
            await Task.Delay(250);
        }
    }

    private async Task<List<PrintImage>> LoadSalePurchaseImagesForPrintAsync(int salePurchaseId, params string[] stages)
    {
        var expectedCameras = await _db.Cameras.AsNoTracking()
            .CountAsync(c => !c.IsDeleted && c.Status && c.CaptureEnabled);
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            var images = await _db.SalePurchaseImages.AsNoTracking()
                .Where(i => i.SalePurchaseId == salePurchaseId && i.Status && stages.Contains(i.CaptureStage))
                .OrderBy(i => i.CapturedAt)
                .ToListAsync();
            var existing = images.Where(i => File.Exists(i.FilePath)).ToList();
            var expected = expectedCameras * stages.Length;
            if (expected == 0 || existing.Count >= expected || DateTime.UtcNow >= deadline)
                return OrderPrintImages(existing.Select(i => new PrintImage(
                    $"{i.CaptureStage} • {Path.GetFileNameWithoutExtension(i.FilePath)}", i.FilePath)), stages);
            await Task.Delay(250);
        }
    }

    private static List<PrintImage> OrderPrintImages(IEnumerable<PrintImage> images, string[] stages)
    {
        var stageOrder = stages.Select((stage, index) => new { stage, index })
            .ToDictionary(x => x.stage, x => x.index, StringComparer.OrdinalIgnoreCase);
        return images.OrderBy(image =>
        {
            var stage = image.Label.Split('•')[0].Trim();
            return stageOrder.GetValueOrDefault(stage, int.MaxValue);
        }).ThenBy(image => image.Label).ToList();
    }

    private static string Text(string? english, string? hindi, string language) =>
        language == "hi" && !string.IsNullOrWhiteSpace(hindi) ? hindi! : english ?? "-";

    private static List<PrintRow> GrossRows(Purchase p, string language) => new()
    {
        new("क्रय क्रमांक", "Purchase ID", p.Id.ToString()),
        new("किसान कोड", "Grower Code", p.GrowerCode),
        new("किसान का नाम", "Grower Name", Text(p.Grower.GrowerName, p.Grower.GrowerNameHi, language)),
        new("पिता का नाम", "Father's Name", Text(p.Grower.FatherName, p.Grower.FatherNameHi, language)),
        new("गाँव", "Village", Text(p.Grower.Village.VillageName, p.Grower.Village.VillageNameHi, language)),
        new("वाहन क्रमांक", "Vehicle Number", p.VehicleNumber),
        new("वाहन प्रकार", "Vehicle Type", Text(p.VehicleType.VehicleTypeName, p.VehicleType.VehicleTypeNameHi, language)),
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

    private static List<PrintRow> LoanRows(Loan l, string language) => new()
    {
        new("ऋण क्रमांक", "Loan ID", l.Id.ToString()),
        new("किसान कोड", "Grower Code", l.GrowerCode),
        new("किसान का नाम", "Grower Name", Text(l.Grower.GrowerName, l.Grower.GrowerNameHi, language)),
        new("पिता का नाम", "Father's Name", Text(l.Grower.FatherName, l.Grower.FatherNameHi, language)),
        new("गाँव", "Village", Text(l.Grower.Village.VillageName, l.Grower.Village.VillageNameHi, language)),
        new("ऋण प्रकार", "Loan Type", l.LoanType.LoanTypeName),
        new("ऋण राशि (₹)", "Loan Amount (Rs)", l.LoanAmount.ToString("F2")),
        new("ऋण तिथि", "Issue Date", l.IssueDate.ToLocalTime().ToString("dd-MM-yyyy HH:mm")),
        new("जारीकर्ता", "Issued By", l.IssuedByUserName),
        new("स्थिति", "Status", l.LoanStatus),
        new("बकाया राशि (₹)", "Outstanding (Rs)", l.OutstandingAmount.ToString("F2")),
    };

    private static List<PrintRow> LoanRecoveryRows(LoanRecovery r, string language) => new()
    {
        new("वसूली क्रमांक", "Recovery ID", r.Id.ToString()),
        new("ऋण क्रमांक", "Loan ID", r.LoanId.ToString()),
        new("किसान कोड", "Grower Code", r.GrowerCode),
        new("किसान का नाम", "Grower Name", Text(r.Loan.Grower.GrowerName, r.Loan.Grower.GrowerNameHi, language)),
        new("वसूली राशि (₹)", "Recovery Amount (Rs)", r.RecoveryAmount.ToString("F2")),
        new("वसूली तिथि", "Recovery Date", r.RecoveryDate.ToLocalTime().ToString("dd-MM-yyyy HH:mm")),
        new("वसूली कर्ता", "Recovered By", r.RecoveredByUserName),
        new("बकाया शेष (₹)", "Remaining Outstanding (Rs)", r.Loan.OutstandingAmount.ToString("F2")),
    };

    private sealed record PaymentPurchasePrintLine(int PurchaseId, decimal? FinalWeightQuintal, decimal Rate, decimal FinalAmount);
    private sealed record PaymentBatchPurchasePrintLine(int PaymentId, int AdviceNumber, string GrowerCode,
        string GrowerName, int PurchaseId, decimal? FinalWeightQuintal, decimal Rate, decimal FinalAmount);

    private static List<PrintRow> PaymentRows(Payment p, List<PaymentPurchasePrintLine> purchases, string language)
    {
        var rows = new List<PrintRow>
        {
            new("भुगतान क्रमांक", "Payment ID", p.Id.ToString()),
            new("अग्रिम क्रमांक", "Advice Number", p.AdviceNumber.ToString()),
            new("किसान कोड", "Grower Code", p.GrowerCode),
            new("किसान का नाम", "Grower Name", Text(p.Grower?.GrowerName, p.Grower?.GrowerNameHi, language)),
            new("पिता का नाम", "Father's Name", Text(p.Grower?.FatherName, p.Grower?.FatherNameHi, language)),
            new("गाँव", "Village", Text(p.Grower?.Village?.VillageName, p.Grower?.Village?.VillageNameHi, language))
        };

        if (purchases.Count == 1)
        {
            var purchase = purchases[0];
            rows.AddRange(new[]
            {
                new PrintRow("क्रय क्रमांक", "Purchase ID", purchase.PurchaseId.ToString()),
                new PrintRow("अंतिम वजन (क्विंटल)", "Final Weight (Qtl)", purchase.FinalWeightQuintal?.ToString("F2") ?? "-"),
                new PrintRow("दर (प्रति क्विंटल)", "Rate (per Qtl)", purchase.Rate.ToString("F2")),
                new PrintRow("अंतिम राशि (₹)", "Final Amount (Rs)", purchase.FinalAmount.ToString("F2"))
            });
        }
        else if (purchases.Count == 0)
            rows.Add(new PrintRow("क्रय क्रमांक", "Purchase IDs", "-"));

        if (IsBankPayment(p))
        {
            rows.AddRange(new[]
            {
                new PrintRow("खाताधारक का नाम", "Account Holder Name", SnapshotOrCurrent(p.AccountHolderNameAtPayment, p.Grower?.AccountHolderName)),
                new PrintRow("बैंक का नाम", "Bank Name", SnapshotOrCurrent(p.BankNameAtPayment, p.Grower?.Bank?.BankName)),
                new PrintRow("शाखा", "Branch", SnapshotOrCurrent(p.BankBranchAtPayment, p.Grower?.Bank?.BranchName)),
                new PrintRow("आईएफएससी कोड", "IFSC Code", SnapshotOrCurrent(p.BankIfscAtPayment, p.Grower?.Bank?.IFSC)),
                new PrintRow("खाता क्रमांक", "Account Number", SnapshotOrCurrent(p.BankAccountNumberAtPayment, p.Grower?.BankAccountNumber))
            });
        }

        rows.AddRange(new[]
        {
            new PrintRow("कुल क्रय राशि (₹)", "Total Purchase Amount (Rs)", p.TotalPurchaseAmount.ToString("F2")),
            new PrintRow("ऋण कटौती (₹)", "Loan Deducted (Rs)", p.LoanDeductedAmount.ToString("F2")),
            new PrintRow("शुद्ध देय राशि (₹)", "Net Payable (Rs)", p.NetPayableAmount.ToString("F2")),
            new PrintRow("भुगतान माध्यम", "Payment Mode", p.PaymentMode?.ModeName ?? "-"),
            new PrintRow("संदर्भ क्रमांक", "Transaction Ref", p.TransactionRefNumber ?? "-"),
            new PrintRow("भुगतान तिथि", "Payment Date", p.PaymentDate.ToLocalTime().ToString("dd-MM-yyyy HH:mm")),
            new PrintRow("भुगतान कर्ता", "Paid By", p.PaidByUserName)
        });
        return rows;
    }

    private static PrintTable PaymentBatchBankTable(List<Payment> payments) => new()
    {
        TitleHindi = "किसान / बैंक / देय राशि विवरण (पंक्ति-वार)",
        TitleEnglish = "Grower / Bank / Payable Details (Row-wise)",
        Columns = new()
        {
            new("अग्रिम क्रमांक", "Advice No"),
            new("किसान", "Grower"),
            new("खाताधारक", "Account Holder"),
            new("बैंक", "Bank"),
            new("शाखा", "Branch"),
            new("आईएफएससी", "IFSC"),
            new("खाता क्रमांक", "Account No"),
            new("देय राशि (₹)", "Payable Amount (Rs)")
        },
        Rows = payments.Select(payment => new List<string>
        {
            payment.AdviceNumber.ToString(),
            payment.Grower.GrowerName,
            IsBankPayment(payment) ? SnapshotOrCurrent(payment.AccountHolderNameAtPayment, payment.Grower.AccountHolderName) : "-",
            IsBankPayment(payment) ? SnapshotOrCurrent(payment.BankNameAtPayment, payment.Grower.Bank?.BankName) : "-",
            IsBankPayment(payment) ? SnapshotOrCurrent(payment.BankBranchAtPayment, payment.Grower.Bank?.BranchName) : "-",
            IsBankPayment(payment) ? SnapshotOrCurrent(payment.BankIfscAtPayment, payment.Grower.Bank?.IFSC) : "-",
            IsBankPayment(payment) ? SnapshotOrCurrent(payment.BankAccountNumberAtPayment, payment.Grower.BankAccountNumber) : "-",
            payment.NetPayableAmount.ToString("F2")
        }).ToList()
    };

    private static PrintTable PaymentBatchPurchaseTable(List<PaymentBatchPurchasePrintLine> purchases) => new()
    {
        TitleHindi = "क्रय विवरण (पंक्ति-वार)",
        TitleEnglish = "Purchase Details (Row-wise)",
        Columns = new()
        {
            new("अग्रिम क्रमांक", "Advice No"),
            new("किसान", "Grower"),
            new("क्रय क्रमांक", "Purchase ID"),
            new("अंतिम वजन (क्विंटल)", "Final Weight (Qtl)"),
            new("दर (प्रति क्विंटल)", "Rate (per Qtl)"),
            new("अंतिम राशि (₹)", "Final Amount (Rs)")
        },
        Rows = purchases.Select(purchase => new List<string>
        {
            purchase.AdviceNumber.ToString(),
            purchase.GrowerName,
            purchase.PurchaseId.ToString(),
            purchase.FinalWeightQuintal?.ToString("F2") ?? "-",
            purchase.Rate.ToString("F2"),
            purchase.FinalAmount.ToString("F2")
        }).ToList()
    };

    private static PrintTable PaymentPurchaseTable(List<PaymentPurchasePrintLine> purchases) => new()
    {
        TitleHindi = "क्रय विवरण (पंक्ति-वार)",
        TitleEnglish = "Purchase Details (Row-wise)",
        Columns = new()
        {
            new("क्रय क्रमांक", "Purchase ID"),
            new("अंतिम वजन (क्विंटल)", "Final Weight (Qtl)"),
            new("दर (प्रति क्विंटल)", "Rate (per Qtl)"),
            new("अंतिम राशि (₹)", "Final Amount (Rs)")
        },
        Rows = purchases.Select(purchase => new List<string>
        {
            purchase.PurchaseId.ToString(),
            purchase.FinalWeightQuintal?.ToString("F2") ?? "-",
            purchase.Rate.ToString("F2"),
            purchase.FinalAmount.ToString("F2")
        }).ToList()
    };

    private static bool IsBankPayment(Payment payment) =>
        string.Equals(payment.PaymentMode?.ModeCode, "BANK", StringComparison.OrdinalIgnoreCase);

    private static string SnapshotOrCurrent(string? snapshot, string? current) =>
        !string.IsNullOrWhiteSpace(snapshot) ? snapshot : current ?? "-";

    private static List<PrintRow> SalePurchaseRows(SalePurchase p, string stage, string language)
    {
        var rows = new List<PrintRow>
        {
            new("बिक्री/खरीद क्रमांक", "SalePurchase ID", p.Id.ToString()),
            new("वस्तु", "Item", Text(p.Item?.ItemName, p.Item?.ItemNameHi, language)),
            new("पार्टी", "Party", Text(p.Party?.PartyName, p.Party?.PartyNameHi, language)),
            new("वाहन प्रकार", "Vehicle Type", Text(p.VehicleType?.VehicleTypeName, p.VehicleType?.VehicleTypeNameHi, language)),
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
