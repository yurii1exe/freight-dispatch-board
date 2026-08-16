using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Api;

/// <summary>
/// Invented load tenders, so that a freshly started board has something on it.
/// </summary>
/// <remarks>
/// <para>Every shipper, consignee, address, contact, SCAC, load number, purchase order and
/// bill of lading below is fictional. Nothing here is derived from a real trading partner,
/// a real implementation guide or a real interchange.</para>
/// <para>These are rendered to X12 by <see cref="Edi204Writer"/> and then fed to the board
/// through <see cref="LoadBoard.Tender"/> like any other file. Constructing
/// <see cref="Load"/> objects and inserting them directly would be shorter and would mean
/// the seeded rows had never been through the parser — which is precisely the part worth
/// exercising every time the process starts.</para>
/// </remarks>
public static class DemoTenders
{
    private sealed record Lane(
        string ShipmentId,
        string Scac,
        string Equipment,
        string Length,
        string Temperature,
        string Trailer,
        decimal Weight,
        decimal Units,
        string UnitOfMeasure,
        string Commodity,
        string Bol,
        string PurchaseOrder,
        string Note,
        string ShipperName,
        string ShipperAddress,
        string ShipperCity,
        string ShipperState,
        string ShipperZip,
        string ShipperContact,
        string ShipperPhone,
        int PickupDayOffset,
        int PickupOpenHour,
        int PickupCloseHour,
        string ConsigneeName,
        string ConsigneeAddress,
        string ConsigneeCity,
        string ConsigneeState,
        string ConsigneeZip,
        string ConsigneeContact,
        string ConsigneePhone,
        int DeliveryDayOffset,
        int DeliveryOpenHour,
        int DeliveryCloseHour,
        LoadStatus SeedTo);

