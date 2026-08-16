using System.Globalization;
using System.Text;
using EdiX12.Core;

namespace FreightDispatch.Core.Edi;

/// <summary>
/// Builds an X12 interchange: the ISA/GS/ST envelope, correct control numbers, and an
/// SE01 segment count that is actually right.
/// </summary>
/// <remarks>
/// <para>Writing X12 is easier than reading it and gets done badly more often, because the
/// failures are on the partner's side. The three that cause most rejections are all
/// handled here rather than left to the caller:</para>
/// <list type="number">
/// <item><description>The ISA is fixed-width. Every element has a defined length and the
/// segment is exactly 105 characters plus its terminator. Receivers read the delimiters
/// out of it by byte offset, so an ISA one character short is not a formatting nit — it is
/// a file the other end cannot tokenize.</description></item>
/// <item><description>SE01 counts the ST and the SE themselves. Counting only the body is
/// the single most common reason a structurally fine document is rejected.</description></item>
/// <item><description>Trailers echo headers: IEA02 = ISA13, GE02 = GS06, SE02 = ST02.
/// The writer copies them rather than being told them twice.</description></item>
/// </list>
/// </remarks>
public sealed class X12Writer
{
    /// <summary>
    /// The fixed widths of ISA01–ISA16, in order. ISA is the only fixed-width segment in
    /// X12; everything after it is delimited.
    /// </summary>
    private static readonly int[] IsaElementWidths =
    {
        2, 10, 2, 10, 2, 15, 2, 15, 6, 4, 1, 5, 9, 1, 1, 1,
    };

    private readonly StringBuilder _output = new();
    private readonly X12Delimiters _delimiters;

    private string _interchangeControlNumber = string.Empty;
    private string _groupControlNumber = string.Empty;
    private string _transactionControlNumber = string.Empty;

    private int _groupCount;
    private int _transactionCount;
    private int _segmentCount;

    /// <summary>Creates a writer.</summary>
    /// <param name="delimiters">
    /// The delimiters to write with. <see cref="X12Delimiters.Default"/> is the
    /// conventional <c>* : ~ ^</c>; a partner who wants something else gets it here, and
    /// the ISA declares whatever is chosen.
    /// </param>
    public X12Writer(X12Delimiters? delimiters = null)
    {
        _delimiters = delimiters ?? X12Delimiters.Default;
    }

    /// <summary>
    /// Opens the interchange.
    /// </summary>
    /// <param name="senderQualifier">ISA05, e.g. <c>ZZ</c> mutually defined or <c>02</c> SCAC.</param>
    /// <param name="senderId">ISA06, padded to 15.</param>
    /// <param name="receiverQualifier">ISA07.</param>
    /// <param name="receiverId">ISA08, padded to 15.</param>
    /// <param name="timestamp">ISA09/ISA10. Sender's local time; X12 carries no zone.</param>
    /// <param name="controlNumber">ISA13, zero-padded to 9. IEA02 will echo it.</param>
    /// <param name="production">ISA15: <c>P</c> when true, <c>T</c> when false.</param>
    public X12Writer BeginInterchange(
        string senderQualifier,
        string senderId,
        string receiverQualifier,
        string receiverId,
        DateTime timestamp,
        string controlNumber,
        bool production)
    {
        _interchangeControlNumber = controlNumber.PadLeft(9, '0');

        // ISA11 is the repetition separator in 5010. In 4010 the same position carries the
        // Interchange Control Standards Identifier, conventionally 'U'. Writing 00501 in
        // ISA12 and 'U' in ISA11 is a contradiction that some receivers act on, so the
        // repetition separator goes in and ISA12 says 00501.
        string repetitionSeparator = (_delimiters.Repetition ?? '^').ToString();

        var elements = new[]
        {
            "00",
            string.Empty,
            "00",
            string.Empty,
            senderQualifier,
            senderId,
            receiverQualifier,
            receiverId,
            timestamp.ToString("yyMMdd", CultureInfo.InvariantCulture),
            timestamp.ToString("HHmm", CultureInfo.InvariantCulture),
            repetitionSeparator,
            "00501",
            _interchangeControlNumber,
            "0",
            production ? "P" : "T",
            _delimiters.Component.ToString(),
        };

        _output.Append("ISA");
        for (int i = 0; i < elements.Length; i++)
        {
            _output.Append(_delimiters.Element);
            _output.Append(FitIsaElement(elements[i], IsaElementWidths[i]));
        }

        _output.Append(_delimiters.Segment);
        AppendLineBreak();

        return this;
    }

    /// <summary>
    /// Opens a functional group.
    /// </summary>
    /// <param name="functionalIdentifierCode">GS01: <c>QM</c> for 214, <c>SM</c> for 204, <c>IM</c> for 210, <c>FA</c> for 997.</param>
    /// <param name="applicationSender">GS02. Distinct from ISA06 and often a different value.</param>
    /// <param name="applicationReceiver">GS03.</param>
    /// <param name="timestamp">GS04/GS05. GS04 is <c>CCYYMMDD</c> in 5010, unlike ISA09.</param>
    /// <param name="controlNumber">GS06. GE02 will echo it.</param>
    public X12Writer BeginGroup(
        string functionalIdentifierCode,
        string applicationSender,
        string applicationReceiver,
        DateTime timestamp,
        string controlNumber)
    {
        _groupControlNumber = controlNumber;
        _transactionCount = 0;

        Segment(
            "GS",
            functionalIdentifierCode,
            applicationSender,
            applicationReceiver,
            timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            timestamp.ToString("HHmm", CultureInfo.InvariantCulture),
            controlNumber,
            "X",
            "005010");

        return this;
    }

