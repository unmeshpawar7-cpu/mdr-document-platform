namespace Mdr.Domain.ReferenceData;

/// <summary>Kind of deliverable (e.g. <c>DWG</c> Drawing, <c>DS</c> Datasheet).</summary>
public sealed class DocumentType : ReferenceDataEntity
{
    private DocumentType()
    {
    }

    public DocumentType(int id, string code, string name)
        : base(id, code, name)
    {
    }
}
