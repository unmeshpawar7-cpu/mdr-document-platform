namespace Mdr.Domain.Validation;

/// <summary>Describes one entry of the validation code catalogue.</summary>
public sealed record ValidationCodeInfo(string Code, IssueSeverity Severity, string Stage, string Description);

/// <summary>
/// Catalogue of every code the processing pipeline can emit. Kept in sync with
/// <c>docs/validation-codes.md</c> (a unit test fails if the document drifts).
/// </summary>
public static class ValidationCodes
{
    // Schema (row level)
    public const string RequiredMissing = "MDR001_REQUIRED_MISSING";
    public const string MaxLengthExceeded = "MDR002_MAX_LENGTH_EXCEEDED";
    public const string InvalidInteger = "MDR003_INVALID_INTEGER";
    public const string InvalidDate = "MDR004_INVALID_DATE";
    public const string MalformedRow = "MDR005_MALFORMED_ROW";

    // Document number
    public const string DocumentNumberFormat = "MDR010_DOCNO_FORMAT";
    public const string DocumentNumberProjectMismatch = "MDR011_DOCNO_PROJECT_MISMATCH";
    public const string DocumentNumberOfficeMismatch = "MDR012_DOCNO_OFFICE_MISMATCH";
    public const string DocumentNumberDisciplineMismatch = "MDR013_DOCNO_DISCIPLINE_MISMATCH";
    public const string DocumentNumberTypeMismatch = "MDR014_DOCNO_DOCTYPE_MISMATCH";

    // Duplicates
    public const string DuplicateInFile = "MDR020_DUPLICATE_IN_FILE";
    public const string DuplicateExisting = "MDR021_DUPLICATE_EXISTING";

    // Reference data
    public const string UnknownDiscipline = "MDR030_UNKNOWN_DISCIPLINE";
    public const string UnknownDocumentType = "MDR031_UNKNOWN_DOCTYPE";
    public const string UnknownOffice = "MDR032_UNKNOWN_OFFICE";
    public const string UnknownGateSet = "MDR033_UNKNOWN_GATESET";
    public const string GateSetNotAllowed = "MDR034_GATESET_NOT_ALLOWED";
    public const string UnknownWbs = "MDR035_UNKNOWN_WBS";

    // Field rules
    public const string QuantityNotPositive = "MDR040_QUANTITY_NOT_POSITIVE";
    public const string InvalidRevision = "MDR041_INVALID_REVISION";
    public const string PlannedDateInPast = "MDR042_PLANNED_DATE_IN_PAST";
    public const string InvalidStatus = "MDR043_INVALID_STATUS";

    // AI enrichment (advisory only - never Error)
    public const string AiUnavailable = "MDR050_AI_UNAVAILABLE";
    public const string AiDisciplineMismatch = "MDR051_AI_DISCIPLINE_MISMATCH";
    public const string AiDocumentTypeMismatch = "MDR052_AI_DOCTYPE_MISMATCH";
    public const string AiLowConfidence = "MDR053_AI_LOW_CONFIDENCE";
    public const string AiPotentialDuplicate = "MDR054_AI_POTENTIAL_DUPLICATE";
    public const string AiMissingMetadata = "MDR055_AI_MISSING_METADATA";
    public const string AiSuggestionRejected = "MDR056_AI_SUGGESTION_REJECTED";

    // File level (row 0)
    public const string FileEmpty = "MDR100_FILE_EMPTY";
    public const string FileExtension = "MDR101_FILE_EXTENSION";
    public const string FileTooLarge = "MDR102_FILE_TOO_LARGE";
    public const string FileEncoding = "MDR103_FILE_ENCODING";
    public const string HeaderMissing = "MDR104_HEADER_MISSING";
    public const string RequiredColumnMissing = "MDR105_REQUIRED_COLUMN_MISSING";
    public const string DuplicateColumn = "MDR106_DUPLICATE_COLUMN";
    public const string UnknownColumn = "MDR107_UNKNOWN_COLUMN";
    public const string NoDataRows = "MDR108_NO_DATA_ROWS";
    public const string TooManyRows = "MDR109_TOO_MANY_ROWS";
    public const string FileUnreadable = "MDR110_FILE_UNREADABLE";

    private const string Schema = "Schema";
    private const string Business = "Business";
    private const string Duplicates = "Duplicates";
    private const string Ai = "AI enrichment";
    private const string File = "File";

