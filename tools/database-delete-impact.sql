/*
  READ-ONLY foreign-key impact report for one row.
  Change only the four values below, then run in the CaneFactory database.
  This script NEVER deletes or updates data.
*/
SET NOCOUNT ON;

DECLARE @TargetSchema sysname = N'dbo';
DECLARE @TargetTable  sysname = N'Purchases';
DECLARE @TargetColumn sysname = N'Id';
DECLARE @TargetId     bigint  = 1;

DECLARE @TargetObjectId int = OBJECT_ID(QUOTENAME(@TargetSchema) + N'.' + QUOTENAME(@TargetTable));
IF @TargetObjectId IS NULL
    THROW 50001, 'Target table was not found in the current database.', 1;

SELECT
    fk.name AS ForeignKeyName,
    OBJECT_SCHEMA_NAME(fk.parent_object_id) AS ReferencingSchema,
    OBJECT_NAME(fk.parent_object_id) AS ReferencingTable,
    parentColumn.name AS ReferencingColumn,
    referencedColumn.name AS TargetColumn,
    fk.delete_referential_action_desc AS OnDeleteAction
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns parentColumn
  ON parentColumn.object_id = fkc.parent_object_id
 AND parentColumn.column_id = fkc.parent_column_id
JOIN sys.columns referencedColumn
  ON referencedColumn.object_id = fkc.referenced_object_id
 AND referencedColumn.column_id = fkc.referenced_column_id
WHERE fk.referenced_object_id = @TargetObjectId
  AND referencedColumn.name = @TargetColumn
ORDER BY ReferencingSchema, ReferencingTable, ReferencingColumn;

DECLARE @CountSql nvarchar(max);
SELECT @CountSql = STRING_AGG(CAST(
    N'SELECT N''' + REPLACE(OBJECT_SCHEMA_NAME(fk.parent_object_id), '''', '''''') + N'.' +
        REPLACE(OBJECT_NAME(fk.parent_object_id), '''', '''''') + N''' AS ReferencingTable, ' +
        N'N''' + REPLACE(parentColumn.name, '''', '''''') + N''' AS ReferencingColumn, ' +
        N'COUNT_BIG(*) AS ReferencingRows FROM ' +
        QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' +
        QUOTENAME(OBJECT_NAME(fk.parent_object_id)) +
        N' WHERE ' + QUOTENAME(parentColumn.name) + N' = @Id'
    AS nvarchar(max)), N' UNION ALL ')
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns parentColumn
  ON parentColumn.object_id = fkc.parent_object_id
 AND parentColumn.column_id = fkc.parent_column_id
JOIN sys.columns referencedColumn
  ON referencedColumn.object_id = fkc.referenced_object_id
 AND referencedColumn.column_id = fkc.referenced_column_id
WHERE fk.referenced_object_id = @TargetObjectId
  AND referencedColumn.name = @TargetColumn;

IF NULLIF(@CountSql, N'') IS NULL
    SELECT N'No enforced foreign key references this target column.' AS Information;
ELSE
    EXEC sys.sp_executesql @CountSql, N'@Id bigint', @Id = @TargetId;

/*
  Important: metadata tables such as PurchaseImages, SalePurchaseImages and
  PaymentImages contain semantic IDs but currently have no enforced FK in the
  EF model. Check those explicitly before any approved physical cleanup.
*/
