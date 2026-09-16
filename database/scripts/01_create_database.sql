-- =====================================================================
-- CaneFactorySystem — 01_create_database.sql
-- Target: SQL Server 2019 Express (compatible with Standard upgrade)
-- Creates the database and a LEAST-PRIVILEGE application login.
-- Run as sysadmin (sa or Windows admin) in SSMS / sqlcmd.
-- =====================================================================

IF DB_ID('AFFLLPCaneFactory') IS NULL
BEGIN
    CREATE DATABASE AFFLLPCaneFactory;
END
GO

ALTER DATABASE AFFLLPCaneFactory SET RECOVERY FULL;  -- supports transaction-log backups
GO

-- ---------------------------------------------------------------------
-- Least-privilege SQL login for the API (NEVER use sa in production).
-- Replace <STRONG_DB_PASSWORD> before running. Do not commit real values.
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'canefactory_app')
BEGIN
    CREATE LOGIN canefactory_app WITH PASSWORD = 'AFFLLP@2026', CHECK_POLICY = ON;
END
GO

USE AFFLLPCaneFactory;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'canefactory_app')
BEGIN
    CREATE USER canefactory_app FOR LOGIN canefactory_app;
END
GO

-- Application needs read/write + DDL for EF Core migrations only.
ALTER ROLE db_datareader ADD MEMBER canefactory_app;
ALTER ROLE db_datawriter ADD MEMBER canefactory_app;
ALTER ROLE db_ddladmin  ADD MEMBER canefactory_app;   -- remove after schema is stable if desired
GO

-- Next step: run 02_schema_migration.sql (idempotent, generated from EF Core migrations)
-- or simply start the API once - it applies migrations automatically.
-- Seed data (roles, 570 permissions, masters, String Type 15 preset, developer account)
-- is inserted automatically by the API at startup and is fully idempotent.
PRINT 'Database and least-privilege login created. Now run 02_schema_migration.sql or start the API.';
GO