    private static readonly Lane[] Lanes =
    {
        new("LD10041903", "DEMO", "TF", "53", "", "", 41800m, 26, "PL",
            "PAPER TOWELS AND TISSUE", "BOL8842401", "PO-556410",
            "NO DRIVER ASSIST. DOCK LEVELERS AVAILABLE.",
            "GREAT LAKES PAPER COMPANY", "7700 WEST 79TH STREET", "BRIDGEVIEW", "IL", "60455",
            "TERRY OKONKWO", "7085550142", 1, 6, 11,
            "MIDLAND RETAIL DC 7", "9100 NORTHEAST 38TH TERRACE", "KANSAS CITY", "MO", "64161",
            "JOELLE BRANDT", "8165550119", 2, 5, 12,
            LoadStatus.InTransit),

        new("LD10041917", "DEMO", "RT", "53", "28F", "531220", 36200m, 20, "PL",
            "FROZEN BAKERY PRODUCTS", "BOL8842418", "PO-556433",
            "PRE-COOL TRAILER TO 28F. TEMP DOWNLOAD REQUIRED AT DELIVERY.",
            "LAKESHORE FROZEN FOODS", "3315 SOUTH ASHLAND AVENUE", "CHICAGO", "IL", "60608",
            "IRENE VALDEZ", "3125550163", 1, 4, 9,
            "SUMMIT GROCERY DISTRIBUTION", "1201 EAST 33RD AVENUE", "DENVER", "CO", "80205",
            "RAY PETTIFORD", "3035550187", 3, 6, 14,
            LoadStatus.Loaded),

        new("LD10041925", "SMPL", "FT", "48", "", "", 47600m, 8, "PC",
            "ALUMINUM EXTRUSIONS BANDED", "BOL8842427", "PO-556449",
            "TARPS REQUIRED. LOAD SECUREMENT INSPECTED BEFORE DEPARTURE.",
            "CASCADE ALUMINUM WORKS", "4200 NORTHWEST YEON AVENUE", "PORTLAND", "OR", "97210",
            "HANK BROUSSARD", "5035550171", 0, 7, 13,
            "SIERRA WINDOW SYSTEMS", "2850 SOUTH 27TH STREET", "PHOENIX", "AZ", "85034",
            "LUZ ARELLANO", "6025550158", 3, 8, 15,
            LoadStatus.AtShipper),

        new("LD10041938", "DEMO", "TF", "53", "", "", 28400m, 14, "PL",
            "AUTOMOTIVE TRIM COMPONENTS", "BOL8842435", "PO-556460",
            "JIT DELIVERY. LATE ARRIVAL MUST BE CALLED IN 2 HOURS AHEAD.",
            "PIEDMONT INJECTION MOLDING", "1800 SOUTH MAIN STREET", "GREENVILLE", "SC", "29601",
            "CLAY MERIWETHER", "8645550136", 0, 5, 10,
            "RIVERBEND ASSEMBLY PLANT 2", "6400 NORTH SHADELAND AVENUE", "INDIANAPOLIS", "IN", "46220",
            "SONIA KRAMARIK", "3175550194", 1, 4, 8,
            LoadStatus.Dispatched),

        new("LD10041944", "TEST", "TF", "53", "", "", 19750m, 9, "PL",
            "PRINTED CARTON BLANKS", "BOL8842446", "PO-556478",
            "LIFTGATE NOT REQUIRED. RECEIVING CLOSES 1600 SHARP.",
            "OLD DOMINION CARTON", "2100 COMMERCE ROAD", "RICHMOND", "VA", "23224",
            "MAE FONTENOT", "8045550149", 1, 8, 12,
            "TIDEWATER PACKAGING SUPPLY", "1425 BRAEBURN DRIVE", "SALEM", "VA", "24153",
            "OSCAR LINDQVIST", "5405550125", 1, 13, 16,
            LoadStatus.Delivered),

        new("LD10041951", "DEMO", "RT", "53", "34F", "", 39900m, 24, "PL",
            "FRESH DAIRY MIXED", "BOL8842459", "PO-556491",
            "PRODUCE PRIORITY. DO NOT BREAK SEAL BEFORE CONSIGNEE.",
            "TWIN RIVERS CREAMERY", "900 SOUTH FRONT STREET", "LA CROSSE", "WI", "54601",
            "BRIDGET ANSELMO", "6085550178", 0, 3, 8,
            "NORTHSTAR MARKETS DC 3", "4550 WEST 78TH STREET", "BLOOMINGTON", "MN", "55435",
            "DEVON ASHWORTH", "9525550166", 0, 12, 18,
            LoadStatus.AtConsignee),

        new("LD10041967", "SMPL", "TF", "53", "", "", 33150m, 20, "PL",
            "PET FOOD PALLETIZED", "BOL8842470", "PO-556503",
            "APPOINTMENT SET. DETENTION AFTER 2 HOURS FREE TIME.",
            "HEARTLAND PET NUTRITION", "1500 INDUSTRIAL BOULEVARD", "TOPEKA", "KS", "66609",
            "ALDEN MCCRARY", "7855550107", 2, 6, 11,
            "GULFPORT SUPPLY WAREHOUSE", "7800 PERKINS RIDGE ROAD", "BATON ROUGE", "LA", "70815",
            "PRIYA NAIR", "2255550183", 3, 7, 15,
            LoadStatus.Tendered),

        new("LD10041972", "DEMO", "FT", "48", "", "", 44300m, 6, "PC",
            "PRECAST CONCRETE PANELS", "BOL8842484", "PO-556517",
            "OVERSIZE PERMIT NOT REQUIRED. CRANE ON SITE 0800 ONLY.",
            "CUMBERLAND PRECAST", "3900 CENTRAL PIKE", "NASHVILLE", "TN", "37214",
            "WYATT DELACROIX", "6155550172", 1, 6, 9,
            "CAPITAL RIDGE CONSTRUCTION", "1200 PEACHTREE INDUSTRIAL COURT", "ATLANTA", "GA", "30318",
            "NADIA FERREIRA", "4045550111", 2, 8, 10,
            LoadStatus.InTransit),
    };

    /// <summary>
    /// Puts the invented tenders on the board and moves each one to the status its lane
    /// says, so a freshly started process shows a board in mixed motion rather than eight
    /// identical rows.
    /// </summary>
    /// <param name="board">The board to seed.</param>
    /// <param name="today">The date the windows are laid out around.</param>
    public static void Seed(LoadBoard board, DateTime today)
    {
        // A separate sequence from the board's outbound numbers. These are the tendering
        // party's control numbers, not ours, and sharing a counter across two directions is
        // how partners end up seeing gaps in a sequence that is supposed to be dense.
        var inbound = new ControlNumbers(4501);
        var writer = new Edi204Writer(inbound);

        foreach (Lane lane in Lanes)
        {
            Load draft = Build(lane, today);
            string edi = writer.Write(draft, today.AddHours(-6));

            Load load = board.Tender(edi).Single();

            // Back-date the events so the history reads like a load that has been running
            // rather than six updates keyed in the same second.
            int step = 0;
            while (StatusCatalog.Next(load.Status, load.StopsRemainAfterCurrent) is { } next &&
                   next <= lane.SeedTo &&
                   step < 24)
            {
                step++;
                board.Advance(load.Id, next, today.AddHours(-2 * ((int)lane.SeedTo - step + 1)));
            }
        }
    }

