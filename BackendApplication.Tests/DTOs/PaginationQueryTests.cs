using BackendApplication.DTOs.Common;

namespace BackendApplication.Tests.DTOs;

/// <summary>
/// A sample of the clamping in <see cref="PaginationQuery"/>.
/// </summary>
/// <remarks>
/// An out-of-range page size is corrected rather than rejected, so these assert the
/// corrected value instead of an error.
/// </remarks>
public class PaginationQueryTests
{
    [Fact]
    public void Defaults_to_the_first_page_of_twenty()
    {
        var query = new PaginationQuery();

        Assert.Equal(1, query.PageNumber);
        Assert.Equal(20, query.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10_000, PaginationQuery.MaxPageSize)]
    [InlineData(50, 50)]
    public void PageSize_is_clamped_to_the_server_ceiling(int requested, int expected)
        => Assert.Equal(expected, new PaginationQuery { PageSize = requested }.PageSize);

    [Fact]
    public void Skip_and_Take_describe_the_SQL_window()
    {
        var query = new PaginationQuery { PageNumber = 3, PageSize = 25 };

        Assert.Equal(50, query.Skip);
        Assert.Equal(25, query.Take);
    }
}
