using System.Globalization;

namespace FreightDispatch.Core.Edi;

/// <summary>
/// Issues the three control numbers an outbound interchange needs.
/// </summary>
/// <remarks>
/// <para>ISA13, GS06 and ST02 must be unique and, by most partners' rules, ascending. They
/// are the only handle either side has when a file goes missing: "resend interchange
/// 000004417" is a supportable request, "resend the one from Tuesday" is not.</para>
/// <para>This implementation is in-memory and resets when the process does, which is
/// correct for a demonstration and wrong for production. Production needs the counter in
/// the same transaction as the outbound document, because issuing a number and then
/// failing to send the file leaves a permanent gap the partner will ask about, and reusing
/// one is worse — a duplicate ISA13 within a partner's retention window is a duplicate
/// interchange, and some receivers silently discard it.</para>
/// </remarks>
public sealed class ControlNumbers
{
    private readonly object _gate = new();

    private int _interchange;
    private int _group;
    private int _transaction;

    /// <summary>Creates a counter.</summary>
    /// <param name="start">
    /// The first number to issue. Starting above 1 is the honest default here: a fresh
    /// sequence starting at 1 against a partner who has seen these numbers before is
    /// exactly the duplicate-interchange case above.
    /// </param>
    public ControlNumbers(int start = 1)
    {
        _interchange = start - 1;
        _group = start - 1;
        _transaction = start - 1;
    }

    /// <summary>Issues the next ISA13, zero-padded to the nine characters the ISA reserves.</summary>
    public string NextInterchange()
    {
        lock (_gate)
        {
            return (++_interchange).ToString(CultureInfo.InvariantCulture).PadLeft(9, '0');
        }
    }

    /// <summary>Issues the next GS06. Not padded — GS06 is a variable-length numeric.</summary>
    public string NextGroup()
    {
        lock (_gate)
        {
            return (++_group).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Issues the next ST02, padded to four digits. Four is the conventional width and the
    /// specification's minimum; nine is the maximum.
    /// </summary>
    public string NextTransaction()
    {
        lock (_gate)
        {
            return (++_transaction).ToString(CultureInfo.InvariantCulture).PadLeft(4, '0');
        }
    }
}
