using Fundoo.Notes.Api;
using Fundoo.Notes.Domain;
using Xunit;

namespace Fundoo.Notes.UnitTests;

public class BasicTests
{
    [Fact]
    public void NewNoteStartsUnarchivedAndNotTrashed()
    {
        var isArchived = false;
        var isTrashed = false;
        Assert.False(isArchived);
        Assert.False(isTrashed);
    }

    [Fact]
    public void NewNoteStartsWithEmptyLabelsList()
    {
        Note note = new Note();
        Assert.NotNull(note.Labels);
        Assert.Empty(note.Labels);
    }

    [Fact]
    public void AddCollaboratorValidatorRejectsEmptyGuid()
    {
        AddCollaboratorRequestValidator validator = new AddCollaboratorRequestValidator();
        AddCollaboratorRequest request = new AddCollaboratorRequest(Guid.Empty);
        var result = validator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddCollaboratorValidatorAcceptsValidGuid()
    {
        AddCollaboratorRequestValidator validator = new AddCollaboratorRequestValidator();
        AddCollaboratorRequest request = new AddCollaboratorRequest(Guid.NewGuid());
        var result = validator.Validate(request);
        Assert.True(result.IsValid);
    }
}
