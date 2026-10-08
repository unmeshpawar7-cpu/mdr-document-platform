namespace Mdr.Domain.Validation;

/// <summary>Severity of a validation finding. Only <see cref="Error"/> makes a row invalid.</summary>
public enum IssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}
