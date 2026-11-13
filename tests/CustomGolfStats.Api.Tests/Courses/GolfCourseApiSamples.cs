namespace CustomGolfStats.Api.Tests.Courses;

/// <summary>
/// GolfCourseAPI responses in the provider's v1 shape (field names, nesting, number formats and the extra fields we
/// ignore, such as bogey ratings and totals). The course itself is fictional.
/// </summary>
public static class GolfCourseApiSamples
{
    /// <summary><c>GET courses/18734</c>: wrapped in <c>{ "course": ... }</c>; two male tees, one female tee whose
    /// handicaps are incomplete (hole 7 has none), so its stroke indexes fall back to hole order.</summary>
    public const string Course = """
        {
          "course": {
            "id": 18734,
            "club_name": "Cedar Ridge Golf Club",
            "course_name": "Ridge Course",
            "location": {
              "address": "4100 Cedar Ridge Rd, Bend, OR 97702, USA",
              "city": "Bend",
              "state": "OR",
              "country": "United States",
              "latitude": 44.0216,
              "longitude": -121.3442
            },
            "tees": {
              "female": [
                {
                  "tee_name": "Red",
                  "course_rating": 71.2,
                  "slope_rating": 126,
                  "bogey_rating": 98.4,
                  "total_yards": 5326,
                  "total_meters": 4870,
                  "number_of_holes": 18,
                  "par_total": 72,
                  "front_course_rating": 35.7,
                  "front_slope_rating": 124,
                  "front_bogey_rating": 49.1,
                  "back_course_rating": 35.5,
                  "back_slope_rating": 128,
                  "back_bogey_rating": 49.3,
                  "holes": [
                    { "par": 4, "yardage": 312, "handicap": 5 },
                    { "par": 5, "yardage": 431, "handicap": 3 },
                    { "par": 4, "yardage": 301, "handicap": 9 },
                    { "par": 3, "yardage": 118, "handicap": 17 },
                    { "par": 4, "yardage": 322, "handicap": 1 },
                    { "par": 4, "yardage": 287, "handicap": 11 },
                    { "par": 3, "yardage": 131 },
                    { "par": 5, "yardage": 446, "handicap": 7 },
                    { "par": 4, "yardage": 318, "handicap": 13 },
                    { "par": 4, "yardage": 305, "handicap": 6 },
                    { "par": 4, "yardage": 331, "handicap": 4 },
                    { "par": 3, "yardage": 112, "handicap": 18 },
                    { "par": 5, "yardage": 452, "handicap": 2 },
                    { "par": 4, "yardage": 298, "handicap": 12 },
                    { "par": 4, "yardage": 336, "handicap": 8 },
                    { "par": 3, "yardage": 139, "handicap": 16 },
                    { "par": 4, "yardage": 294, "handicap": 14 },
                    { "par": 5, "yardage": 443, "handicap": 10 }
                  ]
                }
              ],
              "male": [
                {
                  "tee_name": "Blue",
                  "course_rating": 73.4,
                  "slope_rating": 135,
                  "bogey_rating": 98.9,
                  "total_yards": 7012,
                  "total_meters": 6412,
                  "number_of_holes": 18,
                  "par_total": 72,
                  "front_course_rating": 36.8,
                  "front_slope_rating": 133,
                  "front_bogey_rating": 49.6,
                  "back_course_rating": 36.6,
                  "back_slope_rating": 137,
                  "back_bogey_rating": 49.3,
                  "holes": [
                    { "par": 4, "yardage": 402, "handicap": 7 },
                    { "par": 5, "yardage": 548, "handicap": 1 },
                    { "par": 4, "yardage": 385, "handicap": 11 },
                    { "par": 3, "yardage": 178, "handicap": 17 },
                    { "par": 4, "yardage": 421, "handicap": 5 },
                    { "par": 4, "yardage": 367, "handicap": 9 },
                    { "par": 3, "yardage": 195, "handicap": 15 },
                    { "par": 5, "yardage": 563, "handicap": 3 },
                    { "par": 4, "yardage": 410, "handicap": 13 },
                    { "par": 4, "yardage": 395, "handicap": 8 },
                    { "par": 4, "yardage": 428, "handicap": 10 },
                    { "par": 3, "yardage": 162, "handicap": 18 },
                    { "par": 5, "yardage": 571, "handicap": 2 },
                    { "par": 4, "yardage": 389, "handicap": 12 },
                    { "par": 4, "yardage": 440, "handicap": 6 },
                    { "par": 3, "yardage": 201, "handicap": 16 },
                    { "par": 4, "yardage": 377, "handicap": 14 },
                    { "par": 5, "yardage": 532, "handicap": 4 }
                  ]
                },
                {
                  "tee_name": "White",
                  "course_rating": 71.0,
                  "slope_rating": 128,
                  "bogey_rating": 95.1,
                  "total_yards": 6512,
                  "total_meters": 5955,
                  "number_of_holes": 18,
                  "par_total": 72,
                  "front_course_rating": 35.6,
                  "front_slope_rating": 127,
                  "front_bogey_rating": 47.8,
                  "back_course_rating": 35.4,
                  "back_slope_rating": 129,
                  "back_bogey_rating": 47.3,
                  "holes": [
                    { "par": 4, "yardage": 378, "handicap": 7 },
                    { "par": 5, "yardage": 515, "handicap": 1 },
                    { "par": 4, "yardage": 360, "handicap": 11 },
                    { "par": 3, "yardage": 160, "handicap": 17 },
                    { "par": 4, "yardage": 395, "handicap": 5 },
                    { "par": 4, "yardage": 342, "handicap": 9 },
                    { "par": 3, "yardage": 172, "handicap": 15 },
                    { "par": 5, "yardage": 530, "handicap": 3 },
                    { "par": 4, "yardage": 384, "handicap": 13 },
                    { "par": 4, "yardage": 368, "handicap": 8 },
                    { "par": 4, "yardage": 401, "handicap": 10 },
                    { "par": 3, "yardage": 147, "handicap": 18 },
                    { "par": 5, "yardage": 538, "handicap": 2 },
                    { "par": 4, "yardage": 362, "handicap": 12 },
                    { "par": 4, "yardage": 410, "handicap": 6 },
                    { "par": 3, "yardage": 180, "handicap": 16 },
                    { "par": 4, "yardage": 351, "handicap": 14 },
                    { "par": 5, "yardage": 519, "handicap": 4 }
                  ]
                }
              ]
            }
          }
        }
        """;

