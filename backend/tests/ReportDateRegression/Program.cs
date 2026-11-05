using System.Reflection;
using CaneFactory.API.Controllers;
using CaneFactory.Application.Common;
using Microsoft.AspNetCore.Mvc;

// Exercises the actual shared normalization used by all five report endpoints.
// No production database, hardware, or hosted recovery services are started.
var method = typeof(ReportsController).GetMethod("ValidateDates", BindingFlags.NonPublic | BindingFlags.Static)!;
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
(DateTime? Start, DateTime? End, object? Error) Normalize(DateTime? from, DateTime? to)
{
    object?[] args = [from, to];
    var error = method.Invoke(null, args);
    return ((DateTime?)args[0], (DateTime?)args[1], error);
}
foreach (var day in new[] { new DateTime(2026, 9, 7), new DateTime(2026, 9, 8), new DateTime(2026, 12, 31), new DateTime(2028, 2, 29) })
{
    var range = Normalize(day, day);
    Check(range.Error == null, "same-day accepted");
    Check(range.Start == day && range.End == day.AddDays(1), "full day bounds");
    foreach (var instant in new[] { day, day.AddHours(12), day.AddDays(1).AddTicks(-1) })
        Check(instant >= range.Start && instant < range.End, "midnight, midday and final tick included");
    Check(!(day.AddTicks(-1) >= range.Start), "previous day excluded");
    Check(!(day.AddDays(1) < range.End), "next midnight excluded");
}
Check(Normalize(new DateTime(2026, 9, 9), new DateTime(2026, 9, 7)).Error is BadRequestObjectResult, "reversed range rejected");
Check(Normalize(null, DateTime.MaxValue).Error is BadRequestObjectResult, "overflow rejected");
Check(Normalize(null, null) == (null, null, null), "unbounded preserved");
Check(Normalize(null, new DateTime(2026, 9, 7)).Start == null, "to-only supported");
Check(Normalize(new DateTime(2026, 9, 7), null).End == null, "from-only supported");
var sale = WeightCalculator.CalculateSale(105m, 5m, 2m, 1m, 100m, 5m);
Check(sale.NetWeightQuintal == 100m, "sale net weight");
Check(sale.CuttingWeightQuintal == 2m, "sale cutting percentage deduction");
Check(sale.KantaChhootWeightQuintal == 1m, "sale kanta chhoot kg-per-quintal deduction");
Check(sale.FinalWeightQuintal == 97m, "sale final weight after deductions");
Check(sale.AmountWithoutGst == 9700m && sale.GstAmount == 485m && sale.FinalAmount == 10185m,
    "sale rate and GST use deducted final weight");
Console.WriteLine($"PASS: {checks} report calendar-range regression checks.");
if (args.Contains("--database")) await DatabaseReportChecks.RunAsync();
if (args.Contains("--exports")) await ExportBrandingChecks.RunAsync();
if (args.Contains("--invoice")) await InvoiceLayoutChecks.RunAsync();
