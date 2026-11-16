using Microsoft.EntityFrameworkCore;
using SoftCo.Models;

namespace SoftCo.Data;

/// <summary>
/// Loads the rows currently held in "International Payment Tracker.xlsx" so the tracker can be
/// worked against real data from the first run.
///
/// Development only, and only when the orders table is completely empty, so it can never
/// overwrite or duplicate live records. Exchange rates are derived from the spreadsheet itself:
/// the Rand column divided by the foreign column gives ~2.5 for the CNY suppliers and ~17.0 for
/// the USD ones, which is how each row's currency was identified.
/// </summary>
/// 

//THE DATA IS TAKEN FROM THE SPREADSHEET AS OF 2024-06-13, AND WILL NOT BE UPDATED AGAIN. IT IS ONLY FOR DEVELOPMENT USE.

public static class TrackerSeed
{
    private record Row(
        string Supplier, SupplierType SupplierType, string Product, string[] Projects,
        FulfilmentStatus Status, string? Cargo, string? InvoiceDate, string? InvoiceRef,
        decimal Zar, decimal Foreign, decimal Deposit, decimal Settlement);

    public static async Task SeedAsync(AppDbContext db, IHostEnvironment env)
    {
        if (!env.IsDevelopment()) return;
        if (await db.SupplierOrders.AnyAsync()) return;

        var rows = BuildRows();

        // Suppliers and projects are created from the spreadsheet's own spelling.
        var suppliers = new Dictionary<string, Supplier>(StringComparer.OrdinalIgnoreCase);
        var projects = new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            if (!suppliers.ContainsKey(r.Supplier))
            {
                var s = new Supplier
                {
                    Name = r.Supplier,
                    Type = r.SupplierType,
                    Country = "China",
                    DefaultCurrencyCode = Currency(r)
                };
                suppliers[r.Supplier] = s;
                db.Suppliers.Add(s);
            }

            foreach (var p in r.Projects)
            {
                if (projects.ContainsKey(p)) continue;

                var proj = new Project { Name = p, Code = MakeCode(p, projects) };
                projects[p] = proj;
                db.Projects.Add(proj);
            }
        }

        await db.SaveChangesAsync();

