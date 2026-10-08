namespace Mdr.Domain.ReferenceData;

/// <summary>
/// A gate set is the ordered sequence of issue gates a deliverable passes through,
/// e.g. <c>STD</c> = IFR &gt; IFA &gt; IFC. Stages are stored as a comma-separated list.
/// </summary>
public sealed class GateSet : ReferenceDataEntity
{
    public const int MaxStagesLength = 100;
    public const int MaxDescriptionLength = 300;

    private GateSet()
    {
    }

    public GateSet(int id, string code, string name, string stages, string description)
        : base(id, code, name)
    {
        Stages = stages;
        Description = description;
    }

    /// <summary>Comma-separated gate codes in issue order, e.g. <c>IFR,IFA,IFC</c>.</summary>
    public string Stages { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public IReadOnlyList<string> GetStages() =>
        Stages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
