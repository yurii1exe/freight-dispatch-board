using FreightDispatch.Core.Model;

namespace FreightDispatch.Api;

/// <summary>A load as the board grid needs it: one row, everything scannable.</summary>
public sealed record LoadSummary(
    Guid Id,
    string ShipmentId,
    string Scac,
    string Status,
    string StatusLabel,
    int StatusOrder,
    string? NextStatus,
    string? NextStatusLabel,
    string EquipmentCode,
    string EquipmentLabel,
    string EquipmentLength,
    string TemperatureControl,
    string TrailerNumber,
    decimal? TotalWeight,
    int StopCount,
    int ExtraStops,
    string OriginName,
    string OriginCityState,
    DateTime? OriginEarliest,
    DateTime? OriginLatest,
    string DestinationName,
    string DestinationCityState,
    DateTime? DestinationEarliest,
    DateTime? DestinationLatest,
    string PrimaryReference,
    string BillOfLading,
    bool IsProduction,
    bool HasTenderDiagnostics,
    int EventCount,
    DateTimeOffset ReceivedAt);

/// <summary>Everything about one load, for the detail panel.</summary>
public sealed record LoadDetail(
    LoadSummary Summary,
    string PurposeCode,
    string PaymentMethod,
    string PaymentMethodLabel,
    string TenderedBy,
    string TenderedTo,
    PartyDto? BillTo,
    IReadOnlyList<ReferenceDto> References,
    IReadOnlyList<string> Notes,
    IReadOnlyList<StopDto> Stops,
    IReadOnlyList<StatusEventDto> Events,
    IReadOnlyList<string> TenderDiagnostics,
    string SourceEdi);

/// <summary>A stop on the detail panel.</summary>
public sealed record StopDto(
    int Sequence,
    string ReasonCode,
    string ReasonName,
    bool IsPickup,
    PartyDto Location,
    DateTime? Earliest,
    DateTime? Latest,
    string TimeCode,
    bool IsAppointment,
    decimal? Weight,
    string WeightUnit,
    decimal? Units,
    string UnitOfMeasure,
    IReadOnlyList<ReferenceDto> References,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Commodities);

/// <summary>A party — shipper, consignee, bill-to.</summary>
public sealed record PartyDto(
    string EntityIdentifierCode,
    string Name,
    string IdQualifier,
    string IdCode,
    string Address1,
    string Address2,
    string City,
    string State,
    string PostalCode,
    string Country,
    string CityState,
    string ContactName,
    string ContactPhone);

/// <summary>An L11 reference, with its qualifier expanded.</summary>
public sealed record ReferenceDto(string Value, string Qualifier, string QualifierName);

/// <summary>A status change and the 214 it produced.</summary>
public sealed record StatusEventDto(
    Guid Id,
    string Status,
    string StatusLabel,
    int StatusOrder,
    string StatusCode,
    string StatusCodeName,
    string ReasonCode,
    DateTime OccurredAt,
    string TimeCode,
    string City,
    string State,
    string CityState,
    string Note,
    DateTimeOffset RecordedAt,
    string Edi214,
    string InterchangeControlNumber,
    string TransactionControlNumber,
    IReadOnlyList<string> RoundTripDiagnostics,
    bool RoundTripClean);

/// <summary>The result of ingesting a 204.</summary>
public sealed record TenderResult(
    IReadOnlyList<LoadSummary> Loads,
    IReadOnlyList<string> Diagnostics,
    int SegmentCount,
    string Delimiters);

/// <summary>A bundled sample tender.</summary>
public sealed record SampleTender(string Name, string Title, string Description, string Edi);

/// <summary>Request body for a status change.</summary>
public sealed record AdvanceRequest(
    string Status,
    DateTime? OccurredAt,
    string? ReasonCode,
    string? City,
    string? State,
    string? Note);

/// <summary>Request body for a tender when it arrives as JSON rather than text/plain.</summary>
public sealed record TenderRequest(string Edi);

/// <summary>A board state and its wire code, for the client's status picker.</summary>
public sealed record StatusOption(string Key, string Label, int Order, string StatusCode, string StatusCodeName);

