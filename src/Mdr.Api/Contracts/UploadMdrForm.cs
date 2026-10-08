using System.ComponentModel.DataAnnotations;

namespace Mdr.Api.Contracts;

/// <summary>Multipart form for <c>POST /api/mdr/upload</c>.</summary>
public sealed class UploadMdrForm
{
    /// <summary>Target project id.</summary>
    [Required]
    public Guid? ProjectId { get; set; }

    /// <summary>The MDR CSV file (UTF-8, header row required).</summary>
    [Required]
    public IFormFile? File { get; set; }
}
