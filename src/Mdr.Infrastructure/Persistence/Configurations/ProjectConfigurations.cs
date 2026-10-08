using Mdr.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mdr.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(Project.MaxCodeLength).IsUnicode(false).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(Project.MaxNameLength).IsRequired();
        builder.Property(x => x.Client).HasMaxLength(Project.MaxClientLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(Project.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Ignore(x => x.AcceptsUploads);

        builder.HasMany(x => x.WbsElements)
            .WithOne()
            .HasForeignKey(w => w.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.WbsElements).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class WbsElementConfiguration : IEntityTypeConfiguration<WbsElement>
{
    public void Configure(EntityTypeBuilder<WbsElement> builder)
    {
        builder.ToTable("WbsElements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(WbsCode.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(WbsElement.MaxNameLength).IsRequired();
        builder.Property(x => x.ParentCode).HasMaxLength(WbsCode.MaxLength).IsUnicode(false);
        builder.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
    }
}
