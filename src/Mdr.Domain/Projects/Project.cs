using System.Text.RegularExpressions;
using Mdr.Domain.Common;

namespace Mdr.Domain.Projects;

/// <summary>
/// An engineering project whose Master Deliverables Register (MDR) is managed by the platform.
/// The project code is the first segment of every document number (e.g. <c>NWP-001</c>).
/// </summary>
public sealed class Project
{
    public const int MaxCodeLength = 12;
    public const int MaxNameLength = 200;
    public const int MaxClientLength = 200;
    public const int MaxCreatedByLength = 100;

    private static readonly Regex CodePattern = new(
        @"^[A-Z]{2,5}-\d{3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly List<WbsElement> _wbsElements = new();

    private Project()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Client { get; private set; } = string.Empty;

    public ProjectStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    /// <summary>Optimistic concurrency token, regenerated on every mutation.</summary>
    public Guid Version { get; private set; }

    public IReadOnlyCollection<WbsElement> WbsElements => _wbsElements.AsReadOnly();

    public bool AcceptsUploads => Status == ProjectStatus.Active;

    /// <summary>Project codes are 2-5 upper-case letters, a hyphen and three digits (e.g. <c>NWP-001</c>).</summary>
    public static bool IsValidCode(string? code) => !string.IsNullOrWhiteSpace(code) && CodePattern.IsMatch(code);

    public static Project Create(string code, string name, string client, string createdBy, DateTime nowUtc)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!IsValidCode(normalizedCode))
        {
            throw new DomainException(
                "project_code_invalid",
                "Project code must be 2-5 upper-case letters, a hyphen and three digits (e.g. NWP-001).");
        }

        return new Project
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            Name = Text.Required(name, "project_name", MaxNameLength),
            Client = Text.Required(client, "project_client", MaxClientLength),
            Status = ProjectStatus.Active,
            CreatedAtUtc = nowUtc,
            CreatedBy = Text.Truncate(createdBy, MaxCreatedByLength) ?? "system",
            Version = Guid.NewGuid(),
        };
    }

    /// <summary>
    /// Adds a WBS element. Codes must be unique within the project and the parent element
    /// (e.g. <c>1.2</c> for <c>1.2.3</c>) must already exist.
    /// </summary>
    public WbsElement AddWbsElement(string code, string name)
    {
        var normalizedCode = (code ?? string.Empty).Trim();
        if (!WbsCode.IsValid(normalizedCode))
        {
            throw new DomainException(
                "wbs_code_invalid",
                $"WBS code '{normalizedCode}' is invalid. Use dot-separated numeric segments such as 1.2.3 (max {WbsCode.MaxLevels} levels).");
        }

        if (_wbsElements.Exists(w => w.Code == normalizedCode))
        {
            throw new DomainException("wbs_code_duplicate", $"WBS code '{normalizedCode}' already exists in project {Code}.");
        }

        var parent = WbsCode.GetParent(normalizedCode);
        if (parent is not null && !_wbsElements.Exists(w => w.Code == parent))
        {
            throw new DomainException(
                "wbs_parent_missing",
                $"WBS code '{normalizedCode}' requires parent '{parent}' to exist first.");
        }

        var element = WbsElement.Create(Id, normalizedCode, name);
        _wbsElements.Add(element);
        Version = Guid.NewGuid();
        return element;
    }
}
