using System.Data;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;

namespace AchievementsSW2.Plugin.Database.Migrations;

public static class MigrationRunner
{
	public static void RunMigrations(IDbConnection dbConnection)
	{
		var serviceProvider = new ServiceCollection()
			.AddFluentMigratorCore()
			.ConfigureRunner(runner =>
			{
				ConfigureDatabase(runner, dbConnection);
				runner.ScanIn(typeof(MigrationRunner).Assembly).For.Migrations();
			})
			.AddLogging(logging => logging.AddFluentMigratorConsole())
			.BuildServiceProvider(false);

		using var scope = serviceProvider.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
		runner.MigrateUp();
	}

	private static void ConfigureDatabase(IMigrationRunnerBuilder runner, IDbConnection dbConnection)
	{
		switch (dbConnection)
		{
			case MySqlConnection:
				runner.AddMySql5();
				break;
			case NpgsqlConnection:
				runner.AddPostgres();
				break;
			case SqliteConnection:
				runner.AddSQLite();
				break;
			default:
				throw new NotSupportedException($"Unsupported database connection type: {dbConnection.GetType().Name}");
		}

		runner.WithGlobalConnectionString(dbConnection.ConnectionString);
	}
}
