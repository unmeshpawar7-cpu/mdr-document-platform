namespace Mdr.Application.Projects;

/// <summary>Request to register a new project.</summary>
/// <param name="Code">Project code, 2-5 letters + hyphen + 3 digits, e.g. <c>NWP-001</c>.</param>
/// <param name="Name">Project name, e.g. <c>Northwind Water Plant</c>.</param>
/// <param name="Client">Client organisation name.</param>
public sealed record CreateProjectRequest(string Code, string Name, string Client);

public sealed record WbsElementInput(string Code, string Name);

/// <summary>Bulk WBS creation. Parents may be created in the same request (elements are applied by level).</summary>
public sealed record AddWbsElementsRequest(IReadOnlyList<WbsElementInput> Elements);

public sealed record ProjectDto(Guid Id, string Code, string Name, string Client, string Status, DateTime CreatedAtUtc, string CreatedBy);

public sealed record WbsElementDto(Guid Id, string Code, string Name, int Level, string? ParentCode);

public sealed record ProjectDetailsDto(
    Guid Id,
    string Code,
    string Name,
    string Client,
    string Status,
    DateTime CreatedAtUtc,
    string CreatedBy,
    int DocumentCount,
    int UploadCount,
    IReadOnlyList<WbsElementDto> WbsElements);

public sealed record ProjectListFilter(string? Search, string? Status);
