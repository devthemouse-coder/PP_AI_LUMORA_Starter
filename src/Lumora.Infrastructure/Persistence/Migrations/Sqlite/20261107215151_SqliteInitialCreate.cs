using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteInitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<short>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayNameFormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayNameKeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayNameNonce = table.Column<byte[]>(type: "BLOB", nullable: false),
                    DisplayNameCiphertext = table.Column<byte[]>(type: "BLOB", nullable: false),
                    DisplayNameAuthenticationTag = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Status = table.Column<short>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.UniqueConstraint("AK_Users_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SummaryFormatVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    SummaryKeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SummaryNonce = table.Column<byte[]>(type: "BLOB", nullable: true),
                    SummaryCiphertext = table.Column<byte[]>(type: "BLOB", nullable: true),
                    SummaryAuthenticationTag = table.Column<byte[]>(type: "BLOB", nullable: true),
                    SummaryUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<short>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.UniqueConstraint("AK_Conversations_TenantId_UserId_Id", x => new { x.TenantId, x.UserId, x.Id });
                    table.ForeignKey(
                        name: "FK_Conversations_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Memories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemoryType = table.Column<short>(type: "INTEGER", nullable: false),
                    ConfidenceScore = table.Column<int>(type: "INTEGER", nullable: false),
                    VerificationStatus = table.Column<short>(type: "INTEGER", nullable: false),
                    Sensitivity = table.Column<short>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ValidToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memories", x => x.Id);
                    table.UniqueConstraint("AK_Memories_TenantId_UserId_Id", x => new { x.TenantId, x.UserId, x.Id });
                    table.ForeignKey(
                        name: "FK_Memories_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Role = table.Column<short>(type: "INTEGER", nullable: false),
                    BodyFormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    BodyKeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BodyNonce = table.Column<byte[]>(type: "BLOB", nullable: false),
                    BodyCiphertext = table.Column<byte[]>(type: "BLOB", nullable: false),
                    BodyAuthenticationTag = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.UniqueConstraint("AK_Messages_TenantId_UserId_ConversationId_Id", x => new { x.TenantId, x.UserId, x.ConversationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_TenantId_UserId_ConversationId",
                        columns: x => new { x.TenantId, x.UserId, x.ConversationId },
                        principalTable: "Conversations",
                        principalColumns: new[] { "TenantId", "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemoryLookupTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenType = table.Column<short>(type: "INTEGER", nullable: false),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Token = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryLookupTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemoryLookupTokens_Memories_TenantId_UserId_MemoryId",
                        columns: x => new { x.TenantId, x.UserId, x.MemoryId },
                        principalTable: "Memories",
                        principalColumns: new[] { "TenantId", "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemoryPayloads",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ValueFormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ValueKeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ValueNonce = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ValueCiphertext = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ValueAuthenticationTag = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryPayloads", x => new { x.TenantId, x.UserId, x.MemoryId });
                    table.ForeignKey(
                        name: "FK_MemoryPayloads_Memories_TenantId_UserId_MemoryId",
                        columns: x => new { x.TenantId, x.UserId, x.MemoryId },
                        principalTable: "Memories",
                        principalColumns: new[] { "TenantId", "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StructuredFacts",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FactType = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ValueFormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ValueKeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ValueNonce = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ValueCiphertext = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ValueAuthenticationTag = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructuredFacts", x => new { x.TenantId, x.UserId, x.MemoryId });
                    table.ForeignKey(
                        name: "FK_StructuredFacts_Memories_TenantId_UserId_MemoryId",
                        columns: x => new { x.TenantId, x.UserId, x.MemoryId },
                        principalTable: "Memories",
                        principalColumns: new[] { "TenantId", "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemorySources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<short>(type: "INTEGER", nullable: false),
                    ConfidenceScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemorySources", x => x.Id);
                    table.UniqueConstraint("AK_MemorySources_TenantId_UserId_MemoryId_Id", x => new { x.TenantId, x.UserId, x.MemoryId, x.Id });
                    table.ForeignKey(
                        name: "FK_MemorySources_Memories_TenantId_UserId_MemoryId",
                        columns: x => new { x.TenantId, x.UserId, x.MemoryId },
                        principalTable: "Memories",
                        principalColumns: new[] { "TenantId", "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MemorySources_Messages_TenantId_UserId_ConversationId_MessageId",
                        columns: x => new { x.TenantId, x.UserId, x.ConversationId, x.MessageId },
                        principalTable: "Messages",
                        principalColumns: new[] { "TenantId", "UserId", "ConversationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemoryRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    ChangeType = table.Column<short>(type: "INTEGER", nullable: false),
                    PreviousFormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousKeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviousNonce = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PreviousCiphertext = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PreviousAuthenticationTag = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReasonFormatVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ReasonKeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ReasonNonce = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ReasonCiphertext = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ReasonAuthenticationTag = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemoryRevisions_Memories_TenantId_UserId_MemoryId",
                        columns: x => new { x.TenantId, x.UserId, x.MemoryId },
                        principalTable: "Memories",
                        principalColumns: new[] { "TenantId", "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MemoryRevisions_MemorySources_TenantId_UserId_MemoryId_SourceId",
                        columns: x => new { x.TenantId, x.UserId, x.MemoryId, x.SourceId },
                        principalTable: "MemorySources",
                        principalColumns: new[] { "TenantId", "UserId", "MemoryId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemoryRevisions_Users_TenantId_ChangedByUserId",
                        columns: x => new { x.TenantId, x.ChangedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TenantId_UserId_Status_UpdatedAtUtc_Id",
                table: "Conversations",
                columns: new[] { "TenantId", "UserId", "Status", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Memories_TenantId_UserId_IsActive_VerificationStatus_MemoryType_UpdatedAtUtc_Id",
                table: "Memories",
                columns: new[] { "TenantId", "UserId", "IsActive", "VerificationStatus", "MemoryType", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryLookupTokens_TenantId_UserId_MemoryId",
                table: "MemoryLookupTokens",
                columns: new[] { "TenantId", "UserId", "MemoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryLookupTokens_TenantId_UserId_TokenType_KeyId_Token_MemoryId",
                table: "MemoryLookupTokens",
                columns: new[] { "TenantId", "UserId", "TokenType", "KeyId", "Token", "MemoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRevisions_TenantId_ChangedByUserId",
                table: "MemoryRevisions",
                columns: new[] { "TenantId", "ChangedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRevisions_TenantId_UserId_MemoryId_RevisionNumber",
                table: "MemoryRevisions",
                columns: new[] { "TenantId", "UserId", "MemoryId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRevisions_TenantId_UserId_MemoryId_SourceId",
                table: "MemoryRevisions",
                columns: new[] { "TenantId", "UserId", "MemoryId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemorySources_TenantId_UserId_ConversationId_MessageId",
                table: "MemorySources",
                columns: new[] { "TenantId", "UserId", "ConversationId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_TenantId_UserId_ConversationId_SequenceNumber",
                table: "Messages",
                columns: new[] { "TenantId", "UserId", "ConversationId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StructuredFacts_TenantId_UserId_FactType",
                table: "StructuredFacts",
                columns: new[] { "TenantId", "UserId", "FactType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryLookupTokens");

            migrationBuilder.DropTable(
                name: "MemoryPayloads");

            migrationBuilder.DropTable(
                name: "MemoryRevisions");

            migrationBuilder.DropTable(
                name: "StructuredFacts");

            migrationBuilder.DropTable(
                name: "MemorySources");

            migrationBuilder.DropTable(
                name: "Memories");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
