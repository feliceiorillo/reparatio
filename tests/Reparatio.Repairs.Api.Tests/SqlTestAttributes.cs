using Reparatio.Repairs.Infrastructure;
using Xunit;

namespace Reparatio.Repairs.Api.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(SqlConnectionSettings.ReadOptional()))
            Skip = "Configure the local SQL secret or REPARATIO_SQL_CONNECTION.";
    }
}
public sealed class SqlTheoryAttribute : TheoryAttribute
{
    public SqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(SqlConnectionSettings.ReadOptional()))
            Skip = "Configure the local SQL secret or REPARATIO_SQL_CONNECTION.";
    }
}
