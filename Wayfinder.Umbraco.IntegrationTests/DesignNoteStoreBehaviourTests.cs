using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Engine.Models;
using Wayfinder.Engine.Services;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// Design notes are kept in the wayfinderDesignNote table, beside the blueprints. A note must survive
/// a round trip through the database whole (anchor, replies, resolution), a stale write must lose
/// rather than overwrite, and deleting the blueprint must take its notes with it. Each test saves its
/// own uniquely keyed blueprint.
/// </summary>
[Collection(BootedReferenceHost.Name)]
public sealed class DesignNoteStoreBehaviourTests(ReferenceAppFactory factory)
{
    private static readonly DesignNoteAuthor Designer = new("Sam Designer", DesignNoteAuthorKind.Person);
    private static readonly DesignNoteAuthor Partner = new("Design partner", DesignNoteAuthorKind.DesignPartner);

    private DesignNoteService Notes => factory.Services.GetRequiredService<DesignNoteService>();

    private IDesignNoteStore Store => factory.Services.GetRequiredService<IDesignNoteStore>();

    private ServiceBlueprintAuthoringService Authoring => factory.Services.GetRequiredService<ServiceBlueprintAuthoringService>();

    private async Task<string> SaveBlueprint()
    {
        var key = $"notes-probe-{Guid.NewGuid():N}";
        var blueprint = new ServiceBlueprint
        {
            DefinitionKey = key,
            DisplayName = "Notes probe",
            Stages = [new StageDefinition { StageKey = "check", DisplayName = "Check your answers", QueueKey = "probe" }],
        };
        (await factory.Services.GetRequiredService<IServiceBlueprintSourceStore>().SaveAsync(blueprint, 0)).Saved.Should().BeTrue();
        return key;
    }

    [Fact]
    public void TheHostKeepsNotesInTheDatabase_NotInMemory()
    {
        Store.Should().BeOfType<UmbracoDesignNoteStore>();
    }

    [Fact]
    public async Task ANote_RoundTripsWhole_WithItsRepliesAndResolution()
    {
        var key = await SaveBlueprint();
        try
        {
            var added = await Notes.AddAsync(key, new(DesignNoteAnchorKind.Stage, "check"), "Too many questions", Designer);
            await Notes.ReplyAsync(key, added.Note!.Id, "Merge two of them?", Partner);
            await Notes.ResolveAsync(key, added.Note.Id, "Merged the address questions.", Partner, version: 1);

            var stored = (await Store.ListAsync(key)).Single();

            stored.Anchor.Should().Be(new DesignNoteAnchor(DesignNoteAnchorKind.Stage, "check"));
            stored.Author.Should().Be(Designer);
            stored.Replies.Should().ContainSingle(r => r.Text == "Merge two of them?" && r.Author == Partner);
            stored.Status.Should().Be(DesignNoteStatus.Resolved);
            stored.Resolution!.Summary.Should().Be("Merged the address questions.");
            stored.Revision.Should().Be(3);
        }
        finally
        {
            await Authoring.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task AStaleWrite_LosesInsteadOfOverwriting()
    {
        var key = await SaveBlueprint();
        try
        {
            var note = (await Notes.AddAsync(key, new(DesignNoteAnchorKind.Blueprint), "Original", Designer)).Note!;
            (await Store.UpdateAsync(note with { Text = "First" }, note.Revision)).Should().BeTrue();

            var stale = await Store.UpdateAsync(note with { Text = "Second" }, note.Revision);

            stale.Should().BeFalse();
            (await Store.GetAsync(key, note.Id))!.Text.Should().Be("First");
        }
        finally
        {
            await Authoring.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task DeletingTheBlueprint_DeletesItsNotes()
    {
        var key = await SaveBlueprint();
        await Notes.AddAsync(key, new(DesignNoteAnchorKind.Blueprint), "A note", Designer);

        (await Authoring.DeleteAsync(key)).Should().BeTrue();

        (await Store.ListAsync(key)).Should().BeEmpty();
    }
}
