using System;
using System.Collections.Generic;

namespace RecipePlanner.Core.Production
{
    /// <summary>
    /// Drops production the player quit without saving.
    ///
    /// The problem, observed live on 0.4.6f13: the player cooks at day 40 15:07, quits without
    /// saving, and reloads. The game restores the save's clock — day 40 13:55 — and the stations
    /// re-run the same operations. Those batches are counted twice: once from the abandoned
    /// timeline, once from the real one. Worse, because a replayed operation is deterministic it
    /// often completes at the *same* game-minute, so its idempotency key collides and the genuine
    /// batch is discarded as a duplicate while the phantom one is kept.
    ///
    /// The fix is to treat the save file as the authority on what actually happened. Anything
    /// recorded later than the save's own clock belongs to a timeline the game itself threw away,
    /// so it is removed here — and the replay is then free to record it again with real values.
    ///
    /// Removed events are not destroyed; the caller writes them to a sidecar log. That keeps the
    /// decision auditable, which matters because this is the one place the mod deletes its own
    /// history.
    /// </summary>
    public static class RollbackReconciler
    {
        public sealed class Result
        {
            /// <summary>Events that survive — the caller rewrites events.jsonl from these.</summary>
            public List<ProductionEvent> Kept { get; set; } = new List<ProductionEvent>();

            /// <summary>Events from the abandoned timeline, in original order.</summary>
            public List<ProductionEvent> RolledBack { get; set; } = new List<ProductionEvent>();

            public int Units { get; set; }
            public bool Changed => RolledBack.Count > 0;
        }

        /// <summary>
        /// Partitions the log against the loaded save's clock.
        ///
        /// An event exactly on the save minute is KEPT. The save was written during that minute, so
        /// the batch is at least as likely to be inside it as outside, and keeping a real batch is
        /// the cheaper error — a duplicate is visible and fixable, a silently deleted cook is not.
        /// </summary>
        public static Result Apply(IEnumerable<ProductionEvent> events, int saveElapsedDays, int saveTimeOfDay)
        {
            var result = new Result();
            if (events == null) return result;

            var cutoff = GameClock.Ordinal(saveElapsedDays, saveTimeOfDay);

            foreach (var evt in events)
            {
                if (evt == null) continue;

                if (GameClock.Ordinal(evt) > cutoff)
                {
                    result.RolledBack.Add(evt);
                    result.Units += evt.Quantity;
                }
                else
                {
                    result.Kept.Add(evt);
                }
            }

            return result;
        }

        /// <summary>
        /// One line describing what was dropped, for the log. Names the newest dropped batch so the
        /// player can tell whether it matches what they remember abandoning.
        /// </summary>
        public static string Summarise(Result result, int saveElapsedDays, int saveTimeOfDay)
        {
            if (result == null || !result.Changed) return null;

            var newest = result.RolledBack[0];
            foreach (var evt in result.RolledBack)
                if (GameClock.Ordinal(evt) > GameClock.Ordinal(newest)) newest = evt;

            return string.Format(
                "Discarded {0} batch(es) totalling {1} units that were produced after this save was " +
                "written ({2}) and then not saved — the newest was {3} at {4}. They are kept in " +
                "rolled-back.jsonl and excluded from statistics.",
                result.RolledBack.Count,
                result.Units,
                GameClock.Describe(saveElapsedDays, saveTimeOfDay),
                newest.ProductKey,
                GameClock.Describe(newest.ElapsedDays, newest.TimeOfDay));
        }
    }
}
