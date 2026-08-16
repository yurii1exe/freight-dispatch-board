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
    bool IsMultiStop,
    int CurrentStopSequence,
    int CurrentStopOrdinal,
    string CurrentStopName,
    string CurrentStopCityState,
    string CurrentStopReason,
    bool CurrentStopIsPickup,
    string StopProgress,
    string NextActionLabel,
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
    string AcknowledgmentVerdict,
    string AcknowledgmentLabel,
    bool TenderRejected,
    bool HasInvoice,
    string InvoiceNumber,
    decimal InvoiceTotal,
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
    AcknowledgmentDto? Acknowledgment,
    InvoiceDto? Invoice,
    string SourceEdi);

/// <summary>The 997 that went back for this load's tender, and the generated file itself.</summary>
public sealed record AcknowledgmentDto(
    Guid Id,
    string Verdict,
    string VerdictLabel,
    string TransactionAcknowledgmentCode,
    string TransactionAcknowledgmentLabel,
    bool Rejected,
    string AcknowledgedInterchangeControlNumber,
    string InterchangeControlNumber,
    string TransactionControlNumber,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> OutOfScope,
    IReadOnlyList<string> RoundTripDiagnostics,
    bool RoundTripClean,
    DateTime GeneratedAt,
    string Edi);

/// <summary>The 210 raised on delivery, its charge lines, and the generated file.</summary>
public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? ShippedOn,
    DateTime? DeliveredOn,
    IReadOnlyList<InvoiceChargeDto> Charges,
    decimal Total,
    long TotalCents,
    decimal? TotalWeight,
    decimal? TotalQuantity,
    string CurrencyCode,
    int PaymentTermsDays,
    string InterchangeControlNumber,
    string TransactionControlNumber,
    IReadOnlyList<string> RoundTripDiagnostics,
    bool RoundTripClean,
    string Edi);

/// <summary>One charge line on the invoice.</summary>
public sealed record InvoiceChargeDto(
    int LineNumber,
    string Description,
    string SpecialChargeCode,
    decimal Amount,
    long AmountCents,
    decimal? Rate,
    string RateQualifier,
    decimal? Weight,
    decimal? Quantity);

/// <summary>What the transport is and what has recently moved over it.</summary>
public sealed record TransportStatus(
    string Name,
    string Endpoint,
    bool Running,
    string InboundDirectory,
    string OutboundDirectory,
    string ProcessedDirectory,
    string ErrorDirectory,
    IReadOnlyList<TransportLogDto> Log);

/// <summary>One line of the transport log.</summary>
public sealed record TransportLogDto(
    string Direction,
    string TransactionSet,
    string File,
    string Summary,
    bool Ok,
    DateTimeOffset At);

/// <summary>The TMS adapter, what it is holding, and what has crossed the boundary.</summary>
public sealed record TmsStatus(
    string Name,
    bool Connected,
    IReadOnlyList<string> NativeStatusCodes,
    IReadOnlyList<TmsHeldLoadDto> Held,
    IReadOnlyList<TmsLogDto> Log);

/// <summary>A load the adapter is holding.</summary>
public sealed record TmsHeldLoadDto(
    string TmsLoadId,
    string ShipmentId,
    Guid BoardLoadId,
    string Scac,
    string Origin,
    string Destination,
    int StopCount,
    DateTimeOffset PushedAt);

/// <summary>One line of the TMS bridge log.</summary>
public sealed record TmsLogDto(
    string Kind,
    string ShipmentId,
    string TmsLoadId,
    string Summary,
    bool Ok,
    DateTimeOffset At);

/// <summary>Request body for a simulated TMS status callback.</summary>
public sealed record TmsCallbackRequest(
    string ShipmentId,
    string Code,
    DateTime? OccurredAt,
    string? City,
    string? State,
    string? Note);

