USE [SchemaStudio]
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[SchemaObjectColumn_UpsertFull]
    @Items dbo.SchemaObjectColumnUpsertType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM @Items)
    BEGIN
        RETURN;
    END;

    IF (SELECT COUNT(DISTINCT SchemaObjectId) FROM @Items) > 1
    BEGIN
        THROW 50001, 'SchemaObjectColumn_UpsertFull expects one SchemaObjectId per call.', 1;
    END;

    DECLARE @SchemaObjectId int;

    SELECT TOP (1)
        @SchemaObjectId = SchemaObjectId
    FROM @Items;

    MERGE dbo.SchemaObjectColumn AS target
    USING @Items AS source
        ON target.SchemaObjectColumnId = source.SchemaObjectColumnId
       AND source.SchemaObjectColumnId > 0
    WHEN MATCHED THEN
        UPDATE SET
            target.SchemaObjectId = source.SchemaObjectId,
            target.OrdinalPosition = source.OrdinalPosition,
            target.SourceColumnName = source.SourceColumnName,
            target.SourceColumnKind = source.SourceColumnKind,
            target.BaseDatabaseName = source.BaseDatabaseName,
            target.BaseSchemaName = source.BaseSchemaName,
            target.BaseObjectName = source.BaseObjectName,
            target.BaseColumnName = source.BaseColumnName,
            target.IsBaseDefinition = source.IsBaseDefinition,
            target.DisableInheritance = source.DisableInheritance,
            target.BusinessName = source.BusinessName,
            target.BusinessDescription = source.BusinessDescription,
            target.DeveloperNotes = source.DeveloperNotes,
            target.SemanticDatabase = source.SemanticDatabase,
            target.SemanticSchema = source.SemanticSchema,
            target.SemanticObject = source.SemanticObject,
            target.SemanticColumn = source.SemanticColumn,
            target.LastSynced = SYSDATETIME()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT
        (
            SchemaObjectId,
            OrdinalPosition,
            SourceColumnName,
            SourceColumnKind,
            BaseDatabaseName,
            BaseSchemaName,
            BaseObjectName,
            BaseColumnName,
            IsBaseDefinition,
            DisableInheritance,
            BusinessName,
            BusinessDescription,
            DeveloperNotes,
            SemanticDatabase,
            SemanticSchema,
            SemanticObject,
            SemanticColumn,
            LastSynced
        )
        VALUES
        (
            source.SchemaObjectId,
            source.OrdinalPosition,
            source.SourceColumnName,
            source.SourceColumnKind,
            source.BaseDatabaseName,
            source.BaseSchemaName,
            source.BaseObjectName,
            source.BaseColumnName,
            source.IsBaseDefinition,
            source.DisableInheritance,
            source.BusinessName,
            source.BusinessDescription,
            source.DeveloperNotes,
            source.SemanticDatabase,
            source.SemanticSchema,
            source.SemanticObject,
            source.SemanticColumn,
            SYSDATETIME()
        )
    WHEN NOT MATCHED BY SOURCE
         AND target.SchemaObjectId = @SchemaObjectId THEN
        DELETE;

    UPDATE col
    SET col.IsBaseDefinition = 1
    FROM dbo.SchemaObjectColumn col
    INNER JOIN dbo.SchemaObject obj
        ON col.SchemaObjectId = obj.SchemaObjectId
    WHERE obj.IsBaseObject = 1
      AND col.SchemaObjectId = @SchemaObjectId;

    EXEC [dbo].[ApplyColumnSemanticInheritance];
END
GO
