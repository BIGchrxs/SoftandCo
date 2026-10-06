namespace SoftCo.Services.Invoicing;

/// <summary>What one invoice line comes to.</summary>
public readonly record struct LineAmounts(decimal NetExclVat, decimal VatAmount, decimal TotalInclVat);

/// <summary>What the header shows. Always the sum of the lines, never computed separately.</summary>
public readonly record struct InvoiceTotals(decimal NetTotal, decimal VatTotal, decimal GrandTotal);

/// <summary>
/// Every figure a client sees, and every figure SARS sees.
///
/// Pure and dependency-free so it can be exercised exhaustively on its own - it was written against
/// its tests before any of it existed, because this is the part of the system where being almost
/// right is indistinguishable from being wrong until somebody reconciles a VAT return.
///
/// Three rules it exists to enforce:
///
/// <list type="number">
/// <item>Rounding is away from zero, never the .NET default. <c>Math.Round(decimal, int)</c> uses
/// banker's rounding, which sends a half to the nearest even digit - 0.125 becomes 0.12 - and that
/// shaved cent lands the same way on every line of every invoice.</item>
/// <item>VAT is computed and rounded <b>per line</b>, then summed. Computing it on the subtotal
/// produces a header that differs by cents from the rows the client can add up themselves, which is
/// the most common invoice complaint there is.</item>
/// <item>Amounts are exclusive of VAT. Back-calculating exclusive from a 2dp inclusive price is
/// lossy, so inclusive entry is converted once, at save, and never again.</item>
/// </list>
/// </summary>
public static class InvoiceMath
{
    /// <summary>
    /// Money, to the cent, rounded away from zero.
    ///
    /// The single place rounding happens. Every other method here goes through it, so there is one
    /// answer to "how does this round" rather than one per call site.
    /// </summary>
    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Quantity times unit price, rounded once.</summary>
    public static decimal Net(decimal quantity, decimal unitPriceExclVat)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
            "A line cannot have a negative quantity. Use a credit note to reverse a charge.");

        return Round(quantity * unitPriceExclVat);
    }

    /// <summary>
    /// VAT on a line's net amount, at the rate stored on that line.
    ///
    /// The rate is a parameter and never a constant. South Africa moved from 14% to 15% in 2018 and
    /// a further rise was tabled and withdrawn in 2025; an invoice issued at 14% has to re-render at
    /// 14% forever, so the rate is frozen on the line rather than read from configuration.
    /// </summary>
    public static decimal Vat(decimal netExclVat, decimal vatRatePercent)
    {
        if (vatRatePercent < 0) throw new ArgumentOutOfRangeException(nameof(vatRatePercent),
            vatRatePercent, "A VAT rate cannot be negative.");

        return Round(netExclVat * vatRatePercent / 100m);
    }

    /// <summary>
    /// Strips VAT out of an inclusive price.
    ///
    /// Interior clients are quoted VAT-inclusive, so the entry form accepts what they were quoted
    /// and this converts it once. The result is what gets stored; nothing re-derives it later.
    /// </summary>
    public static decimal ExclusiveFromInclusive(decimal inclusive, decimal vatRatePercent)
    {
        if (vatRatePercent < 0) throw new ArgumentOutOfRangeException(nameof(vatRatePercent),
            vatRatePercent, "A VAT rate cannot be negative.");

        return Round(inclusive / (1m + vatRatePercent / 100m));
    }

    /// <summary>One line, start to finish.</summary>
    public static LineAmounts Line(decimal quantity, decimal unitPriceExclVat, decimal vatRatePercent)
    {
        var net = Net(quantity, unitPriceExclVat);
        var vat = Vat(net, vatRatePercent);

        // Not rounded again: both parts are already at the cent, so adding them cannot introduce a
        // fraction, and rounding a second time is how a total stops matching its own components.
        return new LineAmounts(net, vat, net + vat);
    }

    /// <summary>
    /// The header, summed from the lines.
    ///
    /// Deliberately takes the computed lines rather than the raw quantities: the header must be the
    /// sum of what is printed, so there is no path by which it could be calculated independently and
    /// disagree.
    /// </summary>
    public static InvoiceTotals Totals(IEnumerable<LineAmounts> lines)
    {
        var net = 0m;
        var vat = 0m;

        foreach (var line in lines)
        {
            net += line.NetExclVat;
            vat += line.VatAmount;
        }

        return new InvoiceTotals(net, vat, net + vat);
    }
}
