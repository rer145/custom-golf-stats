

/****** Object:  StoredProcedure [dbo].[Chart_DrivingAccuracy]    Script Date: 11/21/2019 5:00:54 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Chart_DrivingAccuracy]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Chart_DrivingAccuracy] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/17/2015
-- Description:	Driving accuracy stats for a year
-- Change Log:
--		09/18/2015	RR	Filtered out inactive rounds
--		09/18/2015	RR	Formatted column names and data for charting
-- =============================================
ALTER PROCEDURE [dbo].[Chart_DrivingAccuracy]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    declare @driving_holes int
	declare @fairways int
	declare @tee_right int
	declare @tee_left int
	declare @tee_short int
	declare @tee_long int
	
	select 
		@driving_holes = sum(case h.par when 3 then 0 else 1 end),
		@fairways = sum(case rd.fairwayhit when 1 then 1 else 0 end),
		@tee_right = sum(case rd.teeshotright when 1 then 1 else 0 end),
		@tee_left = sum(case rd.teeshotleft when 1 then 1 else 0 end),
		@tee_short = sum(case rd.TeeShotShort when 1 then 1 else 0 end),
		@tee_long = sum(case rd.TeeShotLong when 1 then 1 else 0 end)
	from dbo.[rounddetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[hole] h on h.id = rd.holeid
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1

	
	select 
		round((@fairways / convert(float, @driving_holes)) * 100, 2) as [Fairway],
		round((@tee_right / convert(float, @driving_holes)) * 100, 2) as [Right],
		round((@tee_left / convert(float, @driving_holes)) * 100, 2) as [Left],
		round((@tee_short / convert(float, @driving_holes)) * 100, 2) as [Short],
		round((@tee_long / convert(float, @driving_holes)) * 100, 2) as [Long]
		

END
GO

/****** Object:  StoredProcedure [dbo].[Chart_GreensInRegulation]    Script Date: 11/21/2019 5:00:54 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Chart_GreensInRegulation]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Chart_GreensInRegulation] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 11/09/2015
-- Description:	Greens hit stats for a year
-- Change Log:
-- =============================================
ALTER PROCEDURE [dbo].[Chart_GreensInRegulation]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    declare @holes int
	declare @greens_hit int
	
	select 
		@holes = count(rd.Id),
		@greens_hit = count(case when rd.GreenInRegulation = 1 then rd.Id end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[Hole] h on h.Id = rd.HoleId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1

	select 
		round((@greens_hit / convert(float, @holes)) * 100, 2) as [Hit],
		round(((@holes - @greens_hit) / convert(float, @holes)) * 100, 2) as [Missed]
		

END
GO

/****** Object:  StoredProcedure [dbo].[Chart_HandicapHistory]    Script Date: 11/21/2019 5:00:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Chart_HandicapHistory]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Chart_HandicapHistory] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 11/09/2015
-- Description:	Handicap history for the year (chart)
-- Change Log:
--		11/09/2015	RR	Added assumptions in for posting season in Western PA
-- =============================================
ALTER PROCEDURE [dbo].[Chart_HandicapHistory]
	@year int
AS
BEGIN
	SET NOCOUNT ON;
    
    select
		replace(convert(varchar, hh.HandicapDate, 111), '/', '-') as [Date],
		hh.HandicapIndex
    from dbo.HandicapHistory hh
    where datepart(yyyy, hh.HandicapDate) = @year and datepart(mm, hh.HandicapDate) between 3 and 12
    order by replace(convert(varchar, hh.HandicapDate, 111), '/', '-')
END
GO

/****** Object:  StoredProcedure [dbo].[Chart_PuttingPerHole]    Script Date: 11/21/2019 5:00:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Chart_PuttingPerHole]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Chart_PuttingPerHole] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/18/2015
-- Description:	Putting averages per hole (chart data)
-- Change Log:
-- =============================================
ALTER PROCEDURE [dbo].[Chart_PuttingPerHole]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    select
		round((sum(case when rd.GreenInRegulation = 1 then rd.Putts else 0 end) / convert(float, count(case when rd.GreenInRegulation = 1 then rd.Id end))), 2) as GIR,
		round((sum(rd.Putts) / convert(float, count(rd.Id))), 2) as Total
	from dbo.[Round] r
	inner join dbo.RoundDetail rd on rd.RoundId = r.Id
	inner join dbo.Hole h on h.Id = rd.HoleId
	inner join dbo.Tee t on t.id = h.TeeId
	inner join dbo.Course c on c.Id = t.CourseId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
END
GO

/****** Object:  StoredProcedure [dbo].[Chart_Recovery]    Script Date: 11/21/2019 5:00:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Chart_Recovery]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Chart_Recovery] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/18/2015
-- Description:	Recovery performance per hole (chart data)
-- Change Log:
-- =============================================
ALTER PROCEDURE [dbo].[Chart_Recovery]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    select
		round((count(case when rd.SandShots > 0 and rd.Score <= h.Par then rd.Id end) / convert(float, count(case when rd.SandShots > 0 then rd.Id end))) * 100, 2) as [Sand Saves],
		round((count(case when rd.GreenInRegulation = 0 and rd.Score <= h.Par then rd.Id end) / convert(float, count(case when rd.GreenInRegulation = 0 then rd.Id end))) * 100, 2) as Scrambling
	from dbo.[Round] r
	inner join dbo.RoundDetail rd on rd.RoundId = r.Id
	inner join dbo.Hole h on h.Id = rd.HoleId
	inner join dbo.Tee t on t.id = h.TeeId
	inner join dbo.Course c on c.Id = t.CourseId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
END
GO

/****** Object:  StoredProcedure [dbo].[Chart_ScoresByType]    Script Date: 11/21/2019 5:00:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Chart_ScoresByType]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Chart_ScoresByType] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/18/2015
-- Description:	Count of score types (chart data)
-- Change Log:
-- =============================================
ALTER PROCEDURE [dbo].[Chart_ScoresByType]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    select
		round((count(case when h.Par - rd.Score >= 2 then rd.Id end) / convert(float, count(rd.Id))) * 100, 2) as Eagles,
		round((count(case when h.Par - rd.Score = 1 then rd.Id end) / convert(float, count(rd.Id))) * 100, 2) as Birdies,
		round((count(case when h.Par - rd.Score = 0 then rd.Id end) / convert(float, count(rd.Id))) * 100, 2) as Pars,
		round((count(case when h.Par - rd.Score = -1 then rd.Id end) / convert(float, count(rd.Id))) * 100, 2) as Bogies,
		round((count(case when h.Par - rd.Score <= -2 then rd.Id end) / convert(float, count(rd.Id))) * 100, 2) as [Dbl Bogies]
	from dbo.[Round] r
	inner join dbo.RoundDetail rd on rd.RoundId = r.Id
	inner join dbo.Hole h on h.Id = rd.HoleId
	inner join dbo.Tee t on t.id = h.TeeId
	inner join dbo.Course c on c.Id = t.CourseId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
END
GO

/****** Object:  StoredProcedure [dbo].[CustomMetricData_Create]    Script Date: 11/21/2019 5:00:57 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CustomMetricData_Create]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[CustomMetricData_Create] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/10/2015
-- Description: Adds a custom metric data
-- =============================================
ALTER PROCEDURE [dbo].[CustomMetricData_Create]
	@CustomMetricId int,
	@KeyId int,
	@Value varchar(max) = '',
	@IsActive bit = 1
AS
BEGIN
	SET NOCOUNT ON;

    insert into CustomMetricData (CustomMetricId, KeyId, Value, IsActive)
    values (@CustomMetricId, @KeyId, @Value, @IsActive)
    
    select scope_identity()
END

GO

/****** Object:  StoredProcedure [dbo].[Export_FullGolferStats]    Script Date: 11/21/2019 5:00:57 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Export_FullGolferStats]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Export_FullGolferStats] AS' 
END
GO

-- =============================================
-- Author:		RR
-- Create date: 01/30/2018
-- Description:	Full data export of a golfer's stats
-- Change Log:
--		02/07/2018	RR	Added fields to categorize tee shots
-- =============================================
ALTER PROCEDURE [dbo].[Export_FullGolferStats]
	@GolferId int = 1
AS
BEGIN
	SET NOCOUNT ON;


	select
		g.Id as GolferId,
		g.FirstName,
		g.LastName,
		r.Id as RoundId,
		rt.Name as RoundType,
		c.Id as CourseId,
		c.Name as CourseName,
		t.Id as TeeId,
		t.Name as TeeName,
		t.Par as CoursePar,
		t.CourseRating,
		t.SlopeRating,
		t.FrontNinePar,
		t.FrontNineCourseRating,
		t.FrontNineSlopeRating,
		t.BackNinePar,
		t.BackNineCourseRating,
		t.BackNineSlopeRating,
		r.HolesPlayed,
		r.Score,
		case 
			when h.Par - rd.Score >= 3 then 'Double Eagle or Better'
			when h.Par - rd.Score = 2 then 'Eagle' 
			when h.Par - rd.Score = 1 then 'Birdie'
			when h.Par - rd.Score = 0 then 'Par'
			when rd.Score - h.Par = 1 then 'Bogey'
			when rd.Score - h.Par = 2 then 'Double Bogey'
			when rd.Score - h.Par >= 3 then 'Triple Bogey or Worse'
		end as RelationToParName,
		r.Duration,
		r.Distance,
		r.PlayedOn,
		datepart(yyyy, r.PlayedOn) as PlayedOnYear,
		datepart(mm, r.PlayedOn) as PlayedOnMonth,
		datename(mm, r.PlayedOn) as PlayedOnMonthName,
		datepart(dd, r.PlayedOn) as PlayedOnDay,
		datepart(dw, r.PlayedOn) as PlayedOnDayOfWeek,
		datename(dw, r.PlayedOn) as PlayedOnDayOfWeekName,
		datepart(hh, r.PlayedOn) as PlayedOnHour,
		r.Hole19Url,
		r.GameGolfUrl,
		h.Id as HoleId,
		h.Number as HoleNumber,
		h.Handicap as HoleHandicap,
		h.Par as HolePar,
		h.Yardage as HoleYardage,
		rd.Score,
		rd.Putts,
		rd.FairwayHit,
		rd.TeeShotLeft,
		rd.TeeShotRight,
		rd.TeeShotLong,
		rd.TeeShotShort,
		case
			when rd.FairwayHit = 1 then 'Fairway'
			when rd.TeeShotLeft = 1 then 'Left'
			when rd.TeeShotRight = 1 then 'Right'
			when rd.TeeShotShort = 1 then 'Short'
			when rd.TeeShotLong = 1 then 'Long'
			else ''
		end as TeeShotDirection,
		rd.GreenInRegulation,
		case rd.GreenInRegulation
			when 1 then 'Hit'
			else 'Missed'
		end as GreenInRegulationName,
		rd.PenaltyShots,
		rd.SandShots
	from Golfer g
	inner join [Round] r on r.GolferId = g.Id
	inner join RoundDetail rd on rd.RoundId = r.Id
	inner join Hole h on h.Id = rd.HoleId
	inner join Tee t on t.Id = h.TeeId
	inner join Course c on c.Id = t.CourseId
	inner join RoundType rt on rt.Id = r.RoundTypeId
	where 
		g.Id = @GolferId
		and g.IsActive = 1
		and r.IsActive = 1
		and rd.IsActive = 1
		and h.IsActive = 1
		and t.IsActive = 1
		and c.IsActive = 1
		and rt.IsActive = 1
	order by r.PlayedOn, r.Id, rd.Id

END
GO

/****** Object:  StoredProcedure [dbo].[Export_GolferRoundSummary]    Script Date: 11/21/2019 5:00:58 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Export_GolferRoundSummary]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Export_GolferRoundSummary] AS' 
END
GO

-- =============================================
-- Author:		RR
-- Create date: 02/07/2018
-- Description:	Export of all round summaries played by a golfer
-- =============================================
ALTER PROCEDURE [dbo].[Export_GolferRoundSummary]
	@GolferId int
AS
BEGIN
	SET NOCOUNT ON;

    select
		g.Id as GolferId,
		g.FirstName,
		g.LastName,
		r.Id as RoundId,
		rt.Name as RoundType,
		c.Id as CourseId,
		c.Name as CourseName,
		t.Id as TeeId,
		t.Name as TeeName,
		t.Par as CoursePar,
		t.CourseRating,
		t.SlopeRating,
		t.FrontNinePar,
		t.FrontNineCourseRating,
		t.FrontNineSlopeRating,
		t.BackNinePar,
		t.BackNineCourseRating,
		t.BackNineSlopeRating,
		r.HolesPlayed,
		r.Score,
		r.Duration,
		r.Distance,
		r.PlayedOn,
		datepart(yyyy, r.PlayedOn) as PlayedOnYear,
		datepart(mm, r.PlayedOn) as PlayedOnMonth,
		datename(mm, r.PlayedOn) as PlayedOnMonthName,
		datepart(dd, r.PlayedOn) as PlayedOnDay,
		datepart(dw, r.PlayedOn) as PlayedOnDayOfWeek,
		datename(dw, r.PlayedOn) as PlayedOnDayOfWeekName,
		datepart(hh, r.PlayedOn) as PlayedOnHour,
		r.Hole19Url,
		r.GameGolfUrl,
		count(distinct h.Id) as TotalHolesPlayed,
		sum(h.Par) as TotalPar,
		sum(h.Yardage) as TotalYards,
		sum(rd.Score) as TotalScore,
		sum(rd.Putts) as TotalPutts,
		sum(case when h.Par <> 3 then 1 else 0 end) as TotalDrivingHoles,
		sum(case when rd.FairwayHit = 1 then 1 else 0 end) as FairwaysHit,
		sum(case when rd.TeeShotLeft = 1 then 1 else 0 end) as TeeShotsLeft,
		sum(case when rd.TeeShotRight = 1 then 1 else 0 end) as TeeShotsRight,
		sum(case when rd.TeeShotLong = 1 then 1 else 0 end) as TeeShotsLong,
		sum(case when rd.TeeShotShort = 1 then 1 else 0 end) as TeeShotsShort,
		sum(case when rd.GreenInRegulation = 1 then 1 else 0 end) as GreensInRegulation,
		sum(rd.PenaltyShots) as TotalPenaltyShots,
		sum(rd.SandShots) as TotalSandShots,
		count(case when h.Par - rd.Score >= 3 then h.Id end) as TotalDoubleEagleMore,
		count(case when h.Par - rd.Score = 2 then h.Id end) as TotalEagles,
		count(case when h.Par - rd.Score = 1 then h.Id end) as TotalBirdies,
		count(case when h.Par - rd.Score = 0 then h.Id end) as TotalPars,
		count(case when rd.Score - h.Par = 1 then h.Id end) as TotalBogies,
		count(case when rd.Score - h.Par = 2 then h.Id end) as TotalDoubleBogies,
		count(case when rd.Score - h.Par >= 3 then h.Id end) as TotalTripleBogeyMore,
		count(case when rd.Putts = 0 then h.Id end) as TotalHoleOuts,
		count(case when rd.Putts = 1 then h.Id end) as TotalOnePutts,
		count(case when rd.Putts = 2 then h.Id end) as TotalTwoPutts,
		count(case when rd.Putts = 3 then h.Id end) as TotalThreePutts,
		count(case when rd.Putts >= 4 then h.Id end) as TotalFourPuttsMore
	from Golfer g
	inner join [Round] r on r.GolferId = g.Id
	inner join RoundDetail rd on rd.RoundId = r.Id
	inner join Hole h on h.Id = rd.HoleId
	inner join Tee t on t.Id = h.TeeId
	inner join Course c on c.Id = t.CourseId
	inner join RoundType rt on rt.Id = r.RoundTypeId
	where 
		g.Id = @GolferId
		and g.IsActive = 1
		and r.IsActive = 1
		and rd.IsActive = 1
		and h.IsActive = 1
		and t.IsActive = 1
		and c.IsActive = 1
		and rt.IsActive = 1
	group by 
		g.Id,g.FirstName,g.LastName,r.Id,rt.Name,c.Id,c.Name,t.Id,t.Name,t.Par,t.CourseRating,
		t.SlopeRating,t.FrontNinePar,t.FrontNineCourseRating,t.FrontNineSlopeRating,t.BackNinePar,
		t.BackNineCourseRating,t.BackNineSlopeRating,r.HolesPlayed,r.Score,r.Duration,
		r.Distance,r.PlayedOn,datepart(yyyy, r.PlayedOn),datepart(mm, r.PlayedOn),
		datename(mm, r.PlayedOn),datepart(dd, r.PlayedOn),datepart(dw, r.PlayedOn),
		datename(dw, r.PlayedOn),datepart(hh, r.PlayedOn),r.Hole19Url,r.GameGolfUrl
	order by r.PlayedOn, r.Id
END
GO

/****** Object:  StoredProcedure [dbo].[GameGolf_ClubStatsByRound]    Script Date: 11/21/2019 5:00:58 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_ClubStatsByRound]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[GameGolf_ClubStatsByRound] AS' 
END
GO

-- =============================================
-- Author:		RR
-- Create date: 04/23/2018
-- Description:	Game Golf stats by club for a round
-- =============================================
ALTER PROCEDURE [dbo].[GameGolf_ClubStatsByRound]
	@RoundId int
AS
BEGIN
	SET NOCOUNT ON;

    select 
		isnull(cman.Name, re.ManufacturerId) as Manufacturer, 
		isnull(cmod.Name, re.ModelRangeId) as Model, 
		isnull(et.Name, re.EquipmentTypeId) as ClubType, 
		isnull(est.Name, re.EquipmentSubTypeId) as Club, 
		count(re.Id) as Shots, 
		round(sum(re.CalculatedDistanceMeters)*1.0936, 2) as TotalDistance, 
		round(sum(re.CalculatedDistanceMeters)*1.0936 / convert(float, count(re.Id)), 2) as AvgDistance
	from GameGolf_RoundExport re 
	left outer join GG_ClubManufacturer cman on cman.GameGolfId = re.ManufacturerId
	left outer join GG_ClubModel cmod on cmod.GameGolfId = re.ModelRangeId
	left outer join GG_EquipmentType et on et.GameGolfId = re.EquipmentTypeId
	left outer join GG_EquipmentSubType est on est.GameGolfId = re.EquipmentSubTypeId
	where re.RoundId = @RoundId
	group by 
		isnull(cman.Name, re.ManufacturerId), 
		isnull(cmod.Name, re.ModelRangeId), 
		isnull(et.Name, re.EquipmentTypeId), 
		isnull(est.Name, re.EquipmentSubTypeId)
	order by AvgDistance desc

END
GO

/****** Object:  StoredProcedure [dbo].[GameGolf_SaveClubManufacturer]    Script Date: 11/21/2019 5:00:58 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_SaveClubManufacturer]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[GameGolf_SaveClubManufacturer] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/29/2015
-- Description:	Saves a GameGolf club manufacturer
-- =============================================
ALTER PROCEDURE [dbo].[GameGolf_SaveClubManufacturer]
	@GameGolfId int,
	@Name varchar(100),
	@CreatedOn datetime,
	@ModifiedOn datetime
AS
BEGIN
	SET NOCOUNT ON;

	if exists (select * from GG_ClubManufacturer where GameGolfId = @GameGolfId) begin
		update GG_ClubManufacturer set
			Name = @Name,
			CreatedOn = @CreatedOn,
			ModifiedOn = @ModifiedOn
		where GameGolfId = @GameGolfId
	end else begin
		insert into GG_ClubManufacturer (Name, GameGolfId, CreatedOn, ModifiedOn)
		values (@Name, @GameGolfId, @CreatedOn, @ModifiedOn)
	end
END

GO

/****** Object:  StoredProcedure [dbo].[GameGolf_SaveClubModel]    Script Date: 11/21/2019 5:00:59 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_SaveClubModel]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[GameGolf_SaveClubModel] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/29/2015
-- Description:	Saves a GameGolf club model
-- =============================================
ALTER PROCEDURE [dbo].[GameGolf_SaveClubModel]
	@GameGolfId int,
	@Name varchar(100),
	@GameGolfManufacturerId int,
	@CreatedOn datetime,
	@ModifiedOn datetime
AS
BEGIN
	SET NOCOUNT ON;

	declare @ManufacturerId int
	select @ManufacturerId = Id from GG_ClubManufacturer where GameGolfId = @GameGolfManufacturerId
	
	if @ManufacturerId is null begin
		set @ManufacturerId = 0
	end

	if exists (select * from GG_ClubModel where GameGolfId = @GameGolfId) begin
		update GG_ClubModel set
			ManufacturerId = @ManufacturerId,
			Name = @Name,
			CreatedOn = @CreatedOn,
			ModifiedOn = @ModifiedOn
		where GameGolfId = @GameGolfId
	end else begin
		insert into GG_ClubModel (Name, ManufacturerId, GameGolfId, CreatedOn, ModifiedOn)
		values (@Name, @ManufacturerId, @GameGolfId, @CreatedOn, @ModifiedOn)
	end
END

GO

/****** Object:  StoredProcedure [dbo].[GameGolf_SaveEquipment]    Script Date: 11/21/2019 5:00:59 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_SaveEquipment]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[GameGolf_SaveEquipment] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/29/2015
-- Description:	Saves a GameGolf equipment
-- =============================================
ALTER PROCEDURE [dbo].[GameGolf_SaveEquipment]
	@GameGolfId int,
	@GameGolfEquipmentSubTypeId int,
	@GameGolfModelId int,
	@Name varchar(100),
	@GameGolfMD5 varchar(50),
	@CreatedOn datetime,
	@ModifiedOn datetime
AS
BEGIN
	SET NOCOUNT ON;

	declare @EquipmentSubTypeId int
	declare @ModelId int
	select @EquipmentSubTypeId = Id from GG_EquipmentSubType where GameGolfId = @GameGolfEquipmentSubTypeId
	select @ModelId = Id from GG_ClubModel where GameGolfId = @GameGolfModelId
	
	if @EquipmentSubTypeId is null begin
		set @EquipmentSubTypeId = 0
	end
	
	if @ModelId is null begin
		set @ModelId = 0
	end

	if exists (select * from GG_Equipment where GameGolfId = @GameGolfId) begin
		update GG_Equipment set
			Name = @Name,
			EquipmentSubTypeId = @EquipmentSubTypeId,
			ModelId = @ModelId,
			GameGolfMD5 = @GameGolfMD5,
			CreatedOn = @CreatedOn,
			ModifiedOn = @ModifiedOn
		where GameGolfId = @GameGolfId
	end else begin
		insert into GG_Equipment (Name, GameGolfId, EquipmentSubTypeId, ModelId, GameGolfMD5, CreatedOn, ModifiedOn)
		values (@Name, @GameGolfId, @EquipmentSubTypeId, @ModelId, @GameGolfMD5, @CreatedOn, @ModifiedOn)
	end
END

GO

/****** Object:  StoredProcedure [dbo].[GameGolf_SaveEquipmentSubType]    Script Date: 11/21/2019 5:00:59 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_SaveEquipmentSubType]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[GameGolf_SaveEquipmentSubType] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/29/2015
-- Description:	Saves a GameGolf equipment subtype
-- =============================================
ALTER PROCEDURE [dbo].[GameGolf_SaveEquipmentSubType]
	@GameGolfId int,
	@GameGolfEquipmentTypeId int,
	@Name varchar(100),
	@DisplayName varchar(100),
	@Rank int,
	@IsDefault bit,
	@CreatedOn datetime,
	@ModifiedOn datetime
AS
BEGIN
	SET NOCOUNT ON;

	declare @EquipmentTypeId int
	select @EquipmentTypeId = Id from GG_EquipmentType where GameGolfId = @GameGolfEquipmentTypeId
	
	if @EquipmentTypeId is null begin
		set @EquipmentTypeId = 0
	end

	if exists (select * from GG_EquipmentSubType where GameGolfId = @GameGolfId) begin
		update GG_EquipmentSubType set
			DisplayName = @DisplayName,
			Name = @Name,
			EquipmentTypeId = @EquipmentTypeId,
			[Rank] = @Rank,
			IsDefault = @IsDefault,
			CreatedOn = @CreatedOn,
			ModifiedOn = @ModifiedOn
		where GameGolfId = @GameGolfId
	end else begin
		insert into GG_EquipmentSubType (Name, DisplayName, GameGolfId, EquipmentTypeId, [Rank], IsDefault, CreatedOn, ModifiedOn)
		values (@Name, @DisplayName, @GameGolfId, @EquipmentTypeId, @Rank, @IsDefault, @CreatedOn, @ModifiedOn)
	end
END

GO

/****** Object:  StoredProcedure [dbo].[GameGolf_SaveEquipmentType]    Script Date: 11/21/2019 5:01:00 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_SaveEquipmentType]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[GameGolf_SaveEquipmentType] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/29/2015
-- Description:	Saves a GameGolf equipment type
-- =============================================
ALTER PROCEDURE [dbo].[GameGolf_SaveEquipmentType]
	@GameGolfId int,
	@Name varchar(100),
	@DisplayName varchar(100),
	@IsDefault bit,
	@CreatedOn datetime,
	@ModifiedOn datetime
AS
BEGIN
	SET NOCOUNT ON;

	if exists (select * from GG_EquipmentType where GameGolfId = @GameGolfId) begin
		update GG_EquipmentType set
			DisplayName = @DisplayName,
			Name = @Name,
			CreatedOn = @CreatedOn,
			ModifiedOn = @ModifiedOn
		where GameGolfId = @GameGolfId
	end else begin
		insert into GG_EquipmentType (Name, DisplayName, GameGolfId, CreatedOn, ModifiedOn)
		values (@Name, @DisplayName, @GameGolfId, @CreatedOn, @ModifiedOn)
	end
END

GO

/****** Object:  StoredProcedure [dbo].[GameGolf_SaveTagSet]    Script Date: 11/21/2019 5:01:00 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_SaveTagSet]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[GameGolf_SaveTagSet] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/30/2015
-- Description:	Saves a GameGolf tag set
-- =============================================
ALTER PROCEDURE [dbo].[GameGolf_SaveTagSet]
	@GameGolfUserId int,
	@GameGolfEquipmentTypeId int,
	@GameGolfEquipmentSubTypeId int,
	@GameGolfUserEquipmentId int,
	@CreatedOn datetime,
	@ModifiedOn datetime
AS
BEGIN
	SET NOCOUNT ON;

	declare @GolferId int
	declare @EquipmentTypeId int
	declare @EquipmentSubTypeId int
	declare @UserEquipmentId int
	
	select @GolferId = Id from Golfer where GameGolfUserId = @GameGolfUserId
	select @EquipmentTypeId = Id from GG_EquipmentType where GameGolfId = @GameGolfEquipmentTypeId
	select @EquipmentSubTypeId = Id from GG_EquipmentSubType where GameGolfId = @GameGolfEquipmentSubTypeId
	select @UserEquipmentId = Id from GG_UserEquipment where GameGolfId = @GameGolfUserEquipmentId
	
	if @GolferId is null begin
		set @GolferId = 1
	end
	
	if @EquipmentTypeId is null begin
		set @EquipmentTypeId = 0
	end
	
	if @EquipmentSubTypeId is null begin
		set @EquipmentSubTypeId = 0
	end
	
	if @UserEquipmentId is null begin
		set @UserEquipmentId = 0
	end

	if exists (select * from GG_TagSet where GolferId = @GolferId and EquipmentTypeId = @EquipmentTypeId and EquipmentSubTypeId = @EquipmentSubTypeId) begin
		update GG_TagSet set
			UserEquipmentId = @UserEquipmentId,
			CreatedOn = @CreatedOn,
			ModifiedOn = @ModifiedOn
		where GolferId = @GolferId and EquipmentTypeId = @EquipmentTypeId and EquipmentSubTypeId = @EquipmentSubTypeId
	end else begin
		insert into GG_TagSet (GolferId, EquipmentTypeId, EquipmentSubTypeId, UserEquipmentId, CreatedOn, ModifiedOn)
		values (@GolferId, @EquipmentTypeId, @EquipmentSubTypeId, @UserEquipmentId, @CreatedOn, @ModifiedOn)
	end
END

GO

/****** Object:  StoredProcedure [dbo].[GameGolf_SaveUserEquipment]    Script Date: 11/21/2019 5:01:01 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GameGolf_SaveUserEquipment]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[GameGolf_SaveUserEquipment] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/29/2015
-- Description:	Saves a GameGolf user equipment
-- Change Log:
--		RR	10/30/2015	Added EquipmentId field
-- =============================================
ALTER PROCEDURE [dbo].[GameGolf_SaveUserEquipment]
	@GameGolfId int,
	@GameGolfEquipmentId int,
	@GameGolfEquipmentSubTypeId int,
	@GameGolfUserId int,
	@GameGolfOriginationSourceId int,
	@CreatedOn datetime,
	@ModifiedOn datetime
AS
BEGIN
	SET NOCOUNT ON;

	declare @EquipmentId int
	declare @EquipmentSubTypeId int
	declare @GolferId int
	select @EquipmentId = Id from GG_Equipment where GameGolfId = @GameGolfEquipmentId
	select @EquipmentSubTypeId = Id from GG_EquipmentSubType where GameGolfId = @GameGolfEquipmentSubTypeId
	select @GolferId = Id from Golfer where GameGolfUserId = @GameGolfUserId
	
	if @EquipmentId is null begin
		set @EquipmentId = 0
	end
	
	if @EquipmentSubTypeId is null begin
		set @EquipmentSubTypeId = 0
	end
	
	if @GolferId is null begin
		set @GolferId = 1
	end

	if exists (select * from GG_UserEquipment where GameGolfId = @GameGolfId) begin
		update GG_UserEquipment set
			GolferId = @GolferId,
			EquipmentId = @EquipmentId,
			EquipmentSubTypeId = @EquipmentSubTypeId,
			OriginationSourceId = @GameGolfOriginationSourceId,
			CreatedOn = @CreatedOn,
			ModifiedOn = @ModifiedOn
		where GameGolfId = @GameGolfId
	end else begin
		insert into GG_UserEquipment (GolferId, GameGolfId, EquipmentId, EquipmentSubTypeId, OriginationSourceId, CreatedOn, ModifiedOn)
		values (@GolferId, @GameGolfId, @EquipmentId, @EquipmentSubTypeId, @GameGolfOriginationSourceId, @CreatedOn, @ModifiedOn)
	end
END

GO

/****** Object:  StoredProcedure [dbo].[Round_AddCustomMetrics]    Script Date: 11/21/2019 5:01:01 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_AddCustomMetrics]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_AddCustomMetrics] AS' 
END
GO

-- =============================================
-- Author:		RR
-- Create date: 04/25/2016
-- Description:	Adds custom metrics to a round (hardcoded for now)
-- =============================================
ALTER PROCEDURE [dbo].[Round_AddCustomMetrics]
	@RoundId int,
	@Partners varchar(300),
	@Notes varchar(1000),
	@WalkRode varchar(1)
AS
BEGIN
	SET NOCOUNT ON;

	insert into dbo.CustomMetricData (CustomMetricId, KeyId, Value, IsActive)
	select 5, @RoundId, @Partners, 1 union
	select 6, @RoundId, @Notes, 1 union
	select 4, @RoundId, @WalkRode, 1

END
GO

/****** Object:  StoredProcedure [dbo].[Round_Create]    Script Date: 11/21/2019 5:01:01 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_Create]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_Create] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 08/28/2015
-- Description:	Adds a round
-- Change Log:
--		10/22/2015	RR	Added HolesPlayed field
--		11/02/2015	RR	Added Hole19Url field
--		04/25/2016	RR	Added GameGolfUrl field
-- =============================================
ALTER PROCEDURE [dbo].[Round_Create]
	@GolferId int,
	@TeeId int,
	@RoundTypeId int = null,
	@WeatherConditionId int = null,
	@WindConditionId int = null,
	@TemperatureId int = null,
	@Score int,
	@Duration varchar(50) = null,
	@Distance decimal(18,4) = null,
	@PlayedOn smalldatetime,
	@Notes varchar(2000) = null,
	@HolesPlayed int = null,
	@Hole19Url varchar(100) = null,
	@GameGolfUrl varchar(100) = null,
	@IsActive bit = 1
AS
BEGIN
	SET NOCOUNT ON;

    insert into [Round] (GolferId, TeeId, RoundTypeId, WeatherConditionId, WindConditionId, TemperatureId, HolesPlayed, Score, Duration, Distance, PlayedOn, Notes, Hole19Url, GameGolfUrl, IsActive)
    values (@GolferId, @TeeId, @RoundTypeId, @WeatherConditionId, @WindConditionId, @TemperatureId, @HolesPlayed, @Score, @Duration, @Distance, @PlayedOn, @Notes, @Hole19Url, @GameGolfUrl, @IsActive)
    
    select scope_identity()
END

GO

/****** Object:  StoredProcedure [dbo].[Round_Delete]    Script Date: 11/21/2019 5:01:02 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_Delete]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_Delete] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/10/2015
-- Description:	Delete a round
-- Change Log:
--		09/18/2015	RR	Added option for soft delete
-- =============================================
ALTER PROCEDURE [dbo].[Round_Delete]
	@Id int,
	@IsSoftDelete bit = 1
AS
BEGIN
	
	if @IsSoftDelete = 1 begin
		update RoundDetail set IsActive = 0 where RoundId = @Id
		update [Round] set IsActive = 0 where Id = @Id
	end else begin
		--delete from CustomMetricData where KeyId in (select 
		delete from RoundDetail where RoundId = @Id
		delete from [Round] where Id = @Id
	end
	
END

GO

/****** Object:  StoredProcedure [dbo].[Round_Get]    Script Date: 11/21/2019 5:01:02 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_Get]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_Get] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/14/2015
-- Description:	Gets a round
-- =============================================
ALTER PROCEDURE [dbo].[Round_Get]
	@Id int
AS
BEGIN
	SET NOCOUNT ON;

    select * from [Round] where Id = @Id
END

GO

/****** Object:  StoredProcedure [dbo].[Round_GetGameGolfExport]    Script Date: 11/21/2019 5:01:02 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_GetGameGolfExport]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_GetGameGolfExport] AS' 
END
GO

-- =============================================
-- Author:		RR
-- Create date: 04/23/2018
-- Description:	Gets the full Game Golf Round export details
-- =============================================
ALTER PROCEDURE [dbo].[Round_GetGameGolfExport]
	@RoundId int
AS
BEGIN
	SET NOCOUNT ON;

    SELECT * FROM GameGolf_RoundExport WHERE RoundId = @RoundId
END
GO

/****** Object:  StoredProcedure [dbo].[Round_GetScorecard]    Script Date: 11/21/2019 5:01:03 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_GetScorecard]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_GetScorecard] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/14/2015
-- Description:	Gets a round's scorecard
-- Change Log:
--		09/16/2015	RR	Added round detail summaries
--		09/18/2015	RR	Fixed issue with missing holes showing related to par data
--		11/06/2015	RR	Added custom metric data
--		04/25/2016	RR	Added url fields
-- =============================================
ALTER PROCEDURE [dbo].[Round_GetScorecard]
	@Id int
AS
BEGIN
	SET NOCOUNT ON;

	--TODO: account for NULL scores in details summary calcs for RelatedToPar


	--summary
	select 
		r.Id, r.PlayedOn,
		c.Name as CourseName, t.Name as TeeName, t.CourseRating, t.SlopeRating,
		r.Hole19Url, r.GameGolfUrl,
		count(rd.Id) as HolesPlayed,
		sum(h.Par) as Par, sum(h.Yardage) as Yardage,
		sum(rd.Score) as Score, sum(rd.Putts) as Putts, 
		count(case when rd.FairwayHit = 1 then rd.Id end) as FairwaysHit,
		count(case when rd.TeeShotLeft = 1 then rd.Id end) as TeeShotLeft,
		count(case when rd.TeeShotRight = 1 then rd.Id end) as TeeShotRight,
		count(case when rd.TeeShotLong = 1 then rd.Id end) as TeeShotLong,
		count(case when rd.TeeShotShort = 1 then rd.Id end) as TeeShotShort,
		count(case when rd.GreenInRegulation = 1 then rd.Id end) as GreenInRegulation,
		sum(rd.PenaltyShots) as PenaltyShots,
		sum(rd.SandShots) as SandShots
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Course c on c.Id = t.CourseId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where r.Id = @Id
	group by r.Id, r.PlayedOn, c.Name, t.Name, t.CourseRating, t.SlopeRating, r.Hole19Url, r.GameGolfUrl

	--detail
	declare @details table (
		Number int, 
		Score int,
		RunningScore int,
		RunningRelatedToPar int
	)

	declare @Number int
	declare @Par int
	declare @Score int
	declare @RunningScore int
	declare @RunningRelatedToPar int
	set @RunningScore = 0
	set @RunningRelatedTopar = 0

	declare c cursor
		local static forward_only read_only
		for 
			select 
				h.Number, h.Par, rd.Score
			from [Round] r 
			inner join Tee t on t.Id = r.TeeId
			inner join Hole h on h.TeeId = t.Id
			left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
			where r.Id = @Id
			order by h.Number

	open c

	fetch next from c into @Number, @Par, @Score

	while @@fetch_status = 0 begin

		if @Score is not null begin
			set @RunningScore = @RunningScore + isnull(@Score,0)
			set @RunningRelatedToPar = @RunningRelatedToPar + (isnull(@Score,0) - isnull(@Par,0))
			
			insert into @details (Number, Score, RunningScore, RunningRelatedToPar)
			values (@Number, @Score, @RunningScore, @RunningRelatedToPar)
		end

		fetch next from c into @Number, @Par, @Score

	end

	close c
	deallocate c

	select 
		h.Number, h.Par, h.Yardage, h.Handicap,
		rd.Score, rd.Putts, rd.FairwayHit, rd.TeeShotLeft, rd.TeeShotRight, rd.TeeShotLong, rd.TeeShotShort, isnull(rd.GreenInRegulation,0) as GreenInRegulation, rd.PenaltyShots, rd.SandShots,
		d.RunningScore, 
		case 
			when d.RunningRelatedToPar > 0 then '+' + convert(varchar, d.RunningRelatedToPar)
			when d.RunningRelatedToPar < 0 then convert(varchar, d.RunningRelatedToPar)
			when d.RunningRelatedToPar is null then ''
			else 'E'
		end as RunningRelatedToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join @details d on d.Number = h.Number
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where r.Id = @Id
	order by h.Number
	
	
	
	--detail summary
	select 
		1 as SummaryId,
		'OUT' as Summary, 
		count(case when rd.Score != 0 and h.Number < 10 then rd.Id end) as Holes,
		sum(case when h.Number < 10 then h.Par else 0 end) as Par, 
		sum(case when h.Number < 10 then h.Yardage else 0 end) as Yardage,
		sum(case when h.Number < 10 then rd.Score else 0 end) as Score, 
		sum(case when h.Number < 10 then rd.Putts else 0 end) as Putts, 
		count(case when h.Number < 10 and rd.FairwayHit = 1 then rd.Id end) as FairwayHit, 
		count(case when h.Number < 10 and rd.TeeShotLeft = 1 then rd.Id end) as TeeShotLeft, 
		count(case when h.Number < 10 and rd.TeeShotRight = 1 then rd.Id end) as TeeShotRight, 
		count(case when h.Number < 10 and rd.TeeShotLong = 1 then rd.Id end) as TeeShotLong, 
		count(case when h.Number < 10 and rd.TeeShotShort = 1 then rd.Id end) as TeeShotShort, 
		count(case when h.Number < 10 and rd.GreenInRegulation = 1 then rd.Id end) as GreenInRegulation, 
		sum(case when h.Number < 10 then rd.PenaltyShots else 0 end) as PenaltyShots, 
		sum(case when h.Number < 10 then rd.SandShots else 0 end) as SandShots,
		case 
			when sum(case when h.Number < 10 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Number < 10 then rd.Score - h.Par else 0 end))
			when sum(case when h.Number < 10 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Number < 10 then rd.Score - h.Par else 0 end))
			else 'E'
		end as RelatedToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where r.Id = @Id

	union

	select 
		2 as SummaryId,
		'IN' as Summary, 
		count(case when rd.Score != 0 and h.Number > 9 then rd.Id end) as Holes,
		sum(case when h.Number > 9 then h.Par else 0 end) as Par, 
		sum(case when h.Number > 9 then h.Yardage else 0 end) as Yardage,
		sum(case when h.Number > 9 then rd.Score else 0 end) as Score, 
		sum(case when h.Number > 9 then rd.Putts else 0 end) as Putts, 
		count(case when h.Number > 9 and rd.FairwayHit = 1 then rd.Id end) as FairwayHit, 
		count(case when h.Number > 9 and rd.TeeShotLeft = 1 then rd.Id end) as TeeShotLeft, 
		count(case when h.Number > 9 and rd.TeeShotRight = 1 then rd.Id end) as TeeShotRight, 
		count(case when h.Number > 9 and rd.TeeShotLong = 1 then rd.Id end) as TeeShotLong, 
		count(case when h.Number > 9 and rd.TeeShotShort = 1 then rd.Id end) as TeeShotShort, 
		count(case when h.Number > 9 and rd.GreenInRegulation = 1 then rd.Id end) as GreenInRegulation, 
		sum(case when h.Number > 9 then rd.PenaltyShots else 0 end) as PenaltyShots, 
		sum(case when h.Number > 9 then rd.SandShots else 0 end) as SandShots,
		case 
			when sum(case when h.Number > 9 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Number > 9 then rd.Score - h.Par else 0 end))
			when sum(case when h.Number > 9 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Number > 9 then rd.Score - h.Par else 0 end))
			else 'E'
		end as RelatedToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where r.Id = @Id

	union 

	select 
		3 as SummaryId,
		'TOT' as Summary, 
		count(case when rd.Score != 0 then rd.Id end) as Holes,
		sum(h.Par) as Par, 
		sum(h.Yardage) as Yardage,
		sum(rd.Score) as Score, 
		sum(rd.Putts) as Putts, 
		count(case when rd.FairwayHit = 1 then rd.Id end) as FairwayHit, 
		count(case when rd.TeeShotLeft = 1 then rd.Id end) as TeeShotLeft, 
		count(case when rd.TeeShotRight = 1 then rd.Id end) as TeeShotRight, 
		count(case when rd.TeeShotLong = 1 then rd.Id end) as TeeShotLong, 
		count(case when rd.TeeShotShort = 1 then rd.Id end) as TeeShotShort, 
		count(case when rd.GreenInRegulation = 1 then rd.Id end) as GreenInRegulation, 
		sum(rd.PenaltyShots) as PenaltyShots, 
		sum(rd.SandShots) as SandShots,
		case 
			when sum(rd.Score - h.Par) > 0 then '+' + convert(varchar, sum(rd.Score - h.Par))
			when sum(rd.Score - h.Par) < 0 then convert(varchar, sum(rd.Score - h.Par))
			else 'E'
		end as RelatedToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where r.Id = @Id

	order by SummaryId
	
	
	--custom hole metrics used
	select distinct cm.Id, cm.Name
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	inner join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	inner join CustomMetricData cmd on cmd.KeyId = rd.Id
	inner join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	inner join MetricType mt on mt.Id = cm.MetricTypeId
	where r.Id = @Id and mt.Id = 2 and cm.IsActive = 1 and cmd.IsActive = 1
	order by cm.Id, cm.Name
	
	
	--custom round metrics
	select distinct cm.Id, cm.Name, cmd.Value, cmd.Value as Display
	from [Round] r 
	inner join CustomMetricData cmd on cmd.KeyId = r.Id
	inner join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	where r.Id = @Id and cm.MetricTypeId = 1 and cm.DataTypeId != 5 and cm.IsActive = 1 and cmd.IsActive = 1
	
	union
	
	select distinct cm.Id, cm.Name, cmd.Value, dto.Display
	from [Round] r 
	inner join CustomMetricData cmd on cmd.KeyId = r.Id
	inner join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	inner join DataTypeOption dto on dto.CustomMetricId = cm.Id and convert(int, cmd.Value) = dto.Id
	where r.Id = @Id and cm.MetricTypeId = 1 and cm.DataTypeId = 5 and cm.IsActive = 1 and cmd.IsActive = 1
	
	order by cm.Id, cm.Name
	
	
END

GO

/****** Object:  StoredProcedure [dbo].[Round_GetScorecard_CustomData]    Script Date: 11/21/2019 5:01:03 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_GetScorecard_CustomData]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_GetScorecard_CustomData] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 11/06/2015
-- Description:	Gets the custom metric data for a round
-- =============================================
ALTER PROCEDURE [dbo].[Round_GetScorecard_CustomData]
	@RoundId int,
	@CustomMetricId int
AS
BEGIN
	SET NOCOUNT ON;

	--custom hole metrics detail
	-- need to expand with options
	-- need to also include detail summaries for each
	select 
		cm.Id,
		cm.Name,
		h.Number,
		cmd.Value
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	left outer join CustomMetricData cmd on cmd.KeyId = rd.Id
	left outer join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	left outer join MetricType mt on mt.Id = cm.MetricTypeId
	where 
		r.Id = @RoundId 
		and mt.Id = 2 
		and cm.Id = @CustomMetricId
		and cm.IsActive = 1 
		and cmd.IsActive = 1
	order by cm.Id, cm.Name, h.Number
	
	
	
	
	--custom hole metrics detail summary
	select 
		1 as SummaryId,
		'OUT' as Summary, 
		case dt.Id
			when 1 then --yes/no
				0
			when 2 then --number
				round(sum(case when h.Number < 10 then convert(float, cmd.Value) else 0 end), 2)
			when 3 then --text
				0
			when 4 then --left/right/middle
				0
			when 5 then --option
				0
		end as TotalValue,
		case dt.Id
			when 1 then --yes/no
				0
			when 2 then --number
				round(sum(case when h.Number < 10 then convert(float, cmd.Value) else 0 end) / convert(float, sum(case when h.Number < 10 and rd.Score is not null then 1 else 0 end)), 2)
			when 3 then --text
				0
			when 4 then --left/right/middle
				0
			when 5 then --option
				0
		end as AvgValue
		--sum(case when h.Number < 10 then h.Par else 0 end) as Par, 
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	left outer join CustomMetricData cmd on cmd.KeyId = rd.Id
	left outer join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	left outer join MetricType mt on mt.Id = cm.MetricTypeId
	left outer join DataType dt on dt.Id = cm.DataTypeId
	left outer join DataTypeOption dto on dto.CustomMetricId = cm.Id
	where 
		r.Id = 102 
		and mt.Id = 2 
		and cm.Id = 2
		and cm.IsActive = 1 
		and cmd.IsActive = 1
	group by dt.Id

	union

	select 
		2 as SummaryId,
		'IN' as Summary, 
		case dt.Id
			when 1 then --yes/no
				0
			when 2 then --number
				round(sum(case when h.Number > 9 then convert(float, cmd.Value) else 0 end), 2)
			when 3 then --text
				0
			when 4 then --left/right/middle
				0
			when 5 then --option
				0
		end as TotalValue,
		case dt.Id
			when 1 then --yes/no
				0
			when 2 then --number
				round(sum(case when h.Number > 9 then convert(float, cmd.Value) else 0 end) / convert(float, sum(case when h.Number > 9 and rd.Score is not null then 1 else 0 end)), 2)
			when 3 then --text
				0
			when 4 then --left/right/middle
				0
			when 5 then --option
				0
		end as AvgValue
		--sum(case when h.Number < 10 then h.Par else 0 end) as Par, 
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	left outer join CustomMetricData cmd on cmd.KeyId = rd.Id
	left outer join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	left outer join MetricType mt on mt.Id = cm.MetricTypeId
	left outer join DataType dt on dt.Id = cm.DataTypeId
	left outer join DataTypeOption dto on dto.CustomMetricId = cm.Id
	where 
		r.Id = 102 
		and mt.Id = 2 
		and cm.Id = 2
		and cm.IsActive = 1 
		and cmd.IsActive = 1
	group by dt.Id

	union 

	select 
		3 as SummaryId,
		'TOT' as Summary, 
		case dt.Id
			when 1 then --yes/no
				0
			when 2 then --number
				round(sum(convert(float, cmd.Value)), 2)
			when 3 then --text
				0
			when 4 then --left/right/middle
				0
			when 5 then --option
				0
		end as TotalValue,
		case dt.Id
			when 1 then --yes/no
				0
			when 2 then --number
				round(sum(convert(float, cmd.Value)) / convert(float, sum(case when rd.Score is not null then 1 else 0 end)), 2)
			when 3 then --text
				0
			when 4 then --left/right/middle
				0
			when 5 then --option
				0
		end as AvgValue
		--sum(case when h.Number < 10 then h.Par else 0 end) as Par, 
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	left outer join CustomMetricData cmd on cmd.KeyId = rd.Id
	left outer join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	left outer join MetricType mt on mt.Id = cm.MetricTypeId
	left outer join DataType dt on dt.Id = cm.DataTypeId
	left outer join DataTypeOption dto on dto.CustomMetricId = cm.Id
	where 
		r.Id = 102 
		and mt.Id = 2 
		and cm.Id = 2
		and cm.IsActive = 1 
		and cmd.IsActive = 1
	group by dt.Id

	order by SummaryId
END

GO

/****** Object:  StoredProcedure [dbo].[Round_GetScorecardByCourse]    Script Date: 11/21/2019 5:01:04 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_GetScorecardByCourse]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_GetScorecardByCourse] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 06/24/2017
-- Description:	Gets a course's scorecard and stats
-- =============================================
ALTER PROCEDURE [dbo].[Round_GetScorecardByCourse]
	@GolferId int = 1,
	@Year int,
	@CourseId int,
	@TeeId int
AS
BEGIN
	SET NOCOUNT ON;

	--TODO: account for NULL scores in details summary calcs for RelatedToPar

	declare @roundId int

	select top 1 @roundId = r.Id 
	from dbo.[Round] r 
	inner join dbo.Tee t on t.Id = r.TeeId 
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId
		and r.GolferId = @GolferId
		and datepart(yyyy, r.PlayedOn) = @Year
		and r.IsActive = 1 
		and t.IsActive = 1 
	order by r.PlayedOn desc

	--summary
	select 
		c.Name as CourseName, t.Name as TeeName, t.CourseRating, t.SlopeRating,
		sum(h.Par) as Par, sum(h.Yardage) as Yardage
	from Tee t
	inner join Course c on c.Id = t.CourseId
	inner join Hole h on h.TeeId = t.Id
	where t.Id = @TeeId
	group by c.Name, t.Name, t.CourseRating, t.SlopeRating


	--holes
	select 
		h.Number, h.Par, h.Yardage
	from Tee t 
	inner join Hole h on h.TeeId = t.Id
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId 
		and t.IsActive = 1
	order by h.Number

	--hole summary
	select 
		1 as SummaryId,
		'OUT' as Summary, 
		sum(case when h.Number < 10 then h.Par else 0 end) as Par,
		sum(case when h.Number < 10 then h.Yardage else 0 end) as Yardage
	from Tee t
	inner join Hole h on h.TeeId = t.Id
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId 
		and t.IsActive = 1

	union

	select 
		2 as SummaryId,
		'IN' as Summary, 
		sum(case when h.Number > 9 then h.Par else 0 end) as Par,
		sum(case when h.Number > 9 then h.Yardage else 0 end) as Yardage
	from Tee t
	inner join Hole h on h.TeeId = t.Id
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId 
		and t.IsActive = 1

	union

	select 
		3 as SummaryId,
		'TOT' as Summary, 
		sum(h.Par) as Par,
		sum(h.Yardage) as Yardage
	from Tee t
	inner join Hole h on h.TeeId = t.Id
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId 
		and t.IsActive = 1

	order by SummaryId


	--detail
	declare @details table (
		Number int, 
		Score int,
		RunningScore int,
		RunningRelatedToPar int
	)

	declare @Number int
	declare @Par int
	declare @Score int
	declare @RunningScore int
	declare @RunningRelatedToPar int
	set @RunningScore = 0
	set @RunningRelatedTopar = 0

	declare c cursor
		local static forward_only read_only
		for 
			select 
				h.Number, h.Par, avg(rd.Score) as Score
			from [Round] r 
			inner join Tee t on t.Id = r.TeeId
			inner join Hole h on h.TeeId = t.Id
			left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
			where 
				t.CourseId = @CourseId 
				and t.Id = @TeeId 
				and r.GolferId = @GolferId 
				and datepart(yyyy, r.PlayedOn) = @Year
				and r.IsActive = 1
				and t.IsActive = 1
			group by h.Number, h.Par
			order by h.Number

	open c

	fetch next from c into @Number, @Par, @Score

	while @@fetch_status = 0 begin

		if @Score is not null begin
			set @RunningScore = @RunningScore + isnull(@Score,0)
			set @RunningRelatedToPar = @RunningRelatedToPar + (isnull(@Score,0) - isnull(@Par,0))
			
			insert into @details (Number, Score, RunningScore, RunningRelatedToPar)
			values (@Number, @Score, @RunningScore, @RunningRelatedToPar)
		end

		fetch next from c into @Number, @Par, @Score

	end

	close c
	deallocate c

	select 
		h.Number, h.Par, h.Yardage, h.Handicap,
		count(rd.Id) as Holes,
		avg(convert(float, rd.Score)) as AvgScore,
		min(rd.Score) as BestScore,
		max(rd.Score) as WorstScore,
		avg(convert(float, rd.Putts)) as Putts,
		case count(rd.Id)
			when 0 then 0
			else 
				round(sum(case when rd.FairwayHit = 1 then 1 else 0 end) / convert(float, count(rd.Id)), 2)
		end as FairwayHit,
		case count(rd.Id)
			when 0 then 0
			else 
				round(sum(case when rd.GreenInRegulation = 1 then 1 else 0 end) / convert(float, count(rd.Id)), 2)
		end as GreenInRegulation,
		avg(convert(float, rd.PenaltyShots)) as PenaltyShots,
		avg(convert(float, rd.SandShots)) as SandShots
		--rd.Score, rd.Putts, rd.FairwayHit, rd.TeeShotLeft, rd.TeeShotRight, rd.TeeShotLong, rd.TeeShotShort, rd.GreenInRegulation, rd.PenaltyShots, rd.SandShots,
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join @details d on d.Number = h.Number
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId 
		and r.GolferId = @GolferId 
		and datepart(yyyy, r.PlayedOn) = @Year
		and t.IsActive = 1
		and r.IsActive = 1
	group by h.Number, h.Par, h.Yardage, h.Handicap
	order by h.Number
	
	
	--score summary
	select 
		1 as SummaryId,
		'OUT' as Summary,
		sum(case when a.Number < 10 then a.Holes else 0 end) as Holes,
		--case sum(case when a.Number < 10 then a.Rounds else 0 end)
		--	when 0 then 0 
		--	else round(sum(case when a.Number < 10 then a.AvgScore else 0 end) / convert(float, sum(case when a.Number < 10 then a.Rounds else 0 end)), 2)	
		--end as AvgScore,
		0 as AvgScore,
		sum(case when a.Number < 10 then a.BestScore else 0 end) as BestScore,
		sum(case when a.Number < 10 then a.WorstScore else 0 end) as WorstScore
	from (
		select 
			h.Number,
			count(distinct r.Id) as Rounds,
			count(rd.Id) as Holes,
			sum(rd.Score) as AvgScore,
			min(rd.Score) as BestScore,
			max(rd.Score) as WorstScore
		from [Round] r 
		inner join Tee t on t.Id = r.TeeId
		inner join Hole h on h.TeeId = t.Id
		left outer join @details d on d.Number = h.Number
		left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
		where 
			t.CourseId = @CourseId 
			and t.Id = @TeeId 
			and r.GolferId = @GolferId 
			and datepart(yyyy, r.PlayedOn) = @Year
			and t.IsActive = 1
			and r.IsActive = 1
		group by h.Number
	) a

	union 

	select 
		2 as SummaryId,
		'IN' as Summary,
		sum(case when a.Number > 9 then a.Holes else 0 end) as Holes,
		--avg(case when a.Number > 9 then a.AvgScore else 0 end) as AvgScore,
		0 as AvgScore,
		sum(case when a.Number > 9 then a.BestScore else 0 end) as BestScore,
		sum(case when a.Number > 9 then a.WorstScore else 0 end) as WorstScore
	from (
		select 
			h.Number,
			count(distinct r.Id) as Rounds,
			count(rd.Id) as Holes,
			sum(rd.Score) as AvgScore,
			min(rd.Score) as BestScore,
			max(rd.Score) as WorstScore
		from [Round] r 
		inner join Tee t on t.Id = r.TeeId
		inner join Hole h on h.TeeId = t.Id
		left outer join @details d on d.Number = h.Number
		left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
		where 
			t.CourseId = @CourseId 
			and t.Id = @TeeId 
			and r.GolferId = @GolferId 
			and datepart(yyyy, r.PlayedOn) = @Year
			and t.IsActive = 1
			and r.IsActive = 1
		group by h.Number
	) a

	union 

	select 
		3 as SummaryId,
		'TOT' as Summary,
		sum(a.Holes) as Holes,
		--avg(a.AvgScore) as AvgScore,
		0 as AvgScore,
		sum(a.BestScore) as BestScore,
		sum(a.WorstScore) as WorstScore
	from (
		select 
			h.Number,
			count(distinct r.Id) as Rounds,
			count(rd.Id) as Holes,
			sum(rd.Score) as AvgScore,
			min(rd.Score) as BestScore,
			max(rd.Score) as WorstScore
		from [Round] r 
		inner join Tee t on t.Id = r.TeeId
		inner join Hole h on h.TeeId = t.Id
		left outer join @details d on d.Number = h.Number
		left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
		where 
			t.CourseId = @CourseId 
			and t.Id = @TeeId 
			and r.GolferId = @GolferId 
			and datepart(yyyy, r.PlayedOn) = @Year
			and t.IsActive = 1
			and r.IsActive = 1
		group by h.Number
	) a

	order by SummaryId

	
	--detail summary
	select 
		1 as SummaryId,
		'OUT' as Summary, 
		count(case when rd.Score != 0 and h.Number < 10 then rd.Id end) as Holes,
		--sum(case when h.Number < 10 then h.Par else 0 end) as Par, 
		--sum(case when h.Number < 10 then h.Yardage else 0 end) as Yardage,
		0 as Par,
		0 as Yardage,
		case count(case when rd.Score != 0 and h.Number < 10 then rd.Id end)
			when 0 then 0
			else round(sum(case when h.Number < 10 then rd.Score else 0 end) / convert(float, count(case when rd.Score != 0 and h.Number < 10 then rd.Id end)), 2)
		end as AvgScore, 
		0 as BestScore,
		0 as WorstScore,
		case count(case when rd.Score != 0 and h.Number < 10 then rd.Id end)
			when 0 then 0
			else round(sum(case when h.Number < 10 then rd.Putts else 0 end) / convert(float, count(case when rd.Score != 0 and h.Number < 10 then rd.Id end)), 2)
		end as Putts, 
		case count(case when rd.Score != 0 and h.Number < 10 then rd.Id end)
			when 0 then 0
			else round(count(case when h.Number < 10 and rd.FairwayHit = 1 then rd.Id end) / convert(float, count(case when rd.Score != 0 and h.Number < 10 then rd.Id end)), 2)
		end as FairwayHit, 
		--count(case when h.Number < 10 and rd.TeeShotLeft = 1 then rd.Id end) as TeeShotLeft, 
		--count(case when h.Number < 10 and rd.TeeShotRight = 1 then rd.Id end) as TeeShotRight, 
		--count(case when h.Number < 10 and rd.TeeShotLong = 1 then rd.Id end) as TeeShotLong, 
		--count(case when h.Number < 10 and rd.TeeShotShort = 1 then rd.Id end) as TeeShotShort, 
		case count(case when rd.Score != 0 and h.Number < 10 then rd.Id end)
			when 0 then 0
			else round(count(case when h.Number < 10 and rd.GreenInRegulation = 1 then rd.Id end) / convert(float, count(case when rd.Score != 0 and h.Number < 10 then rd.Id end)), 2)
		end as GreenInRegulation, 
		avg(case when h.Number < 10 then rd.PenaltyShots else 0 end) as PenaltyShots, 
		avg(case when h.Number < 10 then rd.SandShots else 0 end) as SandShots--,
		--case 
		--	when sum(case when h.Number < 10 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Number < 10 then rd.Score - h.Par else 0 end))
		--	when sum(case when h.Number < 10 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Number < 10 then rd.Score - h.Par else 0 end))
		--	else 'E'
		--end as RelatedToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId 
		and r.GolferId = @GolferId 
		and datepart(yyyy, r.PlayedOn) = @Year
		and t.IsActive = 1
		and r.IsActive = 1

	union

	select 
		2 as SummaryId,
		'IN' as Summary, 
		count(case when rd.Score != 0 and h.Number > 9 then rd.Id end) as Holes,
		--sum(case when h.Number > 9 then h.Par else 0 end) as Par, 
		--sum(case when h.Number > 9 then h.Yardage else 0 end) as Yardage,
		0 as Par,
		0 as Yardage,
		case count(case when rd.Score != 0 and h.Number > 9 then rd.Id end)
			when 0 then 0
			else round(sum(case when h.Number > 9 then rd.Score else 0 end) / convert(float, count(case when rd.Score != 0 and h.Number > 9 then rd.Id end)), 2)
		end as AvgScore, 
		0 as BestScore,
		0 as WorstScore,
		case count(case when rd.Score != 0 and h.Number > 9 then rd.Id end)
			when 0 then 0
			else round(sum(case when h.Number > 9 then rd.Putts else 0 end) / convert(float, count(case when rd.Score != 0 and h.Number > 9 then rd.Id end)), 2)
		end as Putts, 
		case count(case when rd.Score != 0 and h.Number > 9 then rd.Id end)
			when 0 then 0
			else round(count(case when h.Number > 9 and rd.FairwayHit = 1 then rd.Id end) / convert(float, count(case when rd.Score != 0 and h.Number > 9 then rd.Id end)), 2)
		end as FairwayHit, 
		--count(case when h.Number < 10 and rd.TeeShotLeft = 1 then rd.Id end) as TeeShotLeft, 
		--count(case when h.Number < 10 and rd.TeeShotRight = 1 then rd.Id end) as TeeShotRight, 
		--count(case when h.Number < 10 and rd.TeeShotLong = 1 then rd.Id end) as TeeShotLong, 
		--count(case when h.Number < 10 and rd.TeeShotShort = 1 then rd.Id end) as TeeShotShort, 
		case count(case when rd.Score != 0 and h.Number > 9 then rd.Id end)
			when 0 then 0
			else round(count(case when h.Number > 9 and rd.GreenInRegulation = 1 then rd.Id end) / convert(float, count(case when rd.Score != 0 and h.Number > 9 then rd.Id end)), 2)
		end as GreenInRegulation,  
		avg(case when h.Number > 9 then rd.PenaltyShots else 0 end) as PenaltyShots, 
		avg(case when h.Number > 9 then rd.SandShots else 0 end) as SandShots--,
		--case 
		--	when sum(case when h.Number > 9 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Number > 9 then rd.Score - h.Par else 0 end))
		--	when sum(case when h.Number > 9 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Number > 9 then rd.Score - h.Par else 0 end))
		--	else 'E'
		--end as RelatedToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId 
		and r.GolferId = @GolferId 
		and datepart(yyyy, r.PlayedOn) = @Year
		and t.IsActive = 1
		and r.IsActive = 1

	union 

	select 
		3 as SummaryId,
		'TOT' as Summary, 
		count(case when rd.Score != 0 then rd.Id end) as Holes,
		--sum(h.Par) as Par, 
		--sum(h.Yardage) as Yardage,
		0 as Par,
		0 as Yardage,
		case count(case when rd.Score != 0 then rd.Id end)
			when 0 then 0
			else round(sum(rd.Score) / convert(float, count(case when rd.Score != 0 then rd.Id end)), 2)
		end as AvgScore, 
		0 as BestScore,
		0 as WorstScore, 
		case count(case when rd.Score != 0 then rd.Id end)
			when 0 then 0
			else round(sum(rd.Putts) / convert(float, count(case when rd.Score != 0 then rd.Id end)), 2)
		end as Putts, 
		case count(case when rd.Score != 0 then rd.Id end)
			when 0 then 0
			else round(count(case when rd.FairwayHit = 1 then rd.Id end) / convert(float, count(case when rd.Score != 0 then rd.Id end)), 2)
		end as FairwayHit, 
		--count(case when h.Number < 10 and rd.TeeShotLeft = 1 then rd.Id end) as TeeShotLeft, 
		--count(case when h.Number < 10 and rd.TeeShotRight = 1 then rd.Id end) as TeeShotRight, 
		--count(case when h.Number < 10 and rd.TeeShotLong = 1 then rd.Id end) as TeeShotLong, 
		--count(case when h.Number < 10 and rd.TeeShotShort = 1 then rd.Id end) as TeeShotShort, 
		case count(case when rd.Score != 0 then rd.Id end)
			when 0 then 0
			else round(count(case when rd.GreenInRegulation = 1 then rd.Id end) / convert(float, count(case when rd.Score != 0 then rd.Id end)), 2)
		end as GreenInRegulation, 
		avg(rd.PenaltyShots) as PenaltyShots, 
		avg(rd.SandShots) as SandShots--,
		--case 
		--	when sum(rd.Score - h.Par) > 0 then '+' + convert(varchar, sum(rd.Score - h.Par))
		--	when sum(rd.Score - h.Par) < 0 then convert(varchar, sum(rd.Score - h.Par))
		--	else 'E'
		--end as RelatedToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Hole h on h.TeeId = t.Id
	left outer join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where 
		t.CourseId = @CourseId 
		and t.Id = @TeeId 
		and r.GolferId = @GolferId 
		and datepart(yyyy, r.PlayedOn) = @Year
		and t.IsActive = 1
		and r.IsActive = 1

	order by SummaryId
	
	
	----custom hole metrics used
	--select distinct cm.Id, cm.Name
	--from [Round] r 
	--inner join Tee t on t.Id = r.TeeId
	--inner join Hole h on h.TeeId = t.Id
	--inner join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	--inner join CustomMetricData cmd on cmd.KeyId = rd.Id
	--inner join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	--inner join MetricType mt on mt.Id = cm.MetricTypeId
	--where r.Id = @Id and mt.Id = 2 and cm.IsActive = 1 and cmd.IsActive = 1
	--order by cm.Id, cm.Name
	
	
	----custom round metrics
	--select distinct cm.Id, cm.Name, cmd.Value, cmd.Value as Display
	--from [Round] r 
	--inner join CustomMetricData cmd on cmd.KeyId = r.Id
	--inner join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	--where r.Id = @Id and cm.MetricTypeId = 1 and cm.DataTypeId != 5 and cm.IsActive = 1 and cmd.IsActive = 1
	
	--union
	
	--select distinct cm.Id, cm.Name, cmd.Value, dto.Display
	--from [Round] r 
	--inner join CustomMetricData cmd on cmd.KeyId = r.Id
	--inner join CustomMetric cm on cm.Id = cmd.CustomMetricId and cm.GolferId = r.GolferId
	--inner join DataTypeOption dto on dto.CustomMetricId = cm.Id and convert(int, cmd.Value) = dto.Id
	--where r.Id = @Id and cm.MetricTypeId = 1 and cm.DataTypeId = 5 and cm.IsActive = 1 and cmd.IsActive = 1
	
	--order by cm.Id, cm.Name
	
	
END

GO

/****** Object:  StoredProcedure [dbo].[Round_List]    Script Date: 11/21/2019 5:01:04 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_List]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_List] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/16/2015
-- Description:	Lists rounds and stats
-- Change Log:
--		09/17/2015	RR	Added Holes Played
--		09/18/2015	RR	Filtered out inactive rounds
--		10/26/2015	RR	Added field for total putts
-- =============================================
ALTER PROCEDURE [dbo].[Round_List]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    select
		r.Id,
		r.PlayedOn,
		c.Name as CourseName,
		t.Name as TeeName,
		sum(rd.Score) as Score,
		count(rd.Id) as HolesPlayed,
		case 
			when sum(rd.Score - h.Par) > 0 then '+' + convert(varchar, sum(rd.Score - h.Par))
			when sum(rd.Score - h.Par) < 0 then convert(varchar, sum(rd.Score - h.Par))
			else 'E' 
		end as RelatedToPar,
		sum(rd.Putts) as Putts,
		round(count(case when rd.GreenInRegulation = 1 then rd.Id end) / convert(float, count(rd.Id)), 4) as GIR,
		round(count(case when h.Par != 3 and rd.FairwayHit = 1 then rd.Id end) / convert(float, count(case when h.Par != 3 then rd.Id end)), 4) as DrivingAccuracy,
		case 
			when sum(case when h.Par = 3 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Par = 3 then rd.Score - h.Par else 0 end))
			when sum(case when h.Par = 3 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Par = 3 then rd.Score - h.Par else 0 end))
			else 'E' 
		end as Par3ScoreToPar,
		case 
			when sum(case when h.Par = 4 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Par = 4 then rd.Score - h.Par else 0 end))
			when sum(case when h.Par = 4 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Par = 4 then rd.Score - h.Par else 0 end))
			else 'E' 
		end as Par4ScoreToPar,
		case 
			when sum(case when h.Par = 5 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Par = 5 then rd.Score - h.Par else 0 end))
			when sum(case when h.Par = 5 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Par = 5 then rd.Score - h.Par else 0 end))
			else 'E' 
		end as Par5ScoreToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Course c on c.Id = t.CourseId
	inner join Hole h on h.TeeId = t.Id
	inner join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1 
	group by r.Id, r.PlayedOn, c.Name, t.Name
	order by r.PlayedOn desc
	
END

GO

/****** Object:  StoredProcedure [dbo].[Round_ListByCourse]    Script Date: 11/21/2019 5:01:04 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_ListByCourse]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_ListByCourse] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 06/24/2017
-- Description:	Lists rounds and stats by course
-- Change Log:
-- =============================================
ALTER PROCEDURE [dbo].[Round_ListByCourse]
	@year int,
	@course int = null
AS
BEGIN
	SET NOCOUNT ON;

    select
		r.Id,
		r.PlayedOn,
		c.Name as CourseName,
		t.Name as TeeName,
		sum(rd.Score) as Score,
		count(rd.Id) as HolesPlayed,
		case 
			when sum(rd.Score - h.Par) > 0 then '+' + convert(varchar, sum(rd.Score - h.Par))
			when sum(rd.Score - h.Par) < 0 then convert(varchar, sum(rd.Score - h.Par))
			else 'E' 
		end as RelatedToPar,
		sum(rd.Putts) as Putts,
		round(count(case when rd.GreenInRegulation = 1 then rd.Id end) / convert(float, count(rd.Id)), 4) as GIR,
		round(count(case when h.Par != 3 and rd.FairwayHit = 1 then rd.Id end) / convert(float, count(case when h.Par != 3 then rd.Id end)), 4) as DrivingAccuracy,
		case 
			when sum(case when h.Par = 3 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Par = 3 then rd.Score - h.Par else 0 end))
			when sum(case when h.Par = 3 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Par = 3 then rd.Score - h.Par else 0 end))
			else 'E' 
		end as Par3ScoreToPar,
		case 
			when sum(case when h.Par = 4 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Par = 4 then rd.Score - h.Par else 0 end))
			when sum(case when h.Par = 4 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Par = 4 then rd.Score - h.Par else 0 end))
			else 'E' 
		end as Par4ScoreToPar,
		case 
			when sum(case when h.Par = 5 then rd.Score - h.Par else 0 end) > 0 then '+' + convert(varchar, sum(case when h.Par = 5 then rd.Score - h.Par else 0 end))
			when sum(case when h.Par = 5 then rd.Score - h.Par else 0 end) < 0 then convert(varchar, sum(case when h.Par = 5 then rd.Score - h.Par else 0 end))
			else 'E' 
		end as Par5ScoreToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Course c on c.Id = t.CourseId
	inner join Hole h on h.TeeId = t.Id
	inner join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where 
		datepart(yyyy, r.PlayedOn) = @year 
		and r.IsActive = 1 
		and t.CourseId = @course
	group by r.Id, r.PlayedOn, c.Name, t.Name
	order by r.PlayedOn desc
	
END

GO

/****** Object:  StoredProcedure [dbo].[Round_SeasonSummary]    Script Date: 11/21/2019 5:01:05 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Round_SeasonSummary]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Round_SeasonSummary] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/16/2015
-- Description:	Gets a season's summary
-- Change Log:
--		09/18/2015	RR	Filtered out inactive rounds
-- =============================================
ALTER PROCEDURE [dbo].[Round_SeasonSummary]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

	select 
		count(r.Id) as Rounds,
		--count(case when count(rd.Id) = 9 then r.Id end) as Rounds9,
		--count(case when count(rd.Id) = 18 then r.Id end) as Rounds18,
		0 as Rounds9,
		0 as Rounds18,
		count(case when r.Score < 80 then r.Id end) as RoundsUnder80,
		count(case when r.Score >= 80 and r.Score < 90 then r.Id end) as Rounds8089,
		count(case when r.Score >= 90 then r.Id end) as RoundsOver90
    from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Course c on c.Id = t.CourseId
	inner join Hole h on h.TeeId = t.Id
	inner join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
END

GO

/****** Object:  StoredProcedure [dbo].[RoundDetail_Create]    Script Date: 11/21/2019 5:01:05 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RoundDetail_Create]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[RoundDetail_Create] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/10/2015
-- Description: Adds a round detail
-- =============================================
ALTER PROCEDURE [dbo].[RoundDetail_Create]
	@RoundId int,
	@HoleId int,
	@Score int,
	@Putts int,
	@FairwayHit bit = 0,
	@TeeShotLeft bit = 0,
	@TeeShotRight bit = 0,
	@TeeShotLong bit = 0,
	@TeeShotShort bit = 0,
	@GreenInRegulation bit = 0,
	@PenaltyShots int = 0,
	@SandShots int = 0,
	@IsActive bit = 1
AS
BEGIN
	SET NOCOUNT ON;

    insert into RoundDetail (RoundId, HoleId, Score, Putts, FairwayHit, TeeShotLeft, TeeShotRight, TeeShotLong, TeeShotShort, GreenInRegulation, PenaltyShots, SandShots, IsActive)
    values (@RoundId, @HoleId, @Score, @Putts, @FairwayHit, @TeeShotLeft, @TeeShotRight, @TeeShotLong, @TeeShotShort, @GreenInRegulation, @PenaltyShots, @SandShots, @IsActive)
    
    select scope_identity()
END

GO

/****** Object:  StoredProcedure [dbo].[Stats_ApproachStats]    Script Date: 11/21/2019 5:01:06 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_ApproachStats]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_ApproachStats] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/26/2015
-- Description:	Collection of approach stats for a season
-- =============================================
ALTER PROCEDURE [dbo].[Stats_ApproachStats]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    declare @holes int
    declare @gir int
	declare @zero_putts int
	
	declare @driving_holes int
	declare @tee_fairway int
	declare @tee_right int
	declare @tee_left int
	declare @tee_short int
	declare @tee_long int
	declare @gir_fairway int
	declare @gir_right int
	declare @gir_left int
	declare @gir_short int
	declare @gir_long int
	
	declare @sand_shots int
	declare @sand_shot_holes int
	declare @sand_shot_par int
	
	declare @scrambling_pars int
	
	select 
		@holes = count(rd.Id),
		@gir = count(case when rd.GreenInRegulation = 1 then rd.Id end),
		@zero_putts = count(case when rd.Putts = 0 then rd.Id end),
		@driving_holes = sum(case h.Par when 3 then 0 else 1 end),
		@tee_fairway = sum(case rd.FairwayHit when 1 then 1 else 0 end),
		@tee_right = sum(case rd.TeeShotRight when 1 then 1 else 0 end),
		@tee_left = sum(case rd.TeeShotLeft when 1 then 1 else 0 end),
		@tee_short = sum(case rd.TeeShotShort when 1 then 1 else 0 end),
		@tee_long = sum(case rd.TeeShotLong when 1 then 1 else 0 end),
		@gir_fairway = count(case when rd.FairwayHit = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@gir_right = count(case when rd.TeeShotRight = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@gir_left = count(case when rd.TeeShotLeft = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@gir_short = count(case when rd.TeeShotShort = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@gir_long = count(case when rd.TeeShotLong = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@sand_shots = sum(rd.SandShots),
		@sand_shot_holes = count(case when rd.SandShots > 0 then rd.Id end),
		@sand_shot_par = count(case when rd.SandShots > 0 and rd.Score - h.Par <= 0 then rd.Id end),
		@scrambling_pars = count(case when rd.GreenInRegulation = 0 and rd.Score - h.Par <= 0 then rd.Id end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[Hole] h on h.id = rd.HoleId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1

	if @holes > 0 begin
		select 
			@holes as [TotalHoles],
			@gir as [GreensInRegulation],
			@holes - @gir as [MissedGIR],
			@zero_putts as [HoleOuts],
			case @holes when 0 then 0 else round((@zero_putts / convert(float, @holes)), 4) end as [HoleOutAvg],
			case @holes when 0 then 0 else round((@gir / convert(float, @holes)), 4) end as [GIRAvg],
			case @tee_fairway when 0 then 0 else round((@gir_fairway / convert(float, @tee_fairway)), 4) end as [GIRFairwayAvg],
			case @tee_right when 0 then 0 else round((@gir_right / convert(float, @tee_right)), 4) end as [GIRRightRoughAvg],
			case @tee_left when 0 then 0 else round((@gir_left / convert(float, @tee_left)), 4) end as [GIRLeftRoughAvg],
			case @tee_short when 0 then 0 else round((@gir_short / convert(float, @tee_short)), 4) end as [GIRShortAvg],
			case @tee_long when 0 then 0 else round((@gir_long / convert(float, @tee_long)), 4) end as [GIRLongAvg],
			@tee_fairway as [TeeShotFairway],
			@tee_right as [TeeShotRight],
			@tee_left as [TeeShotLeft],
			@tee_short as [TeeShotShort],
			@tee_long as [TeeShotLong],
			@gir_fairway as [GIRFromFairway],
			@gir_right as [GIRFromRight],
			@gir_left as [GIRFromLeft],
			@gir_short as [GIRFromShort],
			@gir_long as [GIRFromLong],
			@sand_shots as [TotalSandShots],
			@sand_shot_holes as [HolesWithSandShots],
			@sand_shot_par as [SandSaves],
			case @sand_shot_holes when 0 then 0 else round((@sand_shot_par / convert(float, @sand_shot_holes)), 4) end as [SandSaveAvg],
			@scrambling_pars as [MissedGIRScrambling],
			case (@holes - @gir) when 0 then 0 else round((@scrambling_pars / convert(float, (@holes - @gir))), 4) end as [ScramblingAvg]
	end
		
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_ApproachStatsByCourse]    Script Date: 11/21/2019 5:01:06 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_ApproachStatsByCourse]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_ApproachStatsByCourse] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/26/2015
-- Description:	Collection of approach stats for a season and course
-- =============================================
ALTER PROCEDURE [dbo].[Stats_ApproachStatsByCourse]
	@year int,
	@course int
AS
BEGIN
	SET NOCOUNT ON;

    declare @holes int
    declare @gir int
	declare @zero_putts int
	
	declare @driving_holes int
	declare @tee_fairway int
	declare @tee_right int
	declare @tee_left int
	declare @tee_short int
	declare @tee_long int
	declare @gir_fairway int
	declare @gir_right int
	declare @gir_left int
	declare @gir_short int
	declare @gir_long int
	
	declare @sand_shots int
	declare @sand_shot_holes int
	declare @sand_shot_par int
	
	declare @scrambling_pars int
	
	select 
		@holes = count(rd.Id),
		@gir = count(case when rd.GreenInRegulation = 1 then rd.Id end),
		@zero_putts = count(case when rd.Putts = 0 then rd.Id end),
		@driving_holes = sum(case h.Par when 3 then 0 else 1 end),
		@tee_fairway = sum(case rd.FairwayHit when 1 then 1 else 0 end),
		@tee_right = sum(case rd.TeeShotRight when 1 then 1 else 0 end),
		@tee_left = sum(case rd.TeeShotLeft when 1 then 1 else 0 end),
		@tee_short = sum(case rd.TeeShotShort when 1 then 1 else 0 end),
		@tee_long = sum(case rd.TeeShotLong when 1 then 1 else 0 end),
		@gir_fairway = count(case when rd.FairwayHit = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@gir_right = count(case when rd.TeeShotRight = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@gir_left = count(case when rd.TeeShotLeft = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@gir_short = count(case when rd.TeeShotShort = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@gir_long = count(case when rd.TeeShotLong = 1 and rd.GreenInRegulation = 1 then rd.Id end),
		@sand_shots = sum(rd.SandShots),
		@sand_shot_holes = count(case when rd.SandShots > 0 then rd.Id end),
		@sand_shot_par = count(case when rd.SandShots > 0 and rd.Score - h.Par <= 0 then rd.Id end),
		@scrambling_pars = count(case when rd.GreenInRegulation = 0 and rd.Score - h.Par <= 0 then rd.Id end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.Tee t on t.Id = r.TeeId
	inner join dbo.[Hole] h on h.id = rd.HoleId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1 and t.CourseId = @course

	
	select 
		@holes as [TotalHoles],
		@gir as [GreensInRegulation],
		@holes - @gir as [MissedGIR],
		@zero_putts as [HoleOuts],
		case @holes when 0 then 0 else round((@zero_putts / convert(float, @holes)), 4) end as [HoleOutAvg],
		case @holes when 0 then 0 else round((@gir / convert(float, @holes)), 4) end as [GIRAvg],
		case @tee_fairway when 0 then 0 else round((@gir_fairway / convert(float, @tee_fairway)), 4) end as [GIRFairwayAvg],
		case @tee_right when 0 then 0 else round((@gir_right / convert(float, @tee_right)), 4) end as [GIRRightRoughAvg],
		case @tee_left when 0 then 0 else round((@gir_left / convert(float, @tee_left)), 4) end as [GIRLeftRoughAvg],
		case @tee_short when 0 then 0 else round((@gir_short / convert(float, @tee_short)), 4) end as [GIRShortAvg],
		case @tee_long when 0 then 0 else round((@gir_long / convert(float, @tee_long)), 4) end as [GIRLongAvg],
		@tee_fairway as [TeeShotFairway],
		@tee_right as [TeeShotRight],
		@tee_left as [TeeShotLeft],
		@tee_short as [TeeShotShort],
		@tee_long as [TeeShotLong],
		@gir_fairway as [GIRFromFairway],
		@gir_right as [GIRFromRight],
		@gir_left as [GIRFromLeft],
		@gir_short as [GIRFromShort],
		@gir_long as [GIRFromLong],
		@sand_shots as [TotalSandShots],
		@sand_shot_holes as [HolesWithSandShots],
		@sand_shot_par as [SandSaves],
		case @sand_shot_holes when 0 then 0 else round((@sand_shot_par / convert(float, @sand_shot_holes)), 4) end as [SandSaveAvg],
		@scrambling_pars as [MissedGIRScrambling],
		case (@holes - @gir) when 0 then 0 else round((@scrambling_pars / convert(float, (@holes - @gir))), 4) end as [ScramblingAvg]
		
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_AverageScoreFor18]    Script Date: 11/21/2019 5:01:06 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_AverageScoreFor18]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_AverageScoreFor18] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/11/2015
-- Description:	Average score for 18 hole rounds
-- Change Log:
--		09/18/2015	RR	Filtered out inactive rounds
--		06/24/2017	RR	Added course filter
-- =============================================
ALTER PROCEDURE [dbo].[Stats_AverageScoreFor18]
	@year int,
	@course int = null
AS
BEGIN
	SET NOCOUNT ON;

	if @course is null begin
		select round(avg(convert(float, agg.Score)), 2) as AverageScore
		from (
			select r.Id, count(rd.Id) as Holes, sum(rd.Score) as Score
			from dbo.[Round] r 
			inner join dbo.RoundDetail rd on rd.roundid = r.id
			where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
			group by r.Id
			having count(rd.Id) = 18
		) agg
	end else begin
		select round(avg(convert(float, agg.Score)), 2) as AverageScore
		from (
			select r.Id, count(rd.Id) as Holes, sum(rd.Score) as Score
			from dbo.[Round] r 
			inner join dbo.RoundDetail rd on rd.roundid = r.id
			inner join dbo.Tee t on t.Id = r.TeeId
			where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1 and t.CourseId = @course
			group by r.Id
			having count(rd.Id) = 18
		) agg
	end
	
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_CareerStats]    Script Date: 11/21/2019 5:01:07 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_CareerStats]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_CareerStats] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/26/2015
-- Description:	Stats for a career of a golfer
-- =============================================
ALTER PROCEDURE [dbo].[Stats_CareerStats]
	@GolferId int = 1
AS
BEGIN
	SET NOCOUNT ON;

    select
		datepart(yyyy, r.PlayedOn) as [Year],
		count(distinct r.Id) as [RoundsPlayed],
		count(distinct case when r.HolesPlayed = 9 then r.Id end) as [RoundsPlayed9],
		count(distinct case when r.HolesPlayed = 18 then r.Id end) as [RoundsPlayed18],
		count(rd.Id) as [HolesPlayed],
		round(avg(case when r.HolesPlayed = 9 then convert(float, r.Score) end), 2) as [ScoringAvg9],
		round(avg(case when r.HolesPlayed = 18 then convert(float, r.Score) end), 2) as [ScoringAvg18],
		case 
			when sum(rd.Score - h.Par) > 0 then '+' + convert(varchar, sum(rd.Score - h.Par))
			when sum(rd.Score - h.Par) < 0 then convert(varchar, sum(rd.Score - h.Par))
			else 'E' 
		end as RelatedToPar,
		sum(rd.Putts) as Putts,
		round(sum(rd.Putts) / convert(float, count(rd.Id)), 2) as PuttingAvg,
		round(count(case when rd.GreenInRegulation = 1 then rd.Id end) / convert(float, count(rd.Id)), 4) as GIR,
		round(count(case when h.Par != 3 and rd.FairwayHit = 1 then rd.Id end) / convert(float, count(case when h.Par != 3 then rd.Id end)), 4) as DrivingAccuracy,
		min(case when r.HolesPlayed = 9 then r.Score end) as [Low9HoleScore],
		min(case when r.HolesPlayed = 18 then r.Score end) as [Low18HoleScore],
		max(case when r.HolesPlayed = 9 then r.Score end) as [High9HoleScore],
		max(case when r.HolesPlayed = 18 then r.Score end) as [High18HoleScore],
		min(r.PlayedOn) as [EarliestRound],
		max(r.PlayedOn) as [LatestRound],
		count(distinct c.Id) as [TotalCourses]
    from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[Hole] h on h.Id = rd.HoleId
	inner join dbo.Tee t on t.Id = r.TeeId
	inner join dbo.Course c on c.Id = t.CourseId
	where 
		r.IsActive = 1
		and r.GolferId = @GolferId
	group by datepart(yyyy, r.PlayedOn)
	order by datepart(yyyy, r.PlayedOn) desc
	
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_CareerStatsByCourse]    Script Date: 11/21/2019 5:01:07 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_CareerStatsByCourse]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_CareerStatsByCourse] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 06/24/2017
-- Description:	Stats for a career of a golfer on a course
-- =============================================
ALTER PROCEDURE [dbo].[Stats_CareerStatsByCourse]
	@GolferId int = 1,
	@CourseId int
AS
BEGIN
	SET NOCOUNT ON;

    select
		datepart(yyyy, r.PlayedOn) as [Year],
		count(distinct r.Id) as [RoundsPlayed],
		count(distinct case when r.HolesPlayed = 9 then r.Id end) as [RoundsPlayed9],
		count(distinct case when r.HolesPlayed = 18 then r.Id end) as [RoundsPlayed18],
		count(rd.Id) as [HolesPlayed],
		round(avg(case when r.HolesPlayed = 9 then convert(float, r.Score) end), 2) as [ScoringAvg9],
		round(avg(case when r.HolesPlayed = 18 then convert(float, r.Score) end), 2) as [ScoringAvg18],
		case 
			when sum(rd.Score - h.Par) > 0 then '+' + convert(varchar, sum(rd.Score - h.Par))
			when sum(rd.Score - h.Par) < 0 then convert(varchar, sum(rd.Score - h.Par))
			else 'E' 
		end as RelatedToPar,
		sum(rd.Putts) as Putts,
		round(sum(rd.Putts) / convert(float, count(rd.Id)), 2) as PuttingAvg,
		round(count(case when rd.GreenInRegulation = 1 then rd.Id end) / convert(float, count(rd.Id)), 4) as GIR,
		round(count(case when h.Par != 3 and rd.FairwayHit = 1 then rd.Id end) / convert(float, count(case when h.Par != 3 then rd.Id end)), 4) as DrivingAccuracy,
		min(case when r.HolesPlayed = 9 then r.Score end) as [Low9HoleScore],
		min(case when r.HolesPlayed = 18 then r.Score end) as [Low18HoleScore],
		max(case when r.HolesPlayed = 9 then r.Score end) as [High9HoleScore],
		max(case when r.HolesPlayed = 18 then r.Score end) as [High18HoleScore],
		min(r.PlayedOn) as [EarliestRound],
		max(r.PlayedOn) as [LatestRound],
		count(distinct c.Id) as [TotalCourses]
    from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[Hole] h on h.Id = rd.HoleId
	inner join dbo.Tee t on t.Id = r.TeeId
	inner join dbo.Course c on c.Id = t.CourseId
	where 
		r.IsActive = 1
		and r.GolferId = @GolferId
		and t.CourseId = @CourseId
	group by datepart(yyyy, r.PlayedOn)
	order by datepart(yyyy, r.PlayedOn) desc
	
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_CourseStats]    Script Date: 11/21/2019 5:01:08 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_CourseStats]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_CourseStats] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 11/09/2016
-- Description:	Lists course stat summary
-- Change Log:
--		04/03/2017	RR	Updated to mimic career stats
-- =============================================
ALTER PROCEDURE [dbo].[Stats_CourseStats]
	@year int,
	@GolferId int = 1
AS
BEGIN
	SET NOCOUNT ON;

    select
		c.Id,
		c.Name as CourseName,
		t.Name as TeeName,

		count(distinct r.Id) as [RoundsPlayed],
		count(distinct case when r.HolesPlayed = 9 then r.Id end) as [RoundsPlayed9],
		count(distinct case when r.HolesPlayed = 18 then r.Id end) as [RoundsPlayed18],
		count(rd.Id) as [HolesPlayed],
		isnull(round(avg(case when r.HolesPlayed = 9 then convert(float, r.Score) end), 2), 0) as [ScoringAvg9],
		isnull(round(avg(case when r.HolesPlayed = 18 then convert(float, r.Score) end), 2), 0) as [ScoringAvg18],
		case 
			when sum(rd.Score - h.Par) > 0 then '+' + convert(varchar, sum(rd.Score - h.Par))
			when sum(rd.Score - h.Par) < 0 then convert(varchar, sum(rd.Score - h.Par))
			else 'E' 
		end as RelatedToPar,
		sum(rd.Putts) as Putts,
		round(sum(rd.Putts) / convert(float, count(rd.Id)), 2) as PuttingAvg,
		round(count(case when rd.GreenInRegulation = 1 then rd.Id end) / convert(float, count(rd.Id)), 4) as GIR,
		round(count(case when h.Par != 3 and rd.FairwayHit = 1 then rd.Id end) / convert(float, count(case when h.Par != 3 then rd.Id end)), 4) as DrivingAccuracy,
		isnull(min(case when r.HolesPlayed = 9 then r.Score end), 0) as [Low9HoleScore],
		isnull(min(case when r.HolesPlayed = 18 then r.Score end), 0) as [Low18HoleScore],
		isnull(max(case when r.HolesPlayed = 9 then r.Score end), 0) as [High9HoleScore],
		isnull(max(case when r.HolesPlayed = 18 then r.Score end), 0) as [High18HoleScore],
		min(r.PlayedOn) as [EarliestRound],
		max(r.PlayedOn) as [LatestRound],

		'N/A' as Par3ScoreToPar,
		'N/A' as Par4ScoreToPar,
		'N/A' as Par5ScoreToPar

		--case 
		--	when sum(case when h.Par = 3 then rd.Score else 0 end) / convert(float, count(case when h.Par = 3 then rd.Id end)) > 0 then '+' + convert(varchar, round(sum(case when h.Par = 3 then rd.Score else 0 end) / convert(float, count(case when h.Par = 3 then rd.Id end)), 2) - 3)
		--	when sum(case when h.Par = 3 then rd.Score else 0 end) / convert(float, count(case when h.Par = 3 then rd.Id end)) < 0 then convert(varchar, round(sum(case when h.Par = 3 then rd.Score else 0 end) / convert(float, count(case when h.Par = 3 then rd.Id end)), 2) - 3)
		--	else 'E' 
		--end as Par3ScoreToPar,
		--case 
		--	when sum(case when h.Par = 4 then rd.Score else 0 end) / convert(float, count(case when h.Par = 4 then rd.Id end)) > 0 then '+' + convert(varchar, round(sum(case when h.Par = 4 then rd.Score else 0 end) / convert(float, count(case when h.Par = 4 then rd.Id end)), 2) - 4)
		--	when sum(case when h.Par = 4 then rd.Score else 0 end) / convert(float, count(case when h.Par = 4 then rd.Id end)) < 0 then convert(varchar, round(sum(case when h.Par = 4 then rd.Score else 0 end) / convert(float, count(case when h.Par = 4 then rd.Id end)), 2) - 4)
		--	else 'E' 
		--end as Par4ScoreToPar,
		--case 
		--	when sum(case when h.Par = 5 then rd.Score else 0 end) / convert(float, count(case when h.Par = 5 then rd.Id end)) > 0 then '+' + convert(varchar, round(sum(case when h.Par = 5 then rd.Score else 0 end) / convert(float, count(case when h.Par = 5 then rd.Id end)), 2) - 3)
		--	when sum(case when h.Par = 5 then rd.Score else 0 end) / convert(float, count(case when h.Par = 5 then rd.Id end)) < 0 then convert(varchar, round(sum(case when h.Par = 5 then rd.Score else 0 end) / convert(float, count(case when h.Par = 5 then rd.Id end)), 2) - 5)
		--	else 'E' 
		--end as Par5ScoreToPar
	from [Round] r 
	inner join Tee t on t.Id = r.TeeId
	inner join Course c on c.Id = t.CourseId
	inner join Hole h on h.TeeId = t.Id
	inner join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
	where 
		datepart(yyyy, r.PlayedOn) = @year 
		and r.IsActive = 1
		and r.GolferId = @GolferId
	group by c.Id, c.Name, t.Name
	order by c.Name, t.Name
	
END

GO

/****** Object:  StoredProcedure [dbo].[Stats_DrivingStats]    Script Date: 11/21/2019 5:01:08 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_DrivingStats]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_DrivingStats] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/22/2015
-- Description:	Collection of driving stats for a season
-- =============================================
ALTER PROCEDURE [dbo].[Stats_DrivingStats]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    declare @holes int
    declare @driving_holes int
	declare @fairways int
	declare @tee_right int
	declare @tee_left int
	declare @tee_short int
	declare @tee_long int
	
	declare @par4_holes int
	declare @par4_holes_fairway int
	declare @par4_holes_left int
	declare @par4_holes_right int
	declare @par4_holes_short int
	declare @par4_holes_long int
	
	declare @par5_holes int
	declare @par5_holes_fairway int
	declare @par5_holes_left int
	declare @par5_holes_right int
	declare @par5_holes_short int
	declare @par5_holes_long int
	
	declare @par4_score_fairway int
	declare @par5_score_fairway int
	declare @par4_score_left int
	declare @par5_score_left int
	declare @par4_score_right int
	declare @par5_score_right int
	declare @par4_score_short int
	declare @par5_score_short int
	declare @par4_score_long int
	declare @par5_score_long int
	
	select 
		@holes = count(rd.Id),
		@driving_holes = sum(case h.Par when 3 then 0 else 1 end),
		@fairways = sum(case rd.FairwayHit when 1 then 1 else 0 end),
		@tee_right = sum(case rd.TeeShotRight when 1 then 1 else 0 end),
		@tee_left = sum(case rd.TeeShotLeft when 1 then 1 else 0 end),
		@tee_short = sum(case rd.TeeShotShort when 1 then 1 else 0 end),
		@tee_long = sum(case rd.TeeShotLong when 1 then 1 else 0 end),
		
		@par4_holes = sum(case when h.Par = 4 then 1 else 0 end),
		@par4_holes_fairway = sum(case when h.Par = 4 and rd.FairwayHit = 1 then 1 else 0 end),
		@par4_holes_left = sum(case when h.Par = 4 and rd.TeeShotLeft = 1 then 1 else 0 end),
		@par4_holes_right = sum(case when h.Par = 4 and rd.TeeShotRight = 1 then 1 else 0 end),
		@par4_holes_short = sum(case when h.Par = 4 and rd.TeeShotShort = 1 then 1 else 0 end),
		@par4_holes_long = sum(case when h.Par = 4 and rd.TeeShotLong = 1 then 1 else 0 end),
		@par5_holes = sum(case when h.Par = 5 then 1 else 0 end),
		@par5_holes_fairway = sum(case when h.Par = 5 and rd.FairwayHit = 1 then 1 else 0 end),
		@par5_holes_left = sum(case when h.Par = 5 and rd.TeeShotLeft = 1 then 1 else 0 end),
		@par5_holes_right = sum(case when h.Par = 5 and rd.TeeShotRight = 1 then 1 else 0 end),
		@par5_holes_short = sum(case when h.Par = 5 and rd.TeeShotShort = 1 then 1 else 0 end),
		@par5_holes_long = sum(case when h.Par = 5 and rd.TeeShotLong = 1 then 1 else 0 end),
		
		@par4_score_fairway = sum(case when rd.FairwayHit = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_fairway = sum(case when rd.FairwayHit = 1 and h.Par = 5 then rd.Score else 0 end),
		@par4_score_left = sum(case when rd.TeeShotLeft = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_left = sum(case when rd.TeeShotLeft = 1 and h.Par = 5 then rd.Score else 0 end),
		@par4_score_right = sum(case when rd.TeeShotRight = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_right = sum(case when rd.TeeShotRight = 1 and h.Par = 5 then rd.Score else 0 end),
		@par4_score_short = sum(case when rd.TeeShotShort = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_short = sum(case when rd.TeeShotShort = 1 and h.Par = 5 then rd.Score else 0 end),
		@par4_score_long = sum(case when rd.TeeShotLong = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_long = sum(case when rd.TeeShotLong = 1 and h.Par = 5 then rd.Score else 0 end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[Hole] h on h.id = rd.HoleId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1

	if @holes > 0 begin
		select 
			@holes as [TotalHoles],
			@driving_holes as [DrivingHoles],
			@fairways as [FairwaysHit],
			@tee_right as [RightRoughHit],
			@tee_left as [LeftRoughHit],
			@tee_short as [ShortTeeHit],
			@tee_long as [LongTeeHit],
			@par4_holes as [TotalPar4s],
			@par5_holes as [TotalPar5s],
			round((@fairways / convert(float, @driving_holes)), 4) as [FairwayHitAvg],
			round((@tee_right / convert(float, @driving_holes)), 4) as [RightRoughAvg],
			round((@tee_left / convert(float, @driving_holes)), 4) as [LeftRoughAvg],
			round((@tee_short / convert(float, @driving_holes)), 4) as [ShortTeeAvg],
			round((@tee_long / convert(float, @driving_holes)), 4) as [LongTeeAvg],
			case @par4_holes_fairway when 0 then 0 else round((@par4_score_fairway / convert(float, @par4_holes_fairway)), 2) end as [Par4FairwayHitAvgScore],
			case @par4_holes_left when 0 then 0 else round((@par4_score_left / convert(float, @par4_holes_left)), 2) end as [Par4LeftRoughAvgScore],
			case @par4_holes_right when 0 then 0 else round((@par4_score_right / convert(float, @par4_holes_right)), 2) end as [Par4RightRoughAvgScore],
			case @par4_holes_short when 0 then 0 else round((@par4_score_short / convert(float, @par4_holes_short)), 2) end as [Par4ShortTeeAvgScore],
			case @par4_holes_long when 0 then 0 else round((@par4_score_long / convert(float, @par4_holes_long)), 2) end as [Par4LongTeeAvgScore],
			case @par5_holes_fairway when 0 then 0 else round((@par5_score_fairway / convert(float, @par5_holes_fairway)), 2) end as [Par5FairwayHitAvgScore],
			case @par5_holes_left when 0 then 0 else round((@par5_score_left / convert(float, @par5_holes_left)), 2) end as [Par5LeftRoughAvgScore],
			case @par5_holes_right when 0 then 0 else round((@par5_score_right / convert(float, @par5_holes_right)), 2) end as [Par5RightRoughAvgScore],
			case @par5_holes_short when 0 then 0 else round((@par5_score_short / convert(float, @par5_holes_short)), 2) end as [Par5ShortTeeAvgScore],
			case @par5_holes_long when 0 then 0 else round((@par5_score_long / convert(float, @par5_holes_long)), 2) end as [Par5LongTeeAvgScore],
		
			((case @par4_holes_fairway when 0 then 4 else round((@par4_score_fairway / convert(float, @par4_holes_fairway)), 2) end - 4) + 
			(case @par5_holes_fairway when 0 then 5 else round((@par5_score_fairway / convert(float, @par5_holes_fairway)), 2) end - 5)) / 2.0 as [FairwayHitAvgScoreRelatedToPar],
		
			((case @par4_holes_left when 0 then 4 else round((@par4_score_left / convert(float, @par4_holes_left)), 2) end - 4) + 
			(case @par5_holes_left when 0 then 5 else round((@par5_score_left / convert(float, @par5_holes_left)), 2) end - 5)) / 2.0 as [LeftRoughAvgScoreRelatedToPar],
		
			((case @par4_holes_right when 0 then 4 else round((@par4_score_right / convert(float, @par4_holes_right)), 2) end - 4) + 
			(case @par5_holes_right when 0 then 5 else round((@par5_score_right / convert(float, @par5_holes_right)), 2) end - 5)) / 2.0 as [RightRoughAvgScoreRelatedToPar],
		
			((case @par4_holes_short when 0 then 4 else round((@par4_score_short / convert(float, @par4_holes_short)), 2) end - 4) + 
			(case @par5_holes_short when 0 then 5 else round((@par5_score_short / convert(float, @par5_holes_short)), 2) end - 5)) / 2.0 as [ShortTeeAvgScoreRelatedToPar],
		
			((case @par4_holes_long when 0 then 4 else round((@par4_score_long / convert(float, @par4_holes_long)), 2) end - 4) + 
			(case @par5_holes_long when 0 then 5 else round((@par5_score_long / convert(float, @par5_holes_long)), 2) end - 5)) / 2.0 as [LongTeeAvgScoreRelatedToPar]
	end
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_DrivingStatsByCourse]    Script Date: 11/21/2019 5:01:08 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_DrivingStatsByCourse]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_DrivingStatsByCourse] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/22/2015
-- Description:	Collection of driving stats for a season and course
-- =============================================
ALTER PROCEDURE [dbo].[Stats_DrivingStatsByCourse]
	@year int,
	@course int
AS
BEGIN
	SET NOCOUNT ON;

    declare @holes int
    declare @driving_holes int
	declare @fairways int
	declare @tee_right int
	declare @tee_left int
	declare @tee_short int
	declare @tee_long int
	
	declare @par4_holes int
	declare @par4_holes_fairway int
	declare @par4_holes_left int
	declare @par4_holes_right int
	declare @par4_holes_short int
	declare @par4_holes_long int
	
	declare @par5_holes int
	declare @par5_holes_fairway int
	declare @par5_holes_left int
	declare @par5_holes_right int
	declare @par5_holes_short int
	declare @par5_holes_long int
	
	declare @par4_score_fairway int
	declare @par5_score_fairway int
	declare @par4_score_left int
	declare @par5_score_left int
	declare @par4_score_right int
	declare @par5_score_right int
	declare @par4_score_short int
	declare @par5_score_short int
	declare @par4_score_long int
	declare @par5_score_long int
	
	select 
		@holes = count(rd.Id),
		@driving_holes = sum(case h.Par when 3 then 0 else 1 end),
		@fairways = sum(case rd.FairwayHit when 1 then 1 else 0 end),
		@tee_right = sum(case rd.TeeShotRight when 1 then 1 else 0 end),
		@tee_left = sum(case rd.TeeShotLeft when 1 then 1 else 0 end),
		@tee_short = sum(case rd.TeeShotShort when 1 then 1 else 0 end),
		@tee_long = sum(case rd.TeeShotLong when 1 then 1 else 0 end),
		
		@par4_holes = sum(case when h.Par = 4 then 1 else 0 end),
		@par4_holes_fairway = sum(case when h.Par = 4 and rd.FairwayHit = 1 then 1 else 0 end),
		@par4_holes_left = sum(case when h.Par = 4 and rd.TeeShotLeft = 1 then 1 else 0 end),
		@par4_holes_right = sum(case when h.Par = 4 and rd.TeeShotRight = 1 then 1 else 0 end),
		@par4_holes_short = sum(case when h.Par = 4 and rd.TeeShotShort = 1 then 1 else 0 end),
		@par4_holes_long = sum(case when h.Par = 4 and rd.TeeShotLong = 1 then 1 else 0 end),
		@par5_holes = sum(case when h.Par = 5 then 1 else 0 end),
		@par5_holes_fairway = sum(case when h.Par = 5 and rd.FairwayHit = 1 then 1 else 0 end),
		@par5_holes_left = sum(case when h.Par = 5 and rd.TeeShotLeft = 1 then 1 else 0 end),
		@par5_holes_right = sum(case when h.Par = 5 and rd.TeeShotRight = 1 then 1 else 0 end),
		@par5_holes_short = sum(case when h.Par = 5 and rd.TeeShotShort = 1 then 1 else 0 end),
		@par5_holes_long = sum(case when h.Par = 5 and rd.TeeShotLong = 1 then 1 else 0 end),
		
		@par4_score_fairway = sum(case when rd.FairwayHit = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_fairway = sum(case when rd.FairwayHit = 1 and h.Par = 5 then rd.Score else 0 end),
		@par4_score_left = sum(case when rd.TeeShotLeft = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_left = sum(case when rd.TeeShotLeft = 1 and h.Par = 5 then rd.Score else 0 end),
		@par4_score_right = sum(case when rd.TeeShotRight = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_right = sum(case when rd.TeeShotRight = 1 and h.Par = 5 then rd.Score else 0 end),
		@par4_score_short = sum(case when rd.TeeShotShort = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_short = sum(case when rd.TeeShotShort = 1 and h.Par = 5 then rd.Score else 0 end),
		@par4_score_long = sum(case when rd.TeeShotLong = 1 and h.Par = 4 then rd.Score else 0 end),
		@par5_score_long = sum(case when rd.TeeShotLong = 1 and h.Par = 5 then rd.Score else 0 end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.Tee t on t.Id = r.TeeId
	inner join dbo.[Hole] h on h.id = rd.HoleId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1 and t.CourseId = @course

	
	select 
		@holes as [TotalHoles],
		@driving_holes as [DrivingHoles],
		@fairways as [FairwaysHit],
		@tee_right as [RightRoughHit],
		@tee_left as [LeftRoughHit],
		@tee_short as [ShortTeeHit],
		@tee_long as [LongTeeHit],
		@par4_holes as [TotalPar4s],
		@par5_holes as [TotalPar5s],
		round((@fairways / convert(float, @driving_holes)), 4) as [FairwayHitAvg],
		round((@tee_right / convert(float, @driving_holes)), 4) as [RightRoughAvg],
		round((@tee_left / convert(float, @driving_holes)), 4) as [LeftRoughAvg],
		round((@tee_short / convert(float, @driving_holes)), 4) as [ShortTeeAvg],
		round((@tee_long / convert(float, @driving_holes)), 4) as [LongTeeAvg],
		case @par4_holes_fairway when 0 then 0 else round((@par4_score_fairway / convert(float, @par4_holes_fairway)), 2) end as [Par4FairwayHitAvgScore],
		case @par4_holes_left when 0 then 0 else round((@par4_score_left / convert(float, @par4_holes_left)), 2) end as [Par4LeftRoughAvgScore],
		case @par4_holes_right when 0 then 0 else round((@par4_score_right / convert(float, @par4_holes_right)), 2) end as [Par4RightRoughAvgScore],
		case @par4_holes_short when 0 then 0 else round((@par4_score_short / convert(float, @par4_holes_short)), 2) end as [Par4ShortTeeAvgScore],
		case @par4_holes_long when 0 then 0 else round((@par4_score_long / convert(float, @par4_holes_long)), 2) end as [Par4LongTeeAvgScore],
		case @par5_holes_fairway when 0 then 0 else round((@par5_score_fairway / convert(float, @par5_holes_fairway)), 2) end as [Par5FairwayHitAvgScore],
		case @par5_holes_left when 0 then 0 else round((@par5_score_left / convert(float, @par5_holes_left)), 2) end as [Par5LeftRoughAvgScore],
		case @par5_holes_right when 0 then 0 else round((@par5_score_right / convert(float, @par5_holes_right)), 2) end as [Par5RightRoughAvgScore],
		case @par5_holes_short when 0 then 0 else round((@par5_score_short / convert(float, @par5_holes_short)), 2) end as [Par5ShortTeeAvgScore],
		case @par5_holes_long when 0 then 0 else round((@par5_score_long / convert(float, @par5_holes_long)), 2) end as [Par5LongTeeAvgScore],
		
		((case @par4_holes_fairway when 0 then 4 else round((@par4_score_fairway / convert(float, @par4_holes_fairway)), 2) end - 4) + 
		(case @par5_holes_fairway when 0 then 5 else round((@par5_score_fairway / convert(float, @par5_holes_fairway)), 2) end - 5)) / 2.0 as [FairwayHitAvgScoreRelatedToPar],
		
		((case @par4_holes_left when 0 then 4 else round((@par4_score_left / convert(float, @par4_holes_left)), 2) end - 4) + 
		(case @par5_holes_left when 0 then 5 else round((@par5_score_left / convert(float, @par5_holes_left)), 2) end - 5)) / 2.0 as [LeftRoughAvgScoreRelatedToPar],
		
		((case @par4_holes_right when 0 then 4 else round((@par4_score_right / convert(float, @par4_holes_right)), 2) end - 4) + 
		(case @par5_holes_right when 0 then 5 else round((@par5_score_right / convert(float, @par5_holes_right)), 2) end - 5)) / 2.0 as [RightRoughAvgScoreRelatedToPar],
		
		((case @par4_holes_short when 0 then 4 else round((@par4_score_short / convert(float, @par4_holes_short)), 2) end - 4) + 
		(case @par5_holes_short when 0 then 5 else round((@par5_score_short / convert(float, @par5_holes_short)), 2) end - 5)) / 2.0 as [ShortTeeAvgScoreRelatedToPar],
		
		((case @par4_holes_long when 0 then 4 else round((@par4_score_long / convert(float, @par4_holes_long)), 2) end - 4) + 
		(case @par5_holes_long when 0 then 5 else round((@par5_score_long / convert(float, @par5_holes_long)), 2) end - 5)) / 2.0 as [LongTeeAvgScoreRelatedToPar]
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_HandicapHistory]    Script Date: 11/21/2019 5:01:09 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_HandicapHistory]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_HandicapHistory] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/28/2015
-- Description:	Gets the handicap history of a golfer
-- =============================================
ALTER PROCEDURE [dbo].[Stats_HandicapHistory]
	@GolferId int = 1
AS
BEGIN
	SET NOCOUNT ON;

    select
      HandicapDate,
      HandicapIndex
	from dbo.HandicapHistory
	where
		GolferId = @GolferId
	order by HandicapDate desc
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_HolesPlayed]    Script Date: 11/21/2019 5:01:09 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_HolesPlayed]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_HolesPlayed] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/11/2015
-- Description:	Holes played-- Change Log:
--		09/18/2015	RR	Filtered out inactive rounds
--		06/24/2017	RR	Added course filter
-- =============================================
ALTER PROCEDURE [dbo].[Stats_HolesPlayed]
	@year int,
	@course int = null
AS
BEGIN
	SET NOCOUNT ON;

	if @course is null begin
		select count(rd.Id) as HolesPlayed
		from dbo.[Round] r 
		inner join dbo.RoundDetail rd on rd.roundid = r.id
		where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
	end else begin
		select count(rd.Id) as HolesPlayed
		from dbo.[Round] r 
		inner join dbo.RoundDetail rd on rd.roundid = r.id
		inner join dbo.Tee t on t.Id = r.TeeId
		where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1 and t.CourseId = @course
	end
	
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_Putting]    Script Date: 11/21/2019 5:01:09 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_Putting]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_Putting] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/17/2015
-- Description:	Putting stats summary
-- =============================================
ALTER PROCEDURE [dbo].[Stats_Putting]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

	select
		round(avg(convert(float, rd.Putts)), 2) as PuttsPerHole,
		round(avg(case when rd.GreenInRegulation = 1 then convert(float, rd.Putts) end), 2) as PuttsPerHoleGIR
	from dbo.[Round] r 
	inner join dbo.RoundDetail rd on rd.RoundId = r.Id
	inner join dbo.Hole h on h.Id = rd.HoleId
	where datepart(yyyy, r.PlayedOn) = @year
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_PuttingStats]    Script Date: 11/21/2019 5:01:10 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_PuttingStats]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_PuttingStats] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/22/2015
-- Description:	Collection of putting stats for a season
-- Change Log: 
--		02/22/2016	RR	Fixed divide by zero errors
-- =============================================
ALTER PROCEDURE [dbo].[Stats_PuttingStats]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    declare @rounds int
    declare @rounds9 int
    declare @rounds18 int
    declare @holes int
    declare @holes_gir int
    
    declare @putts int
    declare @putts_gir int
    declare @putts1 int
    declare @putts2 int
    declare @putts3 int
    declare @putts_round9 int
    declare @putts_round18 int
    
    declare @birdies int
    
	select 
		@rounds = count(distinct r.Id),
		@rounds9 = count(distinct case when r.HolesPlayed = 9 then r.Id end),
		@rounds18 = count(distinct case when r.HolesPlayed = 18 then r.Id end),
		@holes = count(rd.Id),
		@holes_gir = sum(case when rd.GreenInRegulation = 1 then 1 else 0 end),
		
		@putts = sum(rd.Putts),
		@putts_gir = sum(case when rd.GreenInRegulation = 1 then rd.Putts else 0 end),
		@putts1 = count(case when rd.Putts = 1 then rd.Id end),
		@putts2 = count(case when rd.Putts = 2 then rd.Id end),
		@putts3 = count(case when rd.Putts >= 3 then rd.Id end),
		@putts_round9 = sum(case when r.HolesPlayed = 9 then rd.Putts else 0 end),
		@putts_round18 = sum(case when r.HolesPlayed = 18 then rd.Putts else 0 end),
		
		@birdies = count(case when h.Par - rd.Score >= 1 then rd.Id end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[Hole] h on h.id = rd.HoleId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1

	
	select 
		isnull(@rounds,0) as [TotalRounds],
		isnull(@rounds9,0) as [9HoleRounds],
		isnull(@rounds18,0) as [18HoleRounds],
		isnull(@holes,0) as [TotalHoles],
		isnull(@holes_gir,0) as [TotalGIRHoles],
		isnull(@putts,0) as [TotalPutts],
		isnull(@putts_gir,0) as [TotalPuttsGIR],
		isnull(@putts1,0) as [OnePutts],
		isnull(@putts2,0) as [TwoPutts],
		isnull(@putts3,0) as [ThreePuttsPlus],
		isnull(@birdies,0) as [TotalBirdiesPlus],
		case isnull(@holes_gir,0) when 0 then 0 else round(@putts_gir / convert(float, @holes_gir), 2) end as [AvgPuttsGIR],
		case isnull(@holes,0) when 0 then 0 else round(@putts / convert(float, @holes), 2) end as [AvgPutts],
		case isnull(@holes,0) when 0 then 0 else round(@putts1 / convert(float, @holes), 4) end as [OnePuttAvg],
		case isnull(@holes,0) when 0 then 0 else round(@putts3 / convert(float, @holes), 4) end as [ThreePuttAvg],
		case isnull(@rounds9,0) when 0 then 0 else round(@putts_round9 / convert(float, @rounds9), 2) end as [AvgPutts9Holes],
		case isnull(@rounds18,0) when 0 then 0 else round(@putts_round18 / convert(float, @rounds18), 2) end as [AvgPutts18Holes],
		case isnull(@holes_gir,0) when 0 then 0 else round(@birdies / convert(float, @holes_gir), 4) end as [BirdieConversion]
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_PuttingStatsByCourse]    Script Date: 11/21/2019 5:01:10 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_PuttingStatsByCourse]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_PuttingStatsByCourse] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 06/24/2017
-- Description:	Collection of putting stats for a season and course
-- =============================================
ALTER PROCEDURE [dbo].[Stats_PuttingStatsByCourse]
	@year int,
	@course int
AS
BEGIN
	SET NOCOUNT ON;

    declare @rounds int
    declare @rounds9 int
    declare @rounds18 int
    declare @holes int
    declare @holes_gir int
    
    declare @putts int
    declare @putts_gir int
    declare @putts1 int
    declare @putts2 int
    declare @putts3 int
    declare @putts_round9 int
    declare @putts_round18 int
    
    declare @birdies int
    
	select 
		@rounds = count(distinct r.Id),
		@rounds9 = count(distinct case when r.HolesPlayed = 9 then r.Id end),
		@rounds18 = count(distinct case when r.HolesPlayed = 18 then r.Id end),
		@holes = count(rd.Id),
		@holes_gir = sum(case when rd.GreenInRegulation = 1 then 1 else 0 end),
		
		@putts = sum(rd.Putts),
		@putts_gir = sum(case when rd.GreenInRegulation = 1 then rd.Putts else 0 end),
		@putts1 = count(case when rd.Putts = 1 then rd.Id end),
		@putts2 = count(case when rd.Putts = 2 then rd.Id end),
		@putts3 = count(case when rd.Putts >= 3 then rd.Id end),
		@putts_round9 = sum(case when r.HolesPlayed = 9 then rd.Putts else 0 end),
		@putts_round18 = sum(case when r.HolesPlayed = 18 then rd.Putts else 0 end),
		
		@birdies = count(case when h.Par - rd.Score >= 1 then rd.Id end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.Tee t on t.Id = r.TeeId
	inner join dbo.[Hole] h on h.id = rd.HoleId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1 and t.CourseId = @course

	
	select 
		isnull(@rounds,0) as [TotalRounds],
		isnull(@rounds9,0) as [9HoleRounds],
		isnull(@rounds18,0) as [18HoleRounds],
		isnull(@holes,0) as [TotalHoles],
		isnull(@holes_gir,0) as [TotalGIRHoles],
		isnull(@putts,0) as [TotalPutts],
		isnull(@putts_gir,0) as [TotalPuttsGIR],
		isnull(@putts1,0) as [OnePutts],
		isnull(@putts2,0) as [TwoPutts],
		isnull(@putts3,0) as [ThreePuttsPlus],
		isnull(@birdies,0) as [TotalBirdiesPlus],
		case isnull(@holes_gir,0) when 0 then 0 else round(@putts_gir / convert(float, @holes_gir), 2) end as [AvgPuttsGIR],
		case isnull(@holes,0) when 0 then 0 else round(@putts / convert(float, @holes), 2) end as [AvgPutts],
		case isnull(@holes,0) when 0 then 0 else round(@putts1 / convert(float, @holes), 4) end as [OnePuttAvg],
		case isnull(@holes,0) when 0 then 0 else round(@putts3 / convert(float, @holes), 4) end as [ThreePuttAvg],
		case isnull(@rounds9,0) when 0 then 0 else round(@putts_round9 / convert(float, @rounds9), 2) end as [AvgPutts9Holes],
		case isnull(@rounds18,0) when 0 then 0 else round(@putts_round18 / convert(float, @rounds18), 2) end as [AvgPutts18Holes],
		case isnull(@holes_gir,0) when 0 then 0 else round(@birdies / convert(float, @holes_gir), 4) end as [BirdieConversion]
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_RelationToPar]    Script Date: 11/21/2019 5:01:11 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_RelationToPar]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_RelationToPar] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/11/2015
-- Description:	Relation to par
-- Change Log:
--		09/18/2015	RR	Filtered out inactive rounds
--		06/24/2017	RR	Added course filter
-- =============================================
ALTER PROCEDURE [dbo].[Stats_RelationToPar]
	@year int,
	@course int = null
AS
BEGIN
	SET NOCOUNT ON;

	if @course is null begin
		select sum(rd.Score) - sum(h.Par) as RelationToPar
		from dbo.[Round] r 
		inner join dbo.RoundDetail rd on rd.RoundId = r.Id
		inner join dbo.Hole h on h.Id = rd.HoleId
		where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
	end else begin
		select sum(rd.Score) - sum(h.Par) as RelationToPar
		from dbo.[Round] r 
		inner join dbo.RoundDetail rd on rd.RoundId = r.Id
		inner join dbo.Tee t on t.Id = r.TeeId
		inner join dbo.Hole h on h.Id = rd.HoleId
		where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1 and t.CourseId = @course
	end 
	
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_ScoresByType]    Script Date: 11/21/2019 5:01:11 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_ScoresByType]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_ScoresByType] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 09/11/2015
-- Description:	Count of score types
-- Change Log:
--		09/17/2015	RR	Added holes played
--		09/18/2015	RR	Filtered out inactive rounds
-- =============================================
ALTER PROCEDURE [dbo].[Stats_ScoresByType]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    select
		count(case when h.Par - rd.Score >= 2 then rd.Id end) as Eagles,
		count(case when h.Par - rd.Score = 1 then rd.Id end) as Birdies,
		count(case when h.Par - rd.Score = 0 then rd.Id end) as Pars,
		count(case when h.Par - rd.Score = -1 then rd.Id end) as Bogies,
		count(case when h.Par - rd.Score <= -2 then rd.Id end) as DoubleBogies,
		count(rd.Id) as HolesPlayed
	from dbo.[Round] r
	inner join dbo.RoundDetail rd on rd.RoundId = r.Id
	inner join dbo.Hole h on h.Id = rd.HoleId
	inner join dbo.Tee t on t.id = h.TeeId
	inner join dbo.Course c on c.Id = t.CourseId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_ScoringStats]    Script Date: 11/21/2019 5:01:11 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_ScoringStats]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_ScoringStats] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 10/26/2015
-- Description:	Collection of scoring stats for a season
-- Change Log: 
--		02/22/2016	RR	Fixed divide by zero errors
-- =============================================
ALTER PROCEDURE [dbo].[Stats_ScoringStats]
	@year int
AS
BEGIN
	SET NOCOUNT ON;

    declare @holes int
    declare @holes9 int
    declare @holes18 int
    declare @holes_par3 int
    declare @holes_par4 int
    declare @holes_par5 int
    declare @rounds9 int
    declare @rounds18 int
    
    declare @low_round9 int
    declare @high_round9 int
    declare @low_round9_par int
    declare @high_round9_par int
    
    declare @low_round18 int
    declare @high_round18 int
    declare @low_round18_par int
    declare @high_round18_par int
    
    declare @birdies int
    declare @eagles int
    
    declare @score9 int
    declare @score18 int
    declare @score_par3 int
    declare @score_par4 int
    declare @score_par5 int
    
	
	select 
		@holes = count(r.Id),
		@holes_par3 = count(case h.Par when 3 then rd.Id end),
		@holes_par4 = count(case h.Par when 4 then rd.Id end),
		@holes_par5 = count(case h.Par when 5 then rd.Id end),
		@rounds9 = count(distinct case r.HolesPlayed when 9 then r.Id end),
		@rounds18 = count(distinct case r.HolesPlayed when 18 then r.Id end),
		@low_round9 = min(case r.HolesPlayed when 9 then r.Score end),
		@high_round9 = max(case r.HolesPlayed when 9 then r.Score end),
		@low_round18 = min(case r.HolesPlayed when 18 then r.Score end),
		@high_round18 = max(case r.HolesPlayed when 18 then r.Score end),
		@birdies = count(case when rd.Score - h.Par = -1 then rd.Id end),
		@eagles = count(case when rd.Score - h.Par = -2 then rd.Id end),
		@score9 = sum(case r.HolesPlayed when 9 then rd.Score else 0 end),
		@score18 = sum(case r.HolesPlayed when 18 then rd.Score else 0 end),
		@score_par3 = sum(case h.Par when 3 then rd.Score else 0 end),
		@score_par4 = sum(case h.Par when 4 then rd.Score else 0 end),
		@score_par5 = sum(case h.Par when 5 then rd.Score else 0 end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[Hole] h on h.id = rd.HoleId
	inner join dbo.Tee t on t.Id = r.TeeId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1

	
	select 
		isnull(@holes,0) as [TotalHoles],
		isnull(@holes_par3,0) as [TotalPar3Holes],
		isnull(@holes_par4,0) as [TotalPar4Holes],
		isnull(@holes_par5,0) as [TotalPar5Holes],
		isnull(@rounds9,0) as [Total9HoleRounds],
		isnull(@rounds18,0) as [Total18HoleRounds],
		isnull(@birdies,0) as [Birdies],
		isnull(@eagles,0) as [Eagles],
		isnull(@birdies,0) + isnull(@eagles,0) as [BirdiesAndEagles],
		isnull(@score_par3,0) as [TotalPar3Score],
		isnull(@score_par4,0) as [TotalPar4Score],
		isnull(@score_par5,0) as [TotalPar5Score],
		isnull(@low_round9,0) as [Low9HoleRound],
		isnull(@high_round9,0) as [High9HoleRound],
		isnull(@low_round18,0) as [Low18HoleRound],
		isnull(@high_round18,0) as [High18HoleRound],
		case isnull(@rounds9,0) when 0 then 0 else round((@score9 / convert(float, @rounds9)), 2) end as [Avg9HoleScore],
		case isnull(@rounds18,0) when 0 then 0 else round((@score18 / convert(float, @rounds18)), 2) end as [Avg18HoleScore],
		case isnull(@holes,0) when 0 then 0 else round(((@birdies + @eagles) / convert(float, @holes)), 4) end as [ParBreakers],
		case isnull(@holes,0) when 0 then 0 else round(((@birdies + @eagles) / convert(float, @holes)), 4) end as [BirdieOrBetterAvg],
		case isnull(@holes_par3,0) when 0 then 0 else round((@score_par3 / convert(float, @holes_par3)), 2) end as [Par3Avg],
		case isnull(@holes_par4,0) when 0 then 0 else round((@score_par4 / convert(float, @holes_par4)), 2) end as [Par4Avg],
		case isnull(@holes_par5,0) when 0 then 0 else round((@score_par5 / convert(float, @holes_par5)), 2) end as [Par5Avg]
		
END
GO

/****** Object:  StoredProcedure [dbo].[Stats_ScoringStatsByCourse]    Script Date: 11/21/2019 5:01:12 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Stats_ScoringStatsByCourse]') AND type in (N'P', N'PC'))
BEGIN
EXEC dbo.sp_executesql @statement = N'CREATE PROCEDURE [dbo].[Stats_ScoringStatsByCourse] AS' 
END
GO


-- =============================================
-- Author:		RR
-- Create date: 06/24/2017
-- Description:	Collection of scoring stats for a season and course
-- =============================================
ALTER PROCEDURE [dbo].[Stats_ScoringStatsByCourse]
	@year int,
	@course int
AS
BEGIN
	SET NOCOUNT ON;

    declare @holes int
    declare @holes9 int
    declare @holes18 int
    declare @holes_par3 int
    declare @holes_par4 int
    declare @holes_par5 int
    declare @rounds9 int
    declare @rounds18 int
    
    declare @low_round9 int
    declare @high_round9 int
    declare @low_round9_par int
    declare @high_round9_par int
    
    declare @low_round18 int
    declare @high_round18 int
    declare @low_round18_par int
    declare @high_round18_par int
    
    declare @birdies int
    declare @eagles int
    
    declare @score9 int
    declare @score18 int
    declare @score_par3 int
    declare @score_par4 int
    declare @score_par5 int
    
	
	select 
		@holes = count(r.Id),
		@holes_par3 = count(case h.Par when 3 then rd.Id end),
		@holes_par4 = count(case h.Par when 4 then rd.Id end),
		@holes_par5 = count(case h.Par when 5 then rd.Id end),
		@rounds9 = count(distinct case r.HolesPlayed when 9 then r.Id end),
		@rounds18 = count(distinct case r.HolesPlayed when 18 then r.Id end),
		@low_round9 = min(case r.HolesPlayed when 9 then r.Score end),
		@high_round9 = max(case r.HolesPlayed when 9 then r.Score end),
		@low_round18 = min(case r.HolesPlayed when 18 then r.Score end),
		@high_round18 = max(case r.HolesPlayed when 18 then r.Score end),
		@birdies = count(case when rd.Score - h.Par = -1 then rd.Id end),
		@eagles = count(case when rd.Score - h.Par = -2 then rd.Id end),
		@score9 = sum(case r.HolesPlayed when 9 then rd.Score else 0 end),
		@score18 = sum(case r.HolesPlayed when 18 then rd.Score else 0 end),
		@score_par3 = sum(case h.Par when 3 then rd.Score else 0 end),
		@score_par4 = sum(case h.Par when 4 then rd.Score else 0 end),
		@score_par5 = sum(case h.Par when 5 then rd.Score else 0 end)
	from dbo.[RoundDetail] rd 
	inner join dbo.[Round] r on r.Id = rd.RoundId
	inner join dbo.[Hole] h on h.id = rd.HoleId
	inner join dbo.Tee t on t.Id = r.TeeId
	where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1 and t.CourseId = @course

	
	select 
		isnull(@holes,0) as [TotalHoles],
		isnull(@holes_par3,0) as [TotalPar3Holes],
		isnull(@holes_par4,0) as [TotalPar4Holes],
		isnull(@holes_par5,0) as [TotalPar5Holes],
		isnull(@rounds9,0) as [Total9HoleRounds],
		isnull(@rounds18,0) as [Total18HoleRounds],
		isnull(@birdies,0) as [Birdies],
		isnull(@eagles,0) as [Eagles],
		isnull(@birdies,0) + isnull(@eagles,0) as [BirdiesAndEagles],
		isnull(@score_par3,0) as [TotalPar3Score],
		isnull(@score_par4,0) as [TotalPar4Score],
		isnull(@score_par5,0) as [TotalPar5Score],
		isnull(@low_round9,0) as [Low9HoleRound],
		isnull(@high_round9,0) as [High9HoleRound],
		isnull(@low_round18,0) as [Low18HoleRound],
		isnull(@high_round18,0) as [High18HoleRound],
		case isnull(@rounds9,0) when 0 then 0 else round((@score9 / convert(float, @rounds9)), 2) end as [Avg9HoleScore],
		case isnull(@rounds18,0) when 0 then 0 else round((@score18 / convert(float, @rounds18)), 2) end as [Avg18HoleScore],
		case isnull(@holes,0) when 0 then 0 else round(((@birdies + @eagles) / convert(float, @holes)), 4) end as [ParBreakers],
		case isnull(@holes,0) when 0 then 0 else round(((@birdies + @eagles) / convert(float, @holes)), 4) end as [BirdieOrBetterAvg],
		case isnull(@holes_par3,0) when 0 then 0 else round((@score_par3 / convert(float, @holes_par3)), 2) end as [Par3Avg],
		case isnull(@holes_par4,0) when 0 then 0 else round((@score_par4 / convert(float, @holes_par4)), 2) end as [Par4Avg],
		case isnull(@holes_par5,0) when 0 then 0 else round((@score_par5 / convert(float, @holes_par5)), 2) end as [Par5Avg]
		
END
GO