/// <summary>Maps domain objects onto the wire format the Angular client consumes.</summary>
public static class Contracts
{
    /// <summary>Projects a load into a grid row.</summary>
    /// <param name="load">The load.</param>
    public static LoadSummary ToSummary(Load load)
    {
        LoadStatus? next = StatusCatalog.Next(load.Status);

        return new LoadSummary(
            load.Id,
            load.ShipmentId,
            load.Scac,
            load.Status.ToString(),
            StatusCatalog.DescribeStatus(load.Status),
            (int)load.Status,
            next?.ToString(),
            next is null ? null : StatusCatalog.DescribeStatus(next.Value),
            load.EquipmentCode,
            DescribeEquipment(load.EquipmentCode),
            load.EquipmentLength,
            load.TemperatureControl,
            load.TrailerNumber,
            load.TotalWeight,
            load.Stops.Count,
            load.ExtraStops,
            load.Origin?.Location.Name ?? string.Empty,
            load.Origin?.Location.CityState ?? string.Empty,
            load.Origin?.Window.Earliest,
            load.Origin?.Window.Latest,
            load.Destination?.Location.Name ?? string.Empty,
            load.Destination?.Location.CityState ?? string.Empty,
            load.Destination?.Window.Earliest,
            load.Destination?.Window.Latest,
            FindReference(load, "OQ") ?? load.ShipmentId,
            FindReference(load, "BM") ?? string.Empty,
            load.IsProduction,
            load.TenderDiagnostics.Count > 0,
            load.Events.Count,
            load.ReceivedAt);
    }

    /// <summary>Projects a load into the full detail view.</summary>
    /// <param name="load">The load.</param>
    public static LoadDetail ToDetail(Load load) => new(
        ToSummary(load),
        load.PurposeCode,
        load.PaymentMethod,
        DescribePayment(load.PaymentMethod),
        load.TenderedBy,
        load.TenderedTo,
        load.BillTo is null ? null : ToParty(load.BillTo),
        load.References.Select(ToReference).ToList(),
        load.Notes,
        load.Stops.Select(ToStop).ToList(),
        load.Events.Select(ToEvent).ToList(),
        load.TenderDiagnostics,
        load.SourceEdi);

    /// <summary>Projects a stop.</summary>
    /// <param name="stop">The stop.</param>
    public static StopDto ToStop(Stop stop) => new(
        stop.Sequence,
        stop.ReasonCode,
        stop.ReasonName,
        stop.IsPickup,
        ToParty(stop.Location),
        stop.Window.Earliest,
        stop.Window.Latest,
        stop.Window.TimeCode,
        stop.Window.IsAppointment,
        stop.Weight,
        stop.WeightUnit,
        stop.Units,
        stop.UnitOfMeasure,
        stop.References.Select(ToReference).ToList(),
        stop.Notes,
        stop.Commodities);

    /// <summary>Projects a party.</summary>
    /// <param name="party">The party.</param>
    public static PartyDto ToParty(Party party) => new(
        party.EntityIdentifierCode,
        party.Name,
        party.IdQualifier,
        party.IdCode,
        party.Address1,
        party.Address2,
        party.City,
        party.State,
        party.PostalCode,
        party.Country,
        party.CityState,
        party.ContactName,
        party.ContactPhone);

    /// <summary>Projects a reference number.</summary>
    /// <param name="reference">The reference.</param>
    public static ReferenceDto ToReference(ReferenceNumber reference) =>
        new(reference.Value, reference.Qualifier, reference.QualifierName);

    /// <summary>Projects a status event, including the generated 214.</summary>
    /// <param name="statusEvent">The event.</param>
    public static StatusEventDto ToEvent(StatusEvent statusEvent) => new(
        statusEvent.Id,
        statusEvent.Status.ToString(),
        StatusCatalog.DescribeStatus(statusEvent.Status),
        (int)statusEvent.Status,
        statusEvent.StatusCode,
        statusEvent.StatusCodeName,
        statusEvent.ReasonCode,
        statusEvent.OccurredAt,
        statusEvent.TimeCode,
        statusEvent.City,
        statusEvent.State,
        string.IsNullOrEmpty(statusEvent.State) ? statusEvent.City : $"{statusEvent.City}, {statusEvent.State}",
        statusEvent.Note,
        statusEvent.RecordedAt,
        statusEvent.Edi214,
        statusEvent.InterchangeControlNumber,
        statusEvent.TransactionControlNumber,
        statusEvent.RoundTripDiagnostics,
        statusEvent.RoundTripClean);

    /// <summary>
    /// Expands X12 element 40 Equipment Description Code for the board. Only the codes a
    /// truckload tender uses; the full list is 200-odd and mostly rail.
    /// </summary>
    /// <param name="code">N711.</param>
    public static string DescribeEquipment(string code) => code switch
    {
        "TF" => "Dry van",
        "RT" => "Reefer",
        "FT" => "Flatbed",
        "TL" => "Trailer",
        "CN" => "Container",
        "TV" => "Van, special dimensions",
        "SS" => "Straight truck",
        "" => "Unspecified",
        _ => code,
    };

    /// <summary>Expands X12 element 146 Shipment Method of Payment.</summary>
    /// <param name="code">B206.</param>
    public static string DescribePayment(string code) => code switch
    {
        "PP" => "Prepaid",
        "CC" => "Collect",
        "TP" => "Third party",
        "PC" => "Prepaid and collect",
        "" => "Unspecified",
        _ => code,
    };

    private static string? FindReference(Load load, string qualifier) =>
        load.References.FirstOrDefault(r => r.Qualifier == qualifier)?.Value;
}
