using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco.Tests;

/// <summary>
/// Proves the access boundary the "Blueprints" section's UI-nav-visibility alone can't: an
/// authenticated backoffice user who lacks a configured admin group alias must be denied the
/// authoring API too, not just hidden from the nav item. See
/// <see cref="WayfinderAdminHandler"/>'s own remarks.
/// </summary>
public class WayfinderAdminHandlerTests
{
    [Fact]
    public async Task DoesNotSucceed_WhenCurrentUserIsNull()
    {
        var securityAccessor = new Mock<IBackOfficeSecurityAccessor>();
        var backOfficeSecurity = new Mock<IBackOfficeSecurity>();
        backOfficeSecurity.SetupGet(x => x.CurrentUser).Returns((IUser?)null);
        securityAccessor.SetupGet(x => x.BackOfficeSecurity).Returns(backOfficeSecurity.Object);

        var handler = new WayfinderAdminHandler(
            securityAccessor.Object,
            Options.Create(new WayfinderServiceDesignOptions { AdminGroupAliases = ["admin"] }));

        var context = CreateContext();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task DoesNotSucceed_WhenAdminGroupAliasesIsEmpty()
    {
        var securityAccessor = BuildSecurityAccessorWithUserGroups("admin");

        var handler = new WayfinderAdminHandler(
            securityAccessor.Object,
            Options.Create(new WayfinderServiceDesignOptions { AdminGroupAliases = [] }));

        var context = CreateContext();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Succeeds_WhenUserHasAllowedGroupAlias()
    {
        var securityAccessor = BuildSecurityAccessorWithUserGroups("admin");

        var handler = new WayfinderAdminHandler(
            securityAccessor.Object,
            Options.Create(new WayfinderServiceDesignOptions { AdminGroupAliases = ["admin"] }));

        var context = CreateContext();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    /// <summary>
    /// The concrete scenario this feature exists for: an Editor with no Blueprints access (the
    /// default for every group except Administrators, per <see cref="WayfinderSectionAccessSeeder"/>)
    /// must not be able to call the authoring API directly, even though they're a perfectly
    /// valid, authenticated backoffice user.
    /// </summary>
    [Fact]
    public async Task DoesNotSucceed_WhenUserLacksAllowedGroupAlias()
    {
        var securityAccessor = BuildSecurityAccessorWithUserGroups("editor");

        var handler = new WayfinderAdminHandler(
            securityAccessor.Object,
            Options.Create(new WayfinderServiceDesignOptions { AdminGroupAliases = ["admin"] }));

        var context = CreateContext();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task GroupAliasMatch_IsCaseInsensitive()
    {
        var securityAccessor = BuildSecurityAccessorWithUserGroups("Admin");

        var handler = new WayfinderAdminHandler(
            securityAccessor.Object,
            Options.Create(new WayfinderServiceDesignOptions { AdminGroupAliases = ["admin"] }));

        var context = CreateContext();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    private static AuthorizationHandlerContext CreateContext() =>
        new([new WayfinderAdminRequirement()],
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "Test")),
            resource: null);

    private static Mock<IBackOfficeSecurityAccessor> BuildSecurityAccessorWithUserGroups(params string[] aliases)
    {
        var groups = aliases
            .Select(alias =>
            {
                var group = new Mock<IReadOnlyUserGroup>();
                group.SetupGet(x => x.Alias).Returns(alias);
                return group.Object;
            })
            .ToArray();

        var user = new Mock<IUser>();
        user.SetupGet(x => x.Groups).Returns(groups);

        var backOfficeSecurity = new Mock<IBackOfficeSecurity>();
        backOfficeSecurity.SetupGet(x => x.CurrentUser).Returns(user.Object);

        var securityAccessor = new Mock<IBackOfficeSecurityAccessor>();
        securityAccessor.SetupGet(x => x.BackOfficeSecurity).Returns(backOfficeSecurity.Object);
        return securityAccessor;
    }
}
