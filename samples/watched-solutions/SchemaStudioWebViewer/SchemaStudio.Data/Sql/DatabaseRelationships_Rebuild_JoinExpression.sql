/*
    Human-run script only.

    Purpose:
    Rebuild the first-class database relationship registry with the join string
    as the primary human-readable relationship definition.

    Changes from the first draft:
    - Removes RelationshipKey. Imports should merge by the natural relationship
      shape instead of an opaque token.
    - Adds JoinExpression nvarchar(2000), because the actual ON clause is the
      important relationship fact for soft joins and query generation.
    - Keeps DatabaseRelationshipColumns as optional parsed support for simple
      column-pair joins.

    Expected use:
    1. Review this script.
    2. Run it manually in the SchemaStudio database only.
    3. Re-import source lookups/relationships from the app.

    Codex must not execute this script.
*/

IF OBJECT_ID(N'dbo.DatabaseRelationshipColumns', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.DatabaseRelationshipColumns;
END;

IF OBJECT_ID(N'dbo.DatabaseRelationships', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.DatabaseRelationships;
END;

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

    JoinExpression nvarchar(2000) NOT NULL,

    RelationshipRole nvarchar(32) NOT NULL
        CONSTRAINT DF_DatabaseRelationships_RelationshipRole DEFAULT (N'Lookup'),

    RelationshipName nvarchar(128) NULL,

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

CREATE UNIQUE INDEX UX_DatabaseRelationships_NaturalJoin
ON dbo.DatabaseRelationships
(
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    RelationshipRole,
    JoinType,
    JoinExpression
)
WHERE Active = 1;

CREATE UNIQUE INDEX UX_DatabaseRelationshipColumns_RelationshipOrdinal
ON dbo.DatabaseRelationshipColumns
(
    DatabaseRelationshipId,
    OrdinalPosition
);
