namespace Mdr.Domain.ReferenceData;

/// <summary>
/// Base type for controlled reference data (disciplines, document types, offices, gate sets).
/// Reference data uses explicit integer keys so it can be seeded deterministically.
/// </summary>
public abstract class ReferenceDataEntity
{
    public const int MaxCodeLength = 10;
    public const int MaxNameLength = 100;

    protected ReferenceDataEntity()
    {
    }

    protected ReferenceDataEntity(int id, string code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
        IsActive = true;
    }

    public int Id { get; protected set; }

    public string Code { get; protected set; } = string.Empty;

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Inactive entries are kept for history but rejected for new MDR rows.</summary>
    public bool IsActive { get; protected set; }
}