    public static IReadOnlyList<ValidationCodeInfo> All { get; } = new List<ValidationCodeInfo>
    {
        new(RequiredMissing, IssueSeverity.Error, Schema, "A required value is empty."),
        new(MaxLengthExceeded, IssueSeverity.Error, Schema, "A value is longer than the column allows."),
        new(InvalidInteger, IssueSeverity.Error, Schema, "Quantity is not a whole number."),
        new(InvalidDate, IssueSeverity.Error, Schema, "PlannedDate is not an ISO date (yyyy-MM-dd)."),
        new(MalformedRow, IssueSeverity.Error, Schema, "The row has more fields than the header."),
        new(DocumentNumberFormat, IssueSeverity.Error, Business, "Document number does not match {Project}-{Office}-{Discipline}-{Type}-{Seq}."),
        new(DocumentNumberProjectMismatch, IssueSeverity.Error, Business, "Document number project segment differs from the upload's project code."),
        new(DocumentNumberOfficeMismatch, IssueSeverity.Error, Business, "Document number office segment differs from the Office column."),
        new(DocumentNumberDisciplineMismatch, IssueSeverity.Error, Business, "Document number discipline segment differs from the Discipline column."),
        new(DocumentNumberTypeMismatch, IssueSeverity.Error, Business, "Document number type segment differs from the DocumentType column."),
        new(DuplicateInFile, IssueSeverity.Error, Duplicates, "Document number already appears on an earlier row of the same file."),
        new(DuplicateExisting, IssueSeverity.Error, Duplicates, "Document number already exists in the project's register."),
        new(UnknownDiscipline, IssueSeverity.Error, Business, "Discipline code is not an active reference value."),
        new(UnknownDocumentType, IssueSeverity.Error, Business, "Document type code is not an active reference value."),
        new(UnknownOffice, IssueSeverity.Error, Business, "Office code is not an active reference value."),
        new(UnknownGateSet, IssueSeverity.Error, Business, "Gate set code is not an active reference value."),
        new(GateSetNotAllowed, IssueSeverity.Error, Business, "Gate set is not allowed for the row's document type."),
        new(UnknownWbs, IssueSeverity.Error, Business, "WBS code does not exist for the project."),
        new(QuantityNotPositive, IssueSeverity.Error, Business, "Quantity must be greater than zero."),
        new(InvalidRevision, IssueSeverity.Error, Business, "Revision must be 1-3 upper-case letters or digits."),
        new(PlannedDateInPast, IssueSeverity.Warning, Business, "Planned date is before the processing date."),
        new(InvalidStatus, IssueSeverity.Error, Business, "Status is not one of Planned, InProgress, Issued, OnHold, Cancelled."),
        new(AiUnavailable, IssueSeverity.Info, Ai, "The AI classifier could not be reached; documents were stored without suggestions."),
        new(AiDisciplineMismatch, IssueSeverity.Info, Ai, "AI suggests a different discipline; document flagged for review."),
        new(AiDocumentTypeMismatch, IssueSeverity.Info, Ai, "AI suggests a different document type; document flagged for review."),
        new(AiLowConfidence, IssueSeverity.Info, Ai, "AI confidence is below the configured threshold; document flagged for review."),
        new(AiPotentialDuplicate, IssueSeverity.Info, Ai, "AI found a near-duplicate title; document flagged for review."),
        new(AiMissingMetadata, IssueSeverity.Info, Ai, "AI reports metadata that would improve classification (e.g. description)."),
        new(AiSuggestionRejected, IssueSeverity.Info, Ai, "AI returned a code outside the reference data; the suggestion was discarded."),
        new(FileEmpty, IssueSeverity.Error, File, "The uploaded file is empty."),
        new(FileExtension, IssueSeverity.Error, File, "Only .csv files are accepted."),
        new(FileTooLarge, IssueSeverity.Error, File, "The file exceeds the configured maximum size."),
        new(FileEncoding, IssueSeverity.Error, File, "The file is not valid UTF-8 (UTF-8 with or without BOM is accepted)."),
        new(HeaderMissing, IssueSeverity.Error, File, "The file has no header row."),
        new(RequiredColumnMissing, IssueSeverity.Error, File, "A required column is missing from the header."),
        new(DuplicateColumn, IssueSeverity.Error, File, "Two header columns map to the same field."),
        new(UnknownColumn, IssueSeverity.Warning, File, "A header column is not recognised and will be ignored."),
        new(NoDataRows, IssueSeverity.Error, File, "The file has a header but no data rows."),
        new(TooManyRows, IssueSeverity.Error, File, "The file exceeds the configured maximum number of rows."),
        new(FileUnreadable, IssueSeverity.Error, File, "The stored file could not be opened for processing."),
    };

    private static readonly Dictionary<string, ValidationCodeInfo> ByCode =
        All.ToDictionary(c => c.Code, StringComparer.Ordinal);

    public static bool TryGet(string code, out ValidationCodeInfo? info)
    {
        var found = ByCode.TryGetValue(code, out var value);
        info = value;
        return found;
    }

    public static IssueSeverity SeverityOf(string code) =>
        ByCode.TryGetValue(code, out var info) ? info.Severity : IssueSeverity.Error;
}
