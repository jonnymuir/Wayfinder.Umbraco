using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Core.Strings;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco.Tests;

/// <summary>
/// Proves the "install the package, get a working authoring UI with zero host wiring" promise:
/// a fresh Administrators group (the default <see cref="WayfinderServiceDesignOptions.AdminGroupAliases"/>)
/// ends up with the "Blueprints" section granted after startup, without any host code.
/// </summary>
public class WayfinderSectionAccessSeederTests
{
    private const string SectionAlias = "Wayfinder.Section";

    [Fact]
    public async Task GrantsSection_ToConfiguredGroup_WhenNotAlreadyGranted()
    {
        var group = CreateGroup("admin");
        var userGroupService = new Mock<IUserGroupService>();
        userGroupService.Setup(x => x.GetAsync("admin")).ReturnsAsync(group);
        userGroupService
            .Setup(x => x.UpdateAsync(group, Constants.Security.SuperUserKey))
            .ReturnsAsync(Attempt<IUserGroup, UserGroupOperationStatus>.Succeed(UserGroupOperationStatus.Success, group));

        var seeder = CreateSeeder(userGroupService.Object, ["admin"]);

        await seeder.HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        group.AllowedSections.Should().Contain(SectionAlias);
        userGroupService.Verify(x => x.UpdateAsync(group, Constants.Security.SuperUserKey), Times.Once);
    }

    [Fact]
    public async Task DoesNotCallUpdate_WhenGroupAlreadyHasSection()
    {
        var group = CreateGroup("admin");
        group.AddAllowedSection(SectionAlias);

        var userGroupService = new Mock<IUserGroupService>();
        userGroupService.Setup(x => x.GetAsync("admin")).ReturnsAsync(group);

        var seeder = CreateSeeder(userGroupService.Object, ["admin"]);

        await seeder.HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        userGroupService.Verify(x => x.UpdateAsync(It.IsAny<IUserGroup>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task DoesNothing_WhenConfiguredGroupDoesNotExist()
    {
        var userGroupService = new Mock<IUserGroupService>();
        userGroupService.Setup(x => x.GetAsync("admin")).ReturnsAsync((IUserGroup?)null);

        var seeder = CreateSeeder(userGroupService.Object, ["admin"]);

        var act = async () => await seeder.HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        await act.Should().NotThrowAsync();
        userGroupService.Verify(x => x.UpdateAsync(It.IsAny<IUserGroup>(), It.IsAny<Guid>()), Times.Never);
    }

    private static UserGroup CreateGroup(string alias) =>
        new(Mock.Of<IShortStringHelper>(), 0, alias, "Test Group", "icon-users");

    private static WayfinderSectionAccessSeeder CreateSeeder(IUserGroupService userGroupService, string[] adminGroupAliases)
    {
        var runtimeState = new Mock<IRuntimeState>();
        runtimeState.SetupGet(x => x.Level).Returns(RuntimeLevel.Run);

        return new WayfinderSectionAccessSeeder(
            userGroupService,
            Options.Create(new WayfinderServiceDesignOptions { AdminGroupAliases = adminGroupAliases }),
            runtimeState.Object,
            NullLogger<WayfinderSectionAccessSeeder>.Instance);
    }
}
