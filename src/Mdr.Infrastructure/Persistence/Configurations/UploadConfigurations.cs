using Mdr.Domain.Auditing;
using Mdr.Domain.Documents;
using Mdr.Domain.Projects;
using Mdr.Domain.Uploads;
using Mdr.Domain.Users;
using Mdr.Domain.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mdr.Infrastructure.Persistence.Configurations;

internal sealed class MdrUploadConfiguration : IEntityTypeConfiguration<MdrUpload>
{
    public void Configure(EntityTypeBuilder<MdrUpload> builder)
    {
        builder.ToTable("MdrUploads");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OriginalFileName).HasMaxLength(MdrUpload.MaxOriginalFileNameLength).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(MdrUpload.MaxStoragePathLength).IsRequired();
        builder.Property(x => x.ChecksumSha256).HasMaxLength(MdrUpload.ChecksumLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.UploadedBy).HasMaxLength(MdrUpload.MaxUploadedByLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(MdrUpload.MaxFailureReasonLength);
        builder.Property(x => x.CorrelationId).HasMaxLength(MdrUpload.MaxCorrelationIdLength).IsUnicode(false);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Ignore(x => x.IsTerminal);
        builder.Ignore(x => x.ProcessingDuration);

        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);

        // Re-uploading identical content for the same project is rejected (409).
        builder.HasIndex(x => new { x.ProjectId, x.ChecksumSha256 })
            .IsUnique()
            .HasDatabaseName("UX_MdrUploads_ProjectId_ChecksumSha256");

        // Upload history (newest first per project).
        builder.HasIndex(x => new { x.ProjectId, x.UploadedAtUtc })
            .HasDatabaseName("IX_MdrUploads_ProjectId_UploadedAtUtc");

        // Small filtered index for the crash-recovery sweep.
        builder.HasIndex(x => x.Status)
            .HasFilter("[Status] IN ('Queued', 'Processing')")
            .HasDatabaseName("IX_MdrUploads_Status_Pending");
    }
}

internal sealed class MdrDocumentConfiguration : IEntityTypeConfiguration<MdrDocument>
{
    public void Configure(EntityTypeBuilder<MdrDocument> builder)
    {
        builder.ToTable("MdrDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DocumentNumber).HasMaxLength(DocumentNumber.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(MdrDocument.MaxTitleLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(MdrDocument.MaxDescriptionLength);
        builder.Property(x => x.Revision).HasMaxLength(MdrDocument.MaxRevisionLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.AiSuggestedDisciplineCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(x => x.AiSuggestedDocumentTypeCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(x => x.AiCategory).HasMaxLength(MdrDocument.MaxCategoryLength);
        builder.Property(x => x.AiPriority).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        builder.Property(x => x.AiClassifierVersion).HasMaxLength(MdrDocument.MaxClassifierVersionLength).IsUnicode(false);
        builder.Property(x => x.AiPotentialDuplicateOf).HasMaxLength(MdrDocument.MaxDuplicateReferenceLength).IsUnicode(false);
        builder.Property(x => x.AiMissingMetadata).HasMaxLength(MdrDocument.MaxMissingMetadataLength);
        builder.Property(x => x.AiRationale).HasMaxLength(MdrDocument.MaxRationaleLength);

        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MdrUpload>().WithMany().HasForeignKey(x => x.UploadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Discipline).WithMany().HasForeignKey(x => x.DisciplineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DocumentType).WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.GateSet).WithMany().HasForeignKey(x => x.GateSetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WbsElement).WithMany().HasForeignKey(x => x.WbsElementId).OnDelete(DeleteBehavior.Restrict);

        // Business key: document numbers are unique per project (also serves the duplicate lookup).
        builder.HasIndex(x => new { x.ProjectId, x.DocumentNumber })
            .IsUnique()
            .HasDatabaseName("UX_MdrDocuments_ProjectId_DocumentNumber");

        // Paged document listing per upload, covering the most common list columns.
        builder.HasIndex(x => new { x.UploadId, x.SourceRowNumber })
            .IncludeProperties(x => new { x.DocumentNumber, x.NeedsReview })
            .HasDatabaseName("IX_MdrDocuments_UploadId_SourceRowNumber");

        // "Needs review" queue: only flagged rows are indexed.
        builder.HasIndex(x => new { x.UploadId, x.NeedsReview })
            .HasFilter("[NeedsReview] = 1")
            .HasDatabaseName("IX_MdrDocuments_UploadId_NeedsReview");
    }
}

internal sealed class ValidationIssueConfiguration : IEntityTypeConfiguration<ValidationIssue>
{
    public void Configure(EntityTypeBuilder<ValidationIssue> builder)
    {
        builder.ToTable("ValidationIssues");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ColumnName).HasMaxLength(ValidationIssue.MaxColumnNameLength);
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(ValidationIssue.MaxCodeLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(ValidationIssue.MaxMessageLength).IsRequired();
        builder.Property(x => x.OffendingValue).HasMaxLength(ValidationIssue.MaxOffendingValueLength);

        builder.HasOne<MdrUpload>().WithMany().HasForeignKey(x => x.UploadId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UploadId, x.RowNumber })
            .IncludeProperties(x => new { x.Severity, x.Code })
            .HasDatabaseName("IX_ValidationIssues_UploadId_RowNumber");
        builder.HasIndex(x => new { x.UploadId, x.Severity, x.Code })
            .HasDatabaseName("IX_ValidationIssues_UploadId_Severity_Code");
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.UserName).HasMaxLength(AuditLog.MaxUserNameLength).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(AuditLog.MaxActionLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(AuditLog.MaxEntityTypeLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(AuditLog.MaxEntityIdLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(AuditLog.MaxDetailsLength);
        builder.Property(x => x.CorrelationId).HasMaxLength(AuditLog.MaxCorrelationIdLength).IsUnicode(false);
        builder.HasIndex(x => x.OccurredAtUtc).HasDatabaseName("IX_AuditLogs_OccurredAtUtc");
        builder.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("IX_AuditLogs_EntityType_EntityId");
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.UserName).HasMaxLength(AppUser.MaxUserNameLength).IsRequired();
        builder.HasIndex(x => x.UserName).IsUnique();
        builder.Property(x => x.DisplayName).HasMaxLength(AppUser.MaxDisplayNameLength).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(AppUser.MaxPasswordHashLength).IsRequired();
        builder.Property(x => x.Roles).HasMaxLength(AppUser.MaxRolesLength).IsUnicode(false).IsRequired();
    }
}
