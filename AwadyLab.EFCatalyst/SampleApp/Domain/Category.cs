namespace SampleApp.Domain;

/// <summary>An application-defined contract with its own global filter (see <c>ArchivedFilter</c>).</summary>
public interface IArchivable
{
    bool IsArchived { get; }
}

/// <summary>A self-referencing hierarchy (a parent key pointing at the same table).</summary>
public sealed class Category : IArchivable
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public string Name { get; set; } = "";

    public bool IsArchived { get; set; }
}
