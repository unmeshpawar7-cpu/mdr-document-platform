namespace Mdr.Domain.Users;

/// <summary>Application roles carried as JWT role claims.</summary>
public static class AppRoles
{
    /// <summary>Manages reference data and can read audit logs; also has all controller rights.</summary>
    public const string Admin = "Admin";

    /// <summary>Document controller: creates projects, maintains WBS and uploads MDR files.</summary>
    public const string DocumentController = "DocumentController";

    /// <summary>Read-only access to projects, uploads, documents and reports.</summary>
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> All { get; } = new[] { Admin, DocumentController, Viewer };

    public static bool IsKnown(string role) => All.Contains(role, StringComparer.Ordinal);
}
