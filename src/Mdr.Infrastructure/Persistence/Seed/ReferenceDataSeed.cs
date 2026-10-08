using Mdr.Domain.ReferenceData;

namespace Mdr.Infrastructure.Persistence.Seed;

/// <summary>
/// Controlled vocabulary seeded through <c>HasData</c> (and mirrored in the InitialCreate migration).
/// Gate sets: STD = IFR&gt;IFA&gt;IFC, PROC = IFR&gt;IFQ&gt;IFP, INFO = IFI, REG = IFR&gt;IFA&gt;IFU.
/// </summary>
public static class ReferenceDataSeed
{
    public static IReadOnlyList<Discipline> Disciplines { get; } = new[]
    {
        new Discipline(1, "ARC", "Architectural"),
        new Discipline(2, "CIV", "Civil"),
        new Discipline(3, "ELE", "Electrical"),
        new Discipline(4, "HSE", "Health, Safety & Environment"),
        new Discipline(5, "INS", "Instrumentation & Control"),
        new Discipline(6, "MEC", "Mechanical"),
        new Discipline(7, "PRO", "Process"),
        new Discipline(8, "STR", "Structural"),
    };

    public static IReadOnlyList<DocumentType> DocumentTypes { get; } = new[]
    {
        new DocumentType(1, "CAL", "Calculation"),
        new DocumentType(2, "DS", "Datasheet"),
        new DocumentType(3, "DWG", "Drawing"),
        new DocumentType(4, "LST", "List / Register"),
        new DocumentType(5, "PID", "Piping & Instrumentation Diagram"),
        new DocumentType(6, "PRC", "Procedure"),
        new DocumentType(7, "REP", "Report"),
        new DocumentType(8, "SPE", "Specification"),
    };

    public static IReadOnlyList<Office> Offices { get; } = new[]
    {
        new Office(1, "HOU", "Houston Engineering Centre", "Houston", "United States"),
        new Office(2, "LDN", "London Engineering Centre", "London", "United Kingdom"),
        new Office(3, "PUN", "Pune Engineering Centre", "Pune", "India"),
    };

    public static IReadOnlyList<GateSet> GateSets { get; } = new[]
    {
        new GateSet(1, "STD", "Standard design deliverable", "IFR,IFA,IFC", "Issued for Review > Issued for Approval > Issued for Construction"),
        new GateSet(2, "PROC", "Procurement deliverable", "IFR,IFQ,IFP", "Issued for Review > Issued for Quotation > Issued for Purchase"),
        new GateSet(3, "INFO", "Information only", "IFI", "Issued for Information"),
        new GateSet(4, "REG", "Regulatory / HSE deliverable", "IFR,IFA,IFU", "Issued for Review > Issued for Approval > Issued for Use"),
    };

    /// <summary>Allowed (document type, gate set) pairs.</summary>
    public static IReadOnlyList<DocumentTypeGateSet> DocumentTypeGateSets { get; } = new[]
    {
        new DocumentTypeGateSet(1, 1), new DocumentTypeGateSet(1, 3),                                 // CAL: STD, INFO
        new DocumentTypeGateSet(2, 1), new DocumentTypeGateSet(2, 2),                                 // DS: STD, PROC
        new DocumentTypeGateSet(3, 1), new DocumentTypeGateSet(3, 3),                                 // DWG: STD, INFO
        new DocumentTypeGateSet(4, 1), new DocumentTypeGateSet(4, 2), new DocumentTypeGateSet(4, 3),  // LST: STD, PROC, INFO
        new DocumentTypeGateSet(5, 1),                                                                // PID: STD
        new DocumentTypeGateSet(6, 1), new DocumentTypeGateSet(6, 4),                                 // PRC: STD, REG
        new DocumentTypeGateSet(7, 1), new DocumentTypeGateSet(7, 3), new DocumentTypeGateSet(7, 4),  // REP: STD, INFO, REG
        new DocumentTypeGateSet(8, 1), new DocumentTypeGateSet(8, 2),                                 // SPE: STD, PROC
    };
}