    /// <summary>
    /// Opens a transaction set and starts counting segments for SE01.
    /// </summary>
    /// <param name="identifierCode">ST01: <c>214</c>.</param>
    /// <param name="controlNumber">ST02, four to nine digits. SE02 will echo it.</param>
    public X12Writer BeginTransaction(string identifierCode, string controlNumber)
    {
        _transactionControlNumber = controlNumber;
        _segmentCount = 0;

        // Deliberately no ST03. The implementation convention reference is for transaction
        // sets that have a published HIPAA-style guide identifier; the motor carrier 214
        // does not, and 005010X214 belongs to a health care transaction entirely.
        Segment("ST", identifierCode, controlNumber);
        return this;
    }

    /// <summary>
    /// Writes one segment. Trailing empty elements are dropped, which is the convention —
    /// a segment states as many elements as it has something to say about.
    /// </summary>
    /// <param name="id">Segment identifier, e.g. <c>AT7</c>.</param>
    /// <param name="elements">Element values in order, element 01 first. Nulls are empty elements.</param>
    public X12Writer Segment(string id, params string?[] elements)
    {
        int last = elements.Length - 1;
        while (last >= 0 && string.IsNullOrEmpty(elements[last]))
        {
            last--;
        }

        _output.Append(id);
        for (int i = 0; i <= last; i++)
        {
            _output.Append(_delimiters.Element);
            _output.Append(Sanitize(elements[i]));
        }

        _output.Append(_delimiters.Segment);
        AppendLineBreak();

        _segmentCount++;
        return this;
    }

    /// <summary>
    /// Closes the transaction set, writing an SE01 that counts the ST and the SE as well as
    /// the body.
    /// </summary>
    public X12Writer EndTransaction()
    {
        // _segmentCount already includes the ST written by BeginTransaction; +1 is the SE
        // about to be written. This is the arithmetic that gets files rejected.
        int declared = _segmentCount + 1;
        _output.Append("SE")
            .Append(_delimiters.Element).Append(declared.ToString(CultureInfo.InvariantCulture))
            .Append(_delimiters.Element).Append(_transactionControlNumber)
            .Append(_delimiters.Segment);
        AppendLineBreak();

        _transactionCount++;
        return this;
    }

    /// <summary>Closes the functional group with a GE that counts its transaction sets and echoes GS06.</summary>
    public X12Writer EndGroup()
    {
        _output.Append("GE")
            .Append(_delimiters.Element).Append(_transactionCount.ToString(CultureInfo.InvariantCulture))
            .Append(_delimiters.Element).Append(_groupControlNumber)
            .Append(_delimiters.Segment);
        AppendLineBreak();

        _groupCount++;
        return this;
    }

    /// <summary>Closes the interchange with an IEA that counts its groups and echoes ISA13.</summary>
    public X12Writer EndInterchange()
    {
        _output.Append("IEA")
            .Append(_delimiters.Element).Append(_groupCount.ToString(CultureInfo.InvariantCulture))
            .Append(_delimiters.Element).Append(_interchangeControlNumber)
            .Append(_delimiters.Segment);
        AppendLineBreak();

        return this;
    }

    /// <summary>The finished interchange.</summary>
    public override string ToString() => _output.ToString();

    /// <summary>
    /// Segments are written one per line. X12 does not require it — the terminator is the
    /// only thing that ends a segment — but every human who has to read the file wants it,
    /// and a reader that cannot cope with the line breaks is broken anyway.
    /// </summary>
    private void AppendLineBreak()
    {
        if (_delimiters.Segment != '\n' && _delimiters.Segment != '\r')
        {
            _output.Append('\n');
        }
    }

    /// <summary>
    /// Pads or truncates an ISA element to its fixed width.
    /// </summary>
    /// <remarks>
    /// Truncation is silent and that is the correct behaviour: ISA06 is 15 characters and a
    /// 16-character sender ID is a configuration error that must not be allowed to shift
    /// every later offset in the segment. The alternative — emitting a malformed ISA — is
    /// worse, because the receiver reports it as an unreadable file rather than a wrong ID.
    /// </remarks>
    private static string FitIsaElement(string? value, int width)
    {
        string text = value ?? string.Empty;
        return text.Length >= width ? text.Substring(0, width) : text.PadRight(width);
    }

    /// <summary>
    /// Removes delimiter characters from element data.
    /// </summary>
    /// <remarks>
    /// The one rule X12 places on data is that it must not contain the characters the
    /// interchange declared as delimiters — there is no escape sequence. A consignee named
    /// <c>SMITH * SONS</c> tendered into a star-delimited interchange silently becomes two
    /// elements and shifts everything after it, so the star comes out here instead.
    /// </remarks>
    private string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        Span<char> forbidden = stackalloc char[4];
        int count = 0;
        forbidden[count++] = _delimiters.Element;
        forbidden[count++] = _delimiters.Component;
        forbidden[count++] = _delimiters.Segment;
        if (_delimiters.Repetition.HasValue)
        {
            forbidden[count++] = _delimiters.Repetition.Value;
        }

        var builder = new StringBuilder(value!.Length);
        foreach (char c in value)
        {
            bool drop = false;
            for (int i = 0; i < count; i++)
            {
                if (c == forbidden[i])
                {
                    drop = true;
                    break;
                }
            }

            if (!drop && c != '\r' && c != '\n')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
