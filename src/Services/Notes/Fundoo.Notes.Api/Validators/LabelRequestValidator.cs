using FluentValidation;

namespace Fundoo.Notes.Api;

public class CreateLabelRequestValidator : AbstractValidator<CreateLabelRequest>
{
    public CreateLabelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}

public class RenameLabelRequestValidator : AbstractValidator<RenameLabelRequest>
{
    public RenameLabelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}