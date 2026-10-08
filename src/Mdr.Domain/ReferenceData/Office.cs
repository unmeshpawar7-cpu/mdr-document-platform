namespace Mdr.Domain.ReferenceData;

/// <summary>Engineering office that originates a deliverable (e.g. <c>PUN</c> Pune).</summary>
public sealed class Office : ReferenceDataEntity
{
    public const int MaxCityLength = 100;
    public const int MaxCountryLength = 100;

    private Office()
    {
    }

    public Office(int id, string code, string name, string city, string country)
        : base(id, code, name)
    {
        City = city;
        Country = country;
    }

    public string City { get; private set; } = string.Empty;

    public string Country { get; private set; } = string.Empty;
}
