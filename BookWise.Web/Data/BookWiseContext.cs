using BookWise.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Data;

public class BookWiseContext(DbContextOptions<BookWiseContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookRemark> BookRemarks => Set<BookRemark>();
    public DbSet<BookQuote> BookQuotes => Set<BookQuote>();
    public DbSet<AuthorRecommendation> AuthorRecommendations => Set<AuthorRecommendation>();
    public DbSet<Author> Authors => Set<Author>();

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
            entity.Property(b => b.AuthorId).IsRequired();
            entity.Property(b => b.PersonalRating)
                .HasPrecision(2, 1)
                .HasColumnName("Rating");

            entity.Property(b => b.PublicRating)
                .HasPrecision(2, 1);
            entity.Property(b => b.DoubanSubjectId)
                .HasMaxLength(32);
            entity.Property(b => b.Quote).HasMaxLength(500);
            entity.Property(b => b.Publisher).HasMaxLength(200);

            entity.HasIndex(b => new { b.Title, b.AuthorId });

            entity.HasOne(b => b.AuthorDetails)
                .WithMany(a => a.Books)
                .HasForeignKey(b => b.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Author>(entity =>
        {
            entity.Property(a => a.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(a => a.NormalizedName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(a => a.AvatarUrl)
                .HasMaxLength(500);

            entity.Property(a => a.ProfileSummary)
                .HasMaxLength(2000);

            entity.Property(a => a.ProfileNotableWorks)
                .HasMaxLength(1000);

            entity.Property(a => a.ProfileRefreshedAt)
                .HasColumnType("TEXT");

            entity.Property(a => a.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(a => a.UpdatedAt)
                .HasColumnType("TEXT");

            entity.HasIndex(a => a.NormalizedName)
                .IsUnique();
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

        modelBuilder.Entity<BookQuote>(entity =>
        {
            entity.Property(q => q.Text)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(q => q.Author)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(q => q.Source)
                .HasMaxLength(200);

            entity.Property(q => q.BackgroundImageUrl)
                .HasMaxLength(500);

            entity.Property(q => q.Origin)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(q => q.AddedOn)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(q => q.Book)
                .WithMany(b => b.Quotes)
                .HasForeignKey(q => q.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(q => q.BookId);
        });

        modelBuilder.Entity<AuthorRecommendation>(entity =>
        {
            entity.Property(r => r.FocusAuthor)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(r => r.RecommendedAuthor)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(r => r.Rationale)
                .HasMaxLength(1000);

            entity.Property(r => r.ImageUrl)
                .HasMaxLength(500);

            entity.Property(r => r.ConfidenceScore)
                .HasPrecision(3, 2);

            entity.Property(r => r.GeneratedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(r => new { r.FocusAuthor, r.RecommendedAuthor }).IsUnique();
        });
    }
}
