using System.Globalization;

namespace FreightDispatch.Core.Model;

/// <summary>
/// The 210 Motor Carrier Freight Details and Invoice generated when a load delivered, and
/// the charge lines it was built from.
/// </summary>
/// <remarks>
/// <para>The invoice is where the lifecycle closes. Everything before it is the carrier
/// telling the broker what is happening; the 210 is the carrier asking to be paid for it,
/// and it is the document a broker's accounts payable system actually cares about.</para>
/// <para><b>Money on this record is in cents, and that is deliberate.</b> X12 monetary
/// elements in the 210 — B307 Net Amount Due, L104 Amount Charged, L305 Amount Charged —
/// are all type N2, which means an implied two decimal places and no decimal point on the
/// wire. <c>265375</c> is $2,653.75. Sending <c>2653.75</c> is not a formatting preference,
/// it is an invoice for two thousand six hundred and fifty three dollars that some
/// receivers will read as $26.54 and others will reject outright. Holding the value as an
/// integer number of cents all the way through means the conversion happens once, at the
/// edge, instead of being re-derived by every caller.</para>
/// </remarks>
public sealed class FreightInvoice
{
    /// <summary>Board identifier, used in the API route for the raw 210.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>B302, the carrier's invoice number.</summary>
    public string InvoiceNumber { get; init; } = string.Empty;

    /// <summary>B306, the invoice date.</summary>
    public DateTime InvoiceDate { get; init; }

    /// <summary>The date the freight was picked up, from the load's own status history.</summary>
    public DateTime? ShippedOn { get; init; }

    /// <summary>The date the freight was delivered, from the load's own status history.</summary>
    public DateTime? DeliveredOn { get; init; }

    /// <summary>The charge lines, in the order they were written into the LX loops.</summary>
    public IReadOnlyList<InvoiceCharge> Charges { get; init; } = Array.Empty<InvoiceCharge>();

    /// <summary>B307 and L305: the total, in cents.</summary>
    public long TotalCents => Charges.Sum(c => c.AmountCents);

    /// <summary>The total as a decimal amount, for display only. Never write this to the wire.</summary>
    public decimal Total => TotalCents / 100m;

    /// <summary>L301, the billed weight for the move.</summary>
    public decimal? TotalWeight { get; init; }

    /// <summary>L302, the Weight Qualifier — <c>G</c> gross.</summary>
    public string WeightQualifier { get; init; } = "G";

    /// <summary>L311, the total lading quantity — handling units, not pieces.</summary>
    public decimal? TotalQuantity { get; init; }

    /// <summary>C301, the currency the amounts are denominated in.</summary>
    public string CurrencyCode { get; init; } = "USD";

    /// <summary>ITD07, the number of days until the invoice is due.</summary>
    public int PaymentTermsDays { get; init; }

    /// <summary>The complete generated interchange.</summary>
    public string Edi { get; init; } = string.Empty;

    /// <summary>ISA13 of the generated interchange.</summary>
    public string InterchangeControlNumber { get; init; } = string.Empty;

    /// <summary>ST02 of the generated transaction set.</summary>
    public string TransactionControlNumber { get; init; } = string.Empty;

    /// <summary>When it was generated, in local time, matching ISA09/ISA10.</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>What <c>EdiX12.Core</c> reported when the generated 210 was parsed back.</summary>
    public IReadOnlyList<string> RoundTripDiagnostics { get; init; } = Array.Empty<string>();

    /// <summary>True when the generated 210 re-parsed with no envelope diagnostics.</summary>
    public bool RoundTripClean => RoundTripDiagnostics.Count == 0;

    /// <summary>Formats cents the way a person reads them, e.g. <c>2,653.75</c>.</summary>
    /// <param name="cents">An amount in cents.</param>
    public static string Money(long cents) =>
        (cents / 100m).ToString("N2", CultureInfo.InvariantCulture);
}

/// <summary>
/// One charge line on the invoice: an LX loop carrying an L1, and an L0 when the line is
/// about physical freight rather than an accessorial.
/// </summary>
/// <param name="LineNumber">L001 and L101, the Lading Line Item Number. 1-based.</param>
/// <param name="Description">L112, Special Charge Description — the words a person reads.</param>
/// <param name="SpecialChargeCode">
/// L108, element 150 Special Charge or Allowance Code: <c>LHS</c> linehaul service,
/// <c>SOC</c> stop-off charge, <c>405</c> fuel surcharge, <c>DET</c> detention of trailers.
/// </param>
/// <param name="AmountCents">L104, Amount Charged. Cents, because L104 is N2.</param>
/// <param name="Rate">L102, Freight Rate. Type R — a decimal point is legal here and is not in L104.</param>
/// <param name="RateQualifier">L103, element 122 Rate/Value Qualifier: <c>CW</c> per hundredweight, <c>FR</c> flat rate.</param>
/// <param name="Weight">L004, the weight this line was rated on, when it has one.</param>
/// <param name="Quantity">L008, Lading Quantity — handling units.</param>
/// <param name="PackagingCode">L009, element 211 Packaging Form Code, e.g. <c>PLT</c> pallet.</param>
/// <param name="Commodity">L502, the lading description, when the line carries freight.</param>
public sealed record InvoiceCharge(
    int LineNumber,
    string Description,
    string SpecialChargeCode,
    long AmountCents,
    decimal? Rate = null,
    string RateQualifier = "",
    decimal? Weight = null,
    decimal? Quantity = null,
    string PackagingCode = "",
    string Commodity = "")
{
    /// <summary>The amount as a decimal, for display only.</summary>
    public decimal Amount => AmountCents / 100m;

    /// <summary>True when this line describes physical freight and therefore gets an L0.</summary>
    public bool HasFreight => Weight.HasValue || Quantity.HasValue;
}
