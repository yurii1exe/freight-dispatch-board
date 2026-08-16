namespace FreightDispatch.Core.Edi;

/// <summary>
/// The rate card the demo invoices against.
/// </summary>
/// <remarks>
/// <para><b>A 204 does not carry a rate.</b> The load tender says what to move, where, and
/// by when; what it pays was agreed separately — on the phone, in a rate confirmation, or
/// against a contracted lane table — and none of that travels in the tender. This is the
/// single most common surprise for a developer wiring up a freight integration for the
/// first time: the document that starts the job says nothing about the money.</para>
/// <para>So the charges on the 210 have to come from somewhere else, and here they come
/// from the invented numbers below. <b>Do not read them as market pricing.</b> Truckload is
/// normally priced per mile or as a flat rate off the rate confirmation; this board has no
/// mileage and no rate confirmation, so linehaul is priced per hundredweight — which is
/// really an LTL convention — for the single reason that it makes L102 and L103 carry a
/// real rate and a real element 122 qualifier instead of sitting empty.</para>
/// <para>Everything is in cents. The wire format is N2 and integer cents is the only
/// representation that cannot round wrong on the way there.</para>
/// </remarks>
public sealed class InvoiceRates
{
    /// <summary>The rate card the board uses unless something else is supplied.</summary>
    public static InvoiceRates Demo { get; } = new();

    /// <summary>The flat portion of linehaul, in cents. $1,600.00.</summary>
    public long LinehaulBaseCents { get; init; } = 160_000;

    /// <summary>The variable portion of linehaul, in cents per hundred pounds. $2.50/cwt.</summary>
    public long LinehaulPerHundredweightCents { get; init; } = 250;

    /// <summary>
    /// Charged once for every stop beyond the first two, in cents. $75.00.
    /// </summary>
    /// <remarks>
    /// A pickup and a delivery is the move. Everything after that is the carrier being
    /// asked to open the doors again, and it is billed — which is why the stop count on the
    /// board and the stop-off lines on the invoice have to come from the same S5 loop, or
    /// the broker and the carrier will disagree about how many drops there were.
    /// </remarks>
    public long StopOffCents { get; init; } = 7_500;

    /// <summary>Fuel surcharge as a fraction of linehaul. 22%.</summary>
    public decimal FuelSurchargeRate { get; init; } = 0.22m;

    /// <summary>ITD07, the number of days until payment is due.</summary>
    public int PaymentTermsDays { get; init; } = 30;

    /// <summary>C301, the currency the invoice is denominated in.</summary>
    public string CurrencyCode { get; init; } = "USD";

    /// <summary>
    /// Linehaul for a given billed weight, in cents.
    /// </summary>
    /// <param name="weight">Billed weight in pounds. A load with no L3 is rated on the base alone.</param>
    public long Linehaul(decimal? weight)
    {
        if (weight is not { } pounds || pounds <= 0)
        {
            return LinehaulBaseCents;
        }

        decimal hundredweight = pounds / 100m;
        return LinehaulBaseCents + Cents(hundredweight * LinehaulPerHundredweightCents);
    }

    /// <summary>Fuel surcharge for a given linehaul, in cents.</summary>
    /// <param name="linehaulCents">The linehaul the surcharge is a percentage of.</param>
    public long FuelSurcharge(long linehaulCents) => Cents(linehaulCents * FuelSurchargeRate);

    /// <summary>
    /// Rounds to whole cents away from zero, which is what a billing system does and what
    /// banker's rounding does not.
    /// </summary>
    private static long Cents(decimal value) =>
        (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
