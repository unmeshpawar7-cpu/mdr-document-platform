using Mdr.Domain.Auditing;
using Mdr.Domain.Documents;
using Mdr.Domain.Projects;
using Mdr.Domain.ReferenceData;
using Mdr.Domain.Uploads;
using Mdr.Domain.Users;
using Mdr.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace Mdr.Infrastructure.Persistence;

public sealed class MdrDbContext : DbContext
{
    public MdrDbContext(DbContextOptions<MdrDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<WbsElement> WbsElements => Set<WbsElement>();

    public DbSet<Discipline> Disciplines => Set<Discipline>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<Office> Offices => Set<Office>();

    public DbSet<GateSet> GateSets => Set<GateSet>();

    public DbSet<DocumentTypeGateSet> DocumentTypeGateSets => Set<DocumentTypeGateSet>();

    public DbSet<MdrUpload> Uploads => Set<MdrUpload>();

    public DbSet<MdrDocument> Documents => Set<MdrDocument>();

    public DbSet<ValidationIssue> ValidationIssues => Set<ValidationIssue>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MdrDbContext).Assembly);
    }
}
