namespace Mdr.Domain.Documents;

/// <summary>Planning status of a deliverable in the register.</summary>
public enum DocumentStatus
{
    Planned = 0,
    InProgress = 1,
    Issued = 2,
    OnHold = 3,
    Cancelled = 4,
}
