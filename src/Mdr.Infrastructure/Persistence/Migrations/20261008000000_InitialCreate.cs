using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mdr.Infrastructure.Persistence.Migrations;

/// <summary>
/// Initial schema. Hand-authored to match <see cref="MdrDbContext"/> (the .NET SDK was not available when it was
/// written); <c>dotnet ef migrations add</c> can regenerate it from the model.
/// </summary>
[DbContext(typeof(MdrDbContext))]
[Migration("20261008000000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Action = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                EntityType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                EntityId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Disciplines",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Disciplines", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DocumentTypes",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentTypes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "GateSets",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                Stages = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GateSets", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Offices",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Offices", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Client = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Projects", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Roles = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DocumentTypeGateSets",
            columns: table => new
            {
                DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                GateSetId = table.Column<int>(type: "int", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentTypeGateSets", x => new { x.DocumentTypeId, x.GateSetId });
                table.ForeignKey(
                    name: "FK_DocumentTypeGateSets_DocumentTypes_DocumentTypeId",
                    column: x => x.DocumentTypeId,
                    principalTable: "DocumentTypes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DocumentTypeGateSets_GateSets_GateSetId",
                    column: x => x.GateSetId,
                    principalTable: "GateSets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MdrUploads",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ChecksumSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                TotalRows = table.Column<int>(type: "int", nullable: false),
                ValidRows = table.Column<int>(type: "int", nullable: false),
                InvalidRows = table.Column<int>(type: "int", nullable: false),
                DuplicateRows = table.Column<int>(type: "int", nullable: false),
                InsertedRows = table.Column<int>(type: "int", nullable: false),
                ProcessingAttempts = table.Column<int>(type: "int", nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MdrUploads", x => x.Id);
                table.ForeignKey(
                    name: "FK_MdrUploads_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "WbsElements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Level = table.Column<int>(type: "int", nullable: false),
                ParentCode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WbsElements", x => x.Id);
                table.ForeignKey(
                    name: "FK_WbsElements_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ValidationIssues",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RowNumber = table.Column<int>(type: "int", nullable: false),
                ColumnName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Severity = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                Code = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                OffendingValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ValidationIssues", x => x.Id);
                table.ForeignKey(
                    name: "FK_ValidationIssues_MdrUploads_UploadId",
                    column: x => x.UploadId,
                    principalTable: "MdrUploads",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MdrDocuments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                DocumentNumber = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                DisciplineId = table.Column<int>(type: "int", nullable: false),
                DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                OfficeId = table.Column<int>(type: "int", nullable: false),
                GateSetId = table.Column<int>(type: "int", nullable: false),
                WbsElementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<int>(type: "int", nullable: false),
                Revision = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                PlannedDate = table.Column<DateOnly>(type: "date", nullable: true),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                AiSuggestedDisciplineCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                AiSuggestedDocumentTypeCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                AiCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                AiPriority = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                AiConfidence = table.Column<double>(type: "float", nullable: true),
                AiClassifierVersion = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                AiPotentialDuplicateOf = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                AiMissingMetadata = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                AiRationale = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                AiDisciplineMismatch = table.Column<bool>(type: "bit", nullable: false),
                AiDocumentTypeMismatch = table.Column<bool>(type: "bit", nullable: false),
                AiClassifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                NeedsReview = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MdrDocuments", x => x.Id);
                table.ForeignKey(
                    name: "FK_MdrDocuments_Disciplines_DisciplineId",
                    column: x => x.DisciplineId,
                    principalTable: "Disciplines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MdrDocuments_DocumentTypes_DocumentTypeId",
                    column: x => x.DocumentTypeId,
                    principalTable: "DocumentTypes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MdrDocuments_GateSets_GateSetId",
                    column: x => x.GateSetId,
                    principalTable: "GateSets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MdrDocuments_MdrUploads_UploadId",
                    column: x => x.UploadId,
                    principalTable: "MdrUploads",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MdrDocuments_Offices_OfficeId",
                    column: x => x.OfficeId,
                    principalTable: "Offices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MdrDocuments_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MdrDocuments_WbsElements_WbsElementId",
                    column: x => x.WbsElementId,
                    principalTable: "WbsElements",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            table: "Disciplines",
            columns: new[] { "Id", "Code", "IsActive", "Name" },
            values: new object[,]
            {
                { 1, "ARC", true, "Architectural" },
                { 2, "CIV", true, "Civil" },
                { 3, "ELE", true, "Electrical" },
                { 4, "HSE", true, "Health, Safety & Environment" },
                { 5, "INS", true, "Instrumentation & Control" },
                { 6, "MEC", true, "Mechanical" },
                { 7, "PRO", true, "Process" },
                { 8, "STR", true, "Structural" },
            });

        migrationBuilder.InsertData(
            table: "DocumentTypes",
            columns: new[] { "Id", "Code", "IsActive", "Name" },
            values: new object[,]
            {
                { 1, "CAL", true, "Calculation" },
                { 2, "DS", true, "Datasheet" },
                { 3, "DWG", true, "Drawing" },
                { 4, "LST", true, "List / Register" },
                { 5, "PID", true, "Piping & Instrumentation Diagram" },
                { 6, "PRC", true, "Procedure" },
                { 7, "REP", true, "Report" },
                { 8, "SPE", true, "Specification" },
            });

        migrationBuilder.InsertData(
            table: "GateSets",
            columns: new[] { "Id", "Code", "Description", "IsActive", "Name", "Stages" },
            values: new object[,]
            {
                { 1, "STD", "Issued for Review > Issued for Approval > Issued for Construction", true, "Standard design deliverable", "IFR,IFA,IFC" },
                { 2, "PROC", "Issued for Review > Issued for Quotation > Issued for Purchase", true, "Procurement deliverable", "IFR,IFQ,IFP" },
                { 3, "INFO", "Issued for Information", true, "Information only", "IFI" },
                { 4, "REG", "Issued for Review > Issued for Approval > Issued for Use", true, "Regulatory / HSE deliverable", "IFR,IFA,IFU" },
            });

        migrationBuilder.InsertData(
            table: "Offices",
            columns: new[] { "Id", "City", "Code", "Country", "IsActive", "Name" },
            values: new object[,]
            {
                { 1, "Houston", "HOU", "United States", true, "Houston Engineering Centre" },
                { 2, "London", "LDN", "United Kingdom", true, "London Engineering Centre" },
                { 3, "Pune", "PUN", "India", true, "Pune Engineering Centre" },
            });

        migrationBuilder.InsertData(
            table: "DocumentTypeGateSets",
            columns: new[] { "DocumentTypeId", "GateSetId" },
            values: new object[,]
            {
                { 1, 1 }, { 1, 3 },
                { 2, 1 }, { 2, 2 },
                { 3, 1 }, { 3, 3 },
                { 4, 1 }, { 4, 2 }, { 4, 3 },
                { 5, 1 },
                { 6, 1 }, { 6, 4 },
                { 7, 1 }, { 7, 3 }, { 7, 4 },
                { 8, 1 }, { 8, 2 },
            });

        migrationBuilder.CreateIndex(name: "IX_AuditLogs_EntityType_EntityId", table: "AuditLogs", columns: new[] { "EntityType", "EntityId" });
        migrationBuilder.CreateIndex(name: "IX_AuditLogs_OccurredAtUtc", table: "AuditLogs", column: "OccurredAtUtc");
        migrationBuilder.CreateIndex(name: "IX_Disciplines_Code", table: "Disciplines", column: "Code", unique: true);
        migrationBuilder.CreateIndex(name: "IX_DocumentTypeGateSets_GateSetId", table: "DocumentTypeGateSets", column: "GateSetId");
        migrationBuilder.CreateIndex(name: "IX_DocumentTypes_Code", table: "DocumentTypes", column: "Code", unique: true);
        migrationBuilder.CreateIndex(name: "IX_GateSets_Code", table: "GateSets", column: "Code", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MdrDocuments_DisciplineId", table: "MdrDocuments", column: "DisciplineId");
        migrationBuilder.CreateIndex(name: "IX_MdrDocuments_DocumentTypeId", table: "MdrDocuments", column: "DocumentTypeId");
        migrationBuilder.CreateIndex(name: "IX_MdrDocuments_GateSetId", table: "MdrDocuments", column: "GateSetId");
        migrationBuilder.CreateIndex(name: "IX_MdrDocuments_OfficeId", table: "MdrDocuments", column: "OfficeId");
        migrationBuilder.CreateIndex(name: "IX_MdrDocuments_WbsElementId", table: "MdrDocuments", column: "WbsElementId");
        migrationBuilder.CreateIndex(
            name: "IX_MdrDocuments_UploadId_NeedsReview",
            table: "MdrDocuments",
            columns: new[] { "UploadId", "NeedsReview" },
            filter: "[NeedsReview] = 1");
        migrationBuilder.CreateIndex(
            name: "IX_MdrDocuments_UploadId_SourceRowNumber",
            table: "MdrDocuments",
            columns: new[] { "UploadId", "SourceRowNumber" })
            .Annotation("SqlServer:Include", new[] { "DocumentNumber", "NeedsReview" });
        migrationBuilder.CreateIndex(
            name: "UX_MdrDocuments_ProjectId_DocumentNumber",
            table: "MdrDocuments",
            columns: new[] { "ProjectId", "DocumentNumber" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_MdrUploads_ProjectId_UploadedAtUtc",
            table: "MdrUploads",
            columns: new[] { "ProjectId", "UploadedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_MdrUploads_Status_Pending",
            table: "MdrUploads",
            column: "Status",
            filter: "[Status] IN ('Queued', 'Processing')");
        migrationBuilder.CreateIndex(
            name: "UX_MdrUploads_ProjectId_ChecksumSha256",
            table: "MdrUploads",
            columns: new[] { "ProjectId", "ChecksumSha256" },
            unique: true);
        migrationBuilder.CreateIndex(name: "IX_Offices_Code", table: "Offices", column: "Code", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Projects_Code", table: "Projects", column: "Code", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Users_UserName", table: "Users", column: "UserName", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_ValidationIssues_UploadId_RowNumber",
            table: "ValidationIssues",
            columns: new[] { "UploadId", "RowNumber" })
            .Annotation("SqlServer:Include", new[] { "Severity", "Code" });
        migrationBuilder.CreateIndex(
            name: "IX_ValidationIssues_UploadId_Severity_Code",
            table: "ValidationIssues",
            columns: new[] { "UploadId", "Severity", "Code" });
        migrationBuilder.CreateIndex(
            name: "IX_WbsElements_ProjectId_Code",
            table: "WbsElements",
            columns: new[] { "ProjectId", "Code" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AuditLogs");
        migrationBuilder.DropTable(name: "DocumentTypeGateSets");
        migrationBuilder.DropTable(name: "MdrDocuments");
        migrationBuilder.DropTable(name: "Users");
        migrationBuilder.DropTable(name: "ValidationIssues");
        migrationBuilder.DropTable(name: "Disciplines");
        migrationBuilder.DropTable(name: "DocumentTypes");
        migrationBuilder.DropTable(name: "GateSets");
        migrationBuilder.DropTable(name: "Offices");
        migrationBuilder.DropTable(name: "WbsElements");
        migrationBuilder.DropTable(name: "MdrUploads");
        migrationBuilder.DropTable(name: "Projects");
    }
}
