using EdiX12.Core;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Edi;

/// <summary>
/// Turns a 204 Motor Carrier Load Tender into <see cref="Load"/> objects.
/// </summary>
/// <remarks>
/// <para>The envelope is <c>EdiX12.Core</c>'s job — it reads the delimiters out of the ISA
/// and hands back ST/SE transaction sets. This class only walks the body.</para>
/// <para>The 204 body is a header area followed by a repeating S5 stop-off loop. There is
/// no explicit loop terminator in X12: a loop ends when the next loop's trigger segment
/// appears, or when the transaction set does. So the walk is a small state machine —
/// everything before the first S5 belongs to the load, everything after belongs to the
/// stop most recently opened.</para>
/// <para>Segments outside what the board needs are ignored rather than rejected. A tender
/// that carries thirty segments this reader has no use for is still a tender, and refusing
/// it would be the wrong answer to "the shipper added a segment".</para>
/// </remarks>
public static class Edi204Reader
{
    /// <summary>
    /// Parses an interchange and returns one <see cref="Load"/> per 204 transaction set.
    /// </summary>
    /// <param name="ediText">Raw X12 text beginning with ISA.</param>
    /// <returns>
    /// The loads found. An interchange with no 204 transaction sets returns an empty list
    /// rather than throwing — that is a routing mistake, not a parse failure.
    /// </returns>
    /// <exception cref="EdiX12.Core.X12ParseException">The ISA is unreadable or the envelope is nested illegally.</exception>
    public static IReadOnlyList<Load> Read(string ediText)
    {
        Interchange interchange = X12Parser.Parse(ediText);

        IReadOnlyList<string> diagnostics = interchange
            .Validate()
            .Select(d => d.ToString())
            .ToList();

        var loads = new List<Load>();

        foreach (TransactionSet transaction in interchange.Transactions)
        {
            if (transaction.IdentifierCode != "204")
            {
                continue;
            }

            loads.Add(ReadTransaction(transaction, interchange, ediText, diagnostics));
        }

        return loads;
    }

