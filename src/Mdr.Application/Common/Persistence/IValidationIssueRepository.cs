using Mdr.Domain.Validation;

namespace Mdr.Application.Common.Persistence;

public interface IValidationIssueRepository
{
    void AddRange(IEnumerable<ValidationIssue> issues);

    Task<int> DeleteByUploadAsync(Guid uploadId, CancellationToken cancellationToken);
}
