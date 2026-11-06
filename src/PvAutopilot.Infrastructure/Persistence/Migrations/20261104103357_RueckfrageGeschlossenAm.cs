using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PvAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RueckfrageGeschlossenAm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RueckfrageGeschlossenAm",
                table: "processes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RueckfrageGeschlossenAm",
                table: "processes");
        }
    }
}
