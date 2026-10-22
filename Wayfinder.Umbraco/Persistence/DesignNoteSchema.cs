using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Database schema for the wayfinderDesignNote table: design notes, kept beside the blueprints they
/// are about rather than inside their JSON. Notes are deliberately not part of uSync: they are about
/// a design in progress, not part of the service it becomes.
/// </summary>
[TableName("wayfinderDesignNote")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class DesignNoteSchema
{
    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("NoteId")]
    [Length(64)]
    [Index(IndexTypes.UniqueNonClustered)]
    public string NoteId { get; set; } = string.Empty;

    [Column("BlueprintKey")]
    [Length(200)]
    [Index(IndexTypes.NonClustered)]
    public string BlueprintKey { get; set; } = string.Empty;

    /// <summary>The whole serialized <c>DesignNote</c>, replies and resolution included.</summary>
    [Column("Json")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string Json { get; set; } = string.Empty;

    /// <summary>The compare-and-write token for <c>UpdateAsync</c>.</summary>
    [Column("Revision")]
    public int Revision { get; set; }

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }
}
