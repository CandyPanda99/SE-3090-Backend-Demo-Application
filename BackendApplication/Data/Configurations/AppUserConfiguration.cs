using BackendApplication.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApplication.Data.Configurations;

/// <summary>
/// What <see cref="AppUser"/> cannot say with attributes.
/// </summary>
/// <remarks>
/// Table name, primary key, index and column lengths live on the entity itself. Only
/// the three things data annotations cannot express are left here.
/// </remarks>
public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        // Value conversion: stores the enum as "Admin" instead of 2, so the column stays
        // readable and reordering the enum cannot silently change what existing rows mean.
        // There is no [EnumToString] attribute, so this whole property stays fluent.
        builder.Property(u => u.Role)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        // Database-level DEFAULT values. No attribute equivalent.
        builder.Property(u => u.IsActive).HasDefaultValue(true);
        builder.Property(u => u.IsDeleted).HasDefaultValue(false);

        // Cascade is right here, unlike on tasks: a refresh token is meaningless without
        // the user it authenticates, so it should go when they do.
        builder.HasMany(u => u.RefreshTokens)
               .WithOne(t => t.User)
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
