using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PvAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedSubmission",
                table: "processes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancellationRequestedAt",
                table: "processes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EinspeisezusageDatum",
                table: "processes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Inbetriebnahmedatum",
                table: "processes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MastrNummerAnlage",
                table: "processes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MastrNummerSpeicher",
                table: "processes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetzbetreiberReferenz",
                table: "processes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "processes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RueckfrageErledigung",
                table: "processes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RueckfrageOffenSeit",
                table: "processes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateChangedAt",
                table: "processes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateOnly>(
                name: "ZaehlerwechselDatum",
                table: "processes",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_processes_State",
                table: "processes",
                column: "State");

            // Bestehende Vorgänge: letzter Zustandswechsel ist bestenfalls die letzte Änderung.
            migrationBuilder.Sql("""UPDATE processes SET "StateChangedAt" = "UpdatedAt";""");

            // Alte Zustandsnamen (nur Testdaten) auf die Netzanmeldungs-Stufe abbilden.
            foreach (var (oldName, newName) in RenamedStates)
            {
                migrationBuilder.Sql($"""UPDATE processes SET "State" = '{newName}' WHERE "State" = '{oldName}';""");
                migrationBuilder.Sql($"""UPDATE processes SET "BlockedFrom" = '{newName}' WHERE "BlockedFrom" = '{oldName}';""");
                migrationBuilder.Sql($"""UPDATE process_transitions SET "From" = '{newName}' WHERE "From" = '{oldName}';""");
                migrationBuilder.Sql($"""UPDATE process_transitions SET "To" = '{newName}' WHERE "To" = '{oldName}';""");
            }
        }

        private static readonly (string Old, string New)[] RenamedStates =
        [
            ("ApplicationPrepared", "NetzanmeldungPrepared"),
            ("AwaitingReview", "NetzanmeldungAwaitingReview"),
            ("ApplicationSubmitted", "NetzanmeldungSubmitted"),
        ];

        /// <summary>Zustände, die es vor dieser Migration nicht gab; ein Rückbau würde sie unlesbar machen.</summary>
        private static readonly string[] NewStates =
        [
            "InbetriebnahmeReady", "InbetriebnahmePrepared", "InbetriebnahmeAwaitingReview",
            "ZaehlerwechselPrepared", "ZaehlerwechselAwaitingReview", "ZaehlerwechselCompleted",
            "MastrPrepared", "MastrAwaitingReview", "Cancelled",
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Erst prüfen, dann zurückbauen: Vorgänge in neuen Zuständen hätten danach keinen gültigen Zustand mehr.
            var states = string.Join(", ", NewStates.Select(s => $"'{s}'"));
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM processes WHERE "State" IN ({states}) OR "BlockedFrom" IN ({states}))
                       OR EXISTS (SELECT 1 FROM process_transitions WHERE "From" IN ({states}) OR "To" IN ({states})) THEN
                        RAISE EXCEPTION 'Rückbau von ProcessFlow nicht möglich: Vorgänge oder Übergänge nutzen Zustände, die es vorher nicht gab ({string.Join(", ", NewStates)}). Diese Vorgänge zuerst bereinigen.';
                    END IF;
                END $$;
                """);

            foreach (var (oldName, newName) in RenamedStates)
            {
                migrationBuilder.Sql($"""UPDATE processes SET "State" = '{oldName}' WHERE "State" = '{newName}';""");
                migrationBuilder.Sql($"""UPDATE processes SET "BlockedFrom" = '{oldName}' WHERE "BlockedFrom" = '{newName}';""");
                migrationBuilder.Sql($"""UPDATE process_transitions SET "From" = '{oldName}' WHERE "From" = '{newName}';""");
                migrationBuilder.Sql($"""UPDATE process_transitions SET "To" = '{oldName}' WHERE "To" = '{newName}';""");
            }

            migrationBuilder.DropIndex(
                name: "IX_processes_State",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "ApprovedSubmission",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "CancellationRequestedAt",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "EinspeisezusageDatum",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "Inbetriebnahmedatum",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "MastrNummerAnlage",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "MastrNummerSpeicher",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "NetzbetreiberReferenz",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "RueckfrageErledigung",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "RueckfrageOffenSeit",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "StateChangedAt",
                table: "processes");

            migrationBuilder.DropColumn(
                name: "ZaehlerwechselDatum",
                table: "processes");
        }
    }
}
