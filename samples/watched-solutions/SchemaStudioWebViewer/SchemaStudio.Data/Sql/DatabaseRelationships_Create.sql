/*
    Human-run script only.

    Purpose:
    Create a first-class database relationship registry. This replaces the old
    lookup-only concept with a relationship header plus one or more column-pair
    rows, so physical FKs, lookup relationships, detail joins, and soft/domain
    joins can all be curated in one place.

    Expected use:
    1. Review this script.
    2. Drop/retire the old relationship table manually if desired.
    3. Run this script manually in the SchemaStudio database.

    Codex must not execute this script.
*/

CREATE TABLE dbo.DatabaseRelationships
(
    DatabaseRelationshipId int IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_DatabaseRelationships PRIMARY KEY,

    DatabaseId int NOT NULL,

    SourceSchemaName nvarchar(128) NOT NULL,
    SourceTableName nvarchar(128) NOT NULL,

    TargetSchemaName nvarchar(128) NOT NULL,
    TargetTableName nvarchar(128) NOT NULL,

    JoinType nvarchar(32) NOT NULL
        CONSTRAINT DF_DatabaseRelationships_JoinType DEFAULT (N'LEFT JOIN'),

    RelationshipRole nvarchar(32) NOT NULL
        CONSTRAINT DF_DatabaseRelationships_RelationshipRole DEFAULT (N'Lookup'),

    RelationshipName nvarchar(128) NULL,
    RelationshipKey nvarchar(256) NULL,

    SourceSystemDetected bit NOT NULL
        CONSTRAINT DF_DatabaseRelationships_SourceSystemDetected DEFAULT (0),

    UserConfirmed bit NOT NULL
        CONSTRAINT DF_DatabaseRelationships_UserConfirmed DEFAULT (0),

    DefaultIncludeInBaseView bit NOT NULL
        CONSTRAINT DF_DatabaseRelationships_DefaultIncludeInBaseView DEFAULT (0),

    UseInDomainObjectModeler bit NOT NULL
        CONSTRAINT DF_DatabaseRelationships_UseInDomainObjectModeler DEFAULT (1),

    UseInQueryBuilder bit NOT NULL
        CONSTRAINT DF_DatabaseRelationships_UseInQueryBuilder DEFAULT (1),

    DisplayColumnName nvarchar(128) NULL,
    FilterColumnName nvarchar(128) NULL,
    FilterValue nvarchar(128) NULL,
    LegalValues nvarchar(1500) NULL,

    DeveloperNotes nvarchar(1000) NULL,

    Active bit NOT NULL
        CONSTRAINT DF_DatabaseRelationships_Active DEFAULT (1),

    CreatedOn datetime2(0) NOT NULL
        CONSTRAINT DF_DatabaseRelationships_CreatedOn DEFAULT (SYSDATETIME()),

    UpdatedOn datetime2(0) NOT NULL
        CONSTRAINT DF_DatabaseRelationships_UpdatedOn DEFAULT (SYSDATETIME()),

    CONSTRAINT FK_DatabaseRelationships_Databases
        FOREIGN KEY (DatabaseId)
        REFERENCES dbo.Databases(DatabaseId)
);

CREATE TABLE dbo.DatabaseRelationshipColumns
(
    DatabaseRelationshipColumnId int IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_DatabaseRelationshipColumns PRIMARY KEY,

    DatabaseRelationshipId int NOT NULL,

    OrdinalPosition int NOT NULL
        CONSTRAINT DF_DatabaseRelationshipColumns_OrdinalPosition DEFAULT (1),

    SourceColumnName nvarchar(128) NOT NULL,
    TargetColumnName nvarchar(128) NOT NULL,

    CreatedOn datetime2(0) NOT NULL
        CONSTRAINT DF_DatabaseRelationshipColumns_CreatedOn DEFAULT (SYSDATETIME()),

    UpdatedOn datetime2(0) NOT NULL
        CONSTRAINT DF_DatabaseRelationshipColumns_UpdatedOn DEFAULT (SYSDATETIME()),

    CONSTRAINT FK_DatabaseRelationshipColumns_DatabaseRelationships
        FOREIGN KEY (DatabaseRelationshipId)
        REFERENCES dbo.DatabaseRelationships(DatabaseRelationshipId)
        ON DELETE CASCADE
);

CREATE INDEX IX_DatabaseRelationships_SourceTable
ON dbo.DatabaseRelationships
(
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    Active,
    RelationshipRole
);

CREATE INDEX IX_DatabaseRelationships_TargetTable
ON dbo.DatabaseRelationships
(
    DatabaseId,
    TargetSchemaName,
    TargetTableName,
    Active,
    RelationshipRole
);

CREATE UNIQUE INDEX UX_DatabaseRelationships_Key
ON dbo.DatabaseRelationships
(
    DatabaseId,
    RelationshipKey
)
WHERE RelationshipKey IS NOT NULL;

CREATE UNIQUE INDEX UX_DatabaseRelationshipColumns_RelationshipOrdinal
ON dbo.DatabaseRelationshipColumns
(
    DatabaseRelationshipId,
    OrdinalPosition
);
