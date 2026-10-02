using System.Linq.Expressions;
using AwadyLab.EFCatalyst.Abstractions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;

namespace SampleApp.Data.Filters;

/// <summary>
/// An application filter, discovered by <c>AddGlobalQueryFilterInterfacesInAssembly</c> and applied to every entity
/// implementing <see cref="IArchivable"/> as a named filter that can be switched off on its own.
/// </summary>
public sealed class ArchivedFilter : IGlobalFilter<IArchivable>
{
    public Expression<Func<IArchivable, bool>> Build(DbContext context) => e => !e.IsArchived;
}