/// <summary>A stop on the detail panel.</summary>
public sealed record StopDto(
    int Sequence,
    string ReasonCode,
    string ReasonName,
    bool IsPickup,
    bool IsCurrent,
    bool IsComplete,
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
    int StopSequence,
    int StopOrdinal,
    string StopName,
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

/// <summary>The result of ingesting a 204, including the 997 that went straight back out.</summary>
public sealed record TenderResult(
    IReadOnlyList<LoadSummary> Loads,
    IReadOnlyList<string> Diagnostics,
    int SegmentCount,
    string Delimiters,
    AcknowledgmentDto? Acknowledgment,
    string Explanation);

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
        bool stopsRemain = load.StopsRemainAfterCurrent;
        LoadStatus? next = StatusCatalog.Next(load.Status, stopsRemain);
        Stop? current = load.CurrentStop;

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
            load.IsMultiStop,
            load.CurrentStopSequence,
            load.CurrentStopOrdinal,
            current?.Location.Name ?? string.Empty,
            current?.Location.CityState ?? string.Empty,
            current?.ReasonName ?? string.Empty,
            current?.IsPickup ?? false,
            load.IsMultiStop ? $"{load.CurrentStopOrdinal}/{load.Stops.Count}" : string.Empty,
            NextAction(load, next, stopsRemain),
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
            load.Acknowledgment?.Verdict ?? string.Empty,
            load.Acknowledgment?.VerdictLabel ?? string.Empty,
            load.TenderRejected,
            load.Invoice is not null,
            load.Invoice?.InvoiceNumber ?? string.Empty,
            load.Invoice?.Total ?? 0m,
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
        load.Stops.Select(stop => ToStop(stop, load)).ToList(),
        load.Events.Select(ToEvent).ToList(),
        load.TenderDiagnostics,
        ToAcknowledgment(load),
        ToInvoice(load.Invoice),
        load.SourceEdi);

    /// <summary>Projects the 997 that answered this load's tender.</summary>
    /// <param name="load">The load.</param>
    public static AcknowledgmentDto? ToAcknowledgment(Load load)
    {
        if (load.Acknowledgment is not { } ack)
        {
            return null;
        }

        AcknowledgedTransactionSet? mine = load.TenderAcknowledgment;

        return new AcknowledgmentDto(
            ack.Id,
            ack.Verdict,
            ack.VerdictLabel,
            mine?.AcknowledgmentCode ?? ack.Verdict,
            mine?.AcknowledgmentLabel ?? ack.VerdictLabel,
            load.TenderRejected,
            ack.AcknowledgedInterchangeControlNumber,
            ack.InterchangeControlNumber,
            ack.TransactionControlNumber,
            ack.Findings,
            ack.OutOfScope,
            ack.RoundTripDiagnostics,
            ack.RoundTripClean,
            ack.GeneratedAt,
            ack.Edi);
    }

    /// <summary>Projects the 210, or null when the load has not delivered.</summary>
    /// <param name="invoice">The invoice.</param>
    public static InvoiceDto? ToInvoice(FreightInvoice? invoice) =>
        invoice is null
            ? null
            : new InvoiceDto(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.ShippedOn,
                invoice.DeliveredOn,
                invoice.Charges.Select(c => new InvoiceChargeDto(
                    c.LineNumber,
                    c.Description,
                    c.SpecialChargeCode,
                    c.Amount,
                    c.AmountCents,
                    c.Rate,
                    c.RateQualifier,
                    c.Weight,
                    c.Quantity)).ToList(),
                invoice.Total,
                invoice.TotalCents,
                invoice.TotalWeight,
                invoice.TotalQuantity,
                invoice.CurrencyCode,
                invoice.PaymentTermsDays,
                invoice.InterchangeControlNumber,
                invoice.TransactionControlNumber,
                invoice.RoundTripDiagnostics,
                invoice.RoundTripClean,
                invoice.Edi);

    /// <summary>
    /// Projects a stop, marked with where the truck is relative to it.
    /// </summary>
    /// <param name="stop">The stop.</param>
    /// <param name="load">The load it belongs to, for the current-stop pointer.</param>
    public static StopDto ToStop(Stop stop, Load load) => new(
        stop.Sequence,
        stop.ReasonCode,
        stop.ReasonName,
        stop.IsPickup,
        stop.Sequence == load.CurrentStopSequence,
        stop.Sequence < load.CurrentStopSequence ||
            (stop.Sequence == load.CurrentStopSequence && load.Status == LoadStatus.Delivered),
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
        string.IsNullOrEmpty(statusEvent.Label)
            ? StatusCatalog.DescribeStatus(statusEvent.Status)
            : statusEvent.Label,
        (int)statusEvent.Status,
        statusEvent.StopSequence,
        statusEvent.StopOrdinal,
        statusEvent.StopName,
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

    /// <summary>
    /// The label on the button that moves the load on.
    /// </summary>
    /// <remarks>
    /// On a two-stop load the board vocabulary is the clearest thing to put on a button.
    /// On a four-stop load it is not: "In transit" three times in a row tells a dispatcher
    /// nothing about which drop is being left, so the button names the stop instead.
    /// </remarks>
    private static string NextAction(Load load, LoadStatus? next, bool stopsRemain)
    {
        if (next is not { } status)
        {
            return string.Empty;
        }

        if (!load.IsMultiStop)
        {
            return StatusCatalog.DescribeStatus(status);
        }

        int ordinal = load.CurrentStopOrdinal;
        int count = load.Stops.Count;

        return status switch
        {
            LoadStatus.AtShipper or LoadStatus.AtConsignee => $"Arrive stop {ordinal} of {count}",
            LoadStatus.InTransit when stopsRemain && load.Status == LoadStatus.AtConsignee =>
                $"Finish + depart stop {ordinal}",
            LoadStatus.InTransit => $"Depart stop {ordinal}",
            LoadStatus.Delivered => "Deliver — final stop",
            _ => StatusCatalog.DescribeStatus(status),
        };
    }

    private static string? FindReference(Load load, string qualifier) =>
        load.References.FirstOrDefault(r => r.Qualifier == qualifier)?.Value;
}