    /// <summary>A bare (unwrapped) 9-hole course with no handicaps, no ratings and string-typed numbers.</summary>
    public const string BareNineHoleCourse = """
        {
          "id": "9021",
          "club_name": "Cedar Ridge Municipal",
          "course_name": "",
          "location": { "city": "Redmond", "state": "OR", "country": "United States" },
          "tees": {
            "male": [
              {
                "tee_name": "Yellow",
                "slope_rating": "112",
                "holes": [
                  { "par": 4, "yardage": 330 }, { "par": 3, "yardage": 140 }, { "par": 4, "yardage": 355 },
                  { "par": 5, "yardage": 470 }, { "par": 4, "yardage": 310 }, { "par": 3, "yardage": 125 },
                  { "par": 4, "yardage": 342 }, { "par": 4, "yardage": 298 }, { "par": 5, "yardage": 455 }
                ]
              },
              { "tee_name": "Broken", "course_rating": 30.1, "holes": [ { "yardage": 300 } ] }
            ]
          }
        }
        """;

    /// <summary><c>GET search?search_query=cedar</c>.</summary>
    public const string Search = """
        {
          "courses": [
            {
              "id": 18734,
              "club_name": "Cedar Ridge Golf Club",
              "course_name": "Ridge Course",
              "location": {
                "address": "4100 Cedar Ridge Rd, Bend, OR 97702, USA",
                "city": "Bend",
                "state": "OR",
                "country": "United States",
                "latitude": 44.0216,
                "longitude": -121.3442
              },
              "tees": { "male": [], "female": [] }
            },
            {
              "id": 9021,
              "club_name": "Cedar Ridge Municipal",
              "course_name": "Cedar Ridge Municipal",
              "location": { "city": "Redmond", "state": "OR", "country": "United States" }
            },
            { "club_name": "No id, skipped" }
          ]
        }
        """;
}
