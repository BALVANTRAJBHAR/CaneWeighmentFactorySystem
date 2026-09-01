IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] int NULL,
        [Username] nvarchar(max) NULL,
        [Role] nvarchar(max) NULL,
        [Action] nvarchar(450) NOT NULL,
        [Module] nvarchar(450) NOT NULL,
        [Entity] nvarchar(max) NULL,
        [EntityId] nvarchar(max) NULL,
        [OldValue] nvarchar(max) NULL,
        [NewValue] nvarchar(max) NULL,
        [Timestamp] datetime2 NOT NULL,
        [Ip] nvarchar(max) NULL,
        [Device] nvarchar(max) NULL,
        [Success] bit NOT NULL,
        [FailureReason] nvarchar(max) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Banks] (
        [Id] int NOT NULL IDENTITY,
        [BankName] nvarchar(450) NOT NULL,
        [BranchName] nvarchar(450) NOT NULL,
        [Address] nvarchar(max) NULL,
        [IFSC] nvarchar(11) NOT NULL,
        [ManagerName] nvarchar(max) NULL,
        [ManagerMobile] nvarchar(max) NULL,
        [ManagerEmail] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Banks] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Cameras] (
        [Id] int NOT NULL IDENTITY,
        [CameraNumber] int NOT NULL,
        [Vendor] nvarchar(max) NOT NULL,
        [Model] nvarchar(max) NULL,
        [Protocol] nvarchar(max) NOT NULL,
        [IpAddress] nvarchar(max) NOT NULL,
        [Port] int NOT NULL,
        [Username] nvarchar(max) NULL,
        [PasswordEncrypted] nvarchar(max) NULL,
        [Channel] int NOT NULL,
        [StreamType] nvarchar(max) NOT NULL,
        [RtspUrl] nvarchar(max) NULL,
        [OnvifSettings] nvarchar(max) NULL,
        [IsapiSettings] nvarchar(max) NULL,
        [Resolution] nvarchar(max) NULL,
        [Fps] int NULL,
        [CaptureEnabled] bit NOT NULL,
        [LiveViewEnabled] bit NOT NULL,
        [RetentionDays] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Cameras] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [CompanyConfigs] (
        [Id] int NOT NULL IDENTITY,
        [CompanyName] nvarchar(max) NOT NULL,
        [Address] nvarchar(max) NULL,
        [LogoPath] nvarchar(max) NULL,
        [DefaultLanguage] nvarchar(max) NOT NULL,
        [ThemeColor] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_CompanyConfigs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [DeviceConfigHistories] (
        [Id] int NOT NULL IDENTITY,
        [DeviceId] int NOT NULL,
        [Action] nvarchar(max) NOT NULL,
        [OldConfigJson] nvarchar(max) NULL,
        [NewConfigJson] nvarchar(max) NULL,
        [ChangedBy] int NULL,
        [ChangedByName] nvarchar(max) NULL,
        [ChangedAt] datetime2 NOT NULL,
        [Result] nvarchar(max) NULL,
        CONSTRAINT [PK_DeviceConfigHistories] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Items] (
        [Id] int NOT NULL IDENTITY,
        [ItemName] nvarchar(450) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Items] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [NumberSequences] (
        [Name] nvarchar(64) NOT NULL,
        [NextValue] bigint NOT NULL,
        CONSTRAINT [PK_NumberSequences] PRIMARY KEY ([Name])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Parties] (
        [Id] int NOT NULL IDENTITY,
        [PartyName] nvarchar(450) NOT NULL,
        [Mobile] nvarchar(10) NOT NULL,
        [Email] nvarchar(max) NULL,
        [Address] nvarchar(max) NULL,
        [Gst] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Parties] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [PaymentModes] (
        [Id] int NOT NULL IDENTITY,
        [ModeCode] nvarchar(450) NOT NULL,
        [ModeName] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_PaymentModes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(450) NOT NULL,
        [Module] nvarchar(max) NOT NULL,
        [Action] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [PrintConfigs] (
        [Id] int NOT NULL IDENTITY,
        [PrinterType] nvarchar(max) NOT NULL,
        [PrinterName] nvarchar(max) NOT NULL,
        [PaperType] nvarchar(max) NOT NULL,
        [AutoPrint] bit NOT NULL,
        [GrossCopies] int NOT NULL,
        [TareCopies] int NOT NULL,
        [PaymentCopies] int NOT NULL,
        [LoanCopies] int NOT NULL,
        [SalePurchaseCopies] int NOT NULL,
        [Language] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_PrintConfigs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [PurchaseImages] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseId] int NOT NULL,
        [GrowerId] int NOT NULL,
        [VillageId] int NOT NULL,
        [CameraId] int NOT NULL,
        [CaptureStage] nvarchar(max) NOT NULL,
        [ImageName] nvarchar(max) NOT NULL,
        [FilePath] nvarchar(max) NOT NULL,
        [FileHash] nvarchar(max) NULL,
        [CapturedAt] datetime2 NOT NULL,
        [CapturedBy] int NULL,
        [Status] bit NOT NULL,
        CONSTRAINT [PK_PurchaseImages] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [RazorpayConfigs] (
        [Id] int NOT NULL IDENTITY,
        [Enabled] bit NOT NULL,
        [Mode] nvarchar(max) NOT NULL,
        [KeyIdEncrypted] nvarchar(max) NULL,
        [KeySecretEncrypted] nvarchar(max) NULL,
        [WebhookSecretEncrypted] nvarchar(max) NULL,
        [AccountNumber] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_RazorpayConfigs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(450) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsSystem] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Seasons] (
        [Id] int NOT NULL IDENTITY,
        [SeasonName] nvarchar(max) NOT NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Seasons] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [SmsConfigs] (
        [Id] int NOT NULL IDENTITY,
        [ProviderName] nvarchar(max) NOT NULL,
        [ApiBaseUrl] nvarchar(max) NOT NULL,
        [HttpMethod] nvarchar(max) NOT NULL,
        [ApiKeyEncrypted] nvarchar(max) NULL,
        [ApiSecretEncrypted] nvarchar(max) NULL,
        [AuthorizationHeader] nvarchar(max) NULL,
        [SenderId] nvarchar(max) NULL,
        [EntityId] nvarchar(max) NULL,
        [Enabled] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_SmsConfigs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [SmsTemplates] (
        [Id] int NOT NULL IDENTITY,
        [EventCode] nvarchar(max) NOT NULL,
        [DltTemplateId] nvarchar(max) NULL,
        [MessageTemplate] nvarchar(max) NOT NULL,
        [Enabled] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_SmsTemplates] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [SoundConfigs] (
        [Id] int NOT NULL IDENTITY,
        [SoundEnabled] bit NOT NULL,
        [Language] nvarchar(max) NOT NULL,
        [VoiceVolume] int NOT NULL,
        [SpeechRate] decimal(4,2) NOT NULL,
        [RepeatIntervalSeconds] int NOT NULL,
        [RepeatMode] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_SoundConfigs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [SoundMessages] (
        [Id] int NOT NULL IDENTITY,
        [EventCode] nvarchar(max) NOT NULL,
        [LanguageCode] nvarchar(max) NOT NULL,
        [MessageText] nvarchar(max) NOT NULL,
        [Enabled] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_SoundMessages] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [StringProfiles] (
        [Id] int NOT NULL IDENTITY,
        [StringProfileName] nvarchar(max) NOT NULL,
        [StringType] nvarchar(max) NOT NULL,
        [ParserType] nvarchar(max) NOT NULL,
        [StartByte] nvarchar(max) NULL,
        [SignByte] nvarchar(max) NULL,
        [SignPosition] int NULL,
        [WeightStartPosition] int NOT NULL,
        [WeightLength] int NOT NULL,
        [WeightCharacterOrder] nvarchar(max) NOT NULL,
        [DecimalPosition] int NULL,
        [DecimalPlaces] int NOT NULL,
        [WeightUnit] nvarchar(max) NOT NULL,
        [EndByte] nvarchar(max) NULL,
        [CarriageReturn] bit NOT NULL,
        [LineFeed] bit NOT NULL,
        [PositiveSignValue] nvarchar(max) NOT NULL,
        [NegativeSignValue] nvarchar(max) NOT NULL,
        [StableWeightRule] nvarchar(max) NOT NULL,
        [StableWeightDurationMs] int NOT NULL,
        [FrameValidation] bit NOT NULL,
        [InvalidFrameHandling] nvarchar(max) NOT NULL,
        [Delimiter] nvarchar(max) NULL,
        [RegexPattern] nvarchar(max) NULL,
        [KeyName] nvarchar(max) NULL,
        [JsonPath] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_StringProfiles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [SystemSettings] (
        [Id] int NOT NULL IDENTITY,
        [Key] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [UserOtps] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NULL,
        [Mobile] nvarchar(max) NOT NULL,
        [OtpHash] nvarchar(max) NOT NULL,
        [Purpose] nvarchar(max) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConsumedAt] datetime2 NULL,
        [Attempts] int NOT NULL,
        CONSTRAINT [PK_UserOtps] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [Username] nvarchar(50) NOT NULL,
        [FullName] nvarchar(100) NOT NULL,
        [Mobile] nvarchar(10) NOT NULL,
        [Email] nvarchar(100) NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [MustChangePassword] bit NOT NULL,
        [FailedLoginCount] int NOT NULL,
        [LockoutEnd] datetime2 NULL,
        [LastLoginAt] datetime2 NULL,
        [PreferredLanguage] nvarchar(max) NULL,
        [ThemeMode] nvarchar(max) NULL,
        [ThemeColor] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [VarietyTypes] (
        [Id] int NOT NULL IDENTITY,
        [VarietyTypeName] nvarchar(450) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_VarietyTypes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [VehicleTypes] (
        [Id] int NOT NULL IDENTITY,
        [VehicleTypeName] nvarchar(450) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_VehicleTypes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [WeightRules] (
        [Id] int NOT NULL IDENTITY,
        [MinimumWeightQuintal] decimal(12,2) NOT NULL,
        [Enabled] bit NOT NULL,
        [ApplyToCanePurchase] bit NOT NULL,
        [ApplyToSalePurchase] bit NOT NULL,
        [ApplyToGross] bit NOT NULL,
        [ApplyToTare] bit NOT NULL,
        [DefaultCuttingPercent] decimal(5,2) NOT NULL,
        [DefaultTaxPercent] decimal(5,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_WeightRules] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Zones] (
        [Id] int NOT NULL,
        [ZoneCode] nvarchar(20) NOT NULL,
        [ZoneName] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Zones] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] int NOT NULL,
        [PermissionId] int NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [WeighingDevices] (
        [Id] int NOT NULL IDENTITY,
        [DeviceName] nvarchar(max) NOT NULL,
        [Manufacturer] nvarchar(max) NULL,
        [ModelNumber] nvarchar(max) NULL,
        [ConnectionType] nvarchar(max) NOT NULL,
        [ComPort] nvarchar(max) NOT NULL,
        [BaudRate] int NOT NULL,
        [Parity] nvarchar(max) NOT NULL,
        [DataBits] int NOT NULL,
        [StopBits] int NOT NULL,
        [FlowControl] nvarchar(max) NOT NULL,
        [CharacterEncoding] nvarchar(max) NOT NULL,
        [ReadTimeoutMs] int NOT NULL,
        [ReadIntervalMs] int NOT NULL,
        [AutoReconnect] bit NOT NULL,
        [ReconnectAttempts] int NOT NULL,
        [IsEnabled] bit NOT NULL,
        [ActiveConfiguration] bit NOT NULL,
        [ActiveStringProfileId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_WeighingDevices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WeighingDevices_StringProfiles_ActiveStringProfileId] FOREIGN KEY ([ActiveStringProfileId]) REFERENCES [StringProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [TokenHash] nvarchar(450) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedByIp] nvarchar(max) NULL,
        [DeviceInfo] nvarchar(max) NULL,
        [RevokedAt] datetime2 NULL,
        [RevokedByIp] nvarchar(max) NULL,
        [ReplacedByTokenHash] nvarchar(max) NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] int NOT NULL,
        [RoleId] int NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Rates] (
        [Id] int NOT NULL IDENTITY,
        [VarietyTypeId] int NOT NULL,
        [Rate] decimal(12,2) NOT NULL,
        [EffectiveFrom] datetime2 NOT NULL,
        [EffectiveTo] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Rates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Rates_VarietyTypes_VarietyTypeId] FOREIGN KEY ([VarietyTypeId]) REFERENCES [VarietyTypes] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Varieties] (
        [Id] int NOT NULL IDENTITY,
        [VarietyTypeId] int NOT NULL,
        [VarietyName] nvarchar(450) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Varieties] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Varieties_VarietyTypes_VarietyTypeId] FOREIGN KEY ([VarietyTypeId]) REFERENCES [VarietyTypes] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Villages] (
        [Id] int NOT NULL,
        [ZoneId] int NOT NULL,
        [VillageName] nvarchar(100) NOT NULL,
        [PradhanName] nvarchar(max) NULL,
        [Mobile] nvarchar(10) NULL,
        [Email] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Villages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Villages_Zones_ZoneId] FOREIGN KEY ([ZoneId]) REFERENCES [Zones] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Growers] (
        [Id] int NOT NULL,
        [VillageId] int NOT NULL,
        [GrowerSequence] int NOT NULL,
        [GrowerCode] nvarchar(20) NOT NULL,
        [GrowerName] nvarchar(max) NOT NULL,
        [FatherName] nvarchar(max) NOT NULL,
        [BankId] int NULL,
        [BankAccountNumber] nvarchar(max) NULL,
        [AccountHolderName] nvarchar(max) NULL,
        [AadhaarEncrypted] nvarchar(max) NULL,
        [AadhaarLast4] nvarchar(max) NULL,
        [AadhaarHash] nvarchar(450) NULL,
        [Mobile] nvarchar(10) NOT NULL,
        [Email] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Growers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Growers_Banks_BankId] FOREIGN KEY ([BankId]) REFERENCES [Banks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Growers_Villages_VillageId] FOREIGN KEY ([VillageId]) REFERENCES [Villages] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE TABLE [Purchases] (
        [Id] int NOT NULL,
        [GrowerId] int NOT NULL,
        [VillageId] int NOT NULL,
        [GrowerCode] nvarchar(450) NOT NULL,
        [VehicleTypeId] int NOT NULL,
        [VehicleNumber] nvarchar(max) NOT NULL,
        [VarietyTypeId] int NOT NULL,
        [VarietyId] int NOT NULL,
        [ScaleReadingGrossKg] decimal(12,2) NOT NULL,
        [GrossWeightQuintal] decimal(12,2) NOT NULL,
        [GrossDateTime] datetime2 NOT NULL,
        [GrossByUserId] int NOT NULL,
        [GrossByUserName] nvarchar(max) NOT NULL,
        [ScaleReadingTareKg] decimal(12,2) NULL,
        [TareWeightQuintal] decimal(12,2) NULL,
        [TareDateTime] datetime2 NULL,
        [TareByUserId] int NULL,
        [TareByUserName] nvarchar(max) NULL,
        [NetWeightQuintal] decimal(12,2) NULL,
        [CuttingPercent] decimal(12,2) NOT NULL,
        [CuttingWeightQuintal] decimal(12,2) NULL,
        [TaxPercent] decimal(12,2) NOT NULL,
        [TaxWeightQuintal] decimal(12,2) NULL,
        [FinalWeightQuintal] decimal(12,2) NULL,
        [Rate] decimal(12,2) NOT NULL,
        [PurchaseAmount] decimal(14,2) NULL,
        [PaymentFlag] nvarchar(max) NOT NULL,
        [PaymentStatus] nvarchar(450) NOT NULL,
        [GrossTareStatus] nvarchar(450) NOT NULL,
        [LockStatus] nvarchar(450) NOT NULL,
        [AdviceNumber] int NULL,
        [SeasonId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] int NULL,
        [Status] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] int NULL,
        CONSTRAINT [PK_Purchases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Purchases_Growers_GrowerId] FOREIGN KEY ([GrowerId]) REFERENCES [Growers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Purchases_Seasons_SeasonId] FOREIGN KEY ([SeasonId]) REFERENCES [Seasons] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Purchases_Varieties_VarietyId] FOREIGN KEY ([VarietyId]) REFERENCES [Varieties] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Purchases_VehicleTypes_VehicleTypeId] FOREIGN KEY ([VehicleTypeId]) REFERENCES [VehicleTypes] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Module_Action] ON [AuditLogs] ([Module], [Action]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Banks_BankName_BranchName] ON [Banks] ([BankName], [BranchName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Banks_IFSC] ON [Banks] ([IFSC]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Growers_AadhaarHash] ON [Growers] ([AadhaarHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Growers_BankId] ON [Growers] ([BankId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Growers_GrowerCode] ON [Growers] ([GrowerCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Growers_Mobile] ON [Growers] ([Mobile]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Growers_VillageId_GrowerSequence] ON [Growers] ([VillageId], [GrowerSequence]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Items_ItemName] ON [Items] ([ItemName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Parties_Mobile] ON [Parties] ([Mobile]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Parties_PartyName] ON [Parties] ([PartyName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentModes_ModeCode] ON [PaymentModes] ([ModeCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Code] ON [Permissions] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PurchaseImages_PurchaseId] ON [PurchaseImages] ([PurchaseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_AdviceNumber] ON [Purchases] ([AdviceNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_GrossDateTime] ON [Purchases] ([GrossDateTime]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_GrossTareStatus] ON [Purchases] ([GrossTareStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_GrowerCode] ON [Purchases] ([GrowerCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_GrowerId] ON [Purchases] ([GrowerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_LockStatus] ON [Purchases] ([LockStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_PaymentStatus] ON [Purchases] ([PaymentStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_SeasonId] ON [Purchases] ([SeasonId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_VarietyId] ON [Purchases] ([VarietyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_VehicleTypeId] ON [Purchases] ([VehicleTypeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Purchases_VillageId] ON [Purchases] ([VillageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Rates_VarietyTypeId_EffectiveFrom] ON [Rates] ([VarietyTypeId], [EffectiveFrom]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Varieties_VarietyTypeId_VarietyName] ON [Varieties] ([VarietyTypeId], [VarietyName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_VarietyTypes_VarietyTypeName] ON [VarietyTypes] ([VarietyTypeName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_VehicleTypes_VehicleTypeName] ON [VehicleTypes] ([VehicleTypeName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Villages_ZoneId_VillageName] ON [Villages] ([ZoneId], [VillageName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WeighingDevices_ActiveStringProfileId] ON [WeighingDevices] ([ActiveStringProfileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Zones_ZoneName] ON [Zones] ([ZoneName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901092642_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901092642_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