    private static Load Build(Lane lane, DateTime today) => new()
    {
        ShipmentId = lane.ShipmentId,
        Scac = lane.Scac,
        PaymentMethod = "PP",
        PurposeCode = "00",
        EquipmentCode = lane.Equipment,
        EquipmentLength = lane.Length,
        TemperatureControl = lane.Temperature,
        TrailerNumber = lane.Trailer,
        TotalWeight = lane.Weight,
        WeightQualifier = "G",
        TenderedBy = "DEMOBROKER",
        TenderedTo = "DEMOCARRIER",
        IsProduction = false,
        References = new[]
        {
            new ReferenceNumber { Value = lane.ShipmentId, Qualifier = "OQ" },
            new ReferenceNumber { Value = lane.Bol, Qualifier = "BM" },
        },
        Notes = new[] { lane.Note },
        Stops = new[]
        {
            new Stop
            {
                Sequence = 1,
                ReasonCode = "CL",
                Weight = lane.Weight,
                WeightUnit = "L",
                Units = lane.Units,
                UnitOfMeasure = lane.UnitOfMeasure,
                Commodities = new[] { lane.Commodity },
                References = new[] { new ReferenceNumber { Value = lane.PurchaseOrder, Qualifier = "PO" } },
                Location = new Party
                {
                    EntityIdentifierCode = "SH",
                    Name = lane.ShipperName,
                    IdQualifier = "93",
                    IdCode = Site(lane.ShipperName, lane.ShipperCity),
                    Address1 = lane.ShipperAddress,
                    City = lane.ShipperCity,
                    State = lane.ShipperState,
                    PostalCode = lane.ShipperZip,
                    Country = "US",
                    ContactName = lane.ShipperContact,
                    ContactPhone = lane.ShipperPhone,
                },
                Window = new StopWindow
                {
                    Earliest = today.Date.AddDays(lane.PickupDayOffset).AddHours(lane.PickupOpenHour),
                    Latest = today.Date.AddDays(lane.PickupDayOffset).AddHours(lane.PickupCloseHour),
                    TimeCode = "LT",
                },
            },
            new Stop
            {
                Sequence = 2,
                ReasonCode = "CU",
                Weight = lane.Weight,
                WeightUnit = "L",
                Units = lane.Units,
                UnitOfMeasure = lane.UnitOfMeasure,
                Location = new Party
                {
                    EntityIdentifierCode = "CN",
                    Name = lane.ConsigneeName,
                    IdQualifier = "93",
                    IdCode = Site(lane.ConsigneeName, lane.ConsigneeCity),
                    Address1 = lane.ConsigneeAddress,
                    City = lane.ConsigneeCity,
                    State = lane.ConsigneeState,
                    PostalCode = lane.ConsigneeZip,
                    Country = "US",
                    ContactName = lane.ConsigneeContact,
                    ContactPhone = lane.ConsigneePhone,
                },
                Window = new StopWindow
                {
                    Earliest = today.Date.AddDays(lane.DeliveryDayOffset).AddHours(lane.DeliveryOpenHour),
                    Latest = today.Date.AddDays(lane.DeliveryDayOffset).AddHours(lane.DeliveryCloseHour),
                    TimeCode = "LT",
                },
            },
        },
    };

    /// <summary>
    /// Builds the kind of site code a shipper puts in N104 — initials and a city stub. Not
    /// globally meaningful, which is exactly what real ones are like.
    /// </summary>
    private static string Site(string name, string city)
    {
        string initials = new string(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2)
            .Take(3)
            .Select(word => word[0])
            .ToArray());

        return $"{initials}-{new string(city.Where(char.IsLetter).Take(3).ToArray()).ToUpperInvariant()}";
    }
}
