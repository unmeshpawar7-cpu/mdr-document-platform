using FluentValidation;
using Mdr.Domain.Projects;

namespace Mdr.Application.Projects;

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(Project.MaxCodeLength)
            .Must(code => Project.IsValidCode(code?.Trim().ToUpperInvariant()))
            .WithMessage("Code must be 2-5 letters, a hyphen and three digits (e.g. NWP-001).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Project.MaxNameLength);
        RuleFor(x => x.Client).NotEmpty().MaximumLength(Project.MaxClientLength);
    }
}

public sealed class AddWbsElementsRequestValidator : AbstractValidator<AddWbsElementsRequest>
{
    public const int MaxElements = 500;

    public AddWbsElementsRequestValidator()
    {
        RuleFor(x => x.Elements)
            .NotNull()
            .Must(e => e is { Count: > 0 and <= MaxElements })
            .WithMessage($"Provide between 1 and {MaxElements} WBS elements.");

        RuleFor(x => x.Elements)
            .Must(e => e is null || e.Select(i => i.Code?.Trim()).Distinct(StringComparer.Ordinal).Count() == e.Count)
            .WithMessage("WBS codes must be unique within the request.");

        RuleForEach(x => x.Elements).ChildRules(element =>
        {
            element.RuleFor(e => e.Code)
                .NotEmpty()
                .Must(code => WbsCode.IsValid(code?.Trim()))
                .WithMessage("WBS code must be dot-separated numeric segments such as 1.2.3.");
            element.RuleFor(e => e.Name).NotEmpty().MaximumLength(WbsElement.MaxNameLength);
        });
    }
}