    /// <summary>
    /// Walks one 204 transaction set.
    /// </summary>
    private static Load ReadTransaction(
        TransactionSet transaction,
        Interchange interchange,
        string ediText,
        IReadOnlyList<string> diagnostics)
    {
        // Header accumulators.
        string shipmentId = string.Empty;
        string scac = string.Empty;
        string paymentMethod = string.Empty;
        string purposeCode = "00";
        string equipmentCode = string.Empty;
        string equipmentLength = string.Empty;
        string temperature = string.Empty;
        string trailerNumber = string.Empty;
        decimal? totalWeight = null;
        string weightQualifier = string.Empty;
        var headerReferences = new List<ReferenceNumber>();
        var headerNotes = new List<string>();
        Party? billTo = null;

        // Stop accumulators. `current` is null until the first S5 is seen, which is what
        // makes "header or stop?" a single null check rather than a flag.
        var stops = new List<StopBuilder>();
        StopBuilder? current = null;

        // The N1 loop currently open, header or stop. N3/N4/G61 attach to whichever it is.
        PartyBuilder? party = null;

        void CloseParty()
        {
            if (party is null)
            {
                return;
            }

            if (current is null)
            {
                billTo ??= party.Build();
            }
            else
            {
                current.Location = party.Build();
            }

            party = null;
        }

        foreach (Segment segment in transaction.Segments)
        {
            switch (segment.Id)
            {
                case "B2":
                    // B201 tariff service, B202 SCAC, B203 SPLC, B204 shipment id,
                    // B205 weight unit, B206 method of payment.
                    scac = segment[2].Trim();
                    shipmentId = segment[4].Trim();
                    paymentMethod = segment[6].Trim();
                    break;

                case "B2A":
                    // B2A01 transaction set purpose: 00 original, 01 cancellation, 04 change.
                    purposeCode = segment[1].Trim();
                    break;

                case "N7":
                    // Equipment. N702 is the trailer number when the tender pre-assigns
                    // one, N711 the equipment description code, N713 temperature control,
                    // N715 length in feet.
                    trailerNumber = segment[2].Trim();
                    equipmentCode = segment[11].Trim();
                    temperature = segment[13].Trim();
                    equipmentLength = segment[15].Trim();
                    break;

                case "L3":
                    // Total weight for the move. L301 weight, L302 weight qualifier.
                    totalWeight = X12Values.ReadDecimal(segment[1]);
                    weightQualifier = segment[2].Trim();
                    break;

                case "S5":
                    CloseParty();
                    current = new StopBuilder
                    {
                        Sequence = X12Values.ReadInt(segment[1]) ?? stops.Count + 1,
                        ReasonCode = segment[2].Trim(),
                        Weight = X12Values.ReadDecimal(segment[3]),
                        WeightUnit = segment[4].Trim(),
                        Units = X12Values.ReadDecimal(segment[5]),
                        UnitOfMeasure = segment[6].Trim(),
                    };
                    stops.Add(current);
                    break;

                case "N1":
                    CloseParty();
                    party = new PartyBuilder
                    {
                        EntityIdentifierCode = segment[1].Trim(),
                        Name = segment[2].Trim(),
                        IdQualifier = segment[3].Trim(),
                        IdCode = segment[4].Trim(),
                    };
                    break;

                case "N3":
                    if (party is not null)
                    {
                        party.Address1 = segment[1].Trim();
                        party.Address2 = segment[2].Trim();
                    }

                    break;

                case "N4":
                    if (party is not null)
                    {
                        party.City = segment[1].Trim();
                        party.State = segment[2].Trim();
                        party.PostalCode = segment[3].Trim();
                        string country = segment[4].Trim();
                        if (country.Length > 0)
                        {
                            party.Country = country;
                        }
                    }

                    break;

                case "G61":
                    // G6101 contact function, G6102 name, G6103 comm qualifier, G6104 number.
                    if (party is not null)
                    {
                        party.ContactName = segment[2].Trim();
                        if (segment[3].Trim() == "TE")
                        {
                            party.ContactPhone = segment[4].Trim();
                        }
                    }

                    break;

                case "L11":
                    {
                        var reference = new ReferenceNumber
                        {
                            Value = segment[1].Trim(),
                            Qualifier = segment[2].Trim(),
                        };

                        if (current is null)
                        {
                            headerReferences.Add(reference);
                        }
                        else
                        {
                            current.References.Add(reference);
                        }

                        break;
                    }

                case "G62":
                    if (current is not null)
                    {
                        current.Dates.Add(segment);
                    }

                    break;

                case "NTE":
                    {
                        // NTE01 is a note reference code, NTE02 the text. The text is what
                        // the driver is actually told.
                        string note = segment[2].Trim();
                        if (note.Length == 0)
                        {
                            break;
                        }

                        if (current is null)
                        {
                            headerNotes.Add(note);
                        }
                        else
                        {
                            current.Notes.Add(note);
                        }

                        break;
                    }

                case "L5":
                    // L502 is the lading description — what is actually on the truck.
                    if (current is not null)
                    {
                        string description = segment[2].Trim();
                        if (description.Length > 0)
                        {
                            current.Commodities.Add(description);
                        }
                    }

                    break;

                case "OID":
                    // OID02 is the purchase order this stop's freight belongs to. It is a
                    // reference by any other name, so it joins the stop's reference list
                    // and shows up in the same place on the board.
                    if (current is not null)
                    {
                        string purchaseOrder = segment[2].Trim();
                        if (purchaseOrder.Length > 0)
                        {
                            current.References.Add(new ReferenceNumber
                            {
                                Value = purchaseOrder,
                                Qualifier = "PO",
                            });
                        }
                    }

                    break;
            }
        }

        CloseParty();

        return new Load
        {
            ShipmentId = shipmentId,
            Scac = scac,
            PaymentMethod = paymentMethod,
            PurposeCode = purposeCode,
            EquipmentCode = equipmentCode,
            EquipmentLength = equipmentLength,
            TemperatureControl = temperature,
            TrailerNumber = trailerNumber,
            TotalWeight = totalWeight,
            WeightQualifier = weightQualifier,
            References = headerReferences,
            Notes = headerNotes,
            BillTo = billTo,
            Stops = stops.OrderBy(s => s.Sequence).Select(s => s.Build()).ToList(),
            TenderedBy = interchange.SenderId,
            TenderedTo = interchange.ReceiverId,
            IsProduction = interchange.IsProduction,
            SourceEdi = ediText,
            TenderDiagnostics = diagnostics,
            Status = LoadStatus.Tendered,
        };
    }

