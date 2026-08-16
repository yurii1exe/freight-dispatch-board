namespace FreightDispatch.Core.Model;

/// <summary>
/// One L11 reference number: a qualifier and a value.
/// </summary>
/// <remarks>
/// Reference numbers are how a load is identified by everyone who touches it, and every
/// party uses a different one. The shipper quotes the BOL, the broker quotes their load
/// number, the carrier quotes the PRO, the customer quotes the PO. A dispatch board that
/// only shows one of them is a board someone has to phone about.
/// </remarks>
public sealed class ReferenceNumber
{
    /// <summary>L1101, the reference value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>L1102, Reference Identification Qualifier (element 128), e.g. <c>BM</c>.</summary>
    public string Qualifier { get; init; } = string.Empty;

    /// <summary>The qualifier expanded for display, e.g. <c>Bill of Lading</c>.</summary>
    public string QualifierName => Describe(Qualifier);

    /// <summary>
    /// Expands the reference identification qualifiers that appear on a load tender.
    /// Unknown qualifiers are returned unchanged rather than dropped — a reference nobody
    /// recognises is still a reference somebody will be asked for.
    /// </summary>
    /// <param name="qualifier">L1102, a value of X12 element 128.</param>
    public static string Describe(string qualifier) => qualifier switch
    {
        "BM" => "Bill of Lading",
        "BN" => "Booking",
        "CN" => "Carrier PRO",
        "CO" => "Customer Order",
        "DO" => "Delivery Order",
        "LO" => "Load Planning",
        "OQ" => "Order",
        "PO" => "Purchase Order",
        "SI" => "Shipper's Identifying Number",
        "SN" => "Seal",
        "TN" => "Transaction Reference",
        "ZZ" => "Mutually Defined",
        "2I" => "Tracking",
        "4C" => "Storage Location",
        "AO" => "Appointment",
        "CR" => "Customer Reference",
        _ => qualifier,
    };
}
