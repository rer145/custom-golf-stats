namespace CustomGolfStats.Domain;

public enum Lie { Tee, Fairway, Rough, FairwaySand, GreensideSand, Recovery, Green, Holed, PenaltyArea, OutOfBounds }

public enum Club
{
    Driver, Wood3, Wood4, Wood5, Wood7, Hybrid2, Hybrid3, Hybrid4, Hybrid5, Hybrid6,
    Iron2, Iron3, Iron4, Iron5, Iron6, Iron7, Iron8, Iron9,
    PitchingWedge, GapWedge, SandWedge, LobWedge, Putter
}

public enum ShotType { Tee, Approach, Layup, Chip, Pitch, Bunker, Recovery, Putt }

public enum Direction { OnTarget, Left, Right, Short, Long, ShortLeft, ShortRight, LongLeft, LongRight }

public enum Shape { Straight, Draw, Fade, Hook, Slice, Push, Pull, Topped, Fat }

public enum EntryMode { Quick, ShotByShot }

public enum Role { User, Admin }

public enum Tier { Free, Paid }

public enum Feature { ShotByShotEntry }

public enum RoundLength { Nine, Eighteen }
