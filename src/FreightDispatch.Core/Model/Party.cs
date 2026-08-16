namespace FreightDispatch.Core.Model;

/// <summary>
/// A party on a load — shipper, consignee, bill-to. Read from an N1/N3/N4 loop.
/// </summary>
/// <remarks>
/// N101 is the Entity Identifier Code (element 98). The three that matter on a load
/// tender are <c>SH</c> shipper, <c>CN</c> consignee and <c>BT</c> bill-to party.
/// N103/N104 are the identification code qualifier and the code itself — usually the
/// shipper's own site number rather than anything globally meaningful, which is why the
/// board shows the name and keeps the code in the detail panel.
/// </remarks>
public sealed class Party
{
    /// <summary>N101, Entity Identifier Code: <c>SH</c>, <c>CN</c>, <c>BT</c>.</summary>
    public string EntityIdentifierCode { get; init; } = string.Empty;

    /// <summary>N102, the party's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>N103, Identification Code Qualifier — e.g. <c>93</c> assigned by the buyer, <c>ZZ</c> mutually defined.</summary>
    public string IdQualifier { get; init; } = string.Empty;

    /// <summary>N104, Identification Code.</summary>
    public string IdCode { get; init; } = string.Empty;

    /// <summary>N301, the first address line.</summary>
    public string Address1 { get; init; } = string.Empty;

    /// <summary>N302, the second address line.</summary>
    public string Address2 { get; init; } = string.Empty;

    /// <summary>N401, city name.</summary>
    public string City { get; init; } = string.Empty;

    /// <summary>N402, state or province code.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>N403, postal code.</summary>
    public string PostalCode { get; init; } = string.Empty;

    /// <summary>N404, country code. Defaults to <c>US</c> when the sender omits it.</summary>
    public string Country { get; init; } = "US";

    /// <summary>G61 contact name, if the stop carried one.</summary>
    public string ContactName { get; init; } = string.Empty;

    /// <summary>G61 communication number — telephone, when the qualifier is <c>TE</c>.</summary>
    public string ContactPhone { get; init; } = string.Empty;

    /// <summary>The <c>CITY, ST</c> form a dispatcher reads, e.g. <c>JOLIET, IL</c>.</summary>
    public string CityState =>
        string.IsNullOrEmpty(State) ? City : $"{City}, {State}";
}
