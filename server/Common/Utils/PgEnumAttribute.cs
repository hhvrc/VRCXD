namespace VRCX.Common.Utils;

/// <summary>
/// Marks an enum as a Postgres native enum type so it is registered with the EF Core
/// model via <see cref="VRCX.Common.VrcxDb.NpgsqlEnumExtensions.RegisterPgEnums"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class PgEnumAttribute : Attribute
{
    /// <summary>The Postgres type name. Always specified explicitly, e.g. <c>[PgEnum(Name = "user_state")]</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Postgres schema; defaults to the model's default schema when null.</summary>
    public string? Schema { get; init; }
}
