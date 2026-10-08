using Mdr.Domain.ReferenceData;
using Mdr.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mdr.Infrastructure.Persistence.Configurations;

internal static class ReferenceDataMapping
{
    public static void MapCommon<T>(EntityTypeBuilder<T> builder, string table)
        where T : ReferenceDataEntity
    {
        builder.ToTable(table);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(ReferenceDataEntity.MaxCodeLength).IsUnicode(false).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(ReferenceDataEntity.MaxNameLength).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
    }
}

internal sealed class DisciplineConfiguration : IEntityTypeConfiguration<Discipline>
{
    public void Configure(EntityTypeBuilder<Discipline> builder)
    {
        ReferenceDataMapping.MapCommon(builder, "Disciplines");
        builder.HasData(ReferenceDataSeed.Disciplines);
    }
}

internal sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        ReferenceDataMapping.MapCommon(builder, "DocumentTypes");
        builder.HasData(ReferenceDataSeed.DocumentTypes);
    }
}

internal sealed class OfficeConfiguration : IEntityTypeConfiguration<Office>
{
    public void Configure(EntityTypeBuilder<Office> builder)
    {
        ReferenceDataMapping.MapCommon(builder, "Offices");
        builder.Property(x => x.City).HasMaxLength(Office.MaxCityLength).IsRequired();
        builder.Property(x => x.Country).HasMaxLength(Office.MaxCountryLength).IsRequired();
        builder.HasData(ReferenceDataSeed.Offices);
    }
}

internal sealed class GateSetConfiguration : IEntityTypeConfiguration<GateSet>
{
    public void Configure(EntityTypeBuilder<GateSet> builder)
    {
        ReferenceDataMapping.MapCommon(builder, "GateSets");
        builder.Property(x => x.Stages).HasMaxLength(GateSet.MaxStagesLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(GateSet.MaxDescriptionLength).IsRequired();
        builder.HasData(ReferenceDataSeed.GateSets);
    }
}

internal sealed class DocumentTypeGateSetConfiguration : IEntityTypeConfiguration<DocumentTypeGateSet>
{
    public void Configure(EntityTypeBuilder<DocumentTypeGateSet> builder)
    {
        builder.ToTable("DocumentTypeGateSets");
        builder.HasKey(x => new { x.DocumentTypeId, x.GateSetId });
        builder.HasOne<DocumentType>().WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GateSet>().WithMany().HasForeignKey(x => x.GateSetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasData(ReferenceDataSeed.DocumentTypeGateSets);
    }
}
