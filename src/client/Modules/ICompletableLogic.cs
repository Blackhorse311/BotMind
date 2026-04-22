namespace Blackhorse311.BotMind.Modules
{
    /// <summary>
    /// Interface for logic classes that report completion status to their parent layer.
    /// Eliminates the need for per-type RegisterLogic overloads and typed _*Logic fields.
    /// Layers hold a single ICompletableLogic reference instead of N typed fields.
    /// </summary>
    public interface ICompletableLogic
    {
        /// <summary>Whether this logic has finished (successfully or via failure).</summary>
        bool IsComplete { get; }

        /// <summary>Whether this logic ended due to failure (nav failure, timeout, etc.).</summary>
        bool HasFailed { get; }
    }
}
