namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Parts of the interface that are built but deliberately not shown.
    ///
    /// These are off by the author's decision, not because anything is broken — the code behind
    /// them works and is tested, and the intent is to bring them back later. Switching one to
    /// <c>true</c> is the whole change; nothing else needs touching.
    ///
    /// Kept as switches rather than deleted so the behaviour they hide stays compiled, stays
    /// covered by tests, and cannot rot quietly while it is out of sight. Deleting them would mean
    /// rebuilding from scratch later, and the reasoning in their comments would go with them.
    ///
    /// Declared <c>static readonly</c> rather than <c>const</c> on purpose: a const false makes
    /// every guarded block unreachable code, which the compiler warns about and which would bury
    /// the parts of the app that are simply switched off in a pile of warnings.
    /// </summary>
    internal static class UiFeatures
    {
        /// <summary>
        /// The Statistics screen and the button that opens it.
        ///
        /// <see cref="StatsScreen"/> is complete and reads from the same PlayerStatistics the
        /// exported cookbook.md uses, so the figures remain available outside the game while this
        /// is off.
        /// </summary>
        internal static readonly bool StatisticsScreen = false;

        /// <summary>
        /// The price, units and total value on each cookbook row.
        ///
        /// With this off the row is name, chain and addictiveness only, and the meter widens to
        /// take the space rather than leaving a gap where the numbers were.
        /// </summary>
        internal static readonly bool RowValueAndUnits = false;
    }
}
