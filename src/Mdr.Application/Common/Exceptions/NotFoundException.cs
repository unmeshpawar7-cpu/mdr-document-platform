namespace Mdr.Application.Common.Exceptions;

/// <summary>Thrown when a requested resource does not exist. Mapped to HTTP 404.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource, object key)
        : base($"{resource} '{key}' was not found.")
    {
        Resource = resource;
        Key = key.ToString() ?? string.Empty;
    }

    public string Resource { get; }

    public string Key { get; }
}
