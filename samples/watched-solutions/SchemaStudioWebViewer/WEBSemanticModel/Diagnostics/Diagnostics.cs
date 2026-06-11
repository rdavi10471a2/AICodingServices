namespace SchemaStudioWebViewer.WEBSemanticModel.Diagnostics
{
    //-----------------------------------------
    // LOG LEVEL (MATCHES YOUR EXISTING SYSTEM)
    //-----------------------------------------
    public enum QueryLogLevel
    {
        Info,
        Warning,
        Error
    }

    //-----------------------------------------
    // LOGGER INTERFACE
    //-----------------------------------------
    public interface IQueryLogger
    {
        //-----------------------------------------
        // CORE ENTRY POINT
        //-----------------------------------------
        void Log(QueryLogLevel level, string message, Exception ex = null);

        //-----------------------------------------
        // CONVENIENCE METHODS
        //-----------------------------------------
        void Info(string message);
        void Warning(string message);
        void Error(string message, Exception ex = null);
    }

    //-----------------------------------------
    // BASE IMPLEMENTATION (OPTIONAL BUT CLEAN)
    //-----------------------------------------
    public abstract class QueryLoggerBase : IQueryLogger
    {
        public abstract void Log(QueryLogLevel level, string message, Exception ex = null);

        public void Info(string message)
        {
            Log(QueryLogLevel.Info, message);
        }

        public void Warning(string message)
        {
            Log(QueryLogLevel.Warning, message);
        }

        public void Error(string message, Exception ex = null)
        {
            Log(QueryLogLevel.Error, message, ex);
        }
    }

    //-----------------------------------------
    // NO-OP LOGGER
    //-----------------------------------------
    public sealed class NullQueryLogger : QueryLoggerBase
    {
        public override void Log(QueryLogLevel level, string message, Exception ex = null)
        {
            // intentionally empty
        }
    }

    //-----------------------------------------
    // SAFE EXECUTION EXTENSIONS
    //-----------------------------------------
    public static class QueryLoggerExtensions
    {
        public static void Safe(this IQueryLogger logger, string context, Action action)
        {
            if (logger == null)
                logger = new NullQueryLogger();

            try
            {
                action();
            }
            catch (Exception ex)
            {
                logger.Log(QueryLogLevel.Error, "FAILED: " + context, ex);
                throw;
            }
        }
    }
}
