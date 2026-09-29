using System.Security.Claims;
using FluentAssertions;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Tests.Services;

public class CurrentUserServiceValueTests
{
    private static ClaimsPrincipal SignedIn(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void GivenASignedInUserWithNameAndEmailClaims_WhenBuilding_ThenBothAreSupplied()
    {
        var value = CurrentUserServiceValue.Build(SignedIn(
            new Claim(ClaimTypes.Name, "Alex Applicant"),
            new Claim(ClaimTypes.Email, "alex@example.test")));

        value["name"].Should().Be("Alex Applicant");
        value["email"].Should().Be("alex@example.test");
    }

    [Fact]
    public void GivenShortClaimNames_WhenBuilding_ThenTheyAreUsed()
    {
        var value = CurrentUserServiceValue.Build(SignedIn(
            new Claim("name", "Sam Sign"),
            new Claim("email", "sam@example.test")));

        value["name"].Should().Be("Sam Sign");
        value["email"].Should().Be("sam@example.test");
    }

    [Fact]
    public void GivenASignedInUserWithNoEmailClaim_WhenBuilding_ThenEmailIsAnEmptyString()
    {
        var value = CurrentUserServiceValue.Build(SignedIn(new Claim(ClaimTypes.Name, "Alex Applicant")));

        value["email"].Should().Be("");
    }

    [Fact]
    public void GivenAnAnonymousVisitor_WhenBuilding_ThenBothAreEmptyStringsNotMissing()
    {
        var value = CurrentUserServiceValue.Build(new ClaimsPrincipal(new ClaimsIdentity()));

        value.Should().ContainKey("name").WhoseValue.Should().Be("");
        value.Should().ContainKey("email").WhoseValue.Should().Be("");
    }

    [Fact]
    public void GivenNoPrincipalAtAll_WhenBuilding_ThenBothAreEmptyStrings()
    {
        var value = CurrentUserServiceValue.Build(null);

        value["name"].Should().Be("");
        value["email"].Should().Be("");
    }
}
