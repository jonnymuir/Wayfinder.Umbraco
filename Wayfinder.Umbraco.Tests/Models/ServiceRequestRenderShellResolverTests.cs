using FluentAssertions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Models;

namespace Wayfinder.Umbraco.Tests.Models;

public class ServiceRequestRenderShellResolverTests
{
    private static ComponentRenderPayload SummaryList() => new()
    {
        Type = "summary-list",
        Fields = [new FieldRenderPayload { FieldKey = "outcome", Label = "Outcome", FieldType = "text", Required = false }]
    };

    [Fact]
    public void GivenAPanelAndSummaryListWithNoActions_WhenResolvingShell_ThenReturnsConfirmation()
    {
        var shell = ServiceRequestRenderShellResolver.ResolveShell(
            new ComponentRenderPayload[] { new() { Type = "panel", Heading = "Your application is complete" }, SummaryList() },
            engineStepType: "Confirmation",
            hasWaitingConfig: false,
            hasAvailableActions: false);

        shell.Should().Be("confirmation");
    }

    [Fact]
    public void GivenAPanelAndSummaryListWithActions_WhenResolvingShell_ThenReturnsCheckAnswers()
    {
        var shell = ServiceRequestRenderShellResolver.ResolveShell(
            new ComponentRenderPayload[] { new() { Type = "panel", Heading = "Check your answers" }, SummaryList() },
            engineStepType: string.Empty,
            hasWaitingConfig: false,
            hasAvailableActions: true);

        shell.Should().Be("check-answers");
    }
}
