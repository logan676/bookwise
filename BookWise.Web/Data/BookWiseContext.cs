using BookWise.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Data;

public class BookWiseContext(DbContextOptions<BookWiseContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Book>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(entity =>
        {
            entity.Property(b => b.Title).IsRequired();
            entity.Property(b => b.Author).IsRequired();
            entity.Property(b => b.Rating).HasPrecision(2, 1);

            entity.HasIndex(b => new { b.Title, b.Author });
        });
    }
}