        foreach (var r in rows)
        {
            var rate = r.Foreign > 0 ? Math.Round(r.Zar / r.Foreign, 6) : 0m;

            var order = new SupplierOrder
            {
                SupplierId = suppliers[r.Supplier].Id,
                ProductDescription = r.Product,
                FulfilmentStatus = r.Status,
                CargoReadinessDate = ParseDate(r.Cargo),
                InvoiceDate = ParseDate(r.InvoiceDate),
                InvoiceRef = r.InvoiceRef,
                CurrencyCode = Currency(r),
                ExchangeRate = rate,
                InvoiceValueForeign = r.Foreign,
                InvoiceValueZar = r.Zar,
                CreatedById = "seed"
            };

            foreach (var p in r.Projects)
                order.OrderProjects.Add(new OrderProject { ProjectId = projects[p].Id });

            if (r.Deposit > 0)
                order.Payments.Add(new OrderPayment
                {
                    Kind = PaymentKind.Deposit,
                    AmountForeign = r.Deposit,
                    AmountZar = Math.Round(r.Deposit * rate, 2),
                    CreatedById = "seed"
                });

            if (r.Settlement > 0)
                order.Payments.Add(new OrderPayment
                {
                    Kind = PaymentKind.Settlement,
                    AmountForeign = r.Settlement,
                    AmountZar = Math.Round(r.Settlement * rate, 2),
                    CreatedById = "seed"
                });

            db.SupplierOrders.Add(order);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A ZAR:foreign ratio near 17 is a dollar invoice; near 2.5 is a yuan invoice. Rows with no
    /// invoice yet inherit the supplier's usual currency.
    /// </summary>
    private static string Currency(Row r)
    {
        if (r.Foreign <= 0) return "CNY";
        var ratio = r.Zar / r.Foreign;
        return ratio > 8m ? "USD" : "CNY";
    }

    private static DateOnly? ParseDate(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var parts = v.Split('.');
        if (parts.Length != 2) return null;
        return int.TryParse(parts[0], out var d) && int.TryParse(parts[1], out var m)
            ? new DateOnly(2026, m, d) : null;
    }

    private static string MakeCode(string name, Dictionary<string, Project> existing)
    {
        var baseCode = new string(name.ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ')
            .ToArray())
            .Replace(" ", "-");

        if (baseCode.Length > 40) baseCode = baseCode[..40];

        var code = baseCode;
        var n = 2;
        while (existing.Values.Any(p => p.Code == code)) code = $"{baseCode}-{n++}";
        return code;
    }

    private static List<Row> BuildRows() =>
    [
        new("Suo Young Lighting", SupplierType.Manufacturer, "Lighting", [],
            FulfilmentStatus.InProduction, null, "14.09", "SY20260914001", 97746.60m, 5749.80m, 1724.94m, 0m),

        new("Interi Furn / Foshan Chengjia Liye Furn", SupplierType.Manufacturer, "Coffee Table", ["Fairmont"],
            FulfilmentStatus.InProduction, null, null, null, 0m, 0m, 0m, 0m),

        new("Hello Sun / Foshan Shunde Husen Furn", SupplierType.Manufacturer, "Outdoor Furn", ["Fairmont"],
            FulfilmentStatus.InProduction, null, null, "20260904H01", 103734m, 6102m, 3051m, 3051m),

        new("EMORY / Foshan Shunde Xintimeinuo", SupplierType.Manufacturer, "Residential Furn", ["Williams"],
            FulfilmentStatus.InProduction, null, null, null, 0m, 0m, 0m, 0m),

        new("EMORY / Foshan Shunde Xintimeinuo", SupplierType.Manufacturer, "Residential Furn", ["Ravello"],
            FulfilmentStatus.InProduction, "20.10", null, null, 0m, 0m, 0m, 0m),

        new("EMORY / Foshan Shunde Xintimeinuo", SupplierType.Manufacturer, "Residential Furn", ["Fairmont"],
            FulfilmentStatus.InProduction, "20.10", null, null, 0m, 0m, 0m, 0m),

        new("EMORY / Foshan Shunde Xintimeinuo", SupplierType.Manufacturer, "Residential Furn", ["Ravello 202 & 401"],
            FulfilmentStatus.InProduction, "30.10", null, "JNYDY20260905-01", 199450m, 79780m, 39890m, 39890m),

        new("Yijin Furniture", SupplierType.Manufacturer, "Residential Furn", ["De Wet"],
            FulfilmentStatus.InProduction, null, null, null, 0m, 0m, 0m, 0m),

        new("Yijin Furniture", SupplierType.Manufacturer, "Residential Furn", ["Ravello 202 & 401"],
            FulfilmentStatus.InProduction, null, null, "JNYDY20260905-01", 0m, 0m, 0m, 0m),

        new("Yijin Furniture", SupplierType.Manufacturer, "Residential Furn", ["Estate Penthouse"],
            FulfilmentStatus.InProduction, null, "11.09", "JNYDY20260911-01", 157450m, 62980m, 31490m, 15745m),

        new("Yijin Furniture", SupplierType.Manufacturer, "Residential Furn", ["Fairmont Dining Chairs"],
            FulfilmentStatus.InProduction, null, "09.09", "JNYDY20260909-01", 0m, 0m, 0m, 0m),

        new("Yijin Furniture", SupplierType.Manufacturer, "Residential Furn", ["Fairmont Furn"],
            FulfilmentStatus.InProduction, null, "05.09", "JNYDY20260905-02", 59150m, 23660m, 11830m, 11830m),

        new("Yijin Furniture", SupplierType.Manufacturer, "Residential Furn", ["Williams", "Ravello"],
            FulfilmentStatus.InProduction, null, "21.08", "JNYDY20260821-01", 119050m, 47620m, 23810m, 23810m),

        new("Yijin Furniture", SupplierType.Manufacturer, "Restaurant", ["T.M Mauritius"],
            FulfilmentStatus.InProduction, "25.09", null, null, 0m, 0m, 0m, 0m),

        new("Yijin Furniture", SupplierType.Manufacturer, "Restaurant", ["Harbour House V&A"],
            FulfilmentStatus.InProduction, "20.09", null, null, 0m, 0m, 0m, 0m),

        new("Jiagmen Jinhan Light", SupplierType.Manufacturer, "Light Fixtures", ["Ravello 202 & 401"],
            FulfilmentStatus.InProduction, null, null, null, 0m, 0m, 0m, 0m),

        new("Jiagmen Jinhan Light", SupplierType.Manufacturer, "Light Fixtures", ["Ravello 301"],
            FulfilmentStatus.InProduction, null, null, null, 0m, 0m, 0m, 0m),

        new("Jiagmen Jinhan Light", SupplierType.Manufacturer, "Light Fixtures", ["Fairmont"],
            FulfilmentStatus.InProduction, null, null, null, 0m, 0m, 0m, 0m),

        new("Jiagmen Jinhan Light", SupplierType.Manufacturer, "Light Fixtures", ["Arcadia", "Williams"],
            FulfilmentStatus.InProduction, null, null, "JH2603142210360", 378000m, 151200m, 60489m, 90711m),

        new("Jiagmen Jinhan Light", SupplierType.Manufacturer, "Light Testing", ["Arcadia", "Williams"],
            FulfilmentStatus.Testing, null, null, "JH2603142210360", 71000m, 28400m, 0m, 0m),

        new("Jiagmen Jinhan Light", SupplierType.Manufacturer, "Light Fixtures", ["De Wet"],
            FulfilmentStatus.InProduction, null, null, "JH260609360", 68430m, 27372m, 10949m, 16423m),

        new("Jiagmen Jinhan Light", SupplierType.Manufacturer, "Light Testing", ["De Wet"],
            FulfilmentStatus.InProduction, null, null, "JH260609361", 20555m, 8222m, 0m, 0m),

        new("UP Furniture / Jiaxing UP Furniture", SupplierType.Manufacturer, "Sofas", ["Williams", "Ravello 301"],
            FulfilmentStatus.InProduction, "20.09", null, "26UP05621", 238208.50m, 95283.40m, 28585.02m, 66698.38m),

        new("Keorh", SupplierType.Manufacturer, "Smart Toilets", ["Samples"],
            FulfilmentStatus.Shipping, null, null, "260810-KJ-01-SO", 6715m, 395m, 0m, 0m),

        new("EMORY / Foshan Shunde Xintimeinuo", SupplierType.Manufacturer, "Residential Furn", ["Ravello", "Williams"],
            FulfilmentStatus.InProduction, null, null, "0009727", 545342.50m, 218137m, 65441.10m, 152695.90m),

        new("EMORY / Foshan Shunde Xintimeinuo", SupplierType.Manufacturer, "Residential Furn", ["Arcadia"],
            FulfilmentStatus.Shipping, null, null, null, 965797.80m, 386319.12m, 115895m, 270424.12m),

        new("Foshan Meiye Furniture", SupplierType.Manufacturer, "Stone side tables", ["Arcadia"],
            FulfilmentStatus.Shipping, null, null, "MY265-07", 12359.375m, 4943.75m, 1483.13m, 3460.62m),

        new("UP Furniture / Jiaxing UP Furniture", SupplierType.Manufacturer, "Sofas", ["Williams", "Arcadia", "Soft & Co Stock"],
            FulfilmentStatus.Clearance, null, null, "26UP056-1", 90117m, 36046.80m, 0m, 36046.80m),

        new("Foshan Lvban Office Furniture", SupplierType.Manufacturer, "Table, chairs and workstations, sofas",
            ["Inospace", "Inhouse Office Chairs"],
            FulfilmentStatus.Delivered, null, null, null, 357900m, 143160m, 42500m, 100660m),

        // "Service Provider" sheet - freight, tracked with the same columns.
        new("UniBest Foshan Freight", SupplierType.ServiceProvider, "Freight Service", ["LVBAN"],
            FulfilmentStatus.Delivered, null, null, "UB202607B01", 73669.50m, 4333.50m, 0m, 4333.50m),

        new("UniBest Foshan Freight", SupplierType.ServiceProvider, "Freight Service", ["Emory"],
            FulfilmentStatus.InProduction, null, null, null, 0m, 0m, 0m, 0m),
    ];
}
