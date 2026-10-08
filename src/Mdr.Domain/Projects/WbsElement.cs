using Mdr.Domain.Common;

namespace Mdr.Domain.Projects;

/// <summary>A node of a project's Work Breakdown Structure. Documents are planned against WBS elements.</summary>
public sealed class WbsElement
{
    public const int MaxNameLength = 200;

    private WbsElement()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int Level { get; private set; }

    public string? ParentCode { get; private set; }

    internal static WbsElement Create(Guid projectId, string code, string name)
    {
        return new WbsElement
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Code = code,
            Name = Text.Required(name, "wbs_name", MaxNameLength),
            Level = WbsCode.GetLevel(code),
            ParentCode = WbsCode.GetParent(code),
        };
    }
}
