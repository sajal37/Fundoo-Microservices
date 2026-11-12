using Xunit;

namespace Fundoo.User.UnitTests;

public class BasicTests
{
    [Fact]
    public void EmailNormalizationIgnoresCaseAndOuterSpaces()
    {
        var email = "  Test@Example.com ";
        Assert.Equal("TEST@EXAMPLE.COM", email.Trim().ToUpperInvariant());
    }
}
