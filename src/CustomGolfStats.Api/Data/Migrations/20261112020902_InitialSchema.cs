using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CustomGolfStats.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_claims", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_logins", x => new { x.login_provider, x.provider_key });
                });

            migrationBuilder.CreateTable(
                name: "account_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_tokens", x => new { x.user_id, x.login_provider, x.name });
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    tier = table.Column<string>(type: "text", nullable: false),
                    trial_shot_rounds_used = table.Column<int>(type: "integer", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    gender = table.Column<string>(type: "text", nullable: false),
                    ghin_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    home_course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    units = table.Column<string>(type: "text", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    club_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    website = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    provider_course_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merged_into_course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_courses", x => x.id);
                    table.ForeignKey(
                        name: "fk_courses_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_courses_courses_merged_into_course_id",
                        column: x => x.merged_into_course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "handicap_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_on = table.Column<DateOnly>(type: "date", nullable: false),
                    index = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_handicap_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_handicap_entries_users_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    gender = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tees", x => x.id);
                    table.ForeignKey(
                        name: "fk_tees_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "course_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hole_number = table.Column<int>(type: "integer", nullable: true),
                    reporter_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    resolved_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_course_flags", x => x.id);
                    table.ForeignKey(
                        name: "fk_course_flags_accounts_reporter_account_id",
                        column: x => x.reporter_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_course_flags_accounts_resolved_by_account_id",
                        column: x => x.resolved_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_course_flags_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_course_flags_tees_tee_id",
                        column: x => x.tee_id,
                        principalTable: "tees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tee_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    par = table.Column<int>(type: "integer", nullable: false),
                    rating = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    slope = table.Column<int>(type: "integer", nullable: false),
                    front_par = table.Column<int>(type: "integer", nullable: true),
                    front_rating = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    front_slope = table.Column<int>(type: "integer", nullable: true),
                    back_par = table.Column<int>(type: "integer", nullable: true),
                    back_rating = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    back_slope = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    changed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tee_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_tee_versions_tees_tee_id",
                        column: x => x.tee_id,
                        principalTable: "tees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tee_versions_users_changed_by_account_id",
                        column: x => x.changed_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rounds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tee_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    played_on = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entry_mode = table.Column<string>(type: "text", nullable: false),
                    round_type = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    weather_conditions = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    temperature_f = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    wind_speed_mph = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    wind_direction_degrees = table.Column<int>(type: "integer", nullable: true),
                    legacy_weather = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    distance_miles = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    step_count = table.Column<int>(type: "integer", nullable: true),
                    client_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    trial_counted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rounds", x => x.id);
                    table.ForeignKey(
                        name: "fk_rounds_tee_versions_tee_version_id",
                        column: x => x.tee_version_id,
                        principalTable: "tee_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rounds_users_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tee_holes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tee_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    par = table.Column<int>(type: "integer", nullable: false),
                    yardage = table.Column<int>(type: "integer", nullable: false),
                    stroke_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tee_holes", x => x.id);
                    table.ForeignKey(
                        name: "fk_tee_holes_tee_versions_tee_version_id",
                        column: x => x.tee_version_id,
                        principalTable: "tee_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hole_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hole_number = table.Column<int>(type: "integer", nullable: false),
                    entry_sequence = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    putts = table.Column<int>(type: "integer", nullable: false),
                    fairway_hit = table.Column<bool>(type: "boolean", nullable: false),
                    tee_shot_left = table.Column<bool>(type: "boolean", nullable: false),
                    tee_shot_right = table.Column<bool>(type: "boolean", nullable: false),
                    tee_shot_long = table.Column<bool>(type: "boolean", nullable: false),
                    tee_shot_short = table.Column<bool>(type: "boolean", nullable: false),
                    gir = table.Column<bool>(type: "boolean", nullable: false),
                    penalty_strokes = table.Column<int>(type: "integer", nullable: false),
                    greenside_sand_shots = table.Column<int>(type: "integer", nullable: false),
                    fairway_sand_shots = table.Column<int>(type: "integer", nullable: false),
                    is_derived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hole_results", x => x.id);
                    table.UniqueConstraint("ak_hole_results_id_round_id", x => new { x.id, x.round_id });
                    table.ForeignKey(
                        name: "fk_hole_results_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hole_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    start_lie = table.Column<string>(type: "text", nullable: false),
                    start_distance = table.Column<int>(type: "integer", nullable: false),
                    club = table.Column<string>(type: "text", nullable: false),
                    shot_type = table.Column<string>(type: "text", nullable: false),
                    end_lie = table.Column<string>(type: "text", nullable: false),
                    end_distance = table.Column<int>(type: "integer", nullable: true),
                    direction = table.Column<string>(type: "text", nullable: false),
                    shape = table.Column<string>(type: "text", nullable: true),
                    penalty_strokes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shots", x => x.id);
                    table.ForeignKey(
                        name: "fk_shots_hole_results_hole_result_id_round_id",
                        columns: x => new { x.hole_result_id, x.round_id },
                        principalTable: "hole_results",
                        principalColumns: new[] { "id", "round_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shots_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "key", "value" },
                values: new object[] { "TrialShotRounds", "3" });

            migrationBuilder.CreateIndex(
                name: "ix_account_claims_user_id",
                table: "account_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_logins_user_id",
                table: "account_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_home_course_id",
                table: "accounts",
                column: "home_course_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_normalized_email",
                table: "accounts",
                column: "normalized_email",
                unique: true,
                filter: "normalized_email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_normalized_user_name",
                table: "accounts",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_course_flags_course_id",
                table: "course_flags",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_flags_reporter_account_id",
                table: "course_flags",
                column: "reporter_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_flags_resolved_by_account_id",
                table: "course_flags",
                column: "resolved_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_flags_status",
                table: "course_flags",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_course_flags_tee_id",
                table: "course_flags",
                column: "tee_id");

            migrationBuilder.CreateIndex(
                name: "ix_courses_created_by_account_id",
                table: "courses",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_courses_merged_into_course_id",
                table: "courses",
                column: "merged_into_course_id");

            migrationBuilder.CreateIndex(
                name: "ix_courses_name",
                table: "courses",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_courses_source_provider_course_id",
                table: "courses",
                columns: new[] { "source", "provider_course_id" },
                unique: true,
                filter: "provider_course_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_handicap_entries_account_id_effective_on",
                table: "handicap_entries",
                columns: new[] { "account_id", "effective_on" });

            migrationBuilder.CreateIndex(
                name: "ix_hole_results_round_id_entry_sequence",
                table: "hole_results",
                columns: new[] { "round_id", "entry_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rounds_account_id_modified_at",
                table: "rounds",
                columns: new[] { "account_id", "modified_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rounds_tee_version_id",
                table: "rounds",
                column: "tee_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_shots_hole_result_id_round_id",
                table: "shots",
                columns: new[] { "hole_result_id", "round_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shots_hole_result_id_sequence",
                table: "shots",
                columns: new[] { "hole_result_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shots_round_id",
                table: "shots",
                column: "round_id");

            migrationBuilder.CreateIndex(
                name: "ix_tee_holes_tee_version_id_number",
                table: "tee_holes",
                columns: new[] { "tee_version_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tee_versions_changed_by_account_id",
                table: "tee_versions",
                column: "changed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_tee_versions_tee_id_effective_from",
                table: "tee_versions",
                columns: new[] { "tee_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tees_course_id",
                table: "tees",
                column: "course_id");

            migrationBuilder.AddForeignKey(
                name: "fk_account_claims_accounts_user_id",
                table: "account_claims",
                column: "user_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_logins_accounts_user_id",
                table: "account_logins",
                column: "user_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_tokens_accounts_user_id",
                table: "account_tokens",
                column: "user_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_accounts_courses_home_course_id",
                table: "accounts",
                column: "home_course_id",
                principalTable: "courses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_courses_accounts_created_by_account_id",
                table: "courses");

            migrationBuilder.DropTable(
                name: "account_claims");

            migrationBuilder.DropTable(
                name: "account_logins");

            migrationBuilder.DropTable(
                name: "account_tokens");

            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "course_flags");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "handicap_entries");

            migrationBuilder.DropTable(
                name: "shots");

            migrationBuilder.DropTable(
                name: "tee_holes");

            migrationBuilder.DropTable(
                name: "hole_results");

            migrationBuilder.DropTable(
                name: "rounds");

            migrationBuilder.DropTable(
                name: "tee_versions");

            migrationBuilder.DropTable(
                name: "tees");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "courses");
        }
    }
}
