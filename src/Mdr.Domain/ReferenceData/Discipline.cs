namespace Mdr.Domain.ReferenceData;

/// <summary>Engineering discipline that owns a deliverable (e.g. <c>MEC</c> Mechanical).</summary>
public sealed class Discipline : ReferenceDataEntity
{
    private Discipline()
    {
    }

    public Discipline(int id, string code, string name)
        : base(id, code, name)
    {
    }
}
