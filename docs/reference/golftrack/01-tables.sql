

/****** Object:  Table [dbo].[Course]    Script Date: 11/21/2019 4:58:27 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Course]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Course](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](100) NOT NULL,
	[Address] [varchar](100) NOT NULL,
	[Address2] [varchar](100) NULL,
	[City] [varchar](50) NOT NULL,
	[StateId] [int] NOT NULL,
	[PostalCode] [varchar](10) NOT NULL,
	[CountryId] [int] NOT NULL,
	[PhoneNumber] [varchar](15) NOT NULL,
	[Website] [varchar](100) NULL,
	[USGACourseId] [int] NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Course] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[CustomMetric]    Script Date: 11/21/2019 4:58:28 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CustomMetric]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[CustomMetric](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[GolferId] [int] NOT NULL,
	[MetricTypeId] [int] NOT NULL,
	[Name] [varchar](100) NOT NULL,
	[DataTypeId] [int] NOT NULL,
	[DataTypeLength] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_CustomMetric] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[CustomMetricData]    Script Date: 11/21/2019 4:58:28 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CustomMetricData]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[CustomMetricData](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CustomMetricId] [int] NOT NULL,
	[KeyId] [int] NOT NULL,
	[Value] [varchar](max) NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_CustomMetricData] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[DataType]    Script Date: 11/21/2019 4:58:29 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DataType]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[DataType](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](50) NOT NULL,
	[DefaultLength] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_DataType] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[DataTypeOption]    Script Date: 11/21/2019 4:58:29 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DataTypeOption]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[DataTypeOption](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CustomMetricId] [int] NOT NULL,
	[Value] [varchar](200) NOT NULL,
	[Display] [varchar](200) NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_DataTypeOption] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[GameGolf_RoundExport]    Script Date: 11/21/2019 4:58:29 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_RoundExport]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[GameGolf_RoundExport](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[RoundId] [int] NULL,
	[GameGolfRoundId] [int] NULL,
	[ScorecardId] [int] NULL,
	[StartTimestamp] [datetime] NULL,
	[DeviceNumber] [int] NULL,
	[DeviceHardwareRevisionNumber] [int] NULL,
	[HoleNumber] [int] NULL,
	[Latitude] [decimal](18, 10) NULL,
	[Longitude] [decimal](18, 10) NULL,
	[EquipmentSubTypeId] [int] NULL,
	[ShotTimestamp] [datetime] NULL,
	[ShotSource] [bit] NULL,
	[ShotHoleSequenceNumber] [int] NULL,
	[ShotRoundSequenceNumber] [int] NULL,
	[StartOfShotPolygonFenceId] [int] NULL,
	[GameGolfHoleId] [int] NULL,
	[TagLabelid] [int] NULL,
	[CalculatedDistanceMeters] [decimal](18, 5) NULL,
	[AccuracyScore] [decimal](18, 5) NULL,
	[ShotOrderingRank] [int] NULL,
	[CalculatedDistance] [decimal](18, 5) NULL,
	[GameGolfShotId] [int] NULL,
	[ShotHasEdits] [bit] NULL,
	[CreatedTimestamp] [datetime] NULL,
	[ModifiedTimestamp] [datetime] NULL,
	[ApplicationVersion] [varchar](50) NULL,
	[LifecycleStatus] [int] NULL,
	[ShotTagSequenceNumber] [int] NULL,
	[EndLocationLatitude] [decimal](18, 10) NULL,
	[EndLocationLongitude] [decimal](18, 10) NULL,
	[StartGfcId] [int] NULL,
	[EndGfcId] [int] NULL,
	[Score] [int] NULL,
	[EquipmentTypeId] [int] NULL,
	[ModelRangeId] [int] NULL,
	[ManufacturerId] [int] NULL,
	[ApproachMissHorizontal] [decimal](18, 15) NULL,
	[ApproachMissVertical] [decimal](18, 15) NULL,
	[ApproachMissTargetLatitude] [decimal](18, 10) NULL,
	[ApproachMissTargetLongitude] [decimal](18, 10) NULL,
	[ApproachMissTargetDistance] [decimal](18, 15) NULL,
	[TeeMissIsFairwayAccurate] [bit] NULL,
	[TeeMissLeftEdgeDistance] [decimal](18, 15) NULL,
	[TeeMissRightEdgeDistance] [decimal](18, 15) NULL,
	[TeeMissFairwayWidth] [decimal](18, 15) NULL,
	[TeeMissHorizontal] [decimal](18, 15) NULL,
	[StrokesGained] [decimal](18, 15) NULL,
	[PuttShot] [int] NULL,
 CONSTRAINT [PK_GameGolf_RoundExport] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[GG_ClubManufacturer]    Script Date: 11/21/2019 4:58:29 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GG_ClubManufacturer]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[GG_ClubManufacturer](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](100) NOT NULL,
	[GameGolfId] [int] NOT NULL,
	[CreatedOn] [datetime] NOT NULL,
	[ModifiedOn] [datetime] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_GG_ClubManufacturer] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[GG_ClubModel]    Script Date: 11/21/2019 4:58:29 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GG_ClubModel]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[GG_ClubModel](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](100) NOT NULL,
	[ManufacturerId] [int] NOT NULL,
	[GameGolfId] [int] NOT NULL,
	[CreatedOn] [datetime] NOT NULL,
	[ModifiedOn] [datetime] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_GG_ClubModel] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[GG_Equipment]    Script Date: 11/21/2019 4:58:30 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GG_Equipment]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[GG_Equipment](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[GameGolfId] [int] NOT NULL,
	[ModelId] [int] NOT NULL,
	[EquipmentSubTypeId] [int] NOT NULL,
	[Name] [varchar](100) NULL,
	[GameGolfMD5] [varchar](100) NOT NULL,
	[CreatedOn] [datetime] NOT NULL,
	[ModifiedOn] [datetime] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_GG_Equipment] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[GG_EquipmentSubType]    Script Date: 11/21/2019 4:58:30 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GG_EquipmentSubType]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[GG_EquipmentSubType](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[GameGolfId] [int] NOT NULL,
	[Name] [varchar](100) NOT NULL,
	[DisplayName] [varchar](100) NOT NULL,
	[IsDefault] [bit] NULL,
	[Rank] [int] NOT NULL,
	[EquipmentTypeId] [int] NOT NULL,
	[CreatedOn] [datetime] NOT NULL,
	[ModifiedOn] [datetime] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_GG_EquipmentSubType] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[GG_EquipmentType]    Script Date: 11/21/2019 4:58:30 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GG_EquipmentType]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[GG_EquipmentType](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[GameGolfId] [int] NOT NULL,
	[Name] [varchar](100) NOT NULL,
	[DisplayName] [varchar](100) NOT NULL,
	[IsDefault] [bit] NULL,
	[CreatedOn] [datetime] NOT NULL,
	[ModifiedOn] [datetime] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_GG_EquipmentType] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[GG_TagSet]    Script Date: 11/21/2019 4:58:30 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GG_TagSet]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[GG_TagSet](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[GolferId] [int] NOT NULL,
	[EquipmentTypeId] [int] NOT NULL,
	[EquipmentSubTypeId] [int] NOT NULL,
	[UserEquipmentId] [int] NOT NULL,
	[CreatedOn] [datetime] NOT NULL,
	[ModifiedOn] [datetime] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_GG_TagSet] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[GG_UserEquipment]    Script Date: 11/21/2019 4:58:31 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GG_UserEquipment]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[GG_UserEquipment](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[GolferId] [int] NOT NULL,
	[GameGolfId] [int] NOT NULL,
	[EquipmentId] [int] NOT NULL,
	[EquipmentSubTypeId] [int] NOT NULL,
	[OriginationSourceId] [int] NOT NULL,
	[CreatedOn] [datetime] NOT NULL,
	[ModifiedOn] [datetime] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_GG_UserEquipment] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[Golfer]    Script Date: 11/21/2019 4:58:31 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Golfer]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Golfer](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[FirstName] [varchar](50) NOT NULL,
	[LastName] [varchar](50) NOT NULL,
	[Email] [varchar](100) NOT NULL,
	[FacebookId] [varchar](50) NOT NULL,
	[Gender] [varchar](1) NULL,
	[GHINNumber] [varchar](15) NULL,
	[GameGolfUserId] [int] NULL,
	[GameGolfUserName] [varchar](30) NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Golfer] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[HandicapHistory]    Script Date: 11/21/2019 4:58:31 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[HandicapHistory]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[HandicapHistory](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[GolferId] [int] NOT NULL,
	[HandicapDate] [datetime] NOT NULL,
	[HandicapIndex] [decimal](18, 1) NOT NULL,
 CONSTRAINT [PK_HandicapHistory] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[Hole]    Script Date: 11/21/2019 4:58:31 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Hole]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Hole](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[TeeId] [int] NOT NULL,
	[Number] [int] NOT NULL,
	[Par] [int] NOT NULL,
	[Yardage] [int] NOT NULL,
	[Handicap] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Hole] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[MetricType]    Script Date: 11/21/2019 4:58:32 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[MetricType]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[MetricType](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](100) NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_MetricType] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[Round]    Script Date: 11/21/2019 4:58:32 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Round](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[GolferId] [int] NOT NULL,
	[TeeId] [int] NOT NULL,
	[RoundTypeId] [int] NULL,
	[WeatherConditionId] [int] NULL,
	[WindConditionId] [int] NULL,
	[TemperatureId] [int] NULL,
	[HolesPlayed] [int] NULL,
	[Score] [int] NOT NULL,
	[Duration] [varchar](50) NULL,
	[Distance] [decimal](18, 4) NULL,
	[PlayedOn] [smalldatetime] NOT NULL,
	[Notes] [varchar](2000) NULL,
	[Hole19Url] [varchar](100) NULL,
	[GameGolfUrl] [varchar](100) NULL,
	[IsActive] [bit] NOT NULL,
	[Hole19Id] [varchar](20) NULL,
	[GameGolfId] [varchar](20) NULL,
 CONSTRAINT [PK_Round] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[RoundDetail]    Script Date: 11/21/2019 4:58:32 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RoundDetail]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[RoundDetail](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[RoundId] [int] NOT NULL,
	[HoleId] [int] NOT NULL,
	[SequenceNumber] [int] NULL,
	[Score] [int] NOT NULL,
	[Putts] [int] NOT NULL,
	[FairwayHit] [bit] NOT NULL,
	[TeeShotLeft] [bit] NOT NULL,
	[TeeShotRight] [bit] NOT NULL,
	[TeeShotLong] [bit] NOT NULL,
	[TeeShotShort] [bit] NOT NULL,
	[GreenInRegulation] [bit] NOT NULL,
	[PenaltyShots] [int] NOT NULL,
	[SandShots] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_RoundDetail] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[RoundType]    Script Date: 11/21/2019 4:58:32 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RoundType]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[RoundType](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](50) NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_RoundType] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[Tee]    Script Date: 11/21/2019 4:58:33 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Tee]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Tee](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CourseId] [int] NOT NULL,
	[Name] [varchar](50) NOT NULL,
	[Gender] [varchar](1) NULL,
	[Par] [int] NOT NULL,
	[CourseRating] [float] NULL,
	[SlopeRating] [int] NULL,
	[FrontNinePar] [int] NOT NULL,
	[FrontNineCourseRating] [float] NULL,
	[FrontNineSlopeRating] [int] NULL,
	[BackNinePar] [int] NOT NULL,
	[BackNineCourseRating] [float] NULL,
	[BackNineSlopeRating] [int] NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Tee] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[Temperature]    Script Date: 11/21/2019 4:58:33 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Temperature]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Temperature](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](50) NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Temperature] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[WeatherCondition]    Script Date: 11/21/2019 4:58:33 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WeatherCondition]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[WeatherCondition](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](50) NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_WeatherCondition] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

/****** Object:  Table [dbo].[WindCondition]    Script Date: 11/21/2019 4:58:33 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WindCondition]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[WindCondition](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](50) NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_WindCondition] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_Course_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[Course] ADD  CONSTRAINT [DF_Course_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_CustomMetric_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[CustomMetric] ADD  CONSTRAINT [DF_CustomMetric_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_CustomMetricData_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[CustomMetricData] ADD  CONSTRAINT [DF_CustomMetricData_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_DataType_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[DataType] ADD  CONSTRAINT [DF_DataType_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_DataTypeOption_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[DataTypeOption] ADD  CONSTRAINT [DF_DataTypeOption_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_ClubManufacturer_CreatedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_ClubManufacturer] ADD  CONSTRAINT [DF_GG_ClubManufacturer_CreatedOn]  DEFAULT (getutcdate()) FOR [CreatedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_ClubManufacturer_ModifiedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_ClubManufacturer] ADD  CONSTRAINT [DF_GG_ClubManufacturer_ModifiedOn]  DEFAULT (getutcdate()) FOR [ModifiedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_ClubManufacturer_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_ClubManufacturer] ADD  CONSTRAINT [DF_GG_ClubManufacturer_IsActive]  DEFAULT ((0)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_ClubModel_CreatedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_ClubModel] ADD  CONSTRAINT [DF_GG_ClubModel_CreatedOn]  DEFAULT (getutcdate()) FOR [CreatedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_ClubModel_ModifiedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_ClubModel] ADD  CONSTRAINT [DF_GG_ClubModel_ModifiedOn]  DEFAULT (getutcdate()) FOR [ModifiedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_ClubModel_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_ClubModel] ADD  CONSTRAINT [DF_GG_ClubModel_IsActive]  DEFAULT ((0)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_Equipment_CreatedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_Equipment] ADD  CONSTRAINT [DF_GG_Equipment_CreatedOn]  DEFAULT (getutcdate()) FOR [CreatedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_Equipment_ModifiedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_Equipment] ADD  CONSTRAINT [DF_GG_Equipment_ModifiedOn]  DEFAULT (getutcdate()) FOR [ModifiedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_Equipment_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_Equipment] ADD  CONSTRAINT [DF_GG_Equipment_IsActive]  DEFAULT ((0)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_EquipmentSubType_CreatedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_EquipmentSubType] ADD  CONSTRAINT [DF_GG_EquipmentSubType_CreatedOn]  DEFAULT (getutcdate()) FOR [CreatedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_EquipmentSubType_ModifiedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_EquipmentSubType] ADD  CONSTRAINT [DF_GG_EquipmentSubType_ModifiedOn]  DEFAULT (getutcdate()) FOR [ModifiedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_EquipmentSubType_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_EquipmentSubType] ADD  CONSTRAINT [DF_GG_EquipmentSubType_IsActive]  DEFAULT ((0)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_EquipmentType_CreatedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_EquipmentType] ADD  CONSTRAINT [DF_GG_EquipmentType_CreatedOn]  DEFAULT (getutcdate()) FOR [CreatedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_EquipmentType_ModifiedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_EquipmentType] ADD  CONSTRAINT [DF_GG_EquipmentType_ModifiedOn]  DEFAULT (getutcdate()) FOR [ModifiedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_EquipmentType_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_EquipmentType] ADD  CONSTRAINT [DF_GG_EquipmentType_IsActive]  DEFAULT ((0)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_TagSet_CreatedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_TagSet] ADD  CONSTRAINT [DF_GG_TagSet_CreatedOn]  DEFAULT (getutcdate()) FOR [CreatedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_TagSet_ModifiedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_TagSet] ADD  CONSTRAINT [DF_GG_TagSet_ModifiedOn]  DEFAULT (getutcdate()) FOR [ModifiedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_TagSet_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_TagSet] ADD  CONSTRAINT [DF_GG_TagSet_IsActive]  DEFAULT ((0)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_UserEquipment_CreatedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_UserEquipment] ADD  CONSTRAINT [DF_GG_UserEquipment_CreatedOn]  DEFAULT (getutcdate()) FOR [CreatedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_UserEquipment_ModifiedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_UserEquipment] ADD  CONSTRAINT [DF_GG_UserEquipment_ModifiedOn]  DEFAULT (getutcdate()) FOR [ModifiedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_GG_UserEquipment_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[GG_UserEquipment] ADD  CONSTRAINT [DF_GG_UserEquipment_IsActive]  DEFAULT ((0)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_Golfer_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[Golfer] ADD  CONSTRAINT [DF_Golfer_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_Hole_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[Hole] ADD  CONSTRAINT [DF_Hole_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_MetricType_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[MetricType] ADD  CONSTRAINT [DF_MetricType_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_Round_PlayedOn]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[Round] ADD  CONSTRAINT [DF_Round_PlayedOn]  DEFAULT (getdate()) FOR [PlayedOn]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_Round_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[Round] ADD  CONSTRAINT [DF_Round_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_RoundDetail_FairwayHit]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[RoundDetail] ADD  CONSTRAINT [DF_RoundDetail_FairwayHit]  DEFAULT ((0)) FOR [FairwayHit]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_RoundDetail_TeeShotLeft]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[RoundDetail] ADD  CONSTRAINT [DF_RoundDetail_TeeShotLeft]  DEFAULT ((0)) FOR [TeeShotLeft]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_RoundDetail_TeeShotShort]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[RoundDetail] ADD  CONSTRAINT [DF_RoundDetail_TeeShotShort]  DEFAULT ((0)) FOR [TeeShotShort]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_RoundDetail_GreenInRegulation]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[RoundDetail] ADD  CONSTRAINT [DF_RoundDetail_GreenInRegulation]  DEFAULT ((0)) FOR [GreenInRegulation]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_RoundDetail_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[RoundDetail] ADD  CONSTRAINT [DF_RoundDetail_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_RoundType_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[RoundType] ADD  CONSTRAINT [DF_RoundType_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_Tee_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[Tee] ADD  CONSTRAINT [DF_Tee_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_Temperature_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[Temperature] ADD  CONSTRAINT [DF_Temperature_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_WeatherCondition_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[WeatherCondition] ADD  CONSTRAINT [DF_WeatherCondition_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DF_WindCondition_IsActive]') AND type = 'D')
BEGIN
ALTER TABLE [dbo].[WindCondition] ADD  CONSTRAINT [DF_WindCondition_IsActive]  DEFAULT ((1)) FOR [IsActive]
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CustomMetric_DataType]') AND parent_object_id = OBJECT_ID(N'[dbo].[CustomMetric]'))
ALTER TABLE [dbo].[CustomMetric]  WITH CHECK ADD  CONSTRAINT [FK_CustomMetric_DataType] FOREIGN KEY([DataTypeId])
REFERENCES [dbo].[DataType] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CustomMetric_DataType]') AND parent_object_id = OBJECT_ID(N'[dbo].[CustomMetric]'))
ALTER TABLE [dbo].[CustomMetric] CHECK CONSTRAINT [FK_CustomMetric_DataType]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CustomMetric_Golfer]') AND parent_object_id = OBJECT_ID(N'[dbo].[CustomMetric]'))
ALTER TABLE [dbo].[CustomMetric]  WITH CHECK ADD  CONSTRAINT [FK_CustomMetric_Golfer] FOREIGN KEY([GolferId])
REFERENCES [dbo].[Golfer] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CustomMetric_Golfer]') AND parent_object_id = OBJECT_ID(N'[dbo].[CustomMetric]'))
ALTER TABLE [dbo].[CustomMetric] CHECK CONSTRAINT [FK_CustomMetric_Golfer]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CustomMetric_MetricType]') AND parent_object_id = OBJECT_ID(N'[dbo].[CustomMetric]'))
ALTER TABLE [dbo].[CustomMetric]  WITH CHECK ADD  CONSTRAINT [FK_CustomMetric_MetricType] FOREIGN KEY([MetricTypeId])
REFERENCES [dbo].[MetricType] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CustomMetric_MetricType]') AND parent_object_id = OBJECT_ID(N'[dbo].[CustomMetric]'))
ALTER TABLE [dbo].[CustomMetric] CHECK CONSTRAINT [FK_CustomMetric_MetricType]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CustomMetricData_CustomMetric]') AND parent_object_id = OBJECT_ID(N'[dbo].[CustomMetricData]'))
ALTER TABLE [dbo].[CustomMetricData]  WITH CHECK ADD  CONSTRAINT [FK_CustomMetricData_CustomMetric] FOREIGN KEY([CustomMetricId])
REFERENCES [dbo].[CustomMetric] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CustomMetricData_CustomMetric]') AND parent_object_id = OBJECT_ID(N'[dbo].[CustomMetricData]'))
ALTER TABLE [dbo].[CustomMetricData] CHECK CONSTRAINT [FK_CustomMetricData_CustomMetric]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_DataTypeOption_CustomMetric]') AND parent_object_id = OBJECT_ID(N'[dbo].[DataTypeOption]'))
ALTER TABLE [dbo].[DataTypeOption]  WITH CHECK ADD  CONSTRAINT [FK_DataTypeOption_CustomMetric] FOREIGN KEY([CustomMetricId])
REFERENCES [dbo].[CustomMetric] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_DataTypeOption_CustomMetric]') AND parent_object_id = OBJECT_ID(N'[dbo].[DataTypeOption]'))
ALTER TABLE [dbo].[DataTypeOption] CHECK CONSTRAINT [FK_DataTypeOption_CustomMetric]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_ClubModel_GG_ClubManufacturer]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_ClubModel]'))
ALTER TABLE [dbo].[GG_ClubModel]  WITH CHECK ADD  CONSTRAINT [FK_GG_ClubModel_GG_ClubManufacturer] FOREIGN KEY([ManufacturerId])
REFERENCES [dbo].[GG_ClubManufacturer] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_ClubModel_GG_ClubManufacturer]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_ClubModel]'))
ALTER TABLE [dbo].[GG_ClubModel] CHECK CONSTRAINT [FK_GG_ClubModel_GG_ClubManufacturer]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_Equipment_GG_ClubModel]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_Equipment]'))
ALTER TABLE [dbo].[GG_Equipment]  WITH CHECK ADD  CONSTRAINT [FK_GG_Equipment_GG_ClubModel] FOREIGN KEY([ModelId])
REFERENCES [dbo].[GG_ClubModel] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_Equipment_GG_ClubModel]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_Equipment]'))
ALTER TABLE [dbo].[GG_Equipment] CHECK CONSTRAINT [FK_GG_Equipment_GG_ClubModel]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_Equipment_GG_EquipmentSubType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_Equipment]'))
ALTER TABLE [dbo].[GG_Equipment]  WITH CHECK ADD  CONSTRAINT [FK_GG_Equipment_GG_EquipmentSubType] FOREIGN KEY([EquipmentSubTypeId])
REFERENCES [dbo].[GG_EquipmentSubType] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_Equipment_GG_EquipmentSubType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_Equipment]'))
ALTER TABLE [dbo].[GG_Equipment] CHECK CONSTRAINT [FK_GG_Equipment_GG_EquipmentSubType]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_EquipmentSubType_GG_EquipmentType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_EquipmentSubType]'))
ALTER TABLE [dbo].[GG_EquipmentSubType]  WITH CHECK ADD  CONSTRAINT [FK_GG_EquipmentSubType_GG_EquipmentType] FOREIGN KEY([EquipmentTypeId])
REFERENCES [dbo].[GG_EquipmentType] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_EquipmentSubType_GG_EquipmentType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_EquipmentSubType]'))
ALTER TABLE [dbo].[GG_EquipmentSubType] CHECK CONSTRAINT [FK_GG_EquipmentSubType_GG_EquipmentType]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_TagSet_GG_EquipmentSubType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_TagSet]'))
ALTER TABLE [dbo].[GG_TagSet]  WITH CHECK ADD  CONSTRAINT [FK_GG_TagSet_GG_EquipmentSubType] FOREIGN KEY([EquipmentSubTypeId])
REFERENCES [dbo].[GG_EquipmentSubType] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_TagSet_GG_EquipmentSubType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_TagSet]'))
ALTER TABLE [dbo].[GG_TagSet] CHECK CONSTRAINT [FK_GG_TagSet_GG_EquipmentSubType]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_TagSet_GG_EquipmentType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_TagSet]'))
ALTER TABLE [dbo].[GG_TagSet]  WITH CHECK ADD  CONSTRAINT [FK_GG_TagSet_GG_EquipmentType] FOREIGN KEY([EquipmentTypeId])
REFERENCES [dbo].[GG_EquipmentType] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_TagSet_GG_EquipmentType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_TagSet]'))
ALTER TABLE [dbo].[GG_TagSet] CHECK CONSTRAINT [FK_GG_TagSet_GG_EquipmentType]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_TagSet_GG_UserEquipment]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_TagSet]'))
ALTER TABLE [dbo].[GG_TagSet]  WITH CHECK ADD  CONSTRAINT [FK_GG_TagSet_GG_UserEquipment] FOREIGN KEY([UserEquipmentId])
REFERENCES [dbo].[GG_UserEquipment] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_TagSet_GG_UserEquipment]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_TagSet]'))
ALTER TABLE [dbo].[GG_TagSet] CHECK CONSTRAINT [FK_GG_TagSet_GG_UserEquipment]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_UserEquipment_GG_EquipmentSubType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_UserEquipment]'))
ALTER TABLE [dbo].[GG_UserEquipment]  WITH CHECK ADD  CONSTRAINT [FK_GG_UserEquipment_GG_EquipmentSubType] FOREIGN KEY([EquipmentSubTypeId])
REFERENCES [dbo].[GG_EquipmentSubType] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_GG_UserEquipment_GG_EquipmentSubType]') AND parent_object_id = OBJECT_ID(N'[dbo].[GG_UserEquipment]'))
ALTER TABLE [dbo].[GG_UserEquipment] CHECK CONSTRAINT [FK_GG_UserEquipment_GG_EquipmentSubType]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Hole_Tee]') AND parent_object_id = OBJECT_ID(N'[dbo].[Hole]'))
ALTER TABLE [dbo].[Hole]  WITH CHECK ADD  CONSTRAINT [FK_Hole_Tee] FOREIGN KEY([TeeId])
REFERENCES [dbo].[Tee] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Hole_Tee]') AND parent_object_id = OBJECT_ID(N'[dbo].[Hole]'))
ALTER TABLE [dbo].[Hole] CHECK CONSTRAINT [FK_Hole_Tee]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_Golfer]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round]  WITH CHECK ADD  CONSTRAINT [FK_Round_Golfer] FOREIGN KEY([GolferId])
REFERENCES [dbo].[Golfer] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_Golfer]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round] CHECK CONSTRAINT [FK_Round_Golfer]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_RoundType]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round]  WITH CHECK ADD  CONSTRAINT [FK_Round_RoundType] FOREIGN KEY([RoundTypeId])
REFERENCES [dbo].[RoundType] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_RoundType]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round] CHECK CONSTRAINT [FK_Round_RoundType]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_Tee]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round]  WITH CHECK ADD  CONSTRAINT [FK_Round_Tee] FOREIGN KEY([TeeId])
REFERENCES [dbo].[Tee] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_Tee]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round] CHECK CONSTRAINT [FK_Round_Tee]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_Temperature]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round]  WITH CHECK ADD  CONSTRAINT [FK_Round_Temperature] FOREIGN KEY([TemperatureId])
REFERENCES [dbo].[Temperature] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_Temperature]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round] CHECK CONSTRAINT [FK_Round_Temperature]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_WeatherCondition]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round]  WITH CHECK ADD  CONSTRAINT [FK_Round_WeatherCondition] FOREIGN KEY([WeatherConditionId])
REFERENCES [dbo].[WeatherCondition] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_WeatherCondition]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round] CHECK CONSTRAINT [FK_Round_WeatherCondition]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_WindCondition]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round]  WITH CHECK ADD  CONSTRAINT [FK_Round_WindCondition] FOREIGN KEY([WindConditionId])
REFERENCES [dbo].[WindCondition] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Round_WindCondition]') AND parent_object_id = OBJECT_ID(N'[dbo].[Round]'))
ALTER TABLE [dbo].[Round] CHECK CONSTRAINT [FK_Round_WindCondition]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_RoundDetail_Hole]') AND parent_object_id = OBJECT_ID(N'[dbo].[RoundDetail]'))
ALTER TABLE [dbo].[RoundDetail]  WITH CHECK ADD  CONSTRAINT [FK_RoundDetail_Hole] FOREIGN KEY([HoleId])
REFERENCES [dbo].[Hole] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_RoundDetail_Hole]') AND parent_object_id = OBJECT_ID(N'[dbo].[RoundDetail]'))
ALTER TABLE [dbo].[RoundDetail] CHECK CONSTRAINT [FK_RoundDetail_Hole]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_RoundDetail_Round]') AND parent_object_id = OBJECT_ID(N'[dbo].[RoundDetail]'))
ALTER TABLE [dbo].[RoundDetail]  WITH CHECK ADD  CONSTRAINT [FK_RoundDetail_Round] FOREIGN KEY([RoundId])
REFERENCES [dbo].[Round] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_RoundDetail_Round]') AND parent_object_id = OBJECT_ID(N'[dbo].[RoundDetail]'))
ALTER TABLE [dbo].[RoundDetail] CHECK CONSTRAINT [FK_RoundDetail_Round]
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Tee_Course]') AND parent_object_id = OBJECT_ID(N'[dbo].[Tee]'))
ALTER TABLE [dbo].[Tee]  WITH CHECK ADD  CONSTRAINT [FK_Tee_Course] FOREIGN KEY([CourseId])
REFERENCES [dbo].[Course] ([Id])
GO

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Tee_Course]') AND parent_object_id = OBJECT_ID(N'[dbo].[Tee]'))
ALTER TABLE [dbo].[Tee] CHECK CONSTRAINT [FK_Tee_Course]
GO


