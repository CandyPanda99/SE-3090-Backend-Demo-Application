namespace BackendApplication.Domain.Enums;

/// <summary>
/// Sort order used by paginated list endpoints (<c>?sortDirection=desc</c>).
/// </summary>
public enum SortDirection
{
    /// <summary>Smallest / earliest / A-Z first.</summary>
    Asc = 0,

    /// <summary>Largest / latest / Z-A first.</summary>
    Desc = 1
}
