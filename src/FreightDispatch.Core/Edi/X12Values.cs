using System.Globalization;

namespace FreightDispatch.Core.Edi;

/// <summary>
/// Conversions between X12 element values and CLR types.
/// </summary>
/// <remarks>
/// Every method here is forgiving on read and strict on write. A partner who sends a
/// six-digit date where the guide says eight is wrong, but rejecting the whole tender over
/// it means a truck does not get loaded, so the reader takes the date and the board carries
/// on. What this project generates, on the other hand, is always in the form the
/// specification asks for.
/// </remarks>
public static class X12Values
{
    /// <summary>
    /// Reads an X12 element 373 date. <c>CCYYMMDD</c> is the 5010 form; <c>YYMMDD</c> is
    /// accepted because 4010 senders still exist and because the ISA never changed.
    /// </summary>
    /// <param name="value">The raw element value.</param>
    /// <returns>The date, or null if it is absent or unreadable.</returns>
    public static DateTime? ReadDate(string? value)
    {
        string date = (value ?? string.Empty).Trim();

        if (DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime parsed))
        {
            return parsed;
        }

        // A two-digit year has no century. 70 and above is 19xx, matching the pivot the
        // ISA uses, so the two readers cannot disagree about the same file.
        if (DateTime.TryParseExact(date, "yyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Reads an X12 element 337 time — <c>HHMM</c>, optionally with seconds or decimal
    /// seconds appended — and applies it to a date.
    /// </summary>
    /// <param name="date">The date the time belongs to.</param>
    /// <param name="value">The raw time element.</param>
    /// <returns>
    /// <paramref name="date"/> with the time applied, or <paramref name="date"/> unchanged
    /// when the time is absent or unreadable. Times carry no zone: X12 has none, and
    /// element 623 says which zone the sender meant.
    /// </returns>
    public static DateTime ApplyTime(DateTime date, string? value)
    {
        string time = (value ?? string.Empty).Trim();
        if (time.Length < 4)
        {
            return date;
        }

        if (!int.TryParse(time.Substring(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hours) ||
            !int.TryParse(time.Substring(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) ||
            hours > 23 || minutes > 59)
        {
            return date;
        }

        return date.Date.AddHours(hours).AddMinutes(minutes);
    }

    /// <summary>Reads a numeric element, returning null rather than throwing on junk.</summary>
    /// <param name="value">The raw element value.</param>
    public static decimal? ReadDecimal(string? value) =>
        decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;

    /// <summary>Reads an integer element, returning null rather than throwing on junk.</summary>
    /// <param name="value">The raw element value.</param>
    public static int? ReadInt(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    /// <summary>Formats a date as element 373 <c>CCYYMMDD</c>.</summary>
    /// <param name="value">The date to write.</param>
    public static string WriteDate(DateTime value) =>
        value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Formats a time as element 337 <c>HHMM</c>.</summary>
    /// <param name="value">The time to write.</param>
    public static string WriteTime(DateTime value) =>
        value.ToString("HHmm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a number for the wire. X12 numeric elements carry no thousands separators
    /// and no trailing zeros beyond what the sender means, so this trims both.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public static string WriteDecimal(decimal value)
    {
        string text = value.ToString("0.############", CultureInfo.InvariantCulture);
        return text;
    }
}
