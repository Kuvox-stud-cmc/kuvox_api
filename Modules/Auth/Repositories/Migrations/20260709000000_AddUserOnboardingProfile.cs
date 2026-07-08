using System;
using Kuvox.Api.Modules.Auth.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Auth.Repositories.Migrations
{
    [DbContext(typeof(AuthDbContext))]
    [Migration("20260709000000_AddUserOnboardingProfile")]
    public partial class AddUserOnboardingProfile : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreationGoalsJson",
                schema: "auth",
                table: "users",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingCompletedAt",
                schema: "auth",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Personality",
                schema: "auth",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Casual");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreationGoalsJson",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Personality",
                schema: "auth",
                table: "users");
        }
    }
}
