namespace Mdr.Domain.Projects;

/// <summary>Lifecycle state of an engineering project.</summary>
public enum ProjectStatus
{
    /// <summary>Project is live; MDR uploads are accepted.</summary>
    Active = 0,

    /// <summary>Project is paused; uploads are rejected until it is re-activated.</summary>
    OnHold = 1,

    /// <summary>Project is closed out; the register is read-only.</summary>
    Closed = 2,
}
