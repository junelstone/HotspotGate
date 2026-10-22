using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StonesHotspot.Core.Data;

/// <summary>Used only by <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HotspotDbContext>
{
    public HotspotDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<HotspotDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
