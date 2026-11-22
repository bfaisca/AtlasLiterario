namespace AtlasLiterario.Infrastructure.Email;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Base URL of the frontend, used to build links sent by e-mail.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
}
