IF COL_LENGTH('dbo.SchemaObject', 'CompositionDefinitionJson') IS NULL
BEGIN
    ALTER TABLE dbo.SchemaObject
    ADD CompositionDefinitionJson nvarchar(2500) NULL;
END
ELSE
BEGIN
    ALTER TABLE dbo.SchemaObject
    ALTER COLUMN CompositionDefinitionJson nvarchar(2500) NULL;
END;
