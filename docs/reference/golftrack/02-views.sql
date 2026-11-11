

/****** Object:  View [dbo].[r_driving_accuracy_history]    Script Date: 11/21/2019 4:59:02 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.views WHERE object_id = OBJECT_ID(N'[dbo].[r_driving_accuracy_history]'))
EXEC dbo.sp_executesql @statement = N'CREATE VIEW [dbo].[r_driving_accuracy_history] AS
	select
		r.Id,
		convert(smalldatetime, r.PlayedOn) as PlayedOn,
		round(count(case when rd.FairwayHit = 1 then h.Id end) / convert(float,count(h.Id)), 4)*100 as FairwayHitPercent
	from dbo.[Round] r 
	inner join dbo.RoundDetail rd on rd.RoundId = r.Id
	inner join dbo.Hole h on h.Id = rd.HoleId
	where
		r.GolferId = 1
		and h.Par in (4,5)
	group by r.Id, convert(smalldatetime, r.PlayedOn)
' 
GO

/****** Object:  View [dbo].[r_driving_gir_history]    Script Date: 11/21/2019 4:59:02 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.views WHERE object_id = OBJECT_ID(N'[dbo].[r_driving_gir_history]'))
EXEC dbo.sp_executesql @statement = N'
CREATE VIEW [dbo].[r_driving_gir_history] AS
	select
		r.Id,
		convert(smalldatetime, r.PlayedOn) as PlayedOn,
		round(count(case when rd.FairwayHit = 1 then h.Id end) / convert(float,count(h.Id)), 4)*100 as FairwayHitPercent,
		round(count(case when rd.GreenInRegulation = 1 then h.Id end) / convert(float,count(h.Id)), 4)*100 as GIRPercent
	from dbo.[Round] r 
	inner join dbo.RoundDetail rd on rd.RoundId = r.Id
	inner join dbo.Hole h on h.Id = rd.HoleId
	where
		r.GolferId = 1
		and h.Par in (4,5)
	group by r.Id, convert(smalldatetime, r.PlayedOn)
' 
GO

/****** Object:  View [dbo].[r_handicap_history]    Script Date: 11/21/2019 4:59:02 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.views WHERE object_id = OBJECT_ID(N'[dbo].[r_handicap_history]'))
EXEC dbo.sp_executesql @statement = N'CREATE VIEW [dbo].[r_handicap_history] AS
	select
      HandicapDate,
      HandicapIndex
	from dbo.HandicapHistory
	where
		GolferId = 1
' 
GO