    /// <summary>
    /// Mutable scratch for an S5 loop. The loop's segments arrive one at a time and the
    /// model they build is immutable, so something has to hold the pieces in between.
    /// </summary>
    private sealed class StopBuilder
    {
        public int Sequence { get; init; }

        public string ReasonCode { get; init; } = string.Empty;

        public decimal? Weight { get; init; }

        public string WeightUnit { get; init; } = string.Empty;

        public decimal? Units { get; init; }

        public string UnitOfMeasure { get; init; } = string.Empty;

        public Party Location { get; set; } = new();

        public List<ReferenceNumber> References { get; } = new();

        public List<string> Notes { get; } = new();

        public List<string> Commodities { get; } = new();

        public List<Segment> Dates { get; } = new();

        public Stop Build() => new()
        {
            Sequence = Sequence,
            ReasonCode = ReasonCode,
            Weight = Weight,
            WeightUnit = WeightUnit,
            Units = Units,
            UnitOfMeasure = UnitOfMeasure,
            Location = Location,
            References = References,
            Notes = Notes,
            Commodities = Commodities,
            Window = BuildWindow(Dates),
        };
    }

    /// <summary>
    /// Collapses a stop's G62 segments into one window.
    /// </summary>
    /// <remarks>
    /// The qualifier in G6201 says which end of the window the date is. Splitting them into
    /// "opens the window" and "closes the window" is the whole job; the rest is that a
    /// tender frequently sends only one of the two, which is an open-ended request and not
    /// a defect. The board shows what it was given rather than filling in a closing time
    /// nobody agreed to.
    /// </remarks>
    private static StopWindow BuildWindow(IReadOnlyList<Segment> dates)
    {
        DateTime? earliest = null;
        DateTime? latest = null;
        string timeCode = string.Empty;

        foreach (Segment g62 in dates)
        {
            DateTime? date = X12Values.ReadDate(g62[2]);
            if (date is null)
            {
                continue;
            }

            DateTime value = X12Values.ApplyTime(date.Value, g62[4]);

            string code = g62[5].Trim();
            if (code.Length > 0)
            {
                timeCode = code;
            }

            switch (g62[1].Trim())
            {
                // Opens the window.
                case "02":  // Delivery Requested on This Date
                case "10":  // Requested Ship Date / Pickup Date
                case "37":  // Ship Not Before Date
                case "53":  // Deliver Not Before Date
                case "68":  // Requested Delivery Date
                case "69":  // Scheduled Pickup Date
                case "70":  // Scheduled Delivery Date
                case "78":  // Delivery Appointment Scheduled Date
                    if (earliest is null || value < earliest)
                    {
                        earliest = value;
                    }

                    break;

                // Closes it.
                case "38":  // Ship Not Later Than Date
                case "54":  // Deliver No Later Than Date
                    if (latest is null || value > latest)
                    {
                        latest = value;
                    }

                    break;
            }
        }

        return new StopWindow
        {
            Earliest = earliest,
            Latest = latest,
            TimeCode = timeCode,
        };
    }

    /// <summary>Mutable scratch for an N1 loop, for the same reason as <see cref="StopBuilder"/>.</summary>
    private sealed class PartyBuilder
    {
        public string EntityIdentifierCode { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string IdQualifier { get; init; } = string.Empty;

        public string IdCode { get; init; } = string.Empty;

        public string Address1 { get; set; } = string.Empty;

        public string Address2 { get; set; } = string.Empty;

        public string City { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public string PostalCode { get; set; } = string.Empty;

        public string Country { get; set; } = "US";

        public string ContactName { get; set; } = string.Empty;

        public string ContactPhone { get; set; } = string.Empty;

        public Party Build() => new()
        {
            EntityIdentifierCode = EntityIdentifierCode,
            Name = Name,
            IdQualifier = IdQualifier,
            IdCode = IdCode,
            Address1 = Address1,
            Address2 = Address2,
            City = City,
            State = State,
            PostalCode = PostalCode,
            Country = Country,
            ContactName = ContactName,
            ContactPhone = ContactPhone,
        };
    }
}
