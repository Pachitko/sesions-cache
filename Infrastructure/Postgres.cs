using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.NameTranslation;

namespace Infrastructure;


public static class Postgres
{
    private static readonly INpgsqlNameTranslator Translator = new NpgsqlSnakeCaseNameTranslator();

    /// <summary>
    /// Map DAL models to composite types (enables UNNEST)
    /// </summary>
    public static void MapCompositeTypes()
    {
        var mapper = NpgsqlConnection.GlobalTypeMapper;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        
        // mapper.MapComposite<UserEntityV1>("users_v1", Translator);
    }

    /// <summary>
    /// Add migration infrastructure
    /// </summary>
    public static void AddMigrations(IServiceCollection services)
    {
        // services.AddFluentMigratorCore()
        //     .ConfigureRunner(rb => rb.AddPostgres()
        //         .WithGlobalConnectionString(s =>
        //         {
        //             var cfg = s.GetRequiredService<IOptions<DalOptions>>();
        //             return cfg.Value.PostgresConnectionString;
        //         })
        //         .ScanIn(typeof(Postgres).Assembly).For.Migrations()
        //     )
        //     .AddLogging(lb => lb.AddFluentMigratorConsole());
    }
}