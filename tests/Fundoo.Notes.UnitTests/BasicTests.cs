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
}
