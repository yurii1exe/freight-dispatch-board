using FreightDispatch.Core.Model;

namespace FreightDispatch.Core;

/// <summary>
/// What came of receiving one interchange: what went on the board and the 997 that was sent
/// back about it.
/// </summary>
/// <remarks>
/// The acknowledgment is returned alongside the loads rather than left for the caller to go
/// and find, because "nothing appeared on the board" has at least four causes — the file was
/// not a 204 at all, it was addressed to a functional group this board does not handle, it
/// was a group with no transaction sets in it, or it genuinely contained nothing — and the
/// partner has already been told which. The operator should be too.
/// </remarks>
/// <param name="Loads">Every load tender the interchange contained, now on the board.</param>
/// <param name="Acknowledgment">The 997 that was generated and sent.</param>
public sealed record TenderReceipt(
    IReadOnlyList<Load> Loads,
    FunctionalAcknowledgment Acknowledgment)
{
    /// <summary>True when at least one load tender came out of the interchange.</summary>
    public bool HasLoads => Loads.Count > 0;

    /// <summary>
    /// How many of those loads were reported to the partner as rejected. They are on the
    /// board anyway — see the remarks on <see cref="LoadBoard.Receive"/> for why — and they
    /// are the rows a supervisor wants to see first.
    /// </summary>
    public int RejectedCount => Loads.Count(l => l.TenderRejected);

    /// <summary>
    /// Why nothing usable came out, in a sentence that can go straight to a person.
    /// </summary>
    /// <remarks>
    /// Every branch names the element that decided it. "Invalid file" costs an hour;
    /// "GS01 is IM, which is the invoice group — a load tender is SM" costs ten seconds.
    /// </remarks>
    public string Explanation
    {
        get
        {
            if (HasLoads)
            {
                return $"{Loads.Count} load tender(s) read, {RejectedCount} of them rejected by the 997.";
            }

            if (Acknowledgment.Groups.Count == 0)
            {
                return
                    "The interchange parsed but carries no functional groups, so there is nothing " +
                    "to read and nothing a 997 can acknowledge.";
            }

            string groups = string.Join(
                ", ",
                Acknowledgment.Groups.Select(g => g.FunctionalIdentifierCode).Distinct());

            return
                "The interchange parsed, but it contains no 204 transaction sets. It carries " +
                $"functional group(s) '{groups}' — a load tender is GS01 'SM', ST01 '204'. A 214 or " +
                "a 990 will parse perfectly and still not be a load tender.";
        }
    }
}
