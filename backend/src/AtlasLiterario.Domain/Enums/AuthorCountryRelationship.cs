namespace AtlasLiterario.Domain.Enums;

/// <summary>Kind of relation between an author and a country. Only Citizenship is used on the authors map.</summary>
public enum AuthorCountryRelationship : short
{
    Citizenship = 0,
    BirthPlace = 1,
    Residence = 2,
}
