using BookWise.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Data;

public class BookWiseContext(DbContextOptions<BookWiseContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookRemark> BookRemarks => Set<BookRemark>();

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
            entity.Property(b => b.Quote).HasMaxLength(500);

            entity.HasIndex(b => new { b.Title, b.Author });
        });

        modelBuilder.Entity<BookRemark>(entity =>
        {
            entity.Property(r => r.Content)
                .IsRequired()
                .HasMaxLength(4000);

            entity.Property(r => r.Title)
                .HasMaxLength(200);

            entity.Property(r => r.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(r => r.Book)
                .WithMany(b => b.Remarks)
                .HasForeignKey(r => r.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.BookId, r.Type, r.AddedOn });
        });
    }
}
