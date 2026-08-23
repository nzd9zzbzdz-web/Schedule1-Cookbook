namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// Logging seam so Core and Game never reference MelonLoader. The mod supplies the real
    /// implementation; tests supply a recorder.
    /// </summary>
    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    public sealed class NullLog : ILog
    {
        public static readonly NullLog Instance = new NullLog();
        private NullLog() { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
    }
}
