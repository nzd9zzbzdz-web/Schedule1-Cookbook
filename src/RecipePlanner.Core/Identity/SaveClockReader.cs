using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RecipePlanner.Core.Identity
{
    /// <summary>
    /// Reads the game clock stored in a save's <c>Time.json</c>.
    ///
    /// This is the moment the save was written, and therefore the boundary between production that
    /// really happened and production the player abandoned by quitting without saving.
    ///
    /// Read-only, like everything else that touches the game's save tree.
    ///
    /// Shape confirmed from ScheduleOne.GameTime.TimeManager.GetSaveString() → TimeData:
    ///   { "DataType": "TimeData", "TimeOfDay": 1355, "ElapsedDays": 40, "Playtime": 60656 }
    ///
    /// Note the field is called TimeOfDay *in the save file* while the runtime property on
    /// TimeManager is CurrentTime. Confusing the two cost this project a real bug once already.
    /// </summary>
    public static class SaveClockReader
    {
        public sealed class SaveClock
        {
            public int ElapsedDays { get; set; }
            public int TimeOfDay { get; set; }

            /// <summary>Why the read failed, when the clock is unusable.</summary>
            public string Error { get; set; }

            public bool IsUsable => Error == null;
        }

        /// <summary>
        /// Returns the save's clock, or a clock carrying an Error. A missing or unreadable
        /// Time.json is not an error condition worth acting on — a brand-new save has not written
        /// one yet, and a multiplayer client has no local save folder at all. The caller must
        /// treat an unusable clock as "reconcile nothing".
        /// </summary>
        public static SaveClock Read(string saveFolderPath)
        {
            if (string.IsNullOrWhiteSpace(saveFolderPath))
                return new SaveClock { Error = "No save folder path." };

            var path = Path.Combine(saveFolderPath, "Time.json");
            if (!File.Exists(path))
                return new SaveClock { Error = "Time.json not present (save never written?)." };

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));

                var days = json["ElapsedDays"];
                var time = json["TimeOfDay"];
                if (days == null || time == null)
                    return new SaveClock { Error = "Time.json has no ElapsedDays/TimeOfDay." };

                var clock = new SaveClock { ElapsedDays = (int)days, TimeOfDay = (int)time };

                // A negative day or an out-of-range packed time means we are misreading the file,
                // and acting on a misread clock would delete real history.
                if (clock.ElapsedDays < 0 || clock.TimeOfDay < 0 || clock.TimeOfDay > 2359 ||
                    clock.TimeOfDay % 100 > 59)
                    return new SaveClock { Error = "Time.json values are out of range: " + json.ToString() };

                return clock;
            }
            catch (Exception ex)
            {
                return new SaveClock { Error = "Could not read Time.json: " + ex.Message };
            }
        }
    }
}
