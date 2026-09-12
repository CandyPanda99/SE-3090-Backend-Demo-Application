namespace BackendApplication.Configuration;

/// <summary>
/// Central place for the API version numbers and OpenAPI document names, so the same
/// strings are not re-typed across controllers and Program.cs.
/// </summary>
/// <remarks>
/// This application ships a single version. The versioning machinery is still wired up,
/// because retrofitting it later means changing every route that clients already depend
/// on - whereas adding <c>V2</c> to this file costs nothing.
/// </remarks>
public static class ApiVersions
{
    public const string V1 = "1.0";

    /// <summary>OpenAPI document names. These become <c>/openapi/v1.json</c>.</summary>
    public static readonly string[] DocumentNames = ["v1"];
}
