namespace Mdr.Domain.ReferenceData;

/// <summary>Allow-list joining document types to the gate sets they may use.</summary>
public sealed class DocumentTypeGateSet
{
    private DocumentTypeGateSet()
    {
    }

    public DocumentTypeGateSet(int documentTypeId, int gateSetId)
    {
        DocumentTypeId = documentTypeId;
        GateSetId = gateSetId;
    }

    public int DocumentTypeId { get; private set; }

    public int GateSetId { get; private set; }
}
